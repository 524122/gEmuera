using Godot;
using System;
using System.Collections.Generic;
using MinorShift.Emuera;
using MinorShift.Emuera.Content;
using MinorShift.Emuera.GameView;

public partial class EmueraContent
{
	enum ConsoleRenderBackend
	{
		Controls,
		Canvas,
	}

	struct ConsoleButtonHit
	{
		public Rect2 Rect;
		public string Input;
		public long Generation;
		public Vector2 ContentCenter;
	}

	struct CanvasImageRenderInfo
	{
		public Texture2D SourceTexture;
		public Rect2 SourceRegion;
		public Vector2 Position;
		public Vector2 Size;
		public Vector2 DrawOffset;
		public Vector2 DrawSize;
		public bool FlipX;
		public bool FlipY;
	}

	struct CanvasImageOverlay
	{
		public EmueraImage Node;
		public ConsoleImagePart Image;
		public int LineNo;
		public int RelX;
		public float LineY;
		public bool IsAnimation;
		public bool EscapesLine;
	}

	struct CanvasDivOverlay
	{
		public Control Node;
		public ConsoleDivPart Div;
		public int LineNo;
		public int RelX;
		public float LineY;
		public bool EscapesLine;
	}

	bool CanRenderLineOnCanvas(ConsoleDisplayLine line)
	{
		if (!UseCanvasRenderBackend || line?.Buttons == null)
			return false;
		foreach (var button in line.Buttons)
		{
			if (button?.StrArray == null)
				continue;
			foreach (var part in button.StrArray)
			{
				if (!CanRenderPartOnCanvas(part))
					return false;
			}
		}
		return true;
	}

	bool CanRenderPartOnCanvas(AConsoleDisplayPart part)
	{
		if (part == null)
			return true;
		if (part is ConsoleStyledString
			|| part is ConsoleRectangleShapePart
			|| part is ConsoleSpacePart
			|| part is ConsoleErrorShapePart)
			return true;
		if (part is ConsoleImagePart)
		{
			// 图片按混合策略处理：普通图片由 Canvas 直接绘制，ColorMatrix/动画/绝对定位图片
			// 只为该图片创建 EmueraImage overlay，避免把包含文本的整行退回旧节点树。
			return true;
		}
		if (part is ConsoleDivPart div)
		{
			// div 的盒模型和子按钮仍复用旧 Control 构建逻辑；这里只允许相对定位 div 做局部 overlay。
			// absolute div 涉及跨行和 depth 排序，继续整行回退，避免改变 v24/snake 的覆盖语义。
			return CanUseCanvasDivOverlay(div);
		}
		return false;
	}

	void AddCanvasLine(ConsoleDisplayLine line, bool isUpdate)
	{
		int lineHeight = GetLineBottom(line);
		int maxLineRight = GetLineRight(line);
		var lineSize = new Vector2(maxLineRight, lineHeight);

		var previousTexturePinCollector = activeTexturePinCollector;
		int previousRenderLineNo = activeRenderLineNo;
		var newTexturePins = new List<SpriteManager.TextureInfo>();
		var newOverlayNodes = new List<CanvasImageOverlay>();
		var newDivOverlayNodes = new List<CanvasDivOverlay>();
		activeTexturePinCollector = newTexturePins;
		activeRenderLineNo = line.LineNo;
		try
		{
			PrepareCanvasLineResources(line);
			BuildCanvasOverlays(line, newOverlayNodes, newDivOverlayNodes);
		}
		catch
		{
			ReleaseCanvasImageOverlayList(newOverlayNodes);
			ReleaseCanvasDivOverlayList(newDivOverlayNodes);
			ReleaseTexturePinList(newTexturePins);
			asyncTexturePendingLineNos.Remove(line.LineNo);
			throw;
		}
		finally
		{
			activeTexturePinCollector = previousTexturePinCollector;
			activeRenderLineNo = previousRenderLineNo;
		}

		bool asyncTexturePendingDuringRender = asyncTexturePendingLineNos.Contains(line.LineNo);
		bool hasExistingLine = lineObjects.ContainsKey(line.LineNo) || lineControls.ContainsKey(line.LineNo);
		if (ShouldDeferLineReplacementForAsyncTexture(line, isUpdate, hasExistingLine, asyncTexturePendingDuringRender))
		{
			ReleaseCanvasImageOverlayList(newOverlayNodes);
			ReleaseCanvasDivOverlayList(newDivOverlayNodes);
			ReleaseTexturePinList(newTexturePins);
			return;
		}

		if (lineControls.TryGetValue(line.LineNo, out var existingControl))
		{
			UnregisterLine(line.LineNo);
			if (existingControl != null)
				SafeQueueFree(existingControl);
		}

		RegisterLine(line.LineNo, line, null, lineSize);
		RegisterCanvasImageOverlays(line.LineNo, newOverlayNodes);
		RegisterCanvasDivOverlays(line.LineNo, newDivOverlayNodes);
		RegisterLineTexturePins(line.LineNo, newTexturePins);
		if (asyncTexturePendingDuringRender)
			asyncTexturePendingLineNos.Add(line.LineNo);
		else
			pendingAsyncLineUpdates.Remove(line.LineNo);
		displayRevision++;
		NotifyConsoleRenderContentChanged();

		if (!batchingDisplayLines && lineNumbers.Count > MaxVisibleLines)
			RemoveTopLines(LineTrimBatch);

		if (!batchingDisplayLines)
		{
			RefreshQuickInputGate();
			if (isUpdate)
				QueueScaleBoundsUpdate();
			else
				QueueDisplayFollowUp();
		}
	}

	void PrepareCanvasLineResources(ConsoleDisplayLine line)
	{
		if (line?.Buttons == null)
			return;
		foreach (var button in line.Buttons)
		{
			if (button?.StrArray == null)
				continue;
			foreach (var part in button.StrArray)
			{
				if (part is ConsoleImagePart image)
					TryResolveCanvasImage(image, 0, out _);
			}
		}
	}

	void BuildCanvasOverlays(ConsoleDisplayLine line, List<CanvasImageOverlay> imageOverlays, List<CanvasDivOverlay> divOverlays)
	{
		if (line?.Buttons == null || lineContainer == null)
			return;
		foreach (var button in line.Buttons)
		{
			if (button?.StrArray == null)
				continue;
			foreach (var part in button.StrArray)
			{
				if (part is ConsoleImagePart image && NeedsCanvasImageOverlay(image))
					AddCanvasImageOverlay(line.LineNo, image, 0, imageOverlays);
				else if (part is ConsoleDivPart div && CanUseCanvasDivOverlay(div))
					AddCanvasDivOverlay(line.LineNo, div, 0, divOverlays);
			}
		}
	}

	ASprite ResolveCanvasImageSprite(ConsoleImagePart image)
	{
		if (image == null)
			return null;
		ASprite sprite = image.Image;
		if (sprite == null && !string.IsNullOrEmpty(image.ResourceName))
			sprite = AppContents.GetSprite(image.ResourceName);
		return sprite;
	}

	void AddCanvasImageOverlay(int lineNo, ConsoleImagePart image, int relX, List<CanvasImageOverlay> overlays)
	{
		if (!TryResolveCanvasImage(image, relX, out var info))
			return;

		var emuImg = new EmueraImage();
		emuImg.MouseFilter = MouseFilterEnum.Ignore;
		emuImg.ClipContents = false;
		emuImg.SourceTexture = info.SourceTexture;
		emuImg.SourceRegion = info.SourceRegion;
		emuImg.DrawOffset = info.DrawOffset;
		emuImg.DrawSize = info.DrawSize;
		emuImg.Size = info.Size;
		emuImg.CustomMinimumSize = Vector2.Zero;
		emuImg.FlipX = info.FlipX;
		emuImg.FlipY = info.FlipY;
		emuImg.SetColorMatrix(image.ColorMatrix);
		bool escapesLine = ImageEscapesLine(image);
		if (escapesLine)
			emuImg.ZIndex = EscapedConsolePartZIndex;
		emuImg.SetMeta("line_no", lineNo);
		lineContainer.AddChild(emuImg);

		var overlay = new CanvasImageOverlay
		{
			Node = emuImg,
			Image = image,
			LineNo = lineNo,
			RelX = relX,
			LineY = 0,
			IsAnimation = ResolveCanvasImageSprite(image) is SpriteAnime,
			EscapesLine = escapesLine,
		};
		UpdateCanvasImageOverlay(overlay, 0);
		overlays.Add(overlay);
	}

	bool NeedsCanvasImageOverlay(ConsoleImagePart image)
	{
		if (image == null)
			return false;
		var sprite = ResolveCanvasImageSprite(image);
		return image.ColorMatrix != null
			|| image.Display != DisplayMode.Relative
			|| sprite is SpriteAnime;
	}

	bool CanUseCanvasDivOverlay(ConsoleDivPart div)
	{
		return div != null
			&& div.IsRelative
			&& div.Display == DisplayMode.Relative;
	}

	void AddCanvasDivOverlay(int lineNo, ConsoleDivPart div, int relX, List<CanvasDivOverlay> overlays)
	{
		if (div == null || overlays == null || lineContainer == null)
			return;

		var wrapper = BuildDivControl(div, relX);
		wrapper.MouseFilter = MouseFilterEnum.Pass;
		wrapper.SetMeta("canvas_overlay_line_no", lineNo);
		lineContainer.AddChild(wrapper);

		var overlay = new CanvasDivOverlay
		{
			Node = wrapper,
			Div = div,
			LineNo = lineNo,
			RelX = relX,
			LineY = 0,
			EscapesLine = DivEscapesLine(div),
		};
		UpdateCanvasDivOverlay(overlay, 0);
		overlays.Add(overlay);
	}

	bool DivEscapesLine(ConsoleDivPart div)
	{
		if (div == null)
			return false;
		return div.Display != DisplayMode.Relative
			|| div.Top < 0
			|| div.Bottom > EffectiveLineHeight;
	}

	void UpdateCanvasDivOverlay(CanvasDivOverlay overlay, float lineY)
	{
		UpdateCanvasDivOverlay(overlay, lineY, GetVisibleCanvasContentRange());
	}

	void UpdateCanvasDivOverlay(CanvasDivOverlay overlay, float lineY, Rect2 visible)
	{
		var node = overlay.Node;
		if (node == null || !GodotObject.IsInstanceValid(node))
			return;
		node.Position = GetHtmlDivPosition(overlay.Div, overlay.RelX) + new Vector2(0, lineY);
		node.Size = new Vector2(overlay.Div.DivWidth, overlay.Div.DivHeight);
		node.CustomMinimumSize = node.Size;
		node.ZIndex = GetGodotZIndexForHtmlDepth(overlay.Div.Depth);
		node.Visible = IsCanvasOverlayRectVisible(new Rect2(node.Position, node.Size), visible);
	}

	void UpdateCanvasImageOverlay(CanvasImageOverlay overlay, float lineY)
	{
		UpdateCanvasImageOverlay(overlay, lineY, GetVisibleCanvasContentRange());
	}

	void UpdateCanvasImageOverlay(CanvasImageOverlay overlay, float lineY, Rect2 visible)
	{
		var node = overlay.Node;
		if (node == null || !GodotObject.IsInstanceValid(node))
			return;
			if (!TryResolveCanvasImage(overlay.Image, overlay.RelX, out var info))
			{
				// XRay/HTML 差分图刷新时，新帧可能还在异步解码。已有节点继续显示旧纹理，
				// 等新纹理解析成功后再替换，避免刷新瞬间露出空白或白色图块。
				node.Visible = node.SourceTexture != null && IsCanvasOverlayRectVisible(GetCanvasImageOverlayRect(node), visible);
				return;
			}
		node.SourceTexture = info.SourceTexture;
		node.SourceRegion = info.SourceRegion;
		node.DrawOffset = info.DrawOffset;
		node.DrawSize = info.DrawSize;
		node.Position = info.Position + new Vector2(0, lineY);
		node.Size = info.Size;
		node.FlipX = info.FlipX;
		node.FlipY = info.FlipY;
		node.SetColorMatrix(overlay.Image.ColorMatrix);
		node.Visible = IsCanvasOverlayRectVisible(GetCanvasImageOverlayRect(node), visible);
	}

	bool TryResolveCanvasImage(ConsoleImagePart image, int relX, out CanvasImageRenderInfo info)
	{
		info = default;
		if (image == null)
			return false;

		ASprite sprite = ResolveCanvasImageSprite(image);

		var texture = GetSpriteTexture(sprite);
		if (texture == null
			&& !string.IsNullOrEmpty(image.ResourceName)
			&& !IsDynamicCutinName(image.ResourceName)
			&& !failedTextureSearches.Contains(image.ResourceName))
		{
			texture = ResolveCanvasTextureByResourceName(image.ResourceName);
		}

		if (texture == null)
			return false;

		int w;
		int imgH;
		if (image.dest_rect.Width > 0 && image.dest_rect.Height > 0)
		{
			w = image.dest_rect.Width;
			imgH = image.dest_rect.Height;
		}
		else if (image.dest_rect.Width > 0)
		{
			w = image.dest_rect.Width;
			imgH = texture.GetHeight() > 0 ? texture.GetHeight() * w / texture.GetWidth() : w;
		}
		else if (image.dest_rect.Height > 0 && texture.GetHeight() > 0)
		{
			imgH = image.dest_rect.Height;
			w = texture.GetWidth() * imgH / texture.GetHeight();
		}
		else
		{
			w = texture.GetWidth() > 0 ? texture.GetWidth() : 32;
			imgH = texture.GetHeight() > 0 ? texture.GetHeight() : 32;
		}

		if (image.Width > 0 && image.Width != w)
		{
			int layoutW = image.Width;
			if (w > 0)
				imgH = imgH * layoutW / w;
			w = layoutW;
		}

		if (texture is AtlasTexture atlas)
		{
			info.SourceTexture = atlas.Atlas;
			info.SourceRegion = atlas.Region;
		}
		else
		{
			info.SourceTexture = texture;
			info.SourceRegion = default;
		}
		info.Position = GetHtmlImagePosition(image, relX);
		info.Size = new Vector2(w, imgH);
		info.DrawOffset = GetSpriteHtmlDrawOffset(sprite, image.ResourceName, w, imgH);
		info.DrawSize = GetSpriteHtmlDrawSize(sprite, image.ResourceName, w, imgH);
		info.FlipX = image.FlipX;
		info.FlipY = image.FlipY;
		return info.SourceTexture != null;
	}

	Texture2D ResolveCanvasTextureByResourceName(string resName)
	{
		if (string.IsNullOrEmpty(resName))
			return null;

		bool TryResolveOrRequest(string path, bool cacheResolvedPath, out Texture2D candidateTexture)
		{
			if (TryResolveCanvasTexturePath(resName, path, cacheResolvedPath, out candidateTexture, out bool asyncRequested))
				return true;
			return asyncRequested;
		}

		if (resolvedTextureSearchPaths.TryGetValue(resName, out var cachedPath))
		{
			if (TryResolveOrRequest(cachedPath, true, out var cachedTexture))
				return cachedTexture;
			resolvedTextureSearchPaths.Remove(resName);
		}

		bool hasExt = resName.Contains(".");
		if (TryResolveOrRequest(resName, true, out var texture))
			return texture;
		if (TryResolveOrRequest(System.IO.Path.Combine(Program.ContentDir, resName), true, out texture))
			return texture;
		if (TryResolveOrRequest(System.IO.Path.Combine(Program.ExeDir, resName), true, out texture))
			return texture;
		if (TryResolveOrRequest(System.IO.Path.Combine(Program.ExeDir, "resources", resName), true, out texture))
			return texture;

		if (!hasExt)
		{
			foreach (var ext in CanvasImageFallbackExtensions)
			{
				string target = resName + ext;
				if (TryResolveOrRequest(target, true, out texture))
					return texture;
				if (TryResolveOrRequest(System.IO.Path.Combine(Program.ContentDir, target), true, out texture))
					return texture;
				if (TryResolveOrRequest(System.IO.Path.Combine(Program.ExeDir, "resources", target), true, out texture))
					return texture;
			}
		}

		if (!string.IsNullOrEmpty(Program.ContentDir))
		{
			if (hasExt)
			{
				var found = uEmuera.Utils.FindFileRecursive(Program.ContentDir, resName);
				if (!string.IsNullOrEmpty(found) && TryResolveOrRequest(found, true, out texture))
				{
					GenericUtils.Info(EmueraLogCategory.Sprite, () => $"[IMG] Found \"{resName}\" via subdirectory search: {found}");
					return texture;
				}
			}
			else
			{
				foreach (var ext in CanvasImageFallbackExtensions)
				{
					string target = resName + ext;
					var found = uEmuera.Utils.FindFileRecursive(Program.ContentDir, target);
					if (string.IsNullOrEmpty(found))
						continue;
					if (TryResolveOrRequest(found, true, out texture))
					{
						GenericUtils.Info(EmueraLogCategory.Sprite, () => $"[IMG] Found \"{resName}\" via subdirectory search: {found}");
						return texture;
					}
				}
			}
		}

		if (!HasPendingAsyncTextureForCurrentRender())
		{
			failedTextureSearches.Add(resName);
			GenericUtils.Info(EmueraLogCategory.Sprite, () => $"[IMG] All fallback paths failed for \"{resName}\"");
		}
		return null;
	}

	bool TryResolveCanvasTexturePath(string resName, string path, bool cacheResolvedPath, out Texture2D texture, out bool asyncRequested)
	{
		texture = null;
		asyncRequested = false;
		if (string.IsNullOrEmpty(path) || !uEmuera.Utils.FileExists(path))
			return false;
		if (cacheResolvedPath)
			resolvedTextureSearchPaths[resName] = path;
		if (TryGetDisplayTextureInfo(resName, path, out var ti))
			{
				texture = ti.texture;
				return true;
			}
			// 异步请求只表示“稍后可能可用”，不能当作本轮已有可绘制纹理。
			// 返回 false 让调用方走挂起刷新逻辑，避免先提交空白/半成品图层。
			asyncRequested = RequestAsyncTextureForCurrentRender(resName, path);
			return false;
		}

	void NotifyConsoleRenderContentChanged()
	{
		if (!UseCanvasRenderBackend)
			return;
		consoleRenderSurface?.MarkDirty();
		if (batchingDisplayLines)
			return;
		FlushCanvasOverlayRowsIfNeeded();
		RefreshCanvasOverlayVisibility();
	}

	void RefreshCanvasOverlayRows()
	{
		if (!UseCanvasRenderBackend || lineContainer == null)
			return;
		EnsureLineLayout();
		var visible = GetVisibleCanvasContentRange();
		foreach (int lineNo in canvasRowsWithPositionedNodes)
		{
			float y = GetLineTopByLayout(lineNo);
			if (lineControls.TryGetValue(lineNo, out var control)
				&& control != null
				&& GodotObject.IsInstanceValid(control))
			{
				control.Position = new Vector2(0, y);
			}
			if (canvasImageOverlayNodes.TryGetValue(lineNo, out var overlays) && overlays != null)
			{
				for (int i = 0; i < overlays.Count; i++)
				{
					var overlay = overlays[i];
					overlay.LineY = y;
					UpdateCanvasImageOverlay(overlay, y, visible);
					overlays[i] = overlay;
				}
			}
			if (canvasDivOverlayNodes.TryGetValue(lineNo, out var divOverlays) && divOverlays != null)
			{
				for (int i = 0; i < divOverlays.Count; i++)
				{
					var overlay = divOverlays[i];
					overlay.LineY = y;
					UpdateCanvasDivOverlay(overlay, y, visible);
					divOverlays[i] = overlay;
				}
			}
		}
		canvasOverlayRowsDirty = false;
	}

	void RefreshCanvasOverlayVisibility()
	{
		if (!UseCanvasRenderBackend)
			return;
		var visible = GetVisibleCanvasContentRange();
		canvasVisibilityTargetRows.Clear();
		canvasVisibilityTargetRowSet.Clear();
		canvasCurrentVisibilityRows.Clear();

		// 普通 overlay 只需要在“当前可见行”和“上一轮可见行”之间切换；
		// 可能越过本行边界的 overlay 额外常驻索引表，并继续用真实矩形做相交判断。
		if (TryGetVisibleCanvasLineLayoutRange(visible, out int firstIndex, out int lastIndex))
		{
			for (int i = firstIndex; i <= lastIndex; i++)
			{
				int lineNo = lineLayoutEntries[i].LineNo;
				if (!HasCanvasOverlaysForLine(lineNo))
					continue;
				AddCanvasVisibilityTargetRow(lineNo);
				canvasCurrentVisibilityRows.Add(lineNo);
			}
		}

		foreach (int lineNo in canvasLastVisibilityRows)
			AddCanvasVisibilityTargetRow(lineNo);
		foreach (int lineNo in canvasRowsWithEscapedOverlays)
			AddCanvasVisibilityTargetRow(lineNo);

		for (int i = 0; i < canvasVisibilityTargetRows.Count; i++)
			RefreshCanvasOverlayVisibilityForLine(canvasVisibilityTargetRows[i], visible);

		canvasLastVisibilityRows.Clear();
		for (int i = 0; i < canvasCurrentVisibilityRows.Count; i++)
			canvasLastVisibilityRows.Add(canvasCurrentVisibilityRows[i]);
	}

	bool HasCanvasOverlaysForLine(int lineNo)
	{
		return canvasImageOverlayNodes.ContainsKey(lineNo)
			|| canvasDivOverlayNodes.ContainsKey(lineNo);
	}

	void AddCanvasVisibilityTargetRow(int lineNo)
	{
		if (canvasVisibilityTargetRowSet.Add(lineNo))
			canvasVisibilityTargetRows.Add(lineNo);
	}

	void RefreshCanvasOverlayVisibilityForLine(int lineNo, Rect2 visible)
	{
		if (canvasImageOverlayNodes.TryGetValue(lineNo, out var imageOverlays) && imageOverlays != null)
		{
			for (int i = 0; i < imageOverlays.Count; i++)
			{
				var node = imageOverlays[i].Node;
				if (node == null || !GodotObject.IsInstanceValid(node))
					continue;
				if (node.SourceTexture == null)
				{
					node.Visible = false;
					continue;
				}
				node.Visible = IsCanvasOverlayRectVisible(GetCanvasImageOverlayRect(node), visible);
			}
		}
		if (canvasDivOverlayNodes.TryGetValue(lineNo, out var divOverlays) && divOverlays != null)
		{
			for (int i = 0; i < divOverlays.Count; i++)
			{
				var node = divOverlays[i].Node;
				if (node == null || !GodotObject.IsInstanceValid(node))
					continue;
				node.Visible = IsCanvasOverlayRectVisible(new Rect2(node.Position, node.Size), visible);
			}
		}
	}

	Rect2 GetVisibleCanvasContentRange()
	{
		if (scrollContainer == null)
			return new Rect2(Vector2.Zero, CalculateLineContentSize());
		float scale = GetSafeContentScale();
		var scroll = new Vector2(scrollContainer.ScrollHorizontal, scrollContainer.ScrollVertical) / scale;
		var viewport = scrollContainer.Size / scale;
		return new Rect2(scroll - new Vector2(4, EffectiveLineHeight * 2), viewport + new Vector2(8, EffectiveLineHeight * 4));
	}

	bool IsCanvasOverlayRectVisible(Rect2 rect)
	{
		return IsCanvasOverlayRectVisible(rect, GetVisibleCanvasContentRange());
	}

	static bool IsCanvasOverlayRectVisible(Rect2 rect, Rect2 visible)
	{
		if (rect.Size.X <= 0 || rect.Size.Y <= 0)
			return false;
		return visible.Intersects(rect, true);
	}

	static Rect2 GetCanvasImageOverlayRect(EmueraImage node)
	{
		if (node == null)
			return new Rect2();
		var drawSize = node.DrawSize.X > 0 && node.DrawSize.Y > 0 ? node.DrawSize : node.Size;
		return new Rect2(node.Position + node.DrawOffset, drawSize);
	}

	bool IsCanvasOverlayLine(int lineNo)
	{
		return UseCanvasRenderBackend
			&& lineControls.TryGetValue(lineNo, out var control)
			&& control != null
			&& GodotObject.IsInstanceValid(control);
	}

	float GetLineTopByLineNo(int lineNo)
	{
		return GetLineTopByLayout(lineNo);
	}

	void ReleaseCanvasImageOverlayList(List<CanvasImageOverlay> overlays)
	{
		if (overlays == null)
			return;
		for (int i = 0; i < overlays.Count; i++)
		{
			var node = overlays[i].Node;
			if (node != null && GodotObject.IsInstanceValid(node))
				SafeQueueFree(node);
		}
		overlays.Clear();
	}

	void ReleaseCanvasDivOverlayList(List<CanvasDivOverlay> overlays)
	{
		if (overlays == null)
			return;
		for (int i = 0; i < overlays.Count; i++)
		{
			var node = overlays[i].Node;
			if (node != null && GodotObject.IsInstanceValid(node))
				SafeQueueFree(node);
		}
		overlays.Clear();
	}

	void ReleaseTexturePinList(List<SpriteManager.TextureInfo> pins)
	{
		if (pins == null)
			return;
		for (int i = 0; i < pins.Count; i++)
			SpriteManager.UnpinTextureInfo(pins[i]);
		pins.Clear();
	}

	void RefreshCanvasImageAnimations()
	{
		if (!UseCanvasRenderBackend || canvasAnimatedImageOverlayKeys.Count == 0)
			return;
		ulong nowMs = Time.GetTicksMsec();
		ulong minIntervalMs = OS.HasFeature("mobile") ? 50UL : 16UL;
		if (lastCanvasAnimationRefreshMs != 0 && nowMs - lastCanvasAnimationRefreshMs < minIntervalMs)
			return;
		lastCanvasAnimationRefreshMs = nowMs;
		for (int i = canvasAnimatedImageOverlayKeys.Count - 1; i >= 0; i--)
		{
			var key = canvasAnimatedImageOverlayKeys[i];
			if (!canvasImageOverlayNodes.TryGetValue(key.LineNo, out var overlays)
				|| overlays == null
				|| key.Index < 0
				|| key.Index >= overlays.Count)
			{
				canvasAnimatedImageOverlayKeys.RemoveAt(i);
				continue;
			}

			var overlay = overlays[key.Index];
			if (!overlay.IsAnimation)
			{
				canvasAnimatedImageOverlayKeys.RemoveAt(i);
				continue;
			}
			if (IsCanvasImageOverlayVisibleOrNear(overlay))
				UpdateCanvasImageOverlayAnimationFrame(overlay);
		}
	}

	bool IsCanvasImageOverlayVisibleOrNear(CanvasImageOverlay overlay)
	{
		var node = overlay.Node;
		if (node == null || !GodotObject.IsInstanceValid(node))
			return false;
		return node.Visible || IsCanvasOverlayRectVisible(GetCanvasImageOverlayRect(node));
	}

	void UpdateCanvasImageOverlayAnimationFrame(CanvasImageOverlay overlay)
	{
		// 动画帧可能来自不同 TextureInfo。刷新时临时复用该行的 pin 列表，
		// 让新触达的帧纹理跟随行生命周期释放，避免缓存清理回收正在显示的帧。
		var previousTexturePinCollector = activeTexturePinCollector;
		int previousRenderLineNo = activeRenderLineNo;
		if (!lineTexturePins.TryGetValue(overlay.LineNo, out var pins))
		{
			pins = new List<SpriteManager.TextureInfo>();
			lineTexturePins[overlay.LineNo] = pins;
		}
		activeTexturePinCollector = pins;
		activeRenderLineNo = overlay.LineNo;
		try
		{
			UpdateCanvasImageOverlay(overlay, overlay.LineY);
		}
		finally
		{
			activeTexturePinCollector = previousTexturePinCollector;
			activeRenderLineNo = previousRenderLineNo;
		}
	}

	sealed partial class ConsoleRenderSurface : Control
	{
		readonly EmueraContent owner;
		readonly List<ConsoleButtonHit> hitRects = new List<ConsoleButtonHit>(128);
		readonly Dictionary<int, List<int>> hitRectBuckets = new Dictionary<int, List<int>>();
		bool hitRectsDirty = true;
		int lastScrollX = int.MinValue;
		int lastScrollY = int.MinValue;
		Vector2 lastViewportSize = Vector2.Zero;
		float lastScale = -1;
		const float HitBucketHeight = 64.0f;

		struct ConsoleRenderStats
		{
			public int VisibleRows;
			public int CanvasRows;
			public int OverlayRows;
			public int DrawnParts;
			public int RebuiltHitRects;
		}

		public ConsoleRenderSurface(EmueraContent owner)
		{
			this.owner = owner;
			TextureFilter = TextureFilterEnum.Nearest;
		}

		public void MarkDirty()
		{
			hitRectsDirty = true;
			QueueRedraw();
		}

		public void SyncScrollRedraw()
		{
			if (owner?.scrollContainer == null)
				return;
			int scrollX = owner.scrollContainer.ScrollHorizontal;
			int scrollY = owner.scrollContainer.ScrollVertical;
			Vector2 viewportSize = owner.scrollContainer.Size;
			float scale = owner.contentScale;
			if (scrollX == lastScrollX && scrollY == lastScrollY && viewportSize == lastViewportSize && Mathf.IsEqualApprox(scale, lastScale))
				return;
			lastScrollX = scrollX;
			lastScrollY = scrollY;
			lastViewportSize = viewportSize;
			lastScale = scale;
			MarkDirty();
			// 滚动或缩放会改变可视区；如果上一批输出刚更新了行布局但还没刷新 overlay 坐标，
			// 先补齐行 Y 坐标再做可见性判断，避免 ColorMatrix/动画图或 div overlay 短暂停在旧位置。
			owner.FlushCanvasOverlayRowsIfNeeded();
			owner.RefreshCanvasOverlayVisibility();
		}

		public bool TryHitGlobal(Vector2 globalPosition, out ConsoleButtonHit hit)
		{
			SyncScrollRedraw();
			if (hitRectsDirty)
				RebuildHitRectsOnly();
			Vector2 local = GetGlobalTransformWithCanvas().AffineInverse() * globalPosition;
			int bucket = GetHitBucket(local.Y);
			if (hitRectBuckets.TryGetValue(bucket, out var indexes))
			{
				for (int i = indexes.Count - 1; i >= 0; i--)
				{
					int hitIndex = indexes[i];
					if (hitIndex < 0 || hitIndex >= hitRects.Count)
						continue;
					if (hitRects[hitIndex].Rect.HasPoint(local))
					{
						hit = hitRects[hitIndex];
						return !string.IsNullOrEmpty(hit.Input);
					}
				}
			}
			hit = default;
			return false;
		}

		public override void _Draw()
		{
			// 绘制和命中表重建彻底分离：滚动或系统刷新可能连续触发 _Draw，
			// 此时只需要重画可视行；按钮命中表延迟到真实点击前由 TryHitGlobal 按需重建。
			bool rebuildHits = false;
			bool sample = GenericUtils.IsPerformanceSamplingEnabled;
			long startTick = sample ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			var stats = DrawVisibleLines(true, rebuildHits);
			if (sample)
				SubmitRenderSample(startTick, true, rebuildHits, stats);
		}

		void RebuildHitRectsOnly()
		{
			owner?.FlushCanvasOverlayRowsIfNeeded();
			bool sample = GenericUtils.IsPerformanceSamplingEnabled;
			long startTick = sample ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			var stats = DrawVisibleLines(false, true);
			hitRectsDirty = false;
			if (sample)
				SubmitRenderSample(startTick, false, true, stats);
		}

		ConsoleRenderStats DrawVisibleLines(bool draw, bool rebuildHits)
		{
			var stats = new ConsoleRenderStats();
			if (rebuildHits)
			{
				hitRects.Clear();
				hitRectBuckets.Clear();
			}
			if (owner == null || owner.lineNumbers.Count == 0)
				return stats;

			var visible = owner.GetVisibleCanvasContentRange();
			if (!owner.TryGetVisibleCanvasLineLayoutRange(visible, out int firstIndex, out int lastIndex))
				return stats;
			stats.VisibleRows = lastIndex - firstIndex + 1;
			for (int i = firstIndex; i <= lastIndex; i++)
			{
				var entry = owner.lineLayoutEntries[i];
				int lineNo = entry.LineNo;
				if (owner.IsCanvasOverlayLine(lineNo))
				{
					stats.OverlayRows++;
					continue;
				}
				if (owner.lineObjects.TryGetValue(lineNo, out var line))
				{
					stats.CanvasRows++;
					bool usedCachedHits = false;
					if (rebuildHits)
						usedCachedHits = AddCachedLineHitRects(lineNo, entry.Top, ref stats);
					if (rebuildHits && !draw && usedCachedHits)
						continue;
					DrawLine(lineNo, line, entry.Top, entry.Size, draw, rebuildHits, ref stats);
				}
			}
			return stats;
		}

		void DrawLine(int lineNo, ConsoleDisplayLine line, float y, Vector2 size, bool draw, bool rebuildHits, ref ConsoleRenderStats stats)
		{
			if (line == null)
				return;
			if (draw && line.TextBackgroundColor.HasValue)
			{
				var c = line.TextBackgroundColor.Value;
				DrawRect(new Rect2(0, y, Config.DrawableWidth, size.Y), c.ToGodotColor());
			}

			if (line.Buttons == null)
				return;
			foreach (var button in line.Buttons)
			{
				if (button == null)
					continue;
				if (rebuildHits && button.IsButton)
				{
					if (owner.canvasLineButtonHits.ContainsKey(lineNo))
						continue;
					int buttonTop = owner.GetButtonTop(button);
					int buttonHeight = owner.GetButtonBottom(button, true) - buttonTop;
					if (buttonHeight <= 0)
						buttonHeight = owner.EffectiveLineHeight;
					var bounds = owner.GetButtonVisualBounds(button, buttonTop, buttonHeight, button.PointX, button.PointX);
					var hitRect = new Rect2(bounds.Position + new Vector2(0, y), bounds.Size);
					AddHitRect(new ConsoleButtonHit
					{
						Rect = hitRect,
						Input = button.Inputs,
						Generation = button.Generation,
						ContentCenter = hitRect.Position + hitRect.Size * 0.5f,
					});
					stats.RebuiltHitRects++;
				}

				if (!draw)
					continue;
				if (button.StrArray == null)
					continue;
				bool isSelecting = owner.IsCanvasButtonVisuallySelected(button);
				bool isBackLog = owner.IsContentBackLogView();
				foreach (var part in button.StrArray)
				{
					stats.DrawnParts++;
					DrawPart(part, y, 0, isSelecting, isBackLog);
				}
			}
		}

		bool AddCachedLineHitRects(int lineNo, float lineY, ref ConsoleRenderStats stats)
		{
			if (owner == null || !owner.canvasLineButtonHits.TryGetValue(lineNo, out var lineHits) || lineHits == null)
				return false;
			var offset = new Vector2(0, lineY);
			for (int i = 0; i < lineHits.Length; i++)
			{
				var hit = lineHits[i];
				hit.Rect = new Rect2(hit.Rect.Position + offset, hit.Rect.Size);
				hit.ContentCenter += offset;
				AddHitRect(hit);
				stats.RebuiltHitRects++;
			}
			return true;
		}

		void AddHitRect(ConsoleButtonHit hit)
		{
			int index = hitRects.Count;
			hitRects.Add(hit);
			int firstBucket = GetHitBucket(hit.Rect.Position.Y);
			int lastBucket = GetHitBucket(hit.Rect.Position.Y + hit.Rect.Size.Y);
			for (int bucket = firstBucket; bucket <= lastBucket; bucket++)
			{
				if (!hitRectBuckets.TryGetValue(bucket, out var indexes))
				{
					indexes = new List<int>();
					hitRectBuckets[bucket] = indexes;
				}
				indexes.Add(index);
			}
		}

		static int GetHitBucket(float y)
		{
			return Mathf.FloorToInt(y / HitBucketHeight);
		}

		void SubmitRenderSample(long startTick, bool draw, bool rebuildHits, ConsoleRenderStats stats)
		{
			long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - startTick;
			double elapsedMs = elapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			GenericUtils.SampleConsoleRenderFrame(elapsedMs, draw, rebuildHits,
				stats.VisibleRows, stats.CanvasRows, stats.OverlayRows, stats.DrawnParts, stats.RebuiltHitRects,
				BuildConsoleRenderSnapshotData);
		}

		string BuildConsoleRenderSnapshotData()
		{
			if (owner == null)
				return "";
			int fallbackRowControls = 0;
			foreach (var control in owner.lineControls.Values)
			{
				if (control != null)
					fallbackRowControls++;
			}
			int imageOverlayNodes = 0;
			foreach (var overlays in owner.canvasImageOverlayNodes.Values)
				imageOverlayNodes += overlays?.Count ?? 0;
			int divOverlayNodes = 0;
			foreach (var overlays in owner.canvasDivOverlayNodes.Values)
				divOverlayNodes += overlays?.Count ?? 0;
			return "retained_lines=" + owner.lineNumbers.Count
				+ " line_container_children=" + (owner.lineContainer?.GetChildCount() ?? 0)
				+ " fallback_row_controls=" + fallbackRowControls
				+ " positioned_rows=" + owner.canvasRowsWithPositionedNodes.Count
				+ " escaped_rows=" + owner.canvasRowsWithEscapedOverlays.Count
				+ " image_overlay_rows=" + owner.canvasImageOverlayNodes.Count
				+ " image_overlay_nodes=" + imageOverlayNodes
				+ " div_overlay_rows=" + owner.canvasDivOverlayNodes.Count
				+ " div_overlay_nodes=" + divOverlayNodes
				+ " animated_overlays=" + owner.canvasAnimatedImageOverlayKeys.Count
				+ " layout_entries=" + owner.lineLayoutEntries.Count
				+ " total_height=" + ((int)owner.totalLineHeight)
				+ " widest_line=" + ((int)owner.widestLineWidth);
		}

		void DrawPart(AConsoleDisplayPart part, float lineY, int relX, bool isSelecting, bool isBackLog)
		{
			if (part == null)
				return;
			if (part is ConsoleStyledString css)
			{
				DrawStyledString(css, lineY, relX, isSelecting, isBackLog);
				return;
			}
			if (part is ConsoleImagePart image)
			{
				DrawImagePart(image, lineY, relX);
				return;
			}
			if (part is ConsoleDivPart)
				return;
			if (part is ConsoleRectangleShapePart rectShape)
			{
				if (rectShape.Width <= 0)
					return;
				DrawRect(new Rect2(
					rectShape.PointX - relX,
					lineY + rectShape.Top,
					rectShape.Width,
					Mathf.Max(rectShape.Bottom - rectShape.Top, 1)),
					(isSelecting ? rectShape.pButtonColor : rectShape.pColor).ToGodotColor());
				return;
			}
			if (part is ConsoleErrorShapePart errShape)
			{
				DrawText(errShape.AltText ?? errShape.Str ?? "", Config.ForeColor.ToGodotColor(), false,
					part.PointX - relX, lineY, Mathf.Max(part.Width, owner.EffectiveLineHeight));
			}
		}

		void DrawStyledString(ConsoleStyledString css, float lineY, int relX, bool isSelecting, bool isBackLog)
		{
			if (css == null || string.IsNullOrEmpty(css.Str))
				return;
			float x = css.PointX - relX;
			float width;
			if (relX == 0)
			{
				float maxW = Config.DrawableWidth - css.PointX;
				width = css.Width > 0 ? Math.Min(css.Width, maxW) : maxW;
				if (width <= 0)
					width = 1;
			}
			else
			{
				width = css.Width > 0 ? css.Width : 9999;
			}
			var color = css.pColor;
			if (isSelecting)
			{
				// 原核心在按钮处于焦点/选中状态时使用 bcolor，并可为非空文字绘制灰色焦点背景。
				// Canvas 自绘不经过 ConsoleStyledString.DrawTo，必须在这里显式复刻这段颜色语义。
				if (Config.UseButtonFocusBackgroundColor && css.Width > 0 && !string.IsNullOrWhiteSpace(css.Str))
					DrawRect(new Rect2(x, lineY, Mathf.Max(1.0f, width), owner.EffectiveLineHeight), new Color(50.0f / 255.0f, 50.0f / 255.0f, 50.0f / 255.0f, 1.0f));
				color = css.pButtonColor;
			}
			else if (isBackLog && !css.pColorChanged)
				color = Config.LogColor;
			DrawText(css.Str, color.ToGodotColor(), css.Font?.Bold == true, x, lineY, width);
		}

		void DrawText(string text, Color color, bool bold, float x, float lineY, float width)
		{
			if (owner.mainFont == null || string.IsNullOrEmpty(text))
				return;
			text = uEmuera.Utils.StripZeroWidth(text) ?? "";
			if (string.IsNullOrEmpty(text))
				return;
			float fontHeight = owner.mainFont.GetHeight(owner.FontSize);
			float baseline = GetTextBaseline(owner.mainFont, owner.FontSize, owner.EffectiveLineHeight, fontHeight);
			if (!ShouldUseGridDrawing(text))
			{
				DrawString(owner.mainFont, new Vector2(x, lineY + baseline), text, HorizontalAlignment.Left,
					Mathf.Max(1.0f, width), owner.FontSize, color);
				if (bold)
					DrawString(owner.mainFont, new Vector2(x + 1.0f, lineY + baseline), text, HorizontalAlignment.Left,
						Mathf.Max(1.0f, width - 1.0f), owner.FontSize, color);
				return;
			}

			float exactX = 0.0f;
			float drawX = x;
			for (int i = 0; i < text.Length; i++)
			{
				bool half = uEmuera.Utils.CheckHalfSize(text[i]);
				float nextExactX = exactX + (half ? owner.FontSize / 2.0f : owner.FontSize);
				float nextDrawX = x + (int)nextExactX;
				float cellWidth = nextDrawX - drawX;
				// 逐字符绘制只负责保持 emuera 的半角/全角格点起点，裁剪仍由整段宽度决定。
				// 若按单元格宽度裁剪，Godot 字体 fallback 下的箱线/空白敏感字符会出现缺笔或整字丢失。
				float drawWidth = Mathf.Max(cellWidth, x + width - drawX);
				DrawGridChar(text[i], drawX, lineY, lineY + baseline, drawWidth, color, bold, fontHeight);
				exactX = nextExactX;
				drawX = nextDrawX;
			}
		}

		void DrawGridChar(char value, float x, float lineTop, float baseline, float cellWidth, Color color, bool bold, float fontHeight)
		{
			if (TryGetSolidBlockElementRect(value, cellWidth, owner.EffectiveLineHeight, fontHeight, out var blockRect))
			{
				DrawRect(new Rect2(x + blockRect.Position.X, lineTop + blockRect.Position.Y, blockRect.Size.X, blockRect.Size.Y), color);
				return;
			}
			string glyph = value.ToString();
			float drawWidth = Mathf.Max(1.0f, cellWidth);
			DrawString(owner.mainFont, new Vector2(x, baseline), glyph, HorizontalAlignment.Left, drawWidth, owner.FontSize, color);
			if (bold)
				DrawString(owner.mainFont, new Vector2(x + 1.0f, baseline), glyph, HorizontalAlignment.Left, Mathf.Max(1.0f, drawWidth - 1.0f), owner.FontSize, color);
		}

		void DrawImagePart(ConsoleImagePart image, float lineY, int relX)
		{
			if (owner.NeedsCanvasImageOverlay(image))
				return;
			if (!owner.TryResolveCanvasImage(image, relX, out var info))
				return;
			var drawSize = info.DrawSize.X > 0 && info.DrawSize.Y > 0 ? info.DrawSize : info.Size;
			var destRect = new Rect2(info.Position + new Vector2(0, lineY) + info.DrawOffset, drawSize);
			bool flip = info.FlipX || info.FlipY;
			if (flip)
			{
				var center = destRect.Position + destRect.Size / 2;
				DrawSetTransform(center, 0, new Vector2(info.FlipX ? -1 : 1, info.FlipY ? -1 : 1));
				destRect.Position = -destRect.Size / 2;
			}

			if (info.SourceRegion.Size.X > 0 && info.SourceRegion.Size.Y > 0)
				DrawTextureRectRegion(info.SourceTexture, destRect, info.SourceRegion);
			else
				DrawTextureRect(info.SourceTexture, destRect, false);

			if (flip)
				DrawSetTransform(Vector2.Zero, 0, Vector2.One);
		}

		static float GetTextBaseline(Font font, int fontSize, float height, float fontHeight = -1.0f)
		{
			if (fontHeight < 0.0f)
				fontHeight = font.GetHeight(fontSize);
			float ascent = font.GetAscent(fontSize);
			return Mathf.Round((height - fontHeight) * 0.5f + ascent);
		}

		static bool ShouldUseGridDrawing(string value)
		{
			for (int i = 0; i < value.Length; i++)
			{
				char c = value[i];
				if (uEmuera.Utils.CheckZeroWidth(c))
					continue;
				if (char.IsWhiteSpace(c) || IsGridSensitiveChar(c))
					return true;
			}
			return false;
		}

		static bool IsGridSensitiveChar(char c)
		{
			return (c >= '\u2500' && c <= '\u257F')
				|| (c >= '\u2580' && c <= '\u259F')
				|| (c >= '\u25A0' && c <= '\u25FF')
				|| (c >= '\u2800' && c <= '\u28FF')
				|| c == '\u3000';
		}
	}
}

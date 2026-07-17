using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using gEmuera.M0;
using MinorShift.Emuera.GameView;

public partial class EmueraContent
{
	const int M0MaxHitEvidence = 4096;

	public string CaptureM0SettlementFingerprint()
	{
		int canvasHitCount = 0;
		foreach (var hits in canvasLineButtonHits.Values)
			canvasHitCount += hits?.Length ?? 0;
		return string.Join("|", new[]
		{
			UseCanvasRenderBackend ? "canvas" : "controls",
			lineNumbers.Count.ToString(CultureInfo.InvariantCulture),
			lineLayoutEntries.Count.ToString(CultureInfo.InvariantCulture),
			lineControls.Count.ToString(CultureInfo.InvariantCulture),
			lineContainer == null ? "-1" : lineContainer.GetChildCount().ToString(CultureInfo.InvariantCulture),
			canvasLineButtonHits.Count.ToString(CultureInfo.InvariantCulture),
			canvasHitCount.ToString(CultureInfo.InvariantCulture),
			canvasRowsWithPositionedNodes.Count.ToString(CultureInfo.InvariantCulture),
			canvasRowsWithEscapedOverlays.Count.ToString(CultureInfo.InvariantCulture),
			canvasImageOverlayNodes.Count.ToString(CultureInfo.InvariantCulture),
			canvasDivOverlayNodes.Count.ToString(CultureInfo.InvariantCulture),
			canvasAnimatedImageOverlayKeys.Count.ToString(CultureInfo.InvariantCulture),
			totalLineHeight.ToString("R", CultureInfo.InvariantCulture),
			widestLineWidth.ToString("R", CultureInfo.InvariantCulture),
			scrollContainer == null ? "-1" : scrollContainer.ScrollHorizontal.ToString(CultureInfo.InvariantCulture),
			scrollContainer == null ? "-1" : scrollContainer.ScrollVertical.ToString(CultureInfo.InvariantCulture)
		});
	}

	LegacyDisplayObservation BuildM0LegacyDisplayObservation(string requestedBackend)
	{
		EnsureLineLayout();
		FlushCanvasOverlayRowsIfNeeded();
		var observation = new LegacyDisplayObservation
		{
			RequestedBackend = requestedBackend ?? "",
			EffectiveBackend = UseCanvasRenderBackend ? "canvas" : "controls",
			BackendEvidence = BuildM0BackendEvidence(),
			Viewport = BuildM0ViewportEvidence()
		};

		int divCount = 0;
		int nestedDivCount = 0;
		int srcCount = 0;
		int srcbCount = 0;
		int dynamicMapCount = 0;
		foreach (var line in lineObjects.Values)
		{
			if (line == null)
				continue;
			if (line.DynamicMapFunctionScoped || line.BitmapCacheEnabled)
				dynamicMapCount++;
			CollectM0FeatureCounts(line, 0, ref divCount, ref nestedDivCount, ref srcCount, ref srcbCount);
		}

		observation.FeatureCoverage.Div.SetObserved(divCount, "retained legacy ConsoleDivPart nodes", "no div reached by this replay");
		observation.FeatureCoverage.NestedDiv.SetObserved(nestedDivCount, "nested retained legacy ConsoleDivPart nodes", "no nested div reached by this replay");
		observation.FeatureCoverage.Src.SetObserved(srcCount, "retained legacy ConsoleImagePart src values", "no image src reached by this replay");
		observation.FeatureCoverage.Srcb.SetObserved(srcbCount, "retained legacy ConsoleImagePart srcb values", "no image srcb reached by this replay");
		observation.FeatureCoverage.DynamicMap.SetObserved(dynamicMapCount,
			"retained dynamic-map/bitmap-cache lines", "no dynamic-map line reached by this replay");

		CaptureM0ControlHitEvidence(observation);
		if (UseCanvasRenderBackend)
			CaptureM0CanvasHitEvidence(observation);
		observation.RefreshCoverageUncovered();
		return observation;
	}

	LegacyDisplayBackendEvidence BuildM0BackendEvidence()
	{
		int controlRows = 0;
		foreach (var control in lineControls.Values)
		{
			if (control != null && GodotObject.IsInstanceValid(control))
				controlRows++;
		}
		int imageOverlayNodes = 0;
		foreach (var overlays in canvasImageOverlayNodes.Values)
			imageOverlayNodes += overlays?.Count ?? 0;
		int divOverlayNodes = 0;
		foreach (var overlays in canvasDivOverlayNodes.Values)
			divOverlayNodes += overlays?.Count ?? 0;
		return new LegacyDisplayBackendEvidence
		{
			RetainedLines = lineNumbers.Count,
			LayoutEntries = lineLayoutEntries.Count,
			LineContainerChildren = lineContainer?.GetChildCount() ?? 0,
			ControlRowNodes = controlRows,
			CanvasRows = UseCanvasRenderBackend ? Math.Max(0, lineNumbers.Count - controlRows) : 0,
			PositionedRows = canvasRowsWithPositionedNodes.Count,
			EscapedRows = canvasRowsWithEscapedOverlays.Count,
			ImageOverlayRows = canvasImageOverlayNodes.Count,
			ImageOverlayNodes = imageOverlayNodes,
			DivOverlayRows = canvasDivOverlayNodes.Count,
			DivOverlayNodes = divOverlayNodes,
			AnimatedOverlays = canvasAnimatedImageOverlayKeys.Count,
			TotalHeight = totalLineHeight,
			WidestLine = widestLineWidth
		};
	}

	LegacyDisplayViewportEvidence BuildM0ViewportEvidence()
	{
		return new LegacyDisplayViewportEvidence
		{
			Viewport = ToM0Rect(GetViewport().GetVisibleRect()),
			SafeArea = ToM0Rect(ContentSafeRect),
			ContentViewport = ToM0Rect(scrollContainer?.GetGlobalRect() ?? new Rect2()),
			ScrollX = scrollContainer?.ScrollHorizontal ?? 0,
			ScrollY = scrollContainer?.ScrollVertical ?? 0,
			ContentScale = contentScale
		};
	}

	void CollectM0FeatureCounts(ConsoleDisplayLine line, int divDepth, ref int divCount, ref int nestedDivCount,
		ref int srcCount, ref int srcbCount)
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
				{
					if (!string.IsNullOrEmpty(image.ResourceName))
						srcCount++;
					if (!string.IsNullOrEmpty(image.ButtonResourceName))
						srcbCount++;
				}
				else if (part is ConsoleDivPart div)
				{
					divCount++;
					if (divDepth > 0)
						nestedDivCount++;
					if (div.Children == null)
						continue;
					foreach (var child in div.Children)
						CollectM0FeatureCounts(child, divDepth + 1, ref divCount, ref nestedDivCount, ref srcCount, ref srcbCount);
				}
			}
		}
	}

	void CaptureM0ControlHitEvidence(LegacyDisplayObservation observation)
	{
		if (scaledContentRoot == null || scrollContainer == null)
			return;
		var controls = new List<Control>();
		CollectRenderedButtonControls(scaledContentRoot, controls);
		Rect2 viewportRect = scrollContainer.GetGlobalRect();
		foreach (var control in controls)
		{
			if (observation.Hits.Count >= M0MaxHitEvidence)
			{
				observation.Uncovered.Add("hit-test:evidence truncated at " + M0MaxHitEvidence);
				break;
			}
			if (control == null || !GodotObject.IsInstanceValid(control) || !control.IsVisibleInTree())
				continue;
			Rect2 rect = control.GetGlobalRect();
			if (!TryGetM0RectIntersection(rect, viewportRect, out Rect2 visibleRect))
				continue;
			string value = control.GetMeta("button_input").As<string>() ?? "";
			long generation = control.HasMeta("generation") ? control.GetMeta("generation").AsInt64() : 0;
			AddM0HitProbe(observation, "control-meta", rect, visibleRect.GetCenter(), value, generation);
		}
	}

	void CaptureM0CanvasHitEvidence(LegacyDisplayObservation observation)
	{
		if (consoleRenderSurface == null || scrollContainer == null)
			return;
		Rect2 viewportRect = scrollContainer.GetGlobalRect();
		Transform2D transform = consoleRenderSurface.GetGlobalTransformWithCanvas();
		foreach (var item in canvasLineButtonHits)
		{
			if (!lineLayoutIndexByLineNo.TryGetValue(item.Key, out int layoutIndex)
				|| layoutIndex < 0 || layoutIndex >= lineLayoutEntries.Count || item.Value == null)
				continue;
			float lineTop = lineLayoutEntries[layoutIndex].Top;
			foreach (var hit in item.Value)
			{
				if (observation.Hits.Count >= M0MaxHitEvidence)
				{
					observation.Uncovered.Add("hit-test:evidence truncated at " + M0MaxHitEvidence);
					return;
				}
				Rect2 localRect = new Rect2(hit.Rect.Position + new Vector2(0, lineTop), hit.Rect.Size);
				Rect2 globalRect = TransformM0Rect(transform, localRect);
				if (!TryGetM0RectIntersection(globalRect, viewportRect, out Rect2 visibleRect))
					continue;
				AddM0HitProbe(observation, "canvas-hit-cache", globalRect, visibleRect.GetCenter(), hit.Input, hit.Generation);
			}
		}
	}

	void AddM0HitProbe(LegacyDisplayObservation observation, string source, Rect2 rect, Vector2 point,
		string expectedValue, long expectedGeneration)
	{
		bool found = TryFindConsoleButtonAtGlobalPosition(point, out _, out string actualValue, out long actualGeneration,
			out _, out _, out _);
		observation.Hits.Add(new LegacyDisplayHitEvidence
		{
			Backend = UseCanvasRenderBackend ? "canvas" : "controls",
			Source = source,
			Rect = ToM0Rect(rect),
			Value = expectedValue ?? "",
			Generation = expectedGeneration,
			Probe = new LegacyDisplayHitProbe
			{
				Point = new LegacyDisplayPoint { X = point.X, Y = point.Y },
				Hit = found,
				Value = actualValue ?? "",
				Generation = actualGeneration
			},
			Matched = found && string.Equals(expectedValue ?? "", actualValue ?? "", StringComparison.Ordinal)
				&& expectedGeneration == actualGeneration
		});
	}

	static Rect2 TransformM0Rect(Transform2D transform, Rect2 rect)
	{
		Vector2 a = transform * rect.Position;
		Vector2 b = transform * new Vector2(rect.End.X, rect.Position.Y);
		Vector2 c = transform * rect.End;
		Vector2 d = transform * new Vector2(rect.Position.X, rect.End.Y);
		float left = Mathf.Min(Mathf.Min(a.X, b.X), Mathf.Min(c.X, d.X));
		float top = Mathf.Min(Mathf.Min(a.Y, b.Y), Mathf.Min(c.Y, d.Y));
		float right = Mathf.Max(Mathf.Max(a.X, b.X), Mathf.Max(c.X, d.X));
		float bottom = Mathf.Max(Mathf.Max(a.Y, b.Y), Mathf.Max(c.Y, d.Y));
		return new Rect2(left, top, right - left, bottom - top);
	}

	static bool TryGetM0RectIntersection(Rect2 a, Rect2 b, out Rect2 intersection)
	{
		float left = Mathf.Max(a.Position.X, b.Position.X);
		float top = Mathf.Max(a.Position.Y, b.Position.Y);
		float right = Mathf.Min(a.End.X, b.End.X);
		float bottom = Mathf.Min(a.End.Y, b.End.Y);
		if (right <= left || bottom <= top)
		{
			intersection = new Rect2();
			return false;
		}
		intersection = new Rect2(left, top, right - left, bottom - top);
		return true;
	}

	static LegacyDisplayRect ToM0Rect(Rect2 rect)
	{
		return new LegacyDisplayRect
		{
			X = rect.Position.X,
			Y = rect.Position.Y,
			Width = rect.Size.X,
			Height = rect.Size.Y
		};
	}
}

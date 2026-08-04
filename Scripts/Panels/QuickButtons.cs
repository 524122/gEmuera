using Godot;
using System;
using System.Collections.Generic;

public partial class QuickButtons : CanvasLayer
{
	Control layerRoot;
	PanelContainer panel;
	ScrollContainer scroll;
	Control resizeHandle;
	VBoxContainer rowsContainer;
	HBoxContainer currentRow;
	List<Control> buttons = new List<Control>();
	Stack<Panel> buttonPool = new Stack<Panel>();
	Stack<HBoxContainer> rowPool = new Stack<HBoxContainer>();
	Dictionary<uint, StyleBoxFlat> quickButtonStyleCache = new Dictionary<uint, StyleBoxFlat>();
	Font fontFile;
	int fontSize;
	bool layoutUpdateQueued;
	int layoutBatchDepth;
	bool layoutBatchPending;
	bool layoutBatchKeepBottom;
	bool resizingWidth;
	bool scrollingByDrag;
	bool dragMoved;
	bool quickInputEnabled = true;
	bool scrollToBottomAfterLayout = true;
	bool quickInertiaActive;
	int quickScrollInteractionSerial;
	bool quickContentSizeDirty = true;
	Vector2 cachedQuickContentSize = Vector2.Zero;
	float resizeStartMouseX;
	float resizeStartWidth;
	float quickInertiaDeceleration = 900.0f;
	Vector2 dragStartPosition;
	Vector2 dragLastPosition;
	Vector2 quickScrollVelocity = Vector2.Zero;
	Vector2 quickInertiaRemainder = Vector2.Zero;
	Control dragButton;
	bool dragPointerIsTouch;
	int dragPointerIndex = -1;
	ulong quickLastDragTick;
	float userWidth = -1;
	const string SettingsPath = "user://settings.cfg";
	const string SettingsSection = "Display";
	const string QuickButtonWidthKey = "QuickButtonWidth";
	const string QuickButtonFontSizeKey = "QuickButtonFontSize";
	const int DefaultQuickButtonFontSize = 12;
	const int DefaultQuickButtonWidth = 90;
	public const int MinQuickButtonFontSize = 8;
	public const int MaxQuickButtonFontSize = 32;
	public const int MinQuickButtonWidth = 48;
	public const int MaxQuickButtonWidth = 220;
	const int QuickButtonPadding = 2;
	const int QuickButtonSpacing = 3;
	const int ResizeHandleWidth = 28;
	const int ScrollBottomTolerance = 4;
	const float DragThreshold = 10.0f;
	const float QuickInertiaMinVelocity = 80.0f;
	const float QuickInertiaFastVelocity = 4500.0f;
	const float QuickInertiaMaxVelocity = 14000.0f;
	const float QuickInertiaMinReleaseBoost = 1.2f;
	const float QuickInertiaMaxReleaseBoost = 3.0f;
	const float QuickInertiaSlowDeceleration = 1800.0f;
	const float QuickInertiaFastDeceleration = 520.0f;
	const float QuickInertiaStopVelocity = 6.0f;
	const int MaxPooledButtons = 256;
	const int MaxPooledRows = 64;
	static int configuredButtonWidth = -1;
	static int configuredFontSize = -1;

	// Component interface: the panel advertises its own show/hide state changes
	// so host scenes can react without polling. Emitted purely additively;
	// callers that never connect are unaffected. Button sizing stays driven by
	// the persisted settings (ConfiguredButtonWidth/ConfiguredFontSize), so no
	// extra export parameters are added here.
	[Signal]
	public delegate void PadShownEventHandler();

	[Signal]
	public delegate void PadHiddenEventHandler();

	public static int ConfiguredButtonWidth
	{
		get
		{
			EnsureSettingsLoaded();
			return configuredButtonWidth;
		}
		set
		{
			configuredButtonWidth = Mathf.Clamp(value, MinQuickButtonWidth, MaxQuickButtonWidth);
			SaveSetting(QuickButtonWidthKey, configuredButtonWidth);
		}
	}

	public static int ConfiguredFontSize
	{
		get
		{
			EnsureSettingsLoaded();
			return configuredFontSize;
		}
		set
		{
			configuredFontSize = Mathf.Clamp(value, MinQuickButtonFontSize, MaxQuickButtonFontSize);
			SaveSetting(QuickButtonFontSizeKey, configuredFontSize);
		}
	}

	public override void _Ready()
	{
		EnsureSettingsLoaded();
		Visible = false;
		Layer = 90;

		layerRoot = new Control();
		layerRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layerRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(layerRoot);

		panel = new PanelContainer();
		panel.AnchorLeft = 1;
		panel.AnchorTop = 1;
		panel.AnchorRight = 1;
		panel.AnchorBottom = 1;
		panel.GrowHorizontal = Control.GrowDirection.Begin;
		panel.GrowVertical = Control.GrowDirection.Begin;
		panel.OffsetRight = -20;
		panel.OffsetBottom = -20;
		panel.MouseFilter = Control.MouseFilterEnum.Stop;
		panel.ClipContents = true;
		layerRoot.AddChild(panel);

		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0, 0, 0, 0);
		panelStyle.BorderColor = new Color(0, 0, 0, 0);
		panelStyle.ContentMarginLeft = 0;
		panelStyle.ContentMarginRight = 0;
		panelStyle.ContentMarginTop = 0;
		panelStyle.ContentMarginBottom = 0;
		panel.AddThemeStyleboxOverride("panel", panelStyle);

		resizeHandle = new Control();
		resizeHandle.AnchorLeft = 1;
		resizeHandle.AnchorTop = 1;
		resizeHandle.AnchorRight = 1;
		resizeHandle.AnchorBottom = 1;
		resizeHandle.GrowHorizontal = Control.GrowDirection.Begin;
		resizeHandle.GrowVertical = Control.GrowDirection.Begin;
		resizeHandle.MouseDefaultCursorShape = Control.CursorShape.Hsize;
		resizeHandle.MouseFilter = Control.MouseFilterEnum.Stop;
		resizeHandle.GuiInput += OnResizeHandleGuiInput;
		layerRoot.AddChild(resizeHandle);

		var resizeStripe = new ColorRect();
		resizeStripe.AnchorLeft = 0.35f;
		resizeStripe.AnchorTop = 0;
		resizeStripe.AnchorRight = 0.65f;
		resizeStripe.AnchorBottom = 1;
		resizeStripe.Color = new Color(1, 1, 1, 0.28f);
		resizeStripe.MouseFilter = Control.MouseFilterEnum.Ignore;
		resizeHandle.AddChild(resizeStripe);

		scroll = new ScrollContainer();
		ConfigureQuickScrollContainer();
		scroll.MouseFilter = Control.MouseFilterEnum.Pass;
		scroll.GuiInput += inputEvent => OnQuickGuiInput(inputEvent, scroll);
		panel.AddChild(scroll);

		rowsContainer = new VBoxContainer();
		rowsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		rowsContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		rowsContainer.MouseFilter = Control.MouseFilterEnum.Pass;
		rowsContainer.GuiInput += inputEvent => OnQuickGuiInput(inputEvent, rowsContainer);
		rowsContainer.AddThemeConstantOverride("separation", QuickButtonSpacing);
		scroll.AddChild(rowsContainer);

		currentRow = AcquireRow();
		rowsContainer.AddChild(currentRow);
	}

	// Enterprise UI policy for the exported APK quick-command panel:
	// the panel must remain drag-scrollable, but the ScrollContainer bars are
	// intentionally hidden. The quick panel sits above gameplay text on phone
	// screens, so visible bars waste command space and look like tappable controls.
	// Do not enable Auto/ShowAlways unless a concrete accessibility or debugging
	// build requires visible scrollbars and that change is tested on Android.
	void ConfigureQuickScrollContainer()
	{
		if (scroll == null)
			return;

		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.ShowNever;
		HideQuickScrollBar(scroll.GetHScrollBar());
		HideQuickScrollBar(scroll.GetVScrollBar());
	}

	// Defense in depth for theme/platform updates: ShowNever is the policy, and
	// this keeps any internal ScrollBar child non-interactive and size-free if
	// Godot still creates it for range management.
	static void HideQuickScrollBar(Godot.ScrollBar scrollBar)
	{
		if (scrollBar == null)
			return;
		scrollBar.Visible = false;
		scrollBar.MouseFilter = Control.MouseFilterEnum.Ignore;
		scrollBar.CustomMinimumSize = Vector2.Zero;
	}

	Rect2 GetSafeRect()
	{
		return EmueraContent.GetSafeViewportRect(GetViewport());
	}

	float GetSafeRightInset(Rect2 safeRect)
	{
		var viewportSize = GetViewport().GetVisibleRect().Size;
		return Mathf.Max(0, viewportSize.X - (safeRect.Position.X + safeRect.Size.X));
	}

	float GetSafeBottomInset(Rect2 safeRect)
	{
		var viewportSize = GetViewport().GetVisibleRect().Size;
		return Mathf.Max(0, viewportSize.Y - (safeRect.Position.Y + safeRect.Size.Y));
	}

	public override void _Process(double delta)
	{
		ProcessQuickInertia((float)delta);
	}

	public override void _Input(InputEvent @event)
	{
		if (scrollingByDrag)
		{
			HandleQuickPointerInput(@event, false);
			return;
		}

		if (!resizingWidth)
			return;

		if (@event is InputEventMouseMotion mouseMotion)
		{
			var safeWidth = GetSafeRect().Size.X;
			var minWidth = EffectiveButtonWidth + ResizeHandleWidth;
			var maxWidth = Mathf.Max(minWidth, safeWidth - 40);
			userWidth = Mathf.Clamp(resizeStartWidth + resizeStartMouseX - mouseMotion.GlobalPosition.X, minWidth, maxWidth);
			ApplyPanelSize();
			GetViewport().SetInputAsHandled();
		}
		else if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left && !mouseButton.Pressed)
		{
			resizingWidth = false;
			GetViewport().SetInputAsHandled();
		}
	}

	void OnQuickGuiInput(InputEvent @event, Control eventSource)
	{
		if (!IsControlAlive(eventSource))
			return;
		HandleQuickPointerInput(@event, true, null, null, eventSource);
	}

	void OnResizeHandleGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			resizingWidth = mouseButton.Pressed;
			resizeStartMouseX = mouseButton.GlobalPosition.X;
			resizeStartWidth = panel != null ? panel.Size.X : EffectiveButtonWidth;
			GetViewport().SetInputAsHandled();
		}
	}

	public void Clear()
	{
		StopQuickInertia();
		scrollingByDrag = false;
		dragMoved = false;
		dragButton = null;
		dragPointerIsTouch = false;
		dragPointerIndex = -1;
		buttons.Clear();
		quickButtonStyleCache.Clear();
		var children = rowsContainer.GetChildren();
		for (int i = 0; i < children.Count; i++)
		{
			if (children[i] is HBoxContainer row)
				ReleaseRow(row);
			else
				children[i].QueueFree();
		}
		currentRow = AcquireRow();
		rowsContainer.AddChild(currentRow);
		MarkQuickContentSizeDirty();
		RequestPanelSizeUpdate(true);
	}

	public void AddButton(string text, Godot.Color color, string code, long generation)
	{
		var btn = AcquireButton();
		ConfigureButton(btn, text, color, code, generation);
		currentRow.AddChild(btn);
		buttons.Add(btn);
		MarkQuickContentSizeDirty();
		RequestPanelSizeUpdate(ShouldStickToBottom());
	}

	public void UpdateButtonGeneration(long generation)
	{
		for (int i = 0; i < buttons.Count; i++)
		{
			var btn = buttons[i];
			if (IsControlAlive(btn))
				btn.SetMeta("input_generation", generation);
		}
	}

	public void BeginBatch()
	{
		layoutBatchDepth++;
	}

	public void EndBatch()
	{
		if (layoutBatchDepth <= 0)
			return;

		layoutBatchDepth--;
		if (layoutBatchDepth > 0)
			return;

		bool pending = layoutBatchPending;
		bool keepBottom = layoutBatchKeepBottom;
		layoutBatchPending = false;
		layoutBatchKeepBottom = false;
		if (pending)
			UpdatePanelSize(keepBottom);
	}

	Panel AcquireButton()
	{
		while (buttonPool.Count > 0)
		{
			var pooled = buttonPool.Pop();
			if (IsControlAlive(pooled))
				return pooled;
		}

		var btn = new Panel();
		btn.FocusMode = Control.FocusModeEnum.None;
		btn.MouseForcePassScrollEvents = false;
		btn.GuiInput += inputEvent => OnQuickButtonGuiInput(inputEvent, btn);

		var label = new Label();
		label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		label.OffsetLeft = QuickButtonPadding;
		label.OffsetTop = QuickButtonPadding;
		label.OffsetRight = -QuickButtonPadding;
		label.OffsetBottom = -QuickButtonPadding;
		label.MouseFilter = Control.MouseFilterEnum.Ignore;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		label.VerticalAlignment = VerticalAlignment.Center;
		btn.AddChild(label);
		return btn;
	}

	void ConfigureButton(Panel btn, string text, Godot.Color color, string code, long generation)
	{
		btn.MouseFilter = quickInputEnabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
		StyleQuickButton(btn, color);
		btn.Modulate = quickInputEnabled ? Colors.White : new Color(1, 1, 1, 0.55f);
		btn.CustomMinimumSize = new Vector2(EffectiveButtonWidth, QuickButtonHeight);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		btn.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;

		var label = GetButtonLabel(btn);
		if (label != null)
		{
			label.Text = (text ?? "").Trim();
			label.AddThemeColorOverride("font_color", color);
			if (fontFile != null)
				label.AddThemeFontOverride("font", fontFile);
			label.AddThemeFontSizeOverride("font_size", EffectiveFontSize);
		}

		string inputCode = code;
		btn.SetMeta("input_code", inputCode);
		btn.SetMeta("input_generation", generation);
	}

	void OnQuickButtonGuiInput(InputEvent @event, Control btn)
	{
		if (!IsControlAlive(btn))
			return;
		HandleQuickPointerInput(@event, true, btn, null, btn);
	}

	HBoxContainer AcquireRow()
	{
		while (rowPool.Count > 0)
		{
			var row = rowPool.Pop();
			if (IsControlAlive(row))
				return row;
		}
		var fresh = new HBoxContainer();
		fresh.AddThemeConstantOverride("separation", QuickButtonSpacing);
		return fresh;
	}

	void ReleaseRow(HBoxContainer row)
	{
		if (!IsControlAlive(row))
			return;

		var children = row.GetChildren();
		for (int i = 0; i < children.Count; i++)
		{
			if (children[i] is Panel button)
				ReleaseButton(button);
			else
				children[i].QueueFree();
		}

		if (row.GetParent() != null)
			row.GetParent().RemoveChild(row);
		if (rowPool.Count < MaxPooledRows)
			rowPool.Push(row);
		else
			row.QueueFree();
	}

	void ReleaseButton(Panel btn)
	{
		if (!IsControlAlive(btn))
			return;

		if (btn.GetParent() != null)
			btn.GetParent().RemoveChild(btn);
		ResetButtonForPool(btn);
		if (buttonPool.Count < MaxPooledButtons)
			buttonPool.Push(btn);
		else
			btn.QueueFree();
	}

	void ResetButtonForPool(Panel btn)
	{
		// The quick panel is rebuilt often when the emulator prints a new command
		// generation. Pooling keeps Godot nodes and signal connections stable while
		// clearing all script-visible state before the next reuse.
		btn.Visible = true;
		btn.Modulate = Colors.White;
		btn.MouseFilter = Control.MouseFilterEnum.Ignore;
		btn.SetMeta("input_code", "");
		btn.SetMeta("input_generation", -1L);
		var label = GetButtonLabel(btn);
		if (label != null)
			label.Text = "";
	}

	bool HandleQuickPointerInput(InputEvent @event, bool acceptEvent, Control button = null, string inputCode = null, Control eventSource = null)
	{
		if (TryGetPointer(@event, out var position, out var pressed, out var released, out var motion, out var isTouch, out var pointerIndex))
		{
			if (!IsControlAlive(scroll) || !IsControlAlive(panel) || (!scrollingByDrag && !panel.GetGlobalRect().HasPoint(position)))
				return false;

			if (scrollingByDrag && motion && !IsActivePointer(isTouch, pointerIndex))
			{
				GetViewport().SetInputAsHandled();
				return true;
			}

			if (pressed && scrollingByDrag)
			{
				if (!IsActivePointer(isTouch, pointerIndex))
				{
					GetViewport().SetInputAsHandled();
					return true;
				}
				AcceptQuickInput(acceptEvent, eventSource);
				return true;
			}

			if (pressed)
			{
				StopQuickInertia();
				scrollingByDrag = true;
				dragMoved = false;
				quickScrollInteractionSerial++;
				scrollToBottomAfterLayout = false;
				dragButton = button;
				dragPointerIsTouch = isTouch;
				dragPointerIndex = pointerIndex;
				dragStartPosition = position;
				dragLastPosition = position;
				quickLastDragTick = Time.GetTicksMsec();
				if (button != null)
				{
					AcceptQuickInput(acceptEvent, eventSource);
					return true;
				}
				return false;
			}

			if (!scrollingByDrag)
				return false;

			if (motion)
			{
				var totalDelta = position - dragStartPosition;
				if (!dragMoved && totalDelta.Length() >= DragThreshold)
				{
					dragMoved = true;
					quickScrollInteractionSerial++;
				}
				if (dragMoved && scroll != null)
				{
					var rawScrollDelta = dragLastPosition - position;
					var appliedDelta = ScrollQuickBy(rawScrollDelta);
					UpdateQuickScrollVelocity(rawScrollDelta, appliedDelta);
					dragLastPosition = position;
					AcceptQuickInput(acceptEvent, eventSource);
					return true;
				}
				dragLastPosition = position;
				return false;
			}

			if (released)
			{
				bool handled = FinishQuickButtonPointer();
				if (handled)
					AcceptQuickInput(acceptEvent, eventSource);
				return handled;
			}
		}
		return false;
	}

	void AcceptQuickInput(bool acceptEvent, Control eventSource)
	{
		GetViewport().SetInputAsHandled();
	}

	bool FinishQuickButtonPointer()
	{
		if (!scrollingByDrag)
			return false;

		bool handled = false;
		string inputCode = null;
		Control activeButton = IsControlAlive(dragButton) ? dragButton : null;
		if (TryGetStringMeta(activeButton, "input_code", out var storedInputCode))
			inputCode = storedInputCode;
		if (!dragMoved && !string.IsNullOrEmpty(inputCode))
		{
			handled = true;
			if (quickInputEnabled)
			{
				long generation = 0;
				TryGetInt64Meta(activeButton, "input_generation", out generation);
				EmueraContent.instance?.SubmitQuickButtonInput(inputCode, generation);
			}
		}
		else if (dragMoved)
		{
			StartQuickInertia();
			handled = true;
		}
		scrollingByDrag = false;
		dragMoved = false;
		dragButton = null;
		dragPointerIsTouch = false;
		dragPointerIndex = -1;
		return handled;
	}

	bool IsActivePointer(bool isTouch, int pointerIndex)
	{
		if (dragPointerIsTouch != isTouch)
			return false;
		return !isTouch || dragPointerIndex == pointerIndex;
	}

	static bool TryGetPointer(InputEvent @event, out Vector2 position, out bool pressed, out bool released, out bool motion, out bool isTouch, out int pointerIndex)
	{
		position = Vector2.Zero;
		pressed = false;
		released = false;
		motion = false;
		isTouch = false;
		pointerIndex = -1;

		if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
		{
			position = mouseButton.GlobalPosition;
			pressed = mouseButton.Pressed;
			released = !mouseButton.Pressed;
			return true;
		}
		if (@event is InputEventMouseMotion mouseMotion && (mouseMotion.ButtonMask & MouseButtonMask.Left) != 0)
		{
			position = mouseMotion.GlobalPosition;
			motion = true;
			return true;
		}
		if (@event is InputEventScreenTouch touch)
		{
			position = touch.Position;
			pressed = touch.Pressed;
			released = !touch.Pressed;
			isTouch = true;
			pointerIndex = touch.Index;
			return true;
		}
		if (@event is InputEventScreenDrag drag)
		{
			position = drag.Position;
			motion = true;
			isTouch = true;
			pointerIndex = drag.Index;
			return true;
		}
		return false;
	}

	public void ShiftLine()
	{
			if (currentRow.GetChildCount() == 0)
			return;
		currentRow = AcquireRow();
		rowsContainer.AddChild(currentRow);
		MarkQuickContentSizeDirty();
		RequestPanelSizeUpdate(ShouldStickToBottom());
	}

	public void ShowPad()
	{
		Visible = true;
		EmitSignal(SignalName.PadShown);
	}

	public void HidePad()
	{
		Visible = false;
		EmitSignal(SignalName.PadHidden);
	}

	public bool IsShow => Visible;

	public void ApplyFont(Font font, int fontSize)
	{
		fontFile = font;
		this.fontSize = ConfiguredFontSize;
		for (int i = buttons.Count - 1; i >= 0; i--)
		{
			var btn = buttons[i];
			if (!IsControlAlive(btn))
			{
				buttons.RemoveAt(i);
				continue;
			}
			ApplyButtonMetrics(btn);
		}
		MarkQuickContentSizeDirty();
		RequestPanelSizeUpdate(ShouldStickToBottom());
	}

	public void RefreshSizing()
	{
		this.fontSize = ConfiguredFontSize;
		for (int i = buttons.Count - 1; i >= 0; i--)
		{
			var btn = buttons[i];
			if (!IsControlAlive(btn))
			{
				buttons.RemoveAt(i);
				continue;
			}
			ApplyButtonMetrics(btn);
		}
		if (userWidth > 0)
		{
			float minWidth = EffectiveButtonWidth + ResizeHandleWidth;
			userWidth = Mathf.Clamp(userWidth, minWidth, Mathf.Max(minWidth, GetSafeRect().Size.X - 40));
		}
		MarkQuickContentSizeDirty();
		RequestPanelSizeUpdate(ShouldStickToBottom());
	}

	public void RefreshSafeAreaLayout()
	{
		RefreshSizing();
	}

	public void SetInputEnabled(bool enabled)
	{
		if (quickInputEnabled == enabled)
			return;
		quickInputEnabled = enabled;
		if (panel != null)
			panel.MouseFilter = enabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
		if (resizeHandle != null)
			resizeHandle.MouseFilter = enabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
		if (scroll != null)
			scroll.MouseFilter = enabled ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
		if (rowsContainer != null)
			rowsContainer.MouseFilter = enabled ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
		for (int i = buttons.Count - 1; i >= 0; i--)
		{
			var btn = buttons[i];
			if (!IsControlAlive(btn))
			{
				buttons.RemoveAt(i);
				continue;
			}
			btn.MouseFilter = enabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
			btn.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.55f);
		}
	}

	int EffectiveFontSize => fontSize > 0 ? fontSize : ConfiguredFontSize;

	int EffectiveButtonWidth => ConfiguredButtonWidth;

	int QuickButtonHeight => EffectiveFontSize * 3 + QuickButtonPadding * 2;

	void StyleQuickButton(Panel btn, Color textColor)
	{
		var bgSource = IsMidGray(textColor)
			? new Color(MinorShift.Emuera.Config.BackColor.r, MinorShift.Emuera.Config.BackColor.g, MinorShift.Emuera.Config.BackColor.b, 1)
			: textColor;
		var bgColor = new Color(1 - bgSource.R, 1 - bgSource.G, 1 - bgSource.B, 0.75f);

		uint key = ColorCacheKey(bgColor);
		if (!quickButtonStyleCache.TryGetValue(key, out var normal))
		{
			normal = new StyleBoxFlat();
			normal.BgColor = bgColor;
			normal.CornerRadiusTopLeft = normal.CornerRadiusTopRight = 3;
			normal.CornerRadiusBottomLeft = normal.CornerRadiusBottomRight = 3;
			normal.ContentMarginLeft = QuickButtonPadding;
			normal.ContentMarginRight = QuickButtonPadding;
			normal.ContentMarginTop = QuickButtonPadding;
			normal.ContentMarginBottom = QuickButtonPadding;
			quickButtonStyleCache[key] = normal;
		}

		btn.AddThemeStyleboxOverride("panel", normal);
	}

	static uint ColorCacheKey(Color color)
	{
		uint r = (uint)Mathf.Clamp(Mathf.RoundToInt(color.R * 255.0f), 0, 255);
		uint g = (uint)Mathf.Clamp(Mathf.RoundToInt(color.G * 255.0f), 0, 255);
		uint b = (uint)Mathf.Clamp(Mathf.RoundToInt(color.B * 255.0f), 0, 255);
		uint a = (uint)Mathf.Clamp(Mathf.RoundToInt(color.A * 255.0f), 0, 255);
		return r | (g << 8) | (b << 16) | (a << 24);
	}

	void ApplyFontToButtonLabel(Control btn)
	{
		var label = GetButtonLabel(btn);
		if (label == null)
			return;
		if (fontFile != null)
			label.AddThemeFontOverride("font", fontFile);
		label.AddThemeFontSizeOverride("font_size", EffectiveFontSize);
	}

	Label GetButtonLabel(Control btn)
	{
		if (btn == null)
			return null;
		var children = btn.GetChildren();
		for (int i = 0; i < children.Count; i++)
		{
			if (children[i] is Label label)
				return label;
		}
		return null;
	}

	void ApplyButtonMetrics(Control btn)
	{
		if (btn == null)
			return;
		btn.CustomMinimumSize = new Vector2(EffectiveButtonWidth, QuickButtonHeight);
		btn.Size = new Vector2(EffectiveButtonWidth, QuickButtonHeight);
		ApplyFontToButtonLabel(btn);
	}

	bool IsMidGray(Color color)
	{
		return Mathf.Abs(color.R - 0.5f) <= 0.063f
			&& Mathf.Abs(color.G - 0.5f) <= 0.063f
			&& Mathf.Abs(color.B - 0.5f) <= 0.063f;
	}

	void RequestPanelSizeUpdate(bool keepBottom)
	{
		if (layoutBatchDepth > 0)
		{
			layoutBatchPending = true;
			layoutBatchKeepBottom = layoutBatchKeepBottom || keepBottom;
			return;
		}

		UpdatePanelSize(keepBottom);
	}

	async void UpdatePanelSize(bool keepBottom)
	{
		if (panel == null || scroll == null || rowsContainer == null)
			return;
		scrollToBottomAfterLayout = !scrollingByDrag && !quickInertiaActive && (scrollToBottomAfterLayout || keepBottom);
		if (layoutUpdateQueued)
			return;

		layoutUpdateQueued = true;
		int autoScrollInteractionSerial = quickScrollInteractionSerial;
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (!IsControlAlive(panel) || !IsControlAlive(scroll) || !IsControlAlive(rowsContainer))
		{
			layoutUpdateQueued = false;
			return;
		}
		layoutUpdateQueued = false;
		var safeSize = GetSafeRect().Size;
		var contentSize = GetQuickContentSize();
		float minWidth = EffectiveButtonWidth;
		float maxWidth = Mathf.Max(minWidth, safeSize.X * 0.6f);
		float maxHeight = Mathf.Max(QuickButtonHeight, safeSize.Y - 66);
		float autoWidth = Mathf.Min(Mathf.Max(contentSize.X, minWidth), maxWidth);
		float width = userWidth > 0
			? Mathf.Clamp(userWidth, minWidth, Mathf.Max(minWidth, safeSize.X - 40))
			: autoWidth;
		float height = Mathf.Min(contentSize.Y, maxHeight);
		ApplyPanelSize(width, height);
		if (scrollToBottomAfterLayout && !scrollingByDrag && !quickInertiaActive && autoScrollInteractionSerial == quickScrollInteractionSerial)
			ScrollQuickToBottom();
		scrollToBottomAfterLayout = false;
	}

	void ApplyPanelSize()
	{
		if (panel == null || rowsContainer == null)
			return;

		var safeSize = GetSafeRect().Size;
		var contentSize = GetQuickContentSize();
		float maxHeight = Mathf.Max(QuickButtonHeight, safeSize.Y - 66);
		float minWidth = EffectiveButtonWidth;
		float maxWidth = Mathf.Max(minWidth, safeSize.X * 0.6f);
		float width = userWidth > 0
			? Mathf.Clamp(userWidth, minWidth, Mathf.Max(minWidth, safeSize.X - 40))
			: Mathf.Min(Mathf.Max(contentSize.X, minWidth), maxWidth);
		float height = Mathf.Min(contentSize.Y, maxHeight);
		ApplyPanelSize(width, height);
	}

	void ApplyPanelSize(float width, float height)
	{
		if (!IsControlAlive(panel) || !IsControlAlive(resizeHandle))
			return;
		var safeRect = GetSafeRect();
		float rightMargin = 20 + GetSafeRightInset(safeRect);
		float bottomMargin = 20 + GetSafeBottomInset(safeRect);
		panel.OffsetLeft = -rightMargin - width;
		panel.OffsetRight = -rightMargin;
		panel.OffsetTop = -bottomMargin - height;
		panel.OffsetBottom = -bottomMargin;
		resizeHandle.OffsetLeft = -rightMargin - width - ResizeHandleWidth;
		resizeHandle.OffsetRight = -rightMargin - width;
		resizeHandle.OffsetTop = -bottomMargin - height;
		resizeHandle.OffsetBottom = -bottomMargin;
	}

	Vector2 ScrollQuickBy(Vector2 delta)
	{
		if (scroll == null)
			return Vector2.Zero;
		delta *= EmueraContent.ButtonDragSensitivity;
		return ApplyQuickScrollDelta(delta);
	}

	Vector2 ApplyQuickScrollDelta(Vector2 delta)
	{
		if (scroll == null)
			return Vector2.Zero;
		int oldHorizontal = scroll.ScrollHorizontal;
		int oldVertical = scroll.ScrollVertical;
		int nextHorizontal = scroll.ScrollHorizontal + Mathf.RoundToInt(delta.X);
		int nextVertical = scroll.ScrollVertical + Mathf.RoundToInt(delta.Y);
		scroll.ScrollHorizontal = Mathf.Clamp(nextHorizontal, 0, GetMaxHorizontalScroll());
		scroll.ScrollVertical = Mathf.Clamp(nextVertical, 0, GetMaxVerticalScroll());
		return new Vector2(scroll.ScrollHorizontal - oldHorizontal, scroll.ScrollVertical - oldVertical);
	}

	void ScrollQuickToBottom()
	{
		if (scroll == null)
			return;
		scroll.ScrollVertical = GetMaxVerticalScroll();
	}

	bool ShouldStickToBottom()
	{
		if (scroll == null || scrollingByDrag || quickInertiaActive)
			return false;
		return scroll.ScrollVertical >= GetMaxVerticalScroll() - ScrollBottomTolerance;
	}

	void UpdateQuickScrollVelocity(Vector2 rawScrollDelta, Vector2 appliedDelta)
	{
		ulong now = Time.GetTicksMsec();
		if (quickLastDragTick == 0)
		{
			quickLastDragTick = now;
			return;
		}

		if (rawScrollDelta.LengthSquared() <= 0.01f)
			return;

		float elapsed = Mathf.Max((now - quickLastDragTick) / 1000.0f, 1.0f / 120.0f);
		if (appliedDelta.LengthSquared() <= 0.01f)
		{
			quickScrollVelocity = Vector2.Zero;
			quickLastDragTick = now;
			return;
		}

		var instantVelocity = rawScrollDelta * EmueraContent.ButtonDragSensitivity / elapsed;
		if (instantVelocity.Length() > QuickInertiaMaxVelocity)
			instantVelocity = instantVelocity.Normalized() * QuickInertiaMaxVelocity;

		quickScrollVelocity = quickScrollVelocity.Lerp(instantVelocity, 0.78f);
		quickLastDragTick = now;
	}

	void StartQuickInertia()
	{
		float releaseSpeed = quickScrollVelocity.Length();
		float fastRatio = Mathf.Clamp(
			(releaseSpeed - QuickInertiaMinVelocity) / (QuickInertiaFastVelocity - QuickInertiaMinVelocity),
			0.0f,
			1.0f);
		float releaseBoost = Mathf.Lerp(QuickInertiaMinReleaseBoost, QuickInertiaMaxReleaseBoost, fastRatio);
		quickInertiaDeceleration = Mathf.Lerp(QuickInertiaSlowDeceleration, QuickInertiaFastDeceleration, fastRatio);
		quickScrollVelocity *= releaseBoost;
		if (quickScrollVelocity.Length() > QuickInertiaMaxVelocity)
			quickScrollVelocity = quickScrollVelocity.Normalized() * QuickInertiaMaxVelocity;
		if (quickScrollVelocity.Length() >= QuickInertiaMinVelocity)
			quickInertiaActive = true;
		else
			StopQuickInertia();
	}

	void StopQuickInertia()
	{
		quickInertiaActive = false;
		quickScrollVelocity = Vector2.Zero;
		quickInertiaRemainder = Vector2.Zero;
		quickLastDragTick = 0;
	}

	void ProcessQuickInertia(float delta)
	{
		if (!quickInertiaActive || scrollingByDrag || scroll == null)
			return;

		var desiredDelta = quickScrollVelocity * delta + quickInertiaRemainder;
		var roundedDelta = new Vector2(Mathf.Round(desiredDelta.X), Mathf.Round(desiredDelta.Y));
		quickInertiaRemainder = desiredDelta - roundedDelta;
		if (roundedDelta.LengthSquared() > 0.01f)
		{
			var appliedDelta = ApplyQuickScrollDelta(roundedDelta);
			if (appliedDelta.LengthSquared() <= 0.01f)
			{
				StopQuickInertia();
				return;
			}
		}

		float speed = quickScrollVelocity.Length();
		speed = Mathf.MoveToward(speed, 0, quickInertiaDeceleration * delta);
		if (speed <= QuickInertiaStopVelocity)
		{
			StopQuickInertia();
			return;
		}
		quickScrollVelocity = quickScrollVelocity.Normalized() * speed;
	}

	int GetMaxHorizontalScroll()
	{
		if (scroll == null || rowsContainer == null)
			return 0;
		float contentWidth = Mathf.Max(rowsContainer.Size.X, GetQuickContentSize().X);
		return Mathf.RoundToInt(Mathf.Max(0, contentWidth - scroll.Size.X));
	}

	int GetMaxVerticalScroll()
	{
		if (scroll == null || rowsContainer == null)
			return 0;
		float contentHeight = Mathf.Max(rowsContainer.Size.Y, GetQuickContentSize().Y);
		return Mathf.RoundToInt(Mathf.Max(0, contentHeight - scroll.Size.Y));
	}

	void MarkQuickContentSizeDirty()
	{
		quickContentSizeDirty = true;
	}

	Vector2 GetQuickContentSize()
	{
		if (rowsContainer == null)
			return Vector2.Zero;
		if (quickContentSizeDirty)
		{
			cachedQuickContentSize = rowsContainer.GetCombinedMinimumSize();
			quickContentSizeDirty = false;
		}
		return cachedQuickContentSize;
	}

	static void EnsureSettingsLoaded()
	{
		if (configuredButtonWidth > 0 && configuredFontSize > 0)
			return;
		var cfg = new ConfigFile();
		cfg.Load(SettingsPath);
		configuredButtonWidth = Mathf.Clamp(
			Mathf.RoundToInt((float)(double)cfg.GetValue(SettingsSection, QuickButtonWidthKey, DefaultQuickButtonWidth)),
			MinQuickButtonWidth,
			MaxQuickButtonWidth);
		configuredFontSize = Mathf.Clamp(
			Mathf.RoundToInt((float)(double)cfg.GetValue(SettingsSection, QuickButtonFontSizeKey, DefaultQuickButtonFontSize)),
			MinQuickButtonFontSize,
			MaxQuickButtonFontSize);
	}

	static void SaveSetting(string key, int value)
	{
		var cfg = new ConfigFile();
		cfg.Load(SettingsPath);
		cfg.SetValue(SettingsSection, key, value);
		cfg.Save(SettingsPath);
	}

	static bool IsControlAlive(Control control)
	{
		if (control == null)
			return false;
		try
		{
			return GodotObject.IsInstanceValid(control) && !control.IsQueuedForDeletion();
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	static bool TryGetStringMeta(Control control, string key, out string value)
	{
		value = null;
		if (!IsControlAlive(control))
			return false;
		try
		{
			if (!control.HasMeta(key))
				return false;
			value = control.GetMeta(key).As<string>();
			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	static bool TryGetInt64Meta(Control control, string key, out long value)
	{
		value = 0;
		if (!IsControlAlive(control))
			return false;
		try
		{
			if (!control.HasMeta(key))
				return false;
			value = control.GetMeta(key).AsInt64();
			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	static bool TryGetBoolMeta(Control control, string key, out bool value)
	{
		value = false;
		if (!IsControlAlive(control))
			return false;
		try
		{
			if (!control.HasMeta(key))
				return false;
			value = control.GetMeta(key).AsBool();
			return true;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}
}

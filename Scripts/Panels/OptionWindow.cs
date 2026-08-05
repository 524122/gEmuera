using Godot;
using System.Collections.Generic;

public partial class OptionWindow : Control
{
	PopupPanel popup;
	Slider fontSizeSlider;
	Label fontSizeLabel;
	Slider quickButtonWidthSlider;
	Label quickButtonWidthLabel;
	Slider quickFontSizeSlider;
	Label quickFontSizeLabel;
	Slider buttonDragSensitivitySlider;
	Label buttonDragSensitivityLabel;
	CheckButton pinchZoomToggle;
	Slider maxVisibleLinesSlider;
	Label maxVisibleLinesLabel;
	OptionButton resolutionOption;
	OptionButton languageOption;
	OptionButton frameRateOption;

	// MultiLanguage-driven description labels (values are slider-derived numbers
	// and stay untouched); refreshed together when the language changes.
	Label fontLabel;
	Label quickWidthLabel;
	Label quickFontLabel;
	Label sensitivityLabel;
	Label pinchZoomLabel;
	Label maxLinesTextLabel;
	Label resLabel;
	Label fpsLabel;
	Label langLabel;
	Button closeBtn;

	// Group section titles (Display / Quick / Input / System), also
	// MultiLanguage-driven and refreshed by ApplyLanguageTexts.
	Label displayGroupTitle;
	Label quickGroupTitle;
	Label inputGroupTitle;
	Label systemGroupTitle;

	static readonly string[] languages = new string[] { "default", "zh_cn", "en_us", "jp" };
	static readonly string[] languageNames = new string[] { "Default", "简体中文", "English", "日本語" };

	// Component interface: the popup advertises its open/close state so host
	// scenes can react without polling. Emitted purely additively; callers that
	// never connect are unaffected.
	[Signal]
	public delegate void PopupOpenedEventHandler();

	[Signal]
	public delegate void PopupClosedEventHandler();

	// Exported so the dialog size is reusable/tunable per scene instance.
	[Export]
	public Vector2I PopupSize = new Vector2I(520, 640);

	public override void _Ready()
	{
		popup = new PopupPanel();
		popup.Size = PopupSize;
		popup.PopupHide += () => EmitSignal(SignalName.PopupClosed);
		GEmueraTheme.ApplyPopup(popup);
		AddChild(popup);

		// Inner padding. PopupPanel's "panel" stylebox is drawn visually only —
		// child layout starts at the window origin in Godot 4 — so the content
		// inset comes from an explicit MarginContainer (16px, deep-modern spec).
		var margin = new MarginContainer();
		margin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		margin.SizeFlagsVertical = SizeFlags.ExpandFill;
		SetMargins(margin, 16);
		popup.AddChild(margin);

		var rootVBox = new VBoxContainer();
		rootVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rootVBox.SizeFlagsVertical = SizeFlags.ExpandFill;
		rootVBox.AddThemeConstantOverride("separation", 12);
		margin.AddChild(rootVBox);

		// Scrollable settings body: the grouped rows scroll on small screens /
		// large fonts, while the Close footer stays pinned at the bottom.
		var scroll = new ScrollContainer();
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		rootVBox.AddChild(scroll);

		var content = new VBoxContainer();
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		content.AddThemeConstantOverride("separation", 16); // 组间距
		scroll.AddChild(content);

		// ---- Display: 字体大小 / 最大行数 / 分辨率 / 帧率 ----
		var displayGroup = CreateGroup(content, out displayGroupTitle);

		var fontRow = CreateRow(displayGroup, out fontLabel);
		fontSizeSlider = new HSlider();
		fontSizeSlider.MinValue = 8;
		fontSizeSlider.MaxValue = 48;
		fontSizeSlider.Value = MinorShift.Emuera.Config.FontSize > 0 ? MinorShift.Emuera.Config.FontSize : 18;
		fontSizeSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		fontSizeSlider.CustomMinimumSize = new Vector2(120, 44);
		fontSizeSlider.Editable = false;
		fontSizeSlider.ValueChanged += OnFontSizeChanged;
		fontRow.AddChild(fontSizeSlider);
		fontSizeLabel = CreateValueLabel();
		fontSizeLabel.Text = fontSizeSlider.Value.ToString();
		fontRow.AddChild(fontSizeLabel);

		var maxLinesRow = CreateRow(displayGroup, out maxLinesTextLabel);
		maxVisibleLinesSlider = new HSlider();
		maxVisibleLinesSlider.MinValue = EmueraContent.MinMaxVisibleLines;
		maxVisibleLinesSlider.MaxValue = EmueraContent.MaxMaxVisibleLines;
		maxVisibleLinesSlider.Step = 20;
		maxVisibleLinesSlider.Value = EmueraContent.ConfiguredMaxVisibleLines;
		maxVisibleLinesSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		maxVisibleLinesSlider.CustomMinimumSize = new Vector2(120, 44);
		maxVisibleLinesSlider.ValueChanged += OnMaxVisibleLinesChanged;
		maxLinesRow.AddChild(maxVisibleLinesSlider);
		maxVisibleLinesLabel = CreateValueLabel();
		maxVisibleLinesLabel.Text = ((int)maxVisibleLinesSlider.Value).ToString();
		maxLinesRow.AddChild(maxVisibleLinesLabel);

		var resRow = CreateRow(displayGroup, out resLabel);
		resolutionOption = new OptionButton();
		ResolutionHelper.RefreshResolutions();
		for (int i = 0; i < ResolutionHelper.resolutions.Count; i++)
		{
			resolutionOption.AddItem(ResolutionHelper.resolutions[i] + "p", i);
		}
		if (ResolutionHelper.resolution_index >= 0 && ResolutionHelper.resolution_index < ResolutionHelper.resolutions.Count)
			resolutionOption.Select(ResolutionHelper.resolution_index);
		resolutionOption.ItemSelected += OnResolutionSelected;
		resolutionOption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		resRow.AddChild(resolutionOption);

		var fpsRow = CreateRow(displayGroup, out fpsLabel);
		frameRateOption = new OptionButton();
		for (int i = 0; i < FrameRateHelper.FrameRates.Count; i++)
		{
			frameRateOption.AddItem(FrameRateHelper.FrameRates[i] + " FPS", i);
		}
		frameRateOption.Select(FrameRateHelper.frame_rate_index);
		frameRateOption.ItemSelected += OnFrameRateSelected;
		frameRateOption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		fpsRow.AddChild(frameRateOption);

		// ---- Quick: 快捷按钮宽度 / 快捷字体 ----
		var quickGroup = CreateGroup(content, out quickGroupTitle);

		var quickWidthRow = CreateRow(quickGroup, out quickWidthLabel);
		quickButtonWidthSlider = new HSlider();
		quickButtonWidthSlider.MinValue = QuickButtons.MinQuickButtonWidth;
		quickButtonWidthSlider.MaxValue = QuickButtons.MaxQuickButtonWidth;
		quickButtonWidthSlider.Step = 1;
		quickButtonWidthSlider.Value = QuickButtons.ConfiguredButtonWidth;
		quickButtonWidthSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		quickButtonWidthSlider.CustomMinimumSize = new Vector2(120, 44);
		quickButtonWidthSlider.ValueChanged += OnQuickButtonWidthChanged;
		quickWidthRow.AddChild(quickButtonWidthSlider);
		quickButtonWidthLabel = CreateValueLabel();
		quickButtonWidthLabel.Text = quickButtonWidthSlider.Value.ToString();
		quickWidthRow.AddChild(quickButtonWidthLabel);

		var quickFontRow = CreateRow(quickGroup, out quickFontLabel);
		quickFontSizeSlider = new HSlider();
		quickFontSizeSlider.MinValue = QuickButtons.MinQuickButtonFontSize;
		quickFontSizeSlider.MaxValue = QuickButtons.MaxQuickButtonFontSize;
		quickFontSizeSlider.Step = 1;
		quickFontSizeSlider.Value = QuickButtons.ConfiguredFontSize;
		quickFontSizeSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		quickFontSizeSlider.CustomMinimumSize = new Vector2(120, 44);
		quickFontSizeSlider.ValueChanged += OnQuickFontSizeChanged;
		quickFontRow.AddChild(quickFontSizeSlider);
		quickFontSizeLabel = CreateValueLabel();
		quickFontSizeLabel.Text = quickFontSizeSlider.Value.ToString();
		quickFontRow.AddChild(quickFontSizeLabel);

		// ---- Input: 滚动灵敏度 / 双指缩放 ----
		var inputGroup = CreateGroup(content, out inputGroupTitle);

		var sensitivityRow = CreateRow(inputGroup, out sensitivityLabel);
		buttonDragSensitivitySlider = new HSlider();
		buttonDragSensitivitySlider.MinValue = 0.5;
		buttonDragSensitivitySlider.MaxValue = 2.0;
		buttonDragSensitivitySlider.Step = 0.05;
		buttonDragSensitivitySlider.Value = EmueraContent.ContentDragSensitivity;
		buttonDragSensitivitySlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		buttonDragSensitivitySlider.CustomMinimumSize = new Vector2(120, 44);
		buttonDragSensitivitySlider.ValueChanged += OnButtonDragSensitivityChanged;
		sensitivityRow.AddChild(buttonDragSensitivitySlider);
		buttonDragSensitivityLabel = CreateValueLabel();
		buttonDragSensitivityLabel.Text = buttonDragSensitivitySlider.Value.ToString("0.00") + "x";
		sensitivityRow.AddChild(buttonDragSensitivityLabel);

		var pinchZoomRow = CreateRow(inputGroup, out pinchZoomLabel);
		pinchZoomToggle = new CheckButton();
		pinchZoomToggle.ButtonPressed = EmueraContent.ContentPinchZoomEnabled;
		pinchZoomToggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		pinchZoomToggle.CustomMinimumSize = new Vector2(120, 44);
		pinchZoomToggle.Toggled += OnPinchZoomToggled;
		pinchZoomRow.AddChild(pinchZoomToggle);

		// ---- System: 语言 ----
		var systemGroup = CreateGroup(content, out systemGroupTitle);

		var langRow = CreateRow(systemGroup, out langLabel);
		languageOption = new OptionButton();
		for (int i = 0; i < languages.Length; i++)
		{
			languageOption.AddItem(languageNames[i], i);
		}
		languageOption.Select(GetLanguageIndex(MultiLanguage.CurrentLanguage));
		languageOption.ItemSelected += OnLanguageSelected;
		languageOption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		langRow.AddChild(languageOption);

		// Close button: pinned footer outside the scroll area.
		closeBtn = new Button();
		EmueraContent.StyleButton(closeBtn);
		closeBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		closeBtn.Pressed += () => popup.Hide();
		rootVBox.AddChild(closeBtn);

		ApplyLanguageTexts();
	}

	/// <summary>分组容器：次级文字色标题 + 1px 分隔线，组内行间距 8。</summary>
	VBoxContainer CreateGroup(VBoxContainer parent, out Label titleLabel)
	{
		var group = new VBoxContainer();
		group.AddThemeConstantOverride("separation", 8);
		parent.AddChild(group);

		titleLabel = new Label();
		titleLabel.AddThemeColorOverride("font_color", GEmueraTheme.TextSecondary);
		titleLabel.AddThemeFontSizeOverride("font_size", 15);
		group.AddChild(titleLabel);

		var sep = new HSeparator();
		sep.AddThemeStyleboxOverride("separator", SeparatorStyle());
		group.AddChild(sep);

		return group;
	}

	/// <summary>组内行：描述 label 固定宽度对齐（160px），控件由调用方 ExpandFill。</summary>
	HBoxContainer CreateRow(VBoxContainer group, out Label descLabel)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		group.AddChild(row);

		descLabel = new Label();
		descLabel.CustomMinimumSize = new Vector2(160, 0);
		descLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		row.AddChild(descLabel);

		return row;
	}

	/// <summary>行尾值 label：右对齐、次级文字色、固定最小宽度。</summary>
	Label CreateValueLabel()
	{
		var label = new Label();
		label.CustomMinimumSize = new Vector2(56, 0);
		label.HorizontalAlignment = HorizontalAlignment.Right;
		label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		label.AddThemeColorOverride("font_color", GEmueraTheme.TextSecondary);
		return label;
	}

	static StyleBox SeparatorStyle()
	{
		var line = new StyleBoxLine();
		line.Vertical = false; // 水平分隔线（HSeparator 用）
		line.Color = GEmueraTheme.Border;
		line.Thickness = 1;
		return line;
	}

	static void SetMargins(MarginContainer margin, int padding)
	{
		margin.AddThemeConstantOverride("margin_left", padding);
		margin.AddThemeConstantOverride("margin_top", padding);
		margin.AddThemeConstantOverride("margin_right", padding);
		margin.AddThemeConstantOverride("margin_bottom", padding);
	}

	// Re-read every MultiLanguage-driven description label and group title.
	// Value labels (slider-derived numbers) are intentionally untouched.
	// Idempotent.
	void ApplyLanguageTexts()
	{
		if (fontLabel != null)
			fontLabel.Text = MultiLanguage.Get("OptionWindow.FontSize", "Font Size");
		if (quickWidthLabel != null)
			quickWidthLabel.Text = MultiLanguage.Get("OptionWindow.QuickButtonWidth", "Quick Width");
		if (quickFontLabel != null)
			quickFontLabel.Text = MultiLanguage.Get("OptionWindow.QuickFontSize", "Quick Font");
		if (sensitivityLabel != null)
			sensitivityLabel.Text = MultiLanguage.Get("OptionWindow.ButtonDragSensitivity", "Scroll Sensitivity");
		if (pinchZoomLabel != null)
			pinchZoomLabel.Text = MultiLanguage.Get("OptionWindow.PinchZoom", "Pinch Zoom");
		if (maxLinesTextLabel != null)
			maxLinesTextLabel.Text = MultiLanguage.Get("OptionWindow.MaxVisibleLines", "Max Lines");
		if (resLabel != null)
			resLabel.Text = MultiLanguage.Get("OptionWindow.Resolution", "Resolution");
		if (fpsLabel != null)
			fpsLabel.Text = MultiLanguage.Get("OptionWindow.FrameRate", "Frame Rate");
		if (langLabel != null)
			langLabel.Text = MultiLanguage.Get("OptionWindow.Language", "Language");
		if (closeBtn != null)
			closeBtn.Text = MultiLanguage.Get("OptionWindow.Close", "Close");
		if (displayGroupTitle != null)
			displayGroupTitle.Text = MultiLanguage.Get("OptionWindow.GroupDisplay", "Display");
		if (quickGroupTitle != null)
			quickGroupTitle.Text = MultiLanguage.Get("OptionWindow.GroupQuick", "Quick");
		if (inputGroupTitle != null)
			inputGroupTitle.Text = MultiLanguage.Get("OptionWindow.GroupInput", "Input");
		if (systemGroupTitle != null)
			systemGroupTitle.Text = MultiLanguage.Get("OptionWindow.GroupSystem", "System");
	}

	public void ShowPopup()
	{
		popup.PopupCentered();
		CallDeferred(nameof(ClampPopupToSafeArea));
		EmitSignal(SignalName.PopupOpened);
	}

	void ClampPopupToSafeArea()
	{
		if (popup == null)
			return;

		Rect2 safeRect = EmueraContent.GetSafeViewportRect(GetViewport());
		Vector2I size = popup.Size;
		int maxWidth = Mathf.Max(1, Mathf.RoundToInt(safeRect.Size.X - 20));
		int maxHeight = Mathf.Max(1, Mathf.RoundToInt(safeRect.Size.Y - 20));
		if (size.X > maxWidth || size.Y > maxHeight)
		{
			size = new Vector2I(System.Math.Min(size.X, maxWidth), System.Math.Min(size.Y, maxHeight));
			popup.Size = size;
		}

		int left = Mathf.RoundToInt(safeRect.Position.X + 10);
		int top = Mathf.RoundToInt(safeRect.Position.Y + 10);
		int right = Mathf.RoundToInt(safeRect.Position.X + safeRect.Size.X - size.X - 10);
		int bottom = Mathf.RoundToInt(safeRect.Position.Y + safeRect.Size.Y - size.Y - 10);
		if (right < left)
			right = left;
		if (bottom < top)
			bottom = top;
		popup.Position = new Vector2I(
			Mathf.Clamp(popup.Position.X, left, right),
			Mathf.Clamp(popup.Position.Y, top, bottom));
	}

	void OnFontSizeChanged(double value)
	{
		int coreSize = MinorShift.Emuera.Config.FontSize > 0 ? MinorShift.Emuera.Config.FontSize : 18;
		fontSizeLabel.Text = coreSize.ToString();
		if (fontSizeSlider != null && (int)fontSizeSlider.Value != coreSize)
		{
			fontSizeSlider.SetBlockSignals(true);
			fontSizeSlider.Value = coreSize;
			fontSizeSlider.SetBlockSignals(false);
		}
	}

	void OnQuickButtonWidthChanged(double value)
	{
		int width = (int)value;
		quickButtonWidthLabel.Text = width.ToString();
		QuickButtons.ConfiguredButtonWidth = width;
		EmueraContent.instance?.RefreshQuickButtonSettings();
	}

	void OnQuickFontSizeChanged(double value)
	{
		int size = (int)value;
		quickFontSizeLabel.Text = size.ToString();
		QuickButtons.ConfiguredFontSize = size;
		EmueraContent.instance?.RefreshQuickButtonSettings();
	}

	void OnButtonDragSensitivityChanged(double value)
	{
		float sensitivity = (float)value;
		buttonDragSensitivityLabel.Text = sensitivity.ToString("0.00") + "x";
		EmueraContent.ContentDragSensitivity = sensitivity;
	}

	void OnPinchZoomToggled(bool enabled)
	{
		EmueraContent.ContentPinchZoomEnabled = enabled;
	}

	void OnMaxVisibleLinesChanged(double value)
	{
		int lines = (int)value;
		maxVisibleLinesLabel.Text = lines.ToString();
		EmueraContent.ConfiguredMaxVisibleLines = lines;
	}

	void OnResolutionSelected(long index)
	{
		ResolutionHelper.resolution_index = (int)index;
		ResolutionHelper.Apply();
	}

	void OnFrameRateSelected(long index)
	{
		FrameRateHelper.frame_rate_index = (int)index;
		FrameRateHelper.Apply();
	}

	void OnLanguageSelected(long index)
	{
		MultiLanguage.Load(languages[index]);
		// LanguageChanged already refreshed EmueraContent texts; refresh this
		// window's own description labels as well (idempotent).
		ApplyLanguageTexts();
	}

	int GetLanguageIndex(string lang)
	{
		for (int i = 0; i < languages.Length; i++)
		{
			if (languages[i] == lang)
				return i;
		}
		return 0;
	}
}

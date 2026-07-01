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

	static readonly string[] languages = new string[] { "default", "zh_cn", "en_us", "jp" };
	static readonly string[] languageNames = new string[] { "Default", "简体中文", "English", "日本語" };

	public override void _Ready()
	{
		popup = new PopupPanel();
		popup.Size = new Vector2I(460, 560);
		AddChild(popup);

		var vbox = new VBoxContainer();
		vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
		popup.AddChild(vbox);

		// Font size
		var fontHBox = new HBoxContainer();
		vbox.AddChild(fontHBox);
		var fontLabel = new Label();
		fontLabel.Text = MultiLanguage.Get("OptionWindow.FontSize", "Font Size");
		fontHBox.AddChild(fontLabel);
		fontSizeSlider = new HSlider();
		fontSizeSlider.MinValue = 8;
		fontSizeSlider.MaxValue = 48;
		fontSizeSlider.Value = MinorShift.Emuera.Config.FontSize > 0 ? MinorShift.Emuera.Config.FontSize : 18;
		fontSizeSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		fontSizeSlider.CustomMinimumSize = new Vector2(120, 44);
		fontSizeSlider.Editable = false;
		fontSizeSlider.ValueChanged += OnFontSizeChanged;
		fontHBox.AddChild(fontSizeSlider);
		fontSizeLabel = new Label();
		fontSizeLabel.Text = fontSizeSlider.Value.ToString();
		fontHBox.AddChild(fontSizeLabel);

		// Quick button width
		var quickWidthHBox = new HBoxContainer();
		vbox.AddChild(quickWidthHBox);
		var quickWidthLabel = new Label();
		quickWidthLabel.Text = MultiLanguage.Get("OptionWindow.QuickButtonWidth", "Quick Width");
		quickWidthHBox.AddChild(quickWidthLabel);
		quickButtonWidthSlider = new HSlider();
		quickButtonWidthSlider.MinValue = QuickButtons.MinQuickButtonWidth;
		quickButtonWidthSlider.MaxValue = QuickButtons.MaxQuickButtonWidth;
		quickButtonWidthSlider.Step = 1;
		quickButtonWidthSlider.Value = QuickButtons.ConfiguredButtonWidth;
		quickButtonWidthSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		quickButtonWidthSlider.CustomMinimumSize = new Vector2(120, 44);
		quickButtonWidthSlider.ValueChanged += OnQuickButtonWidthChanged;
		quickWidthHBox.AddChild(quickButtonWidthSlider);
		quickButtonWidthLabel = new Label();
		quickButtonWidthLabel.Text = quickButtonWidthSlider.Value.ToString();
		quickWidthHBox.AddChild(quickButtonWidthLabel);

		// Quick font size
		var quickFontHBox = new HBoxContainer();
		vbox.AddChild(quickFontHBox);
		var quickFontLabel = new Label();
		quickFontLabel.Text = MultiLanguage.Get("OptionWindow.QuickFontSize", "Quick Font");
		quickFontHBox.AddChild(quickFontLabel);
		quickFontSizeSlider = new HSlider();
		quickFontSizeSlider.MinValue = QuickButtons.MinQuickButtonFontSize;
		quickFontSizeSlider.MaxValue = QuickButtons.MaxQuickButtonFontSize;
		quickFontSizeSlider.Step = 1;
		quickFontSizeSlider.Value = QuickButtons.ConfiguredFontSize;
		quickFontSizeSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		quickFontSizeSlider.CustomMinimumSize = new Vector2(120, 44);
		quickFontSizeSlider.ValueChanged += OnQuickFontSizeChanged;
		quickFontHBox.AddChild(quickFontSizeSlider);
		quickFontSizeLabel = new Label();
		quickFontSizeLabel.Text = quickFontSizeSlider.Value.ToString();
		quickFontHBox.AddChild(quickFontSizeLabel);

		// Button drag sensitivity
		var sensitivityHBox = new HBoxContainer();
		vbox.AddChild(sensitivityHBox);
		var sensitivityLabel = new Label();
		sensitivityLabel.Text = MultiLanguage.Get("OptionWindow.ButtonDragSensitivity", "Scroll Sensitivity");
		sensitivityHBox.AddChild(sensitivityLabel);
		buttonDragSensitivitySlider = new HSlider();
		buttonDragSensitivitySlider.MinValue = 0.5;
		buttonDragSensitivitySlider.MaxValue = 2.0;
		buttonDragSensitivitySlider.Step = 0.05;
		buttonDragSensitivitySlider.Value = EmueraContent.ContentDragSensitivity;
		buttonDragSensitivitySlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		buttonDragSensitivitySlider.CustomMinimumSize = new Vector2(120, 44);
		buttonDragSensitivitySlider.ValueChanged += OnButtonDragSensitivityChanged;
		sensitivityHBox.AddChild(buttonDragSensitivitySlider);
		buttonDragSensitivityLabel = new Label();
		buttonDragSensitivityLabel.Text = buttonDragSensitivitySlider.Value.ToString("0.00") + "x";
		sensitivityHBox.AddChild(buttonDragSensitivityLabel);

		// Pinch zoom
		var pinchZoomHBox = new HBoxContainer();
		vbox.AddChild(pinchZoomHBox);
		var pinchZoomLabel = new Label();
		pinchZoomLabel.Text = MultiLanguage.Get("OptionWindow.PinchZoom", "Pinch Zoom");
		pinchZoomHBox.AddChild(pinchZoomLabel);
		pinchZoomToggle = new CheckButton();
		pinchZoomToggle.ButtonPressed = EmueraContent.ContentPinchZoomEnabled;
		pinchZoomToggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		pinchZoomToggle.CustomMinimumSize = new Vector2(120, 44);
		pinchZoomToggle.Toggled += OnPinchZoomToggled;
		pinchZoomHBox.AddChild(pinchZoomToggle);

		// Max visible lines
		var maxLinesHBox = new HBoxContainer();
		vbox.AddChild(maxLinesHBox);
		var maxLinesTextLabel = new Label();
		maxLinesTextLabel.Text = MultiLanguage.Get("OptionWindow.MaxVisibleLines", "Max Lines");
		maxLinesHBox.AddChild(maxLinesTextLabel);
		maxVisibleLinesSlider = new HSlider();
		maxVisibleLinesSlider.MinValue = EmueraContent.MinMaxVisibleLines;
		maxVisibleLinesSlider.MaxValue = EmueraContent.MaxMaxVisibleLines;
		maxVisibleLinesSlider.Step = 20;
		maxVisibleLinesSlider.Value = EmueraContent.ConfiguredMaxVisibleLines;
		maxVisibleLinesSlider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		maxVisibleLinesSlider.CustomMinimumSize = new Vector2(120, 44);
		maxVisibleLinesSlider.ValueChanged += OnMaxVisibleLinesChanged;
		maxLinesHBox.AddChild(maxVisibleLinesSlider);
		maxVisibleLinesLabel = new Label();
		maxVisibleLinesLabel.Text = ((int)maxVisibleLinesSlider.Value).ToString();
		maxLinesHBox.AddChild(maxVisibleLinesLabel);

		// Resolution
		var resHBox = new HBoxContainer();
		vbox.AddChild(resHBox);
		var resLabel = new Label();
		resLabel.Text = MultiLanguage.Get("OptionWindow.Resolution", "Resolution");
		resHBox.AddChild(resLabel);
		resolutionOption = new OptionButton();
		ResolutionHelper.RefreshResolutions();
		for (int i = 0; i < ResolutionHelper.resolutions.Count; i++)
		{
			resolutionOption.AddItem(ResolutionHelper.resolutions[i] + "p", i);
		}
		if (ResolutionHelper.resolution_index >= 0 && ResolutionHelper.resolution_index < ResolutionHelper.resolutions.Count)
			resolutionOption.Select(ResolutionHelper.resolution_index);
		resolutionOption.ItemSelected += OnResolutionSelected;
		resHBox.AddChild(resolutionOption);

		// Frame rate
		var fpsHBox = new HBoxContainer();
		vbox.AddChild(fpsHBox);
		var fpsLabel = new Label();
		fpsLabel.Text = MultiLanguage.Get("OptionWindow.FrameRate", "Frame Rate");
		fpsHBox.AddChild(fpsLabel);
		frameRateOption = new OptionButton();
		for (int i = 0; i < FrameRateHelper.FrameRates.Count; i++)
		{
			frameRateOption.AddItem(FrameRateHelper.FrameRates[i] + " FPS", i);
		}
		frameRateOption.Select(FrameRateHelper.frame_rate_index);
		frameRateOption.ItemSelected += OnFrameRateSelected;
		fpsHBox.AddChild(frameRateOption);

		// Language
		var langHBox = new HBoxContainer();
		vbox.AddChild(langHBox);
		var langLabel = new Label();
		langLabel.Text = MultiLanguage.Get("OptionWindow.Language", "Language");
		langHBox.AddChild(langLabel);
		languageOption = new OptionButton();
		for (int i = 0; i < languages.Length; i++)
		{
			languageOption.AddItem(languageNames[i], i);
		}
		languageOption.Select(GetLanguageIndex(MultiLanguage.CurrentLanguage));
		languageOption.ItemSelected += OnLanguageSelected;
		langHBox.AddChild(languageOption);

		// Close button
		var closeBtn = new Button();
		closeBtn.Text = MultiLanguage.Get("OptionWindow.Close", "Close");
		EmueraContent.StyleButton(closeBtn);
		closeBtn.Pressed += () => popup.Hide();
		vbox.AddChild(closeBtn);
	}

	public void ShowPopup()
	{
		popup.PopupCentered();
		CallDeferred(nameof(ClampPopupToSafeArea));
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

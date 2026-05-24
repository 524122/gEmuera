using Godot;
using System;
using System.Collections.Generic;
using MinorShift.Emuera;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Content;
using EmuFont = uEmuera.Drawing.Font;
using EmuColor = uEmuera.Drawing.Color;

/// <summary>
/// Godot-side presentation surface for the emuera console.
/// This node owns the mobile-facing UI tree: rendered console lines, command
/// buttons, quick buttons, scaling, scroll state, background images, and audio
/// players. The emuera core remains the source of game state; this class only
/// translates core output into Godot Controls and translates user input back
/// into emuera input events.
/// </summary>
public partial class EmueraContent : Control
{
	// Global access point used by legacy bridge code and overlay helpers.
	public static EmueraContent instance { get; private set; }

	// Core UI nodes. scrollContainer clips the console viewport, scaledContentRoot
	// provides a stable scaled scroll area, and lineContainer holds one Control per
	// console line. htmlIslandContainer renders floating/div-like console islands.
	ScrollContainer scrollContainer;
	Control scaledContentRoot;
	VBoxContainer lineContainer;
	VBoxContainer htmlIslandContainer;

	// Overlay and tool UI. These are Canvas/Control overlays above the console and
	// should not own emuera state directly.
	HBoxContainer menuBar;
	Inputpad inputpad;
	QuickButtons quickButtons;
	Scalepad scalepad;
	ColorRect bgRect;
	Control cbgContainer;
	OptionWindow optionWindow;

	// Audio players are pooled by logical emuera sound channel. Channel indexes
	// are stable so script commands can pause/stop/speed-change the same channel.
	AudioStreamPlayer bgmPlayer;
	List<AudioStreamPlayer> soundPlayers = new List<AudioStreamPlayer>();
	List<int> soundRepeatRemaining = new List<int>();
	float soundVolume = 1.0f;
	float bgmVolume = 1.0f;
	bool applicationPauseActive = false;
	bool bgmPausedBeforeApplicationPause = false;
	List<bool> soundPausedBeforeApplicationPause = new List<bool>();

	// Rendered line indexes. The dictionaries let update/remove operations target
	// a line by emuera LineNo without scanning the Godot child list on every call.
	// lineSizes/lineNumbers keep layout metrics O(1) for mobile scroll performance.
	Dictionary<int, ConsoleDisplayLine> lineObjects = new Dictionary<int, ConsoleDisplayLine>();
	Dictionary<int, Control> lineControls = new Dictionary<int, Control>();
	Dictionary<int, Vector2> lineSizes = new Dictionary<int, Vector2>();
	SortedSet<int> lineNumbers = new SortedSet<int>();
	// Texture pins mirror presentation lifetime: console rows own line pins and
	// CBG owns background pins. activeTexturePinCollector is scoped to the current
	// render pass so GetSpriteTexture can remain a pure conversion helper.
	Dictionary<int, List<SpriteManager.TextureInfo>> lineTexturePins = new Dictionary<int, List<SpriteManager.TextureInfo>>();
	List<SpriteManager.TextureInfo> cbgTexturePins = new List<SpriteManager.TextureInfo>();
	List<SpriteManager.TextureInfo> activeTexturePinCollector;

	// Texture lookup failures are memoized to avoid repeated recursive file scans
	// on Android storage where I/O stalls are very visible.
	HashSet<string> failedTextureSearches = new HashSet<string>();

	// CBG nodes are reused instead of recreated whenever possible. This reduces
	// CanvasItem churn and texture upload pressure during rapid script updates.
	List<EmueraImage> cbgNodes = new List<EmueraImage>();
	List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage> renderedCbgLayers = new List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage>();

	// Batched display updates defer expensive follow-up work until a group of
	// lines has been applied.
	bool batchingDisplayLines = false;
	float totalLineHeight = 0;
	float widestLineWidth = 0;

	// Visible line cap. Android memory pressure is the main constraint here, so
	// old Controls are trimmed in batches instead of letting the scene tree grow
	// without bound.
	public const int DefaultMaxVisibleLines = 360;
	public const int MinMaxVisibleLines = 120;
	public const int MaxMaxVisibleLines = 3000;
	static int MaxVisibleLines => ConfiguredMaxVisibleLines;
	static int LineTrimBatch => System.Math.Max(40, System.Math.Min(200, MaxVisibleLines / 6));

	// Button generation and quick-button cache state. emuera reuses button text
	// across waits, so generation guards prevent an old visible button from
	// submitting into a newer input prompt.
	Font mainFont;
	int lastButtonGeneration = -1;
	int displayRevision = 0;
	int quickRenderedGeneration = int.MinValue;
	int quickRenderedRevision = -1;
	bool quickInputGateActive = false;
	bool quickAutoHiddenUntilNextButtons = false;
	int quickAutoHiddenGeneration = int.MinValue;
	long quickInputGateGeneration = -1;
	int quickInputGateRevision = -1;
	ulong quickInputGateTick = 0;
	int lastCbgScrollVertical = int.MinValue;
	uint lastClickTick = 0;

	// Drag and inertia state for the main console viewport. The code handles both
	// mouse emulation and real touch events because Android can deliver either
	// depending on project input settings.
	bool contentDragActive = false;
	bool contentDragMoved = false;
	bool contentDragStartedOnButton = false;
	Vector2 contentDragStartPosition;
	Vector2 contentDragLastPosition;
	Vector2 contentScrollVelocity = Vector2.Zero;
	Vector2 contentInertiaRemainder = Vector2.Zero;
	Control contentDragButton;
	string contentDragButtonInput;
	long contentDragButtonGeneration;
	ulong contentLastDragTick = 0;
	bool contentInertiaActive = false;
	float contentInertiaDeceleration = 900.0f;
	int contentScrollInteractionSerial = 0;

	// Desired scroll is a mirror of the viewport position we want after Godot has
	// completed its layout pass. This prevents layout refreshes from snapping the
	// ScrollContainer back to the top-left after buttons are regenerated.
	bool desiredContentScrollValid = false;
	int desiredContentScrollHorizontal = 0;
	int desiredContentScrollVertical = 0;

	// Pending bottom-scroll retry state. Console output often arrives across
	// multiple frames, so bottom snapping waits for layout height to stabilize.
	bool pendingScroll = false;
	int pendingScrollInteractionSerial = 0;
	int pendingScrollLastMax = int.MinValue;
	ulong pendingScrollDeadlineTick = 0;
	ulong pendingScrollStableSinceTick = 0;
	bool pendingScaleBoundsUpdate = false;
	ulong lastScrollTraceDragTick = 0;

	// Scale and pinch gesture state. Pinch zoom is opt-in because accidental
	// two-finger input is common on phones while tapping dense command buttons.
	float contentScale = 1.0f;
	Dictionary<int, Vector2> contentTouchPositions = new Dictionary<int, Vector2>();
	bool contentTouchGestureActive = false;
	bool contentPinchActive = false;
	bool contentPinchDirty = false;
	float contentPinchStartSpread = 0.0f;
	float contentPinchStartScale = 1.0f;
	int contentPinchTouchCount = 0;
	bool contentPinchFocusValid = false;
	Vector2 scaleFocusContentPoint = Vector2.Zero;
	Vector2 scaleFocusLocalPoint = Vector2.Zero;

	// Input and physics constants are kept together so mobile feel can be tuned
	// without hunting through the gesture code.
	const float ScrollDragThreshold = 10.0f;
	const float ContentScaleMin = 0.5f;
	const float ContentScaleMax = 3.0f;
	const float ContentScaleEpsilon = 0.0005f;
	const int ContentPinchTouchCount = 2;
	const float ContentPinchMinSpread = 28.0f;
	const float ContentPinchScaleDeadZone = 0.006f;
	const ulong ScrollToBottomRetryMs = 2000;
	const ulong ScrollToBottomStableMs = 80;
	const int ScrollToBottomTolerancePx = 1;
	const ulong ScrollTraceDragIntervalMs = 120;
	const float ContentInertiaMinVelocity = 80.0f;
	const float ContentInertiaFastVelocity = 4500.0f;
	const float ContentInertiaMaxVelocity = 14000.0f;
	const float ContentInertiaMinReleaseBoost = 1.2f;
	const float ContentInertiaMaxReleaseBoost = 3.0f;
	const float ContentInertiaSlowDeceleration = 1800.0f;
	const float ContentInertiaFastDeceleration = 520.0f;
	const float ContentInertiaStopVelocity = 6.0f;
	const int SystemButtonTouchSize = 48;
	const string SettingsPath = "user://settings.cfg";
	const string SettingsSection = "Display";
	const string ContentDragSensitivityKey = "ContentDragSensitivity";
	const string ButtonDragSensitivityKey = "ButtonDragSensitivity";
	const string MaxVisibleLinesKey = "MaxVisibleLines";
	const string ContentPinchZoomEnabledKey = "ContentPinchZoomEnabled";
	const ulong QuickInputGateFallbackMs = 500;
	static float contentDragSensitivity = -1.0f;
	static int configuredMaxVisibleLines = -1;
	static bool contentPinchZoomEnabled = false;
	static bool contentPinchZoomEnabledLoaded = false;

	// Processing label shown while the emuera worker is busy.
	Label inProcessLabel;

	// Shared modal confirmation/message box used by menu actions.
	PopupPanel msgBox;
	Label msgBoxTitle;
	Label msgBoxMessage;
	Button msgBoxConfirmBtn;
	Button msgBoxCancelBtn;
	System.Action msgBoxConfirmCallback;
	System.Action msgBoxCancelCallback;

	// Top-right system menu overlay. It lives on a high CanvasLayer so it remains
	// tappable above the console but uses narrow hit areas to avoid blocking
	// click-to-advance.
	Control rootContent;
	CanvasLayer menuLayer;
	HBoxContainer menuRoot;
	HBoxContainer menuExpandedBar;
	bool menuExpanded = false;
	TextureButton inputMenuButton;
	TextureButton quickMenuButton;
	TextureButton autoSkipMenuButton;
	TextureButton scaleMenuButton;
	bool autoClickSkipEnabled = false;
	ulong lastAutoClickSkipTick = 0;
	static readonly Color ActiveSystemButtonColor = new Color(1.0f, 0.86f, 0.15f, 1.0f);
	static readonly Color NormalSystemButtonColor = new Color(1, 1, 1, 1);

	public static int ContentWidth { get; private set; }
	public static int ContentHeight { get; private set; }

	// User-configurable cap for rendered console rows. The value is persisted in
	// user:// so exported APK builds can keep device-specific settings.
	public static int ConfiguredMaxVisibleLines
	{
		get
		{
			if (configuredMaxVisibleLines < 0)
			{
				var cfg = new ConfigFile();
				cfg.Load(SettingsPath);
				configuredMaxVisibleLines = ClampMaxVisibleLines(
					(int)cfg.GetValue(SettingsSection, MaxVisibleLinesKey, DefaultMaxVisibleLines));
			}
			return configuredMaxVisibleLines;
		}
		set
		{
			configuredMaxVisibleLines = ClampMaxVisibleLines(value);
			var cfg = new ConfigFile();
			cfg.Load(SettingsPath);
			cfg.SetValue(SettingsSection, MaxVisibleLinesKey, configuredMaxVisibleLines);
			cfg.Save(SettingsPath);
			instance?.TrimVisibleLinesToLimit();
		}
	}

	static int ClampMaxVisibleLines(int value)
	{
		return System.Math.Max(MinMaxVisibleLines, System.Math.Min(MaxMaxVisibleLines, value));
	}

	// emuera still exposes the console viewport through Config.WindowY. Keeping
	// this helper isolated makes it easier to replace later with actual viewport
	// measurements if the core API changes.
	static int GetContentViewportHeight()
	{
		return Config.WindowY;
	}

	// Drag sensitivity shared by the main console and quick-button panel. It is
	// clamped tightly because too high a multiplier makes touch scrolling skip
	// command rows on small screens.
	public static float ContentDragSensitivity
	{
		get
		{
			if (contentDragSensitivity < 0)
			{
				var cfg = new ConfigFile();
				cfg.Load(SettingsPath);
				var fallback = cfg.GetValue(SettingsSection, ButtonDragSensitivityKey, 2.0);
				contentDragSensitivity = Mathf.Clamp(
					(float)(double)cfg.GetValue(SettingsSection, ContentDragSensitivityKey, fallback),
					0.5f,
					2.0f);
			}
			return contentDragSensitivity;
		}
		set
		{
			contentDragSensitivity = Mathf.Clamp(value, 0.5f, 2.0f);
			var cfg = new ConfigFile();
			cfg.Load(SettingsPath);
			cfg.SetValue(SettingsSection, ContentDragSensitivityKey, contentDragSensitivity);
			cfg.Save(SettingsPath);
		}
	}

	public static float ButtonDragSensitivity
	{
		get => ContentDragSensitivity;
		set => ContentDragSensitivity = value;
	}

	// Optional mobile pinch zoom. Disabled by default to preserve legacy tap
	// behavior unless the user explicitly enables it in settings.
	public static bool ContentPinchZoomEnabled
	{
		get
		{
			EnsureContentPinchZoomSettingLoaded();
			return contentPinchZoomEnabled;
		}
		set
		{
			if (contentPinchZoomEnabledLoaded && contentPinchZoomEnabled == value)
				return;
			contentPinchZoomEnabled = value;
			contentPinchZoomEnabledLoaded = true;
			var cfg = new ConfigFile();
			cfg.Load(SettingsPath);
			cfg.SetValue(SettingsSection, ContentPinchZoomEnabledKey, contentPinchZoomEnabled);
			cfg.Save(SettingsPath);
			if (!contentPinchZoomEnabled)
				instance?.ResetContentTouchGestureState();
		}
	}

	static void EnsureContentPinchZoomSettingLoaded()
	{
		if (contentPinchZoomEnabledLoaded)
			return;
		var cfg = new ConfigFile();
		cfg.Load(SettingsPath);
		contentPinchZoomEnabled = (bool)cfg.GetValue(SettingsSection, ContentPinchZoomEnabledKey, false);
		contentPinchZoomEnabledLoaded = true;
	}

	// Build the UI tree entirely in code because the emulator surface is dynamic:
	// lines, buttons, overlays, and background images are all generated from ERB
	// output rather than fixed scene resources.
	public override void _Ready()
	{
		instance = this;
		Size = GetViewportRect().Size;
		ContentWidth = (int)Size.X;
		ContentHeight = (int)Size.Y;
		GetViewport().SizeChanged += OnViewportSizeChanged;

		mainFont = LoadConfiguredFont();

		// Background is a full-screen ColorRect so emuera's configured back color
		// can be applied without touching the generated line nodes.
		bgRect = new ColorRect();
		bgRect.AnchorLeft = 0;
		bgRect.AnchorTop = 0;
		bgRect.AnchorRight = 1;
		bgRect.AnchorBottom = 1;
		AddChild(bgRect);

		rootContent = new Control();
		rootContent.AnchorLeft = 0;
		rootContent.AnchorTop = 0;
		rootContent.AnchorRight = 1;
		rootContent.AnchorBottom = 1;
		AddChild(rootContent);

		// Menu bar on a separate CanvasLayer so it doesn't block click-to-advance.
		// Toggle sits in the top-right corner; tapping it expands icons to the left.
		menuLayer = new CanvasLayer();
		menuLayer.Layer = 100;
		AddChild(menuLayer);

		menuRoot = new HBoxContainer();
		menuRoot.AnchorLeft = 1;
		menuRoot.AnchorRight = 1;
		menuRoot.AnchorTop = 0;
		menuRoot.AnchorBottom = 0;
		menuRoot.GrowHorizontal = Control.GrowDirection.Begin;
		menuRoot.OffsetRight = -4;
		menuRoot.OffsetTop = 4;
		menuRoot.MouseFilter = MouseFilterEnum.Pass;
		menuRoot.AddThemeConstantOverride("separation", 2);
		menuLayer.AddChild(menuRoot);

		// Panel holding the 9 action icons (hidden until toggled).
		// Placed BEFORE the toggle in the HBox so it sits on the toggle's left.
		var menuPanel = new PanelContainer();
		menuPanel.MouseFilter = MouseFilterEnum.Stop;
		var menuPanelStyle = new StyleBoxFlat();
		menuPanelStyle.BgColor = new Color(0.1f, 0.1f, 0.1f, 0.85f);
		menuPanelStyle.CornerRadiusTopLeft = menuPanelStyle.CornerRadiusBottomLeft = 4;
		menuPanelStyle.CornerRadiusTopRight = menuPanelStyle.CornerRadiusBottomRight = 4;
		menuPanelStyle.ContentMarginLeft = menuPanelStyle.ContentMarginRight = 4;
		menuPanelStyle.ContentMarginTop = menuPanelStyle.ContentMarginBottom = 2;
		menuPanel.AddThemeStyleboxOverride("panel", menuPanelStyle);
		menuPanel.Visible = false;
		menuRoot.AddChild(menuPanel);

		menuExpandedBar = new HBoxContainer();
		menuExpandedBar.AddThemeConstantOverride("separation", 2);
		menuPanel.AddChild(menuExpandedBar);

		menuBar = menuExpandedBar;
		AddIconButton("res://Icons/fenxiang.svg", OnBackPressed);
		AddIconButton("res://Icons/restart.svg", OnRestartPressed);
		AddIconButton("res://Icons/options.svg", OnOptionsPressed);
		inputMenuButton = AddIconButton("res://Icons/io-input.svg", OnInputTogglePressed);
		quickMenuButton = AddIconButton("res://Icons/quick.svg", OnQuickTogglePressed);
		autoSkipMenuButton = AddIconButton("res://Icons/autoskip.svg", OnAutoSkipTogglePressed);
		AddIconButton("res://Icons/menu_save_log.svg", OnSaveLogPressed);
		AddIconButton("res://Icons/Title.svg", OnGotoTitlePressed);
		AddIconButton("res://Icons/exit.svg", OnExitPressed);
		scaleMenuButton = AddIconButton("res://Icons/Scale.svg", OnScaleTogglePressed);

		// Toggle at the right edge (last child = rightmost in HBox).
		var menuToggleBtn = new TextureButton();
		menuToggleBtn.CustomMinimumSize = new Vector2(SystemButtonTouchSize, SystemButtonTouchSize);
		menuToggleBtn.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
		menuToggleBtn.MouseFilter = MouseFilterEnum.Stop;
		if (ResourceLoader.Exists("res://Icons/menu.svg"))
			menuToggleBtn.TextureNormal = ResourceLoader.Load<Texture2D>("res://Icons/menu.svg");
		WireSystemButton(menuToggleBtn, OnMenuTogglePressed);
		menuRoot.AddChild(menuToggleBtn);

		// Tool overlays are siblings of the console so their CanvasLayer/z-order
		// and input capture are independent from the scrollable console content.
		quickButtons = new QuickButtons();
		AddChild(quickButtons);

		inputpad = new Inputpad();
		AddChild(inputpad);

		scalepad = new Scalepad();
		AddChild(scalepad);

		optionWindow = new OptionWindow();
		AddChild(optionWindow);

		// Main console viewport. The scrollbars are left in Auto mode so Godot
		// owns the internal range, but their visual nodes are hidden because this
		// emulator uses direct touch drag instead of visible scrollbars on mobile.
		scrollContainer = new ScrollContainer();
		scrollContainer.AnchorLeft = 0;
		scrollContainer.AnchorTop = 0;
		scrollContainer.AnchorRight = 1;
		scrollContainer.AnchorBottom = 1;
		ConfigureContentScrollContainer();
		scrollContainer.FollowFocus = false;
		scrollContainer.ClipContents = true;
		scrollContainer.MouseFilter = MouseFilterEnum.Pass;
		scrollContainer.GuiInput += OnContentGuiInput;
		rootContent.AddChild(scrollContainer);

		inProcessLabel = new Label();
		inProcessLabel.AnchorLeft = 0;
		inProcessLabel.AnchorTop = 0;
		inProcessLabel.AnchorRight = 1;
		inProcessLabel.AnchorBottom = 0;
		inProcessLabel.OffsetTop = 4;
		inProcessLabel.OffsetBottom = 32;
		inProcessLabel.Text = MultiLanguage.Get("EmueraContent.InProcess", "Processing...");
		inProcessLabel.MouseFilter = MouseFilterEnum.Ignore;
		inProcessLabel.ZIndex = 20;
		ApplyFont(inProcessLabel);
		inProcessLabel.HorizontalAlignment = HorizontalAlignment.Center;
		inProcessLabel.Visible = false;
		rootContent.AddChild(inProcessLabel);

		// scaledContentRoot is the actual ScrollContainer child. lineContainer is
		// scaled inside it so ScrollContainer receives the scaled minimum size and
		// can compute a correct scroll range.
		scaledContentRoot = new Control();
		scaledContentRoot.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scaledContentRoot.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		scaledContentRoot.MouseFilter = MouseFilterEnum.Pass;
		scaledContentRoot.GuiInput += OnContentGuiInput;
		scrollContainer.AddChild(scaledContentRoot);

		lineContainer = new VBoxContainer();
		lineContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lineContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		lineContainer.ClipContents = false;
		lineContainer.AddThemeConstantOverride("separation", 0);
		scaledContentRoot.AddChild(lineContainer);

		htmlIslandContainer = new VBoxContainer();
		htmlIslandContainer.MouseFilter = MouseFilterEnum.Ignore;
		htmlIslandContainer.ClipContents = false;
		htmlIslandContainer.AddThemeConstantOverride("separation", 0);
		scaledContentRoot.AddChild(htmlIslandContainer);

		// Message box popup
		msgBox = new PopupPanel();
		msgBox.Size = new Vector2I(400, 220);
		AddChild(msgBox);

		var msgVBox = new VBoxContainer();
		msgVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		msgVBox.SizeFlagsVertical = SizeFlags.ExpandFill;
		msgVBox.Alignment = BoxContainer.AlignmentMode.Center;
		msgBox.AddChild(msgVBox);

		msgBoxTitle = new Label();
		msgBoxTitle.HorizontalAlignment = HorizontalAlignment.Center;
		msgVBox.AddChild(msgBoxTitle);

		msgBoxMessage = new Label();
		msgBoxMessage.HorizontalAlignment = HorizontalAlignment.Center;
		msgBoxMessage.AutowrapMode = TextServer.AutowrapMode.Word;
		msgBoxMessage.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		msgVBox.AddChild(msgBoxMessage);

		var msgBtnHBox = new HBoxContainer();
		msgBtnHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		msgBtnHBox.Alignment = BoxContainer.AlignmentMode.Center;
		msgVBox.AddChild(msgBtnHBox);

		msgBoxConfirmBtn = new Button();
		msgBoxConfirmBtn.Text = MultiLanguage.Get("MsgBox.Confirm", "OK");
		StyleButton(msgBoxConfirmBtn);
		msgBoxConfirmBtn.Pressed += OnMsgConfirm;
		msgBtnHBox.AddChild(msgBoxConfirmBtn);

		msgBoxCancelBtn = new Button();
		msgBoxCancelBtn.Text = MultiLanguage.Get("MsgBox.Cancel", "Cancel");
		StyleButton(msgBoxCancelBtn);
		msgBoxCancelBtn.Pressed += OnMsgCancel;
		msgBtnHBox.AddChild(msgBoxCancelBtn);

		// Apply fonts to auxiliary UI
		inputpad.ApplyFont(mainFont, FontSize);
		quickButtons.ApplyFont(mainFont, FontSize);
		scalepad.ApplyFont(mainFont, FontSize);
		ApplyFont(msgBoxTitle);
		ApplyFont(msgBoxMessage);
		ApplyFont(msgBoxConfirmBtn);
		ApplyFont(msgBoxCancelBtn);

		// CBG is drawn above the background but below text overlays. It is not a
		// child of the ScrollContainer because some layers follow scroll through
		// their own emuera z-depth semantics.
		cbgContainer = new Control();
		cbgContainer.AnchorLeft = 0;
		cbgContainer.AnchorTop = 0;
		cbgContainer.AnchorRight = 1;
		cbgContainer.AnchorBottom = 1;
		cbgContainer.MouseFilter = MouseFilterEnum.Ignore;
		cbgContainer.ZIndex = 10;
		AddChild(cbgContainer);

	}

	// Clear all generated console state. This is used for title changes/reloads
	// and must reset both Godot nodes and the O(1) lookup indexes.
	public void Clear()
	{
		GenericUtils.ClearPointingButton();
		foreach(var child in lineContainer.GetChildren())
			SafeQueueFree(child);
		ResetLineIndexes();
		failedTextureSearches.Clear();
		displayRevision++;
		RefreshQuickInputGate();
		quickRenderedRevision = -1;
		if (quickButtons != null && quickButtons.IsShow)
			quickButtons.Clear();
	}

	public override void _ExitTree()
	{
		GetViewport().SizeChanged -= OnViewportSizeChanged;
		ResetLineTexturePins();
		ReleaseCbgTexturePins();
		if (instance == this)
			instance = null;
	}

	int FontSize => Config.FontSize > 0 ? Config.FontSize : 18;

	// Cached line height derived from the active font. Console rows are fixed
	// height for predictable emuera layout and fast scroll-size calculation.
	int _effectiveLineHeight = -1;
	int EffectiveLineHeight
	{
		get
		{
			if (_effectiveLineHeight < 0)
			{
				if (mainFont != null)
				{
					int fontH = (int)System.Math.Ceiling(mainFont.GetHeight(FontSize));
					int lineSpacing = 3;
					_effectiveLineHeight = System.Math.Max(Config.LineHeight, fontH + lineSpacing);
				}
				else
				{
					_effectiveLineHeight = System.Math.Max(Config.LineHeight, FontSize + 9);
				}
			}
			return _effectiveLineHeight;
		}
	}

	void ApplyFont(Control control)
	{
		if (mainFont != null)
			control.AddThemeFontOverride("font", mainFont);
		control.AddThemeFontSizeOverride("font_size", FontSize);
	}

	// Godot containers respect CustomMinimumSize during layout, while direct Size
	// is needed here because many console parts are absolutely positioned.
	static void SetFixedControlSize(Control control, Vector2 size)
	{
		control.CustomMinimumSize = size;
		control.Size = size;
	}

	// Render a text fragment as a fixed-size Label. emuera gives absolute X/width
	// for console text, so autowrap is deliberately disabled here.
	Label CreateTextPart(string text, EmuColor color, EmuFont font, float width)
	{
		var label = new Label();
		label.MouseFilter = MouseFilterEnum.Ignore;
		label.Text = uEmuera.Utils.StripZeroWidth(text) ?? "";
		ApplyFont(label);
		label.AddThemeColorOverride("font_color", new Godot.Color(color.r, color.g, color.b, color.a));
		if (font?.Bold == true)
		{
			label.AddThemeConstantOverride("outline_size", 1);
			label.AddThemeColorOverride("font_outline_color", new Godot.Color(color.r, color.g, color.b, color.a));
		}
		label.VerticalAlignment = VerticalAlignment.Center;
		label.ClipText = true;
		label.AutowrapMode = TextServer.AutowrapMode.Off;
		SetFixedControlSize(label, new Vector2(width, EffectiveLineHeight));
		return label;
	}

	const string BundledConsoleFontPath = "res://Fonts/MS Gothic.ttf";

	// Prefer the bundled console font on Android to avoid missing glyphs and
	// device-specific font metric differences in exported APKs.
	Font LoadConfiguredFont()
	{
		string requested = Config.FontName;
		Font bundledFont = ResourceLoader.Load<Font>(BundledConsoleFontPath);
		if (ShouldUseBundledConsoleFont(requested))
			return bundledFont;
		if (!string.IsNullOrWhiteSpace(requested))
		{
			return new SystemFont
			{
				FontNames = new[]
				{
					requested,
					"SimHei",
					"Microsoft YaHei",
					"MS Gothic",
					"Noto Sans CJK SC",
					"Noto Sans CJK JP",
					"Droid Sans Fallback",
                    "sans-serif"
				}
			};
		}
		return bundledFont;
	}

	static bool ShouldUseBundledConsoleFont(string requested)
	{
		if (OS.GetName() == "Android")
			return true;
		if (string.IsNullOrWhiteSpace(requested))
			return true;
		string normalized = requested.Trim();
		if (normalized.StartsWith("@", System.StringComparison.Ordinal))
			normalized = normalized.Substring(1);
		return normalized.Equals("MS Gothic", System.StringComparison.OrdinalIgnoreCase)
			|| normalized.Equals("MS UI Gothic", System.StringComparison.OrdinalIgnoreCase)
			|| normalized.Equals("\uFF2D\uFF33 \u30B4\u30B7\u30C3\u30AF", System.StringComparison.OrdinalIgnoreCase);
	}

	// The following helpers compute absolute row bounds from every part in a
	// ConsoleDisplayLine. Images and div-like parts can extend outside the normal
	// baseline, so row height cannot rely on font height alone.
	int GetPartTop(AConsoleDisplayPart part)
	{
		if (part == null)
			return 0;
		if (part is ConsoleImagePart image)
		{
			if (image.Display == DisplayMode.Relative)
				return 0;
			return System.Math.Min(0, image.PositionY);
		}
		if (part is ConsoleDivPart div && div.IsRelative)
			return System.Math.Min(0, div.Y);
		return System.Math.Min(0, part.Top);
	}

	int GetPartBottom(AConsoleDisplayPart part)
	{
		if (part == null)
			return EffectiveLineHeight;
		if (part is ConsoleImagePart image)
		{
			if (image.Display == DisplayMode.Relative)
				return EffectiveLineHeight;
			return EffectiveLineHeight;
		}
		if (part is ConsoleDivPart div && div.IsRelative)
			return System.Math.Max(EffectiveLineHeight, div.Y + div.DivHeight);
		return System.Math.Max(EffectiveLineHeight, part.Bottom);
	}

	int GetButtonTop(ConsoleButtonString button)
	{
		int top = 0;
		if (button?.StrArray == null)
			return top;
		foreach (var part in button.StrArray)
			top = System.Math.Min(top, GetPartTop(part));
		return top;
	}

	int GetButtonBottom(ConsoleButtonString button)
	{
		int bottom = EffectiveLineHeight;
		if (button?.StrArray == null)
			return bottom;
		foreach (var part in button.StrArray)
			bottom = System.Math.Max(bottom, GetPartBottom(part));
		return bottom;
	}

	int GetLineBottom(ConsoleDisplayLine line)
	{
		int bottom = EffectiveLineHeight;
		if (line?.Buttons == null)
			return bottom;
		foreach (var button in line.Buttons)
			bottom = System.Math.Max(bottom, GetButtonBottom(button));
		return bottom;
	}

	// Transparent styles keep emuera buttons visually driven by their child text
	// and image parts while still providing a Godot input target.
	static StyleBoxFlat _btnNormalStyle;
	static StyleBoxFlat _btnHoverStyle;

	static void EnsureButtonStyles()
	{
		if (_btnNormalStyle != null)
			return;
		_btnNormalStyle = new StyleBoxFlat();
		_btnNormalStyle.BgColor = new Color(0, 0, 0, 0);
		_btnNormalStyle.BorderColor = new Color(0, 0, 0, 0);
		_btnNormalStyle.BorderWidthLeft = _btnNormalStyle.BorderWidthRight = 0;
		_btnNormalStyle.BorderWidthTop = _btnNormalStyle.BorderWidthBottom = 0;
		_btnNormalStyle.ContentMarginLeft = _btnNormalStyle.ContentMarginRight = 0;
		_btnNormalStyle.ContentMarginTop = _btnNormalStyle.ContentMarginBottom = 0;

		_btnHoverStyle = new StyleBoxFlat();
		_btnHoverStyle.BgColor = new Color(0, 0, 0, 0);
		_btnHoverStyle.BorderColor = new Color(0, 0, 0, 0);
		_btnHoverStyle.BorderWidthLeft = _btnHoverStyle.BorderWidthRight = 0;
		_btnHoverStyle.BorderWidthTop = _btnHoverStyle.BorderWidthBottom = 0;
		_btnHoverStyle.ContentMarginLeft = _btnHoverStyle.ContentMarginRight = 0;
		_btnHoverStyle.ContentMarginTop = _btnHoverStyle.ContentMarginBottom = 0;
	}

	internal static void StyleButton(Button btn)
	{
		EnsureButtonStyles();
		btn.AddThemeStyleboxOverride("normal", _btnNormalStyle);
		btn.AddThemeStyleboxOverride("hover", _btnHoverStyle);
		btn.AddThemeStyleboxOverride("pressed", _btnHoverStyle);
		btn.AddThemeStyleboxOverride("focus", _btnHoverStyle);
		btn.AddThemeColorOverride("font_color", new Color(1, 1, 1, 1));
		var focusColor = new Godot.Color(Config.FocusColor.r, Config.FocusColor.g, Config.FocusColor.b, Config.FocusColor.a);
		btn.AddThemeColorOverride("font_hover_color", focusColor);
		btn.AddThemeColorOverride("font_pressed_color", focusColor);
		btn.AddThemeColorOverride("font_focus_color", focusColor);
	}

	// Add a small menu icon and wire it through pointer-tracking code instead of
	// Button.Pressed so touch drags do not accidentally trigger menu actions.
	TextureButton AddIconButton(string iconPath, System.Action callback)
	{
		var btn = new TextureButton();
		btn.CustomMinimumSize = new Vector2(SystemButtonTouchSize, SystemButtonTouchSize);
		btn.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
		btn.MouseFilter = MouseFilterEnum.Stop;
		if (!string.IsNullOrWhiteSpace(iconPath)
			&& !string.Equals(iconPath, "res://", System.StringComparison.OrdinalIgnoreCase)
			&& ResourceLoader.Exists(iconPath))
		{
			var tex = ResourceLoader.Load<Texture2D>(iconPath);
			btn.TextureNormal = tex;
		}
		WireSystemButton(btn, callback);
		menuBar.AddChild(btn);
		return btn;
	}

	void WireSystemButton(TextureButton btn, System.Action callback)
	{
		bool tracking = false;
		bool moved = false;
		Vector2 start = Vector2.Zero;
		btn.GuiInput += inputEvent =>
		{
			if (!TryGetPointer(inputEvent, out var position, out var pressed, out var released, out var motion))
				return;

			if (pressed)
			{
				tracking = true;
				moved = false;
				start = position;
				GetViewport().SetInputAsHandled();
				return;
			}

			if (!tracking)
				return;

			if (motion)
			{
				if ((position - start).Length() >= ScrollDragThreshold)
					moved = true;
				GetViewport().SetInputAsHandled();
				return;
			}

			if (released)
			{
				tracking = false;
				GetViewport().SetInputAsHandled();
				if (!moved)
					callback?.Invoke();
			}
		};
	}

	// Add or replace one rendered console line. The method preserves the emuera
	// LineNo index, registers exact layout metrics, and creates Panel hit targets
	// for command buttons without using Button's focus behavior.
	internal void AddLine(ConsoleDisplayLine line, bool isUpdate)
	{
		if (line == null)
			return;
		int lineHeight = GetLineBottom(line);
		var lineControl = new Control();
		lineControl.MouseFilter = MouseFilterEnum.Pass;
		lineControl.ClipContents = false;
		int maxLineRight = 0;

		// Collect every TextureInfo touched while building this row. The pins are
		// committed only after the row is inserted, which keeps replacement/update
		// flows balanced even when rendering throws before registration.
		var previousTexturePinCollector = activeTexturePinCollector;
		var newTexturePins = new List<SpriteManager.TextureInfo>();
		activeTexturePinCollector = newTexturePins;
		try
		{
			AddLineBackground(line, lineControl, lineHeight);
			foreach(var button in line.Buttons)
			{
				if(button.IsButton)
				{
					int buttonTop = GetButtonTop(button);
					int buttonHeight = GetButtonBottom(button) - buttonTop;
					if (buttonHeight <= 0)
						buttonHeight = EffectiveLineHeight;
					var btn = new Panel();
					btn.FocusMode = FocusModeEnum.None;
					btn.MouseForcePassScrollEvents = false;
					btn.MouseFilter = MouseFilterEnum.Stop;
					btn.ClipContents = false;
					EnsureButtonStyles();
					btn.AddThemeStyleboxOverride("panel", _btnNormalStyle);
					string inputs = button.Inputs;
					long generation = button.Generation;
					btn.GuiInput += inputEvent => OnContentButtonGuiInput(inputEvent, btn, inputs, generation);
					btn.MouseEntered += () => GenericUtils.SetPointingButton(inputs, generation);
					btn.MouseExited += () => GenericUtils.ClearPointingButton(generation);
					btn.SetMeta("generation", generation);

					var contentBox = new Control();
					contentBox.MouseFilter = MouseFilterEnum.Ignore;
					contentBox.ClipContents = false;
					contentBox.Position = new Vector2(0, -buttonTop);
					btn.AddChild(contentBox);

					foreach(var part in button.StrArray)
					{
						AddPartToContainer(part, contentBox, button.PointX);
					}

					// Let clicks pass through to the Button
					foreach (var child in contentBox.GetChildren())
					{
						if (child is Control c)
							c.MouseFilter = MouseFilterEnum.Ignore;
					}

					SetFixedControlSize(contentBox, new Vector2(button.Width, buttonHeight));
					btn.CustomMinimumSize = new Vector2(button.Width, buttonHeight);
					btn.Position = new Vector2(button.PointX, buttonTop);
					btn.Size = new Vector2(button.Width, buttonHeight);
					lineControl.AddChild(btn);

					int btnRight = button.PointX + button.Width;
					if (btnRight > maxLineRight) maxLineRight = btnRight;
				}
				else
				{
					foreach(var part in button.StrArray)
					{
						AddPartToContainer(part, lineControl, 0);
					}
					int right = button.PointX + button.Width;
					if (right > maxLineRight) maxLineRight = right;
				}
			}
		}
		finally
		{
			activeTexturePinCollector = previousTexturePinCollector;
		}

		int fixedLineHeight = lineControl.GetChildCount() == 0 ? 0 : lineHeight;
		var lineSize = new Vector2(maxLineRight, fixedLineHeight);
		SetFixedControlSize(lineControl, lineSize);

		int insertIndex = -1;
		if (lineControls.TryGetValue(line.LineNo, out var existingControl))
		{
			if (existingControl != null && existingControl.GetParent() == lineContainer)
				insertIndex = existingControl.GetIndex();
			UnregisterLine(line.LineNo);
			if (existingControl != null)
				SafeQueueFree(existingControl);
		}

		lineContainer.AddChild(lineControl);
		if (insertIndex >= 0)
			lineContainer.MoveChild(lineControl, System.Math.Min(insertIndex, lineContainer.GetChildCount() - 1));

		lineControl.SetMeta("line_no", line.LineNo);
		RegisterLine(line.LineNo, line, lineControl, lineSize);
		RegisterLineTexturePins(line.LineNo, newTexturePins);
		displayRevision++;

		// Enforce node cap to prevent unbounded memory growth
		if (!batchingDisplayLines && lineContainer.GetChildCount() > MaxVisibleLines)
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

	internal void AddLines(IReadOnlyList<(ConsoleDisplayLine Line, bool Update)> lines)
	{
		if (lines == null || lines.Count == 0)
			return;

		batchingDisplayLines = true;
		try
		{
			for (int i = 0; i < lines.Count; i++)
			{
				var item = lines[i];
				if (item.Line != null)
					AddLine(item.Line, item.Update);
			}
		}
		finally
		{
			batchingDisplayLines = false;
		}

		int overflow = lineContainer.GetChildCount() - MaxVisibleLines;
		if (overflow > 0)
			RemoveTopLines(System.Math.Max(LineTrimBatch, overflow));

		RefreshQuickInputGate();
		QueueDisplayFollowUp();
	}

	// Apply a core display delta: remove from bottom, add/update lines, trim old
	// top rows, then schedule one layout/scroll follow-up for the whole batch.
	internal void ApplyTextChanges(int removeBottomCount, IReadOnlyList<(ConsoleDisplayLine Line, bool Update)> lines, bool update, int lastButtonGeneration)
	{
		bool changed = false;
		batchingDisplayLines = true;
		try
		{
			if (removeBottomCount > 0)
			{
				RemoveBottomLines(removeBottomCount);
				changed = true;
			}

			if (lines != null && lines.Count > 0)
			{
				for (int i = 0; i < lines.Count; i++)
				{
					var item = lines[i];
					if (item.Line == null)
						continue;
					AddLine(item.Line, item.Update);
					changed = true;
				}
			}
		}
		finally
		{
			batchingDisplayLines = false;
		}

		int overflow = lineContainer.GetChildCount() - MaxVisibleLines;
		if (overflow > 0)
		{
			RemoveTopLines(System.Math.Max(LineTrimBatch, overflow));
			changed = true;
		}

		if (changed || update)
		{
			RefreshQuickInputGate();
			QueueDisplayFollowUp();
		}

		TraceScroll("apply_text_changes", $"removeBottom={removeBottomCount} add={lines?.Count ?? 0} changed={changed} update={update} lastGen={lastButtonGeneration} maxLine={GetMaxLineNo()}");
		SetLastButtonGeneration(lastButtonGeneration);
	}

	void QueueDisplayFollowUp()
	{
		QueueScaleBoundsUpdate();
		RequestScrollToBottom();
	}

	// Background color rectangles are attached per line so PRINT background color
	// semantics scroll together with the corresponding text row.
	void AddLineBackground(ConsoleDisplayLine line, Control lineControl, int lineHeight)
	{
		if (line.TextBackgroundColor == null)
			return;
		var c = line.TextBackgroundColor.Value;
		var bg = new ColorRect();
		bg.MouseFilter = MouseFilterEnum.Ignore;
		bg.Color = new Godot.Color(c.r, c.g, c.b, c.a);
		bg.Position = Vector2.Zero;
		bg.Size = new Vector2(Config.DrawableWidth, lineHeight);
		bg.CustomMinimumSize = new Vector2(Config.DrawableWidth, lineHeight);
		bg.ZIndex = -1;
		lineControl.AddChild(bg);
	}

	internal void SetHtmlIsland(ConsoleDisplayLine[] lines)
	{
		ClearHtmlIsland();
		if (htmlIslandContainer == null || lines == null)
			return;
		foreach (var line in lines)
		{
			if (line == null)
				continue;
			int lineHeight = GetLineBottom(line);
			var lineControl = new Control();
			lineControl.MouseFilter = MouseFilterEnum.Ignore;
			lineControl.ClipContents = false;
			AddLineBackground(line, lineControl, lineHeight);
			foreach (var button in line.Buttons)
			{
				foreach (var part in button.StrArray)
					AddPartToContainer(part, lineControl, 0);
			}
			SetFixedControlSize(lineControl, new Vector2(0, lineHeight));
			htmlIslandContainer.AddChild(lineControl);
		}
		displayRevision++;
		RefreshQuickInputGate();
		QueueScaleBoundsUpdate();
	}

	// Remove any temporary HTML island output. The island container is separate
	// from normal rows because some emuera HTML/div output is positioned as a
	// composite overlay rather than as ordinary text.
	internal void ClearHtmlIsland()
	{
		if (htmlIslandContainer == null)
			return;
		foreach (var child in htmlIslandContainer.GetChildren())
			SafeQueueFree(child);
		displayRevision++;
		RefreshQuickInputGate();
	}

	// Register one row in all lookup tables and aggregate metrics used by the
	// manual layout calculator.
	void RegisterLine(int lineNo, ConsoleDisplayLine line, Control control, Vector2 size)
	{
		lineObjects[lineNo] = line;
		lineControls[lineNo] = control;
		lineSizes[lineNo] = size;
		lineNumbers.Add(lineNo);
		totalLineHeight += size.Y;
		if (size.X > widestLineWidth)
			widestLineWidth = size.X;
	}

	// Remove one row from lookup tables and subtract its cached contribution from
	// the aggregate layout metrics.
	void UnregisterLine(int lineNo)
	{
		ReleaseLineTexturePins(lineNo);
		lineObjects.Remove(lineNo);
		lineControls.Remove(lineNo);
		lineNumbers.Remove(lineNo);
		if (!lineSizes.TryGetValue(lineNo, out var size))
			return;
		lineSizes.Remove(lineNo);
		totalLineHeight = System.Math.Max(0, totalLineHeight - size.Y);
		if (size.X >= widestLineWidth)
			RecalculateWidestLineWidth();
	}

	// Reset every line cache after a full clear.
	void ResetLineIndexes()
	{
		ResetLineTexturePins();
		lineObjects.Clear();
		lineControls.Clear();
		lineSizes.Clear();
		lineNumbers.Clear();
		totalLineHeight = 0;
		widestLineWidth = 0;
	}

	void RegisterLineTexturePins(int lineNo, List<SpriteManager.TextureInfo> pins)
	{
		// Store pins by emuera LineNo so trimming or replacing a specific row can
		// release exactly the textures owned by that visible Control.
		if (pins == null || pins.Count == 0)
			return;
		lineTexturePins[lineNo] = pins;
	}

	void TrackTexturePin(SpriteManager.TextureInfo ti)
	{
		if (ti == null || activeTexturePinCollector == null)
			return;
		// A single row may reference the same atlas multiple times. Deduplicate by
		// object identity to keep pin/unpin counts balanced and O(row image count).
		for (int i = 0; i < activeTexturePinCollector.Count; i++)
		{
			if (object.ReferenceEquals(activeTexturePinCollector[i], ti))
				return;
		}
		SpriteManager.PinTextureInfo(ti);
		activeTexturePinCollector.Add(ti);
	}

	void ReleaseLineTexturePins(int lineNo)
	{
		// Release is tied to row removal, not cache pressure. SpriteManager decides
		// later whether the now-unpinned texture is old enough to evict.
		if (!lineTexturePins.TryGetValue(lineNo, out var pins))
			return;
		for (int i = 0; i < pins.Count; i++)
			SpriteManager.UnpinTextureInfo(pins[i]);
		lineTexturePins.Remove(lineNo);
	}

	void ResetLineTexturePins()
	{
		foreach (var pins in lineTexturePins.Values)
		{
			for (int i = 0; i < pins.Count; i++)
				SpriteManager.UnpinTextureInfo(pins[i]);
		}
		lineTexturePins.Clear();
	}

	void ReleaseCbgTexturePins()
	{
		// CBG is refreshed as a whole layer set, so all previous background pins are
		// released before collecting the next frame's visible textures.
		for (int i = 0; i < cbgTexturePins.Count; i++)
			SpriteManager.UnpinTextureInfo(cbgTexturePins[i]);
		cbgTexturePins.Clear();
	}

	// Width is only rescanned when the removed row may have been the widest one.
	void RecalculateWidestLineWidth()
	{
		widestLineWidth = 0;
		foreach (var size in lineSizes.Values)
		{
			if (size.X > widestLineWidth)
				widestLineWidth = size.X;
		}
	}

	// Scroll tracing is intentionally centralized so noisy diagnostics can be
	// toggled from GenericUtils without leaving prints in mobile hot paths.
	void TraceScroll(string action, string detail = null)
	{
		if (!GenericUtils.ScrollTraceEnabled)
			return;
		string suffix = string.IsNullOrEmpty(detail) ? "" : " " + detail;
		GenericUtils.ScrollTrace("ui", $"{action}{suffix} {GetScrollTraceState()}");
	}

	string GetScrollTraceState()
	{
		if (scrollContainer == null)
			return "scroll=null";

		var limit = GetContentScrollLimit();
		var vScroll = scrollContainer.GetVScrollBar();
		int vMax = Mathf.RoundToInt(Mathf.Max(0, vScroll?.MaxValue ?? 0.0));
		int vPage = Mathf.RoundToInt(Mathf.Max(0, vScroll?.Page ?? 0.0));
		int vRangeMax = Mathf.Max(0, vMax - vPage);
		var scrollSize = scrollContainer.Size;
		var rootSize = scaledContentRoot != null ? scaledContentRoot.Size : Vector2.Zero;
		int lineCount = lineContainer != null ? lineContainer.GetChildCount() : -1;
		return $"scroll=({scrollContainer.ScrollHorizontal},{scrollContainer.ScrollVertical}) max=({limit.X},{vMax}) pageY={vPage} barY={vRangeMax} calcY={limit.Y} desired=({desiredContentScrollHorizontal},{desiredContentScrollVertical},valid={desiredContentScrollValid}) pending={pendingScroll} serial={pendingScrollInteractionSerial}/{contentScrollInteractionSerial} drag={contentDragActive}/{contentDragMoved}/btn={contentDragStartedOnButton} inertia={contentInertiaActive} lines={lineCount} totalH={Mathf.RoundToInt(totalLineHeight)} view=({Mathf.RoundToInt(scrollSize.X)},{Mathf.RoundToInt(scrollSize.Y)}) root=({Mathf.RoundToInt(rootSize.X)},{Mathf.RoundToInt(rootSize.Y)}) scale={contentScale:0.###}";
	}

	// Schedule a deferred scroll-to-bottom. We wait across frames because Godot
	// updates ScrollContainer range after child minimum sizes settle.
	void RequestScrollToBottom()
	{
		ulong now = Time.GetTicksMsec();
		pendingScrollInteractionSerial = contentScrollInteractionSerial;
		pendingScrollLastMax = int.MinValue;
		pendingScrollStableSinceTick = 0;
		pendingScrollDeadlineTick = now + ScrollToBottomRetryMs;
		if (pendingScroll)
		{
			TraceScroll("scroll_bottom_request_merge", $"deadline={pendingScrollDeadlineTick}");
			return;
		}
		pendingScroll = true;
		TraceScroll("scroll_bottom_request", $"deadline={pendingScrollDeadlineTick}");
		CallDeferred(nameof(DeferredScrollToBottom));
	}

	// Retry bottom scrolling until content height has remained stable long enough
	// or a user interaction starts. This prevents output batches from ending one
	// frame before the final buttons are laid out.
	async void DeferredScrollToBottom()
	{
		string endReason = "loop_end";
		bool loggedDragWait = false;
		TraceScroll("scroll_bottom_start");
		try
		{
			while (pendingScroll && scrollContainer != null)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				if (scrollContainer == null)
				{
					endReason = "scroll_null";
					break;
				}
				if (contentInertiaActive || pendingScrollInteractionSerial != contentScrollInteractionSerial)
				{
					endReason = contentInertiaActive ? "inertia_active" : "interaction_changed";
					break;
				}
				if (contentDragActive)
				{
					if (!loggedDragWait)
					{
						loggedDragWait = true;
						TraceScroll("scroll_bottom_wait_drag");
					}
					continue;
				}

				bool layoutChanged = UpdateScaleBounds();
				if (layoutChanged)
				{
					TraceScroll("scroll_bottom_wait_layout");
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
					if (scrollContainer == null)
					{
						endReason = "scroll_null_after_layout";
						break;
					}
					if (contentInertiaActive || pendingScrollInteractionSerial != contentScrollInteractionSerial)
					{
						endReason = contentInertiaActive ? "inertia_active" : "interaction_changed";
						break;
					}
					if (contentDragActive)
					{
						if (!loggedDragWait)
						{
							loggedDragWait = true;
							TraceScroll("scroll_bottom_wait_drag");
						}
						continue;
					}
				}
				SyncContentVerticalScrollRange();
				int maxScroll = GetMaxContentVerticalScroll();
				ulong now = Time.GetTicksMsec();
				if (maxScroll != pendingScrollLastMax)
				{
					pendingScrollLastMax = maxScroll;
					pendingScrollStableSinceTick = now;
					ulong stableDeadline = now + ScrollToBottomStableMs;
					if (pendingScrollDeadlineTick < stableDeadline)
						pendingScrollDeadlineTick = stableDeadline;
					TraceScroll("scroll_bottom_max", $"max={maxScroll} stableDeadline={stableDeadline}");
				}

				scrollContainer.ScrollVertical = maxScroll;
				RememberDesiredContentScroll(scrollContainer.ScrollHorizontal, maxScroll);
				bool atBottom = scrollContainer.ScrollVertical >= maxScroll - ScrollToBottomTolerancePx;
				bool maxStable = pendingScrollStableSinceTick > 0 && now - pendingScrollStableSinceTick >= ScrollToBottomStableMs;
				if (atBottom && maxStable)
				{
					endReason = "at_bottom";
					break;
				}
				if (now >= pendingScrollDeadlineTick)
				{
					endReason = "deadline";
					break;
				}
			}
		}
		finally
		{
			TraceScroll("scroll_bottom_end", $"reason={endReason}");
			pendingScroll = false;
			pendingScrollLastMax = int.MinValue;
			pendingScrollStableSinceTick = 0;
		}
	}

	// Queue one scale/layout pass for the next frame. Multiple line changes in
	// the same frame collapse into a single update.
	void QueueScaleBoundsUpdate()
	{
		if (pendingScaleBoundsUpdate)
			return;
		pendingScaleBoundsUpdate = true;
		CallDeferred(nameof(DeferredUpdateScaleBounds));
	}

	// Run deferred layout after Godot has processed pending child additions.
	async void DeferredUpdateScaleBounds()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		pendingScaleBoundsUpdate = false;
		UpdateScaleBounds();
	}

	// Enterprise UI policy for the exported APK console viewport:
	// the ScrollContainer must stay fully scrollable, but its visual scrollbars
	// are intentionally hidden. Phone users scroll by touch/drag, and exposing
	// thin desktop-style bars costs visible text width, creates misleading touch
	// targets, and can overlap emulator content. Do not change these modes back
	// to Auto/ShowAlways unless a concrete accessibility or debugging requirement
	// needs visible bars for a specific build.
	void ConfigureContentScrollContainer()
	{
		if (scrollContainer == null)
			return;

		scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever;
		scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		HideContentScrollBar(scrollContainer.GetHScrollBar());
		HideContentScrollBar(scrollContainer.GetVScrollBar());
	}

	// Defense in depth for themes/platform defaults: ShowNever is the authoritative
	// policy, and this keeps the child bars non-interactive and size-free if Godot
	// or a future theme still instantiates them internally.
	static void HideContentScrollBar(Godot.ScrollBar scrollBar)
	{
		if (scrollBar == null)
			return;
		scrollBar.Visible = false;
		scrollBar.MouseFilter = MouseFilterEnum.Ignore;
		scrollBar.CustomMinimumSize = Vector2.Zero;
	}

	// Recompute unscaled and scaled content bounds, then restore the intended
	// scroll position. When pendingScroll is active, vertical scroll is pinned to
	// the latest bottom limit.
	bool UpdateScaleBounds(bool allowShrink = true)
	{
		if (scaledContentRoot == null || lineContainer == null)
			return false;

		int previousHorizontal = desiredContentScrollValid
			? desiredContentScrollHorizontal
			: (scrollContainer != null ? scrollContainer.ScrollHorizontal : 0);
		int previousVertical = desiredContentScrollValid
			? desiredContentScrollVertical
			: (scrollContainer != null ? scrollContainer.ScrollVertical : 0);
		var scrollSize = scrollContainer != null ? scrollContainer.Size : Vector2.Zero;
		var contentSize = CalculateLineContentSize();
		var layoutSize = CalculateContentLayoutSize(contentSize);
		float safeScale = GetSafeContentScale();
		var scaledSize = CalculateScaledContentRootSize(layoutSize, scrollSize, safeScale);
		if (!allowShrink)
		{
			scaledSize.X = Mathf.Max(scaledSize.X, Mathf.Max(scaledContentRoot.CustomMinimumSize.X, scaledContentRoot.Size.X));
			scaledSize.Y = Mathf.Max(scaledSize.Y, Mathf.Max(scaledContentRoot.CustomMinimumSize.Y, scaledContentRoot.Size.Y));
		}

		bool layoutChanged =
			lineContainer.CustomMinimumSize != layoutSize ||
			lineContainer.Size != layoutSize ||
			scaledContentRoot.CustomMinimumSize != scaledSize ||
			scaledContentRoot.Size != scaledSize;

		lineContainer.CustomMinimumSize = layoutSize;
		lineContainer.Position = Vector2.Zero;
		lineContainer.Size = layoutSize;
		scaledContentRoot.Position = Vector2.Zero;
		scaledContentRoot.CustomMinimumSize = scaledSize;
		scaledContentRoot.Size = scaledSize;
		SyncContentVerticalScrollRange();

		if (scrollContainer != null)
		{
			var limit = GetContentScrollLimit();
			int targetHorizontal = Mathf.Clamp(previousHorizontal, 0, limit.X);
			int targetVertical = pendingScroll ? limit.Y : Mathf.Clamp(previousVertical, 0, limit.Y);
			int oldHorizontal = scrollContainer.ScrollHorizontal;
			int oldVertical = scrollContainer.ScrollVertical;
			scrollContainer.ScrollHorizontal = targetHorizontal;
			scrollContainer.ScrollVertical = targetVertical;
			RememberDesiredContentScroll(targetHorizontal, targetVertical);
			if (oldHorizontal != targetHorizontal || oldVertical != targetVertical)
				TraceScroll("scale_bounds_scroll_set", $"allowShrink={allowShrink} from=({oldHorizontal},{oldVertical}) to=({targetHorizontal},{targetVertical}) limit=({limit.X},{limit.Y})");
		}

		return layoutChanged;
	}

	// Keep scrollbar visuals hidden after Godot recreates or reconfigures them.
	// This intentionally does not write Page or MaxValue.
	void SyncContentVerticalScrollRange()
	{
		if (scrollContainer == null)
			return;

		// Keep ScrollContainer's own range calculation authoritative. Manually
		// writing ScrollBar.Page/MaxValue during content relayout can race
		// Godot's internal layout pass and temporarily snap the viewport.
		HideContentScrollBar(scrollContainer.GetHScrollBar());
		HideContentScrollBar(scrollContainer.GetVScrollBar());
	}

	// Capture the live viewport as the desired scroll target before sending input
	// to the core or before a layout-changing action.
	void RememberCurrentContentScroll()
	{
		if (scrollContainer == null)
			return;
		RememberDesiredContentScroll(scrollContainer.ScrollHorizontal, scrollContainer.ScrollVertical);
	}

	// Store the scroll target that ProcessContentScrollCorrection will preserve
	// across Godot layout passes.
	void RememberDesiredContentScroll(int horizontal, int vertical)
	{
		desiredContentScrollHorizontal = horizontal;
		desiredContentScrollVertical = vertical;
		desiredContentScrollValid = true;
	}

	// Protect divisions and scroll math from zero scale.
	float GetSafeContentScale()
	{
		return Mathf.Max(contentScale, 0.001f);
	}

	// Unscaled console layout must never be narrower than emuera's drawable area.
	Vector2 CalculateContentLayoutSize(Vector2 contentSize)
	{
		return new Vector2(Mathf.Max(Config.DrawableWidth, contentSize.X), contentSize.Y);
	}

	// Scaled root size is what ScrollContainer sees. It is clamped to viewport
	// size so empty or short output still fills the phone screen.
	Vector2 CalculateScaledContentRootSize(Vector2 layoutSize, Vector2 scrollSize, float safeScale)
	{
		return new Vector2(
			Mathf.Ceil(Mathf.Max(layoutSize.X * safeScale, scrollSize.X)),
			Mathf.Ceil(Mathf.Max(layoutSize.Y * safeScale, scrollSize.Y)));
	}

	// Fast layout size calculation from cached line metrics.
	Vector2 CalculateLineContentSize()
	{
		float width = Mathf.Max(Config.DrawableWidth, widestLineWidth);
		float height = totalLineHeight;
		int visibleRows = lineNumbers.Count;
		if (visibleRows > 1)
			height += (visibleRows - 1) * lineContainer.GetThemeConstant("separation");
		return new Vector2(width, height);
	}

	// Convert one emuera display part into Godot Controls under container.
	// relX is used for button/div-local coordinates.
	int AddPartToContainer(AConsoleDisplayPart part, Control container, int relX)
	{
		if(part is ConsoleStyledString css)
		{
			if (string.IsNullOrEmpty(css.Str))
				return EffectiveLineHeight;
			float posX = css.PointX - relX;
			float w;
			if (relX == 0)
			{
				float maxW = Config.DrawableWidth - css.PointX;
				w = css.Width > 0 ? System.Math.Min(css.Width, maxW) : maxW;
				if (w <= 0) w = 1;
			}
			else
			{
				w = css.Width > 0 ? css.Width : 9999;
			}
			var text = CreateTextPart(css.Str, css.pColor, css.Font, w);
			text.Position = new Vector2(posX, 0);
			container.AddChild(text);

			return EffectiveLineHeight;
		}
		else if(part is ConsoleDivPart div)
		{
			return AddDivPartToContainer(div, container, relX);
		}
		else if(part is ConsoleImagePart cip)
		{
			// Lazy retry: if cip.Image was null at construction time, try again now.
			// Dynamic sprites (e.g. CSPRITE/GDRAWCIMG-created 颜绘) may not have been
			// registered in imageDictionary when ConsoleImagePart was constructed.
			ASprite sprite = cip.Image;
			if (sprite == null && !string.IsNullOrEmpty(cip.ResourceName))
			{
				sprite = AppContents.GetSprite(cip.ResourceName);
			}

			var texture = GetSpriteTexture(sprite);
			if (texture == null && !string.IsNullOrEmpty(cip.ResourceName) && !IsDynamicCutinName(cip.ResourceName) && !failedTextureSearches.Contains(cip.ResourceName))
			{
				string resName = cip.ResourceName;
				var tryPaths = new List<string>
				{
					resName,
					System.IO.Path.Combine(Program.ContentDir, resName),
					System.IO.Path.Combine(Program.ExeDir, resName),
					System.IO.Path.Combine(Program.ExeDir, "resources", resName),
				};
				bool hasExt = resName.Contains(".");
				if (!hasExt)
				{
					foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tga" })
					{
						tryPaths.Add(resName + ext);
						tryPaths.Add(System.IO.Path.Combine(Program.ContentDir, resName + ext));
						tryPaths.Add(System.IO.Path.Combine(Program.ExeDir, "resources", resName + ext));
					}
				}
				foreach (var tryPath in tryPaths)
				{
					bool exists = uEmuera.Utils.FileExists(tryPath);
					if (exists)
					{
						var ti = SpriteManager.GetTextureInfo(resName, tryPath);
						if (ti != null)
						{
							texture = ti.texture;
							break;
						}
					}
				}
				// Subdirectory search: scan ContentDir recursively for a matching filename
				if (texture == null && !string.IsNullOrEmpty(Program.ContentDir))
				{
					string searchName = hasExt ? resName : null;
					string[] exts = hasExt ? new[] { "" } : new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tga" };
					foreach (var ext in exts)
					{
						string target = (searchName ?? resName) + ext;
						var found = uEmuera.Utils.FindFileRecursive(Program.ContentDir, target);
						if (!string.IsNullOrEmpty(found))
						{
							var ti = SpriteManager.GetTextureInfo(resName, found);
							if (ti != null)
							{
								GenericUtils.Info(EmueraLogCategory.Sprite, () => $"[IMG] Found \"{resName}\" via subdirectory search: {found}");
								texture = ti.texture;
								break;
							}
						}
					}
				}
				if (texture == null)
				{
					failedTextureSearches.Add(resName);
					GenericUtils.Info(EmueraLogCategory.Sprite, () => $"[IMG] All fallback paths failed for \"{resName}\"");
				}
			}
			if (texture != null)
			{
				int w, imgH;
				if (cip.dest_rect.Width > 0 && cip.dest_rect.Height > 0)
				{
					// Both dimensions specified explicitly
					w = cip.dest_rect.Width;
					imgH = cip.dest_rect.Height;
				}
				else if (cip.dest_rect.Width > 0)
				{
					// Width specified, compute height from aspect ratio
					w = cip.dest_rect.Width;
					imgH = texture.GetHeight() > 0 ? texture.GetHeight() * w / texture.GetWidth() : w;
				}
				else if (cip.dest_rect.Height > 0 && texture.GetHeight() > 0)
				{
					// Height specified, compute width from aspect ratio (matches constructor logic)
					imgH = cip.dest_rect.Height;
					w = texture.GetWidth() * imgH / texture.GetHeight();
				}
				else
				{
					// No dimensions specified, use natural size
					w = texture.GetWidth() > 0 ? texture.GetWidth() : 32;
					imgH = texture.GetHeight() > 0 ? texture.GetHeight() : 32;
				}
				// Ensure rendered width matches layout-allocated width to prevent gaps/overlaps
				if (cip.Width > 0 && cip.Width != w)
				{
					int layoutW = cip.Width;
					if (w > 0)
						imgH = imgH * layoutW / w;
					w = layoutW;
				}

				var emuImg = new EmueraImage();
				emuImg.MouseFilter = MouseFilterEnum.Ignore;
				if (texture is AtlasTexture atlas)
				{
					emuImg.SourceTexture = atlas.Atlas;
					emuImg.SourceRegion = atlas.Region;
				}
				else
				{
					emuImg.SourceTexture = texture;
				}
				emuImg.DrawOffset = GetSpriteHtmlDrawOffset(sprite, cip.ResourceName, w, imgH);
				emuImg.DrawSize = GetSpriteHtmlDrawSize(sprite, cip.ResourceName, w, imgH);
				emuImg.Position = GetHtmlImagePosition(cip, relX);
				emuImg.Size = new Vector2(w, imgH);
				emuImg.FlipX = cip.FlipX;
				emuImg.FlipY = cip.FlipY;
				emuImg.SetColorMatrix(cip.ColorMatrix);
				// Inline images are absolutely positioned inside a fixed-height Emuera line.
				// Giving them a minimum size lets Godot containers add blank vertical space.
				emuImg.CustomMinimumSize = Vector2.Zero;
				container.AddChild(emuImg);
				return EffectiveLineHeight;
			}
			else
			{
				var spacer = new Control();
				spacer.MouseFilter = MouseFilterEnum.Ignore;
				float placeholderWidth = System.Math.Max(cip.Width, EffectiveLineHeight);
				SetFixedControlSize(spacer, new Vector2(placeholderWidth, EffectiveLineHeight));
				spacer.Position = new Vector2(cip.PointX - relX, 0);
				container.AddChild(spacer);
				return EffectiveLineHeight;
			}
		}
		else if(part is ConsoleShapePart csp)
		{
			if (csp is ConsoleRectangleShapePart rectShape)
			{
				var colorRect = new ColorRect();
				colorRect.MouseFilter = MouseFilterEnum.Ignore;
				colorRect.CustomMinimumSize = new Vector2(rectShape.Width, rectShape.Bottom - rectShape.Top);
				var sc = rectShape.pColor;
				colorRect.Color = new Godot.Color(sc.r, sc.g, sc.b, sc.a);
				colorRect.Position = new Vector2(rectShape.PointX - relX, rectShape.Top);
				container.AddChild(colorRect);
				return rectShape.Bottom;
			}
			else if (csp is ConsoleSpacePart)
			{
				if (csp.Width > 0)
				{
					var spacer = new Control();
					spacer.MouseFilter = MouseFilterEnum.Ignore;
					spacer.CustomMinimumSize = new Vector2(csp.Width, FontSize);
					spacer.Position = new Vector2(csp.PointX - relX, 0);
					container.AddChild(spacer);
				}
				return EffectiveLineHeight;
			}
			else if (csp is ConsoleErrorShapePart errShape)
			{
				float labelWidth = System.Math.Max(csp.Width, EffectiveLineHeight);
				var text = CreateTextPart(errShape.AltText ?? errShape.Str ?? "", Config.ForeColor, Config.Font, labelWidth);
				text.Position = new Vector2(csp.PointX - relX, 0);
				container.AddChild(text);
				return EffectiveLineHeight;
			}
			return EffectiveLineHeight;
		}
		return EffectiveLineHeight;
	}

	// Add an HTML-like div part and return the row height it contributes.
	int AddDivPartToContainer(ConsoleDivPart div, Control container, int relX)
	{
		var wrapper = BuildDivControl(div, relX);
		container.AddChild(wrapper);
		if (!div.IsRelative)
			return EffectiveLineHeight;
		return System.Math.Max(EffectiveLineHeight, div.Y + div.DivHeight);
	}

	// Build a nested Control tree for styled div output. Margins, borders, and
	// padding are drawn manually because this is emuera console layout rather
	// than Godot theme layout.
	Control BuildDivControl(ConsoleDivPart div, int relX)
	{
		var wrapper = new Control();
		wrapper.MouseFilter = MouseFilterEnum.Pass;
		wrapper.ClipContents = true;
		wrapper.Position = GetHtmlDivPosition(div, relX);
		wrapper.Size = new Vector2(div.DivWidth, div.DivHeight);
		wrapper.CustomMinimumSize = new Vector2(div.DivWidth, div.DivHeight);
		wrapper.ZIndex = GetGodotZIndexForHtmlDepth(div.Depth);

		int[] margin = div.StyledBox?.Margin;
		int[] padding = div.StyledBox?.Padding;
		int[] border = div.StyledBox?.Border;
		int[] borderColor = div.StyledBox?.BorderColor;

		int marginLeft = BoxValue(margin, BoxDirection.Left);
		int marginTop = BoxValue(margin, BoxDirection.Top);
		int marginRight = BoxValue(margin, BoxDirection.Right);
		int marginBottom = BoxValue(margin, BoxDirection.Bottom);
		int borderLeft = BoxValue(border, BoxDirection.Left);
		int borderTop = BoxValue(border, BoxDirection.Top);
		int borderRight = BoxValue(border, BoxDirection.Right);
		int borderBottom = BoxValue(border, BoxDirection.Bottom);
		int paddingLeft = BoxValue(padding, BoxDirection.Left);
		int paddingTop = BoxValue(padding, BoxDirection.Top);
		int paddingRight = BoxValue(padding, BoxDirection.Right);
		int paddingBottom = BoxValue(padding, BoxDirection.Bottom);

		float boxX = marginLeft;
		float boxY = marginTop;
		float boxW = Mathf.Max(0, div.DivWidth - marginLeft - marginRight);
		float boxH = Mathf.Max(0, div.DivHeight - marginTop - marginBottom);

		if (div.BackgroundColor.HasValue && boxW > 0 && boxH > 0)
		{
			var bg = new ColorRect();
			bg.MouseFilter = MouseFilterEnum.Ignore;
			var c = div.BackgroundColor.Value;
			bg.Color = new Godot.Color(c.r, c.g, c.b, c.a);
			bg.Position = new Vector2(boxX, boxY);
			bg.Size = new Vector2(boxW, boxH);
			wrapper.AddChild(bg);
		}

		AddDivBorder(wrapper, border, borderColor, boxX, boxY, boxW, boxH);

		var content = new Control();
		content.MouseFilter = MouseFilterEnum.Pass;
		content.ClipContents = true;
		content.Position = new Vector2(boxX + borderLeft + paddingLeft, boxY + borderTop + paddingTop);
		content.Size = new Vector2(
			Mathf.Max(0, boxW - borderLeft - borderRight - paddingLeft - paddingRight),
			Mathf.Max(0, boxH - borderTop - borderBottom - paddingTop - paddingBottom));
		content.CustomMinimumSize = content.Size;
		wrapper.AddChild(content);

		int y = 0;
		foreach (var childLine in div.Children)
		{
			AddDisplayLineToContainer(childLine, content, y);
			y += EffectiveLineHeight;
		}

		return wrapper;
	}

	// Render a child ConsoleDisplayLine into an existing container at yOffset.
	// Used by nested divs and island output.
	int AddDisplayLineToContainer(ConsoleDisplayLine line, Control container, int yOffset)
	{
		if (line == null)
			return 0;
		var row = new Control();
		row.MouseFilter = MouseFilterEnum.Pass;
		row.ClipContents = false;
		row.Position = new Vector2(0, yOffset);

		foreach (var button in line.Buttons)
		{
			if (button.IsButton)
			{
				int buttonTop = GetButtonTop(button);
				int buttonHeight = GetButtonBottom(button) - buttonTop;
				if (buttonHeight <= 0)
					buttonHeight = EffectiveLineHeight;
				var btn = new Panel();
				btn.FocusMode = FocusModeEnum.None;
				btn.MouseForcePassScrollEvents = false;
				btn.MouseFilter = MouseFilterEnum.Stop;
				btn.ClipContents = false;
				EnsureButtonStyles();
				btn.AddThemeStyleboxOverride("panel", _btnNormalStyle);
				string inputs = button.Inputs;
				long generation = button.Generation;
				btn.GuiInput += inputEvent => OnContentButtonGuiInput(inputEvent, btn, inputs, generation);
				btn.MouseEntered += () => GenericUtils.SetPointingButton(inputs, generation);
				btn.MouseExited += () => GenericUtils.ClearPointingButton(generation);
				btn.SetMeta("generation", generation);

				var contentBox = new Control();
				contentBox.MouseFilter = MouseFilterEnum.Ignore;
				contentBox.ClipContents = false;
				contentBox.Position = new Vector2(0, -buttonTop);
				btn.AddChild(contentBox);

				foreach (var part in button.StrArray)
					AddPartToContainer(part, contentBox, button.PointX);

				foreach (var child in contentBox.GetChildren())
				{
					if (child is Control c)
						c.MouseFilter = MouseFilterEnum.Ignore;
				}

				SetFixedControlSize(contentBox, new Vector2(button.Width, buttonHeight));
				btn.CustomMinimumSize = new Vector2(button.Width, buttonHeight);
				btn.Position = new Vector2(button.PointX, buttonTop);
				btn.Size = new Vector2(button.Width, buttonHeight);
				row.AddChild(btn);
			}
			else
			{
				foreach (var part in button.StrArray)
					AddPartToContainer(part, row, 0);
			}
		}

		int maxHeight = GetLineBottom(line);
		SetFixedControlSize(row, new Vector2(GetLineRight(line), maxHeight));
		container.AddChild(row);
		return maxHeight;
	}

	// Compute the right edge of a console line for manual minimum-size tracking.
	static int GetLineRight(ConsoleDisplayLine line)
	{
		int right = 0;
		if (line?.Buttons == null)
			return right;
		foreach (var button in line.Buttons)
		{
			if (button == null)
				continue;
			right = System.Math.Max(right, button.PointX + button.Width);
		}
		return right;
	}

	// Draw each div border side as a ColorRect so per-side widths and colors match
	// emuera HTML styling.
	void AddDivBorder(Control wrapper, int[] border, int[] borderColor, float boxX, float boxY, float boxW, float boxH)
	{
		if (border == null || borderColor == null || boxW <= 0 || boxH <= 0)
			return;
		AddBorderRect(wrapper, boxX, boxY, boxW, BoxValue(border, BoxDirection.Top), ColorValue(borderColor, BoxDirection.Top));
		AddBorderRect(wrapper, boxX + boxW - BoxValue(border, BoxDirection.Right), boxY, BoxValue(border, BoxDirection.Right), boxH, ColorValue(borderColor, BoxDirection.Right));
		AddBorderRect(wrapper, boxX, boxY + boxH - BoxValue(border, BoxDirection.Bottom), boxW, BoxValue(border, BoxDirection.Bottom), ColorValue(borderColor, BoxDirection.Bottom));
		AddBorderRect(wrapper, boxX, boxY, BoxValue(border, BoxDirection.Left), boxH, ColorValue(borderColor, BoxDirection.Left));
	}

	// Add one border rectangle if the side has positive thickness.
	void AddBorderRect(Control wrapper, float x, float y, float w, float h, int color)
	{
		if (w <= 0 || h <= 0 || color < 0)
			return;
		var rect = new ColorRect();
		rect.MouseFilter = MouseFilterEnum.Ignore;
		rect.Color = new Godot.Color(((color >> 16) & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, (color & 0xFF) / 255f, 1f);
		rect.Position = new Vector2(x, y);
		rect.Size = new Vector2(w, h);
		wrapper.AddChild(rect);
	}

	// Safe CSS-like four-value lookup.
	static int BoxValue(int[] values, int index)
	{
		if (values == null || index < 0 || index >= values.Length)
			return 0;
		return values[index];
	}

	// Safe border-color lookup with transparent fallback.
	static int ColorValue(int[] values, int index)
	{
		if (values == null || index < 0 || index >= values.Length)
			return -1;
		return values[index];
	}

	// Resolve div coordinates into the target container's local coordinate space.
	Vector2 GetHtmlDivPosition(ConsoleDivPart div, int relX)
	{
		switch (div.Display)
		{
			case DisplayMode.Absolute:
			case DisplayMode.AbsoluteLeftBottom:
				return new Vector2(div.X, GetContentViewportHeight() - div.Y - div.DivHeight);
			case DisplayMode.AbsoluteLeftTop:
				return new Vector2(div.X, div.Y);
			default:
				return new Vector2(div.PointX - relX + div.X, div.Y);
		}
	}

	// Keep emuera HTML depth order stable while mapping it into Godot's ZIndex.
	static int GetGodotZIndexForHtmlDepth(int depth)
	{
		return -depth;
	}

	// Resolve absolute/relative image placement for HTML-style output.
	Vector2 GetHtmlImagePosition(ConsoleImagePart imagePart, int relX)
	{
		switch (imagePart.Display)
		{
			case DisplayMode.Absolute:
			case DisplayMode.AbsoluteLeftBottom:
				return new Vector2(
					imagePart.PositionX + imagePart.dest_rect.X,
					GetContentViewportHeight() + imagePart.PositionY);
			case DisplayMode.AbsoluteLeftTop:
				return new Vector2(
					imagePart.PositionX + imagePart.dest_rect.X,
					imagePart.PositionY);
			default:
				return new Vector2(
					imagePart.PointX - relX + imagePart.PositionX + imagePart.dest_rect.X,
					imagePart.dest_rect.Y);
		}
	}

	// Apply sprite-origin metadata when HTML images use emuera sprite resources.
	static bool TryGetSpriteHtmlBasePosition(ASprite sprite, string resourceName, out uEmuera.Drawing.Point basePosition)
	{
		basePosition = uEmuera.Drawing.Point.Empty;
		if (sprite != null && !sprite.DestBasePosition.IsEmpty)
		{
			basePosition = sprite.DestBasePosition;
			return true;
		}
		if (AppContents.TryGetSpriteBasePosition(resourceName, out var cachedPosition) && !cachedPosition.IsEmpty)
		{
			basePosition = cachedPosition;
			return true;
		}
		return false;
	}

	static bool ShouldUseSpriteHtmlCanvas(ASpriteSingle single, string resourceName)
	{
		if (single == null || single.DestBaseSize.Width <= 0 || single.DestBaseSize.Height <= 0)
			return false;
		int srcW = System.Math.Abs(single.SrcRectangle.Width);
		int srcH = System.Math.Abs(single.SrcRectangle.Height);
		if (srcW == 0 || srcH == 0)
			return false;
		return srcW != single.DestBaseSize.Width
			|| srcH != single.DestBaseSize.Height
			|| TryGetSpriteHtmlBasePosition(single, resourceName, out _);
	}

	static Vector2 GetSpriteHtmlDrawOffset(ASprite sprite, string resourceName, int width, int height)
	{
		if (width == 0 || height == 0)
			return Vector2.Zero;
		if (sprite == null)
			return Vector2.Zero;

		if (!TryGetSpriteHtmlBasePosition(sprite, resourceName, out var basePosition))
			return Vector2.Zero;

		if (sprite is ASpriteSingle)
		{
			if (sprite.DestBaseSize.Width == 0 || sprite.DestBaseSize.Height == 0)
				return Vector2.Zero;
			return new Vector2(
				basePosition.X * width / (float)sprite.DestBaseSize.Width,
				basePosition.Y * height / (float)sprite.DestBaseSize.Height);
		}

		if (sprite.DestBaseSize.Width == 0 || sprite.DestBaseSize.Height == 0)
			return Vector2.Zero;
		return new Vector2(
			basePosition.X * width / (float)sprite.DestBaseSize.Width,
			basePosition.Y * height / (float)sprite.DestBaseSize.Height);
	}

	static Vector2 GetSpriteHtmlDrawSize(ASprite sprite, string resourceName, int width, int height)
	{
		if (width == 0 || height == 0)
			return new Vector2(width, height);
		if (sprite is ASpriteSingle single
			&& ShouldUseSpriteHtmlCanvas(single, resourceName))
		{
			int srcW = single.SrcRectangle.Width;
			int srcH = single.SrcRectangle.Height;
			if (srcW > 0 && srcH > 0)
			{
				return new Vector2(
					srcW * width / (float)sprite.DestBaseSize.Width,
					srcH * height / (float)sprite.DestBaseSize.Height);
			}
		}
		return new Vector2(width, height);
	}

	// Dynamic cut-ins are generated by script and may not exist as files yet.
	static bool IsDynamicCutinName(string name)
	{
		if (string.IsNullOrEmpty(name) || !name.StartsWith("CUTIN", StringComparison.OrdinalIgnoreCase) || name.Length == 5)
			return false;
		for (int i = 5; i < name.Length; i++)
		{
			if (!char.IsDigit(name[i]))
				return false;
		}
		return true;
	}

	// Convert emuera sprite/image abstractions into Godot textures. TextureInfo
	// backed outputs are tracked for the active render scope before returning so
	// cache cleanup cannot dispose a texture still assigned to a visible Control.
	// AtlasTexture is used when a frame only references a source rectangle.
	Texture2D GetSpriteTexture(ASprite sprite)
	{
		if (sprite == null)
			return null;

		if (sprite.Bitmap is uEmuera.Drawing.BitmapTexture bt)
		{
			var ti = bt.TextureInfo;
			if (ti == null || ti.texture == null)
				return null;
			TrackTexturePin(ti);
			if (sprite is ASpriteSingle single)
			{
				var srcRect = single.SrcRectangle;
				if (srcRect.X == 0 && srcRect.Y == 0 &&
					srcRect.Width == ti.texture.GetWidth() &&
					srcRect.Height == ti.texture.GetHeight())
				{
					return ti.texture;
				}
				return ti.GetAtlasTexture(BuildAtlasCacheKey(sprite, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height),
					new Rect2(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height));
			}
			return ti.texture;
		}

		if (sprite is ASpriteSingle singleSprite)
		{
			if (singleSprite.BaseImage is GraphicsImage gImg && gImg.godotImage != null)
			{
				var srcRect = singleSprite.SrcRectangle;
				if (srcRect.X == 0 && srcRect.Y == 0 &&
					srcRect.Width == gImg.godotImage.GetWidth() &&
					srcRect.Height == gImg.godotImage.GetHeight())
				{
					return Godot.ImageTexture.CreateFromImage(gImg.godotImage);
				}
				var region = gImg.godotImage.GetRegion(new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height));
				if (region != null)
					return Godot.ImageTexture.CreateFromImage(region);
				return Godot.ImageTexture.CreateFromImage(gImg.godotImage);
			}
			if (singleSprite.BaseImage?.Bitmap != null)
			{
				var bmp = singleSprite.BaseImage.Bitmap;
				var ti = SpriteManager.GetTextureInfo(bmp.path, bmp.path);
				if (ti == null && !string.IsNullOrEmpty(bmp.filename))
					ti = SpriteManager.GetTextureInfo(bmp.filename, bmp.path);
				if (ti != null)
				{
					TrackTexturePin(ti);
					return ti.GetAtlasTexture(
						BuildAtlasCacheKey(sprite, singleSprite.SrcRectangle.X, singleSprite.SrcRectangle.Y,
							singleSprite.SrcRectangle.Width, singleSprite.SrcRectangle.Height),
						new Rect2(singleSprite.SrcRectangle.X, singleSprite.SrcRectangle.Y,
							singleSprite.SrcRectangle.Width, singleSprite.SrcRectangle.Height));
				}
			}
		}

		if (sprite is SpriteAnime anime)
		{
			AbstractImage baseImage;
			uEmuera.Drawing.Rectangle srcRect;
			uEmuera.Drawing.Point offset;
			if (anime.GetCurrentFrameInfo(out baseImage, out srcRect, out offset))
			{
				if (baseImage is GraphicsImage gImg && gImg.godotImage != null)
				{
					if (srcRect.X == 0 && srcRect.Y == 0 &&
						srcRect.Width == gImg.godotImage.GetWidth() &&
						srcRect.Height == gImg.godotImage.GetHeight())
					{
						return Godot.ImageTexture.CreateFromImage(gImg.godotImage);
					}
					var region = gImg.godotImage.GetRegion(new Godot.Rect2I(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height));
					if (region != null)
						return Godot.ImageTexture.CreateFromImage(region);
					return Godot.ImageTexture.CreateFromImage(gImg.godotImage);
				}
				if (baseImage?.Bitmap != null)
				{
					var bmp = baseImage.Bitmap;
					var ti = SpriteManager.GetTextureInfo(bmp.path, bmp.path);
					if (ti == null && !string.IsNullOrEmpty(bmp.filename))
						ti = SpriteManager.GetTextureInfo(bmp.filename, bmp.path);
					if (ti != null)
					{
						TrackTexturePin(ti);
						return ti.GetAtlasTexture(
							BuildAtlasCacheKey(sprite, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height),
							new Rect2(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height));
					}
				}
			}
		}

		return null;
	}

	static string BuildAtlasCacheKey(ASprite sprite, int x, int y, int width, int height)
	{
		string name = sprite?.Name ?? "";
		return $"{name}:{x},{y},{width},{height}";
	}

	// Lookup a currently rendered console line by emuera line number.
	internal ConsoleDisplayLine GetLine(int lineno)
	{
		lineObjects.TryGetValue(lineno, out var line);
		return line;
	}

	// Highest currently retained emuera line number.
	public int GetMaxLineNo()
	{
		return lineNumbers.Count == 0 ? -1 : lineNumbers.Max;
	}

	// Lowest currently retained emuera line number after trimming.
	public int GetMinLineNo()
	{
		return lineNumbers.Count == 0 ? -1 : lineNumbers.Min;
	}

	// Trim old rows from the top to cap memory and scene-tree size.
	public void RemoveTopLines(int count)
	{
		bool removedAny = false;
		if (count > 0)
			GenericUtils.ClearPointingButton();
		for(int i = 0; i < count && lineContainer.GetChildCount() > 0; i++)
		{
			var child = lineContainer.GetChild(0);
			removedAny = true;
			if (child.HasMeta("line_no"))
			{
				int lineNo = (int)child.GetMeta("line_no");
				UnregisterLine(lineNo);
			}
			SafeQueueFree(child);
		}
		if (removedAny)
		{
			displayRevision++;
			RefreshQuickInputGate();
		}
	}

	// Re-apply the row cap after the user changes MaxVisibleLines.
	public void TrimVisibleLinesToLimit()
	{
		if (lineContainer == null)
			return;
		int overflow = lineContainer.GetChildCount() - MaxVisibleLines;
		if (overflow > 0)
			RemoveTopLines(overflow);
	}

	// Remove recent rows when the core overwrites or updates the bottom output.
	public void RemoveBottomLines(int count)
	{
		bool removedAny = false;
		if (count > 0)
			GenericUtils.ClearPointingButton();
		for(int i = 0; i < count && lineContainer.GetChildCount() > 0; i++)
		{
			var child = lineContainer.GetChild(lineContainer.GetChildCount() - 1);
			removedAny = true;
			if (child.HasMeta("line_no"))
			{
				int lineNo = (int)child.GetMeta("line_no");
				UnregisterLine(lineNo);
			}
			SafeQueueFree(child);
		}
		if (removedAny)
		{
			displayRevision++;
			RefreshQuickInputGate();
		}
	}

	// Remove a node from the tree before QueueFree so container layout updates
	// immediately and no stale input callbacks keep firing.
	static void SafeQueueFree(Node node)
	{
		node.SetProcess(false);
		node.SetPhysicsProcess(false);
		node.SetProcessInput(false);
		node.SetProcessUnhandledInput(false);
		node.SetProcessUnhandledKeyInput(false);
		node.GetParent()?.RemoveChild(node);
		node.QueueFree();
	}

	// Kept for compatibility with older callers. Layout is now updated through
	// deferred size passes instead of a separate imperative redraw call.
	public void UpdateDisplay()
	{
		// Layout is handled automatically by Godot containers
	}

	// Refresh client background graphics from the core. Nodes are reused by index
	// so animated/background-heavy scenes avoid repeated allocation.
	internal void RefreshCBG(List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage> list)
	{
		if (cbgContainer == null)
			return;

		ReleaseCbgTexturePins();
		if (list == null || list.Count == 0)
		{
			TrimCbgNodes(0);
			return;
		}

		int nodeIndex = 0;
		renderedCbgLayers.Clear();
		int currentScrollY = GetCurrentContentScrollY();
		// Treat one CBG refresh as an ownership transaction. GetSpriteTexture pins
		// into this temporary collector, then the collector becomes cbgTexturePins
		// only after all visible layers have been rebuilt.
		var previousTexturePinCollector = activeTexturePinCollector;
		var newCbgTexturePins = new List<SpriteManager.TextureInfo>();
		activeTexturePinCollector = newCbgTexturePins;
		try
		{
			foreach (var cbg in list)
			{
				if (cbg.zdepth == 0)
					continue;
				if (cbg.Img == null || !cbg.Img.IsCreated)
					continue;

				var texture = GetSpriteTexture(cbg.Img);
				if (texture == null)
					continue;

				var emuImg = GetOrCreateCbgNode(nodeIndex);
				if (texture is AtlasTexture atlas)
				{
					emuImg.SourceTexture = atlas.Atlas;
					emuImg.SourceRegion = atlas.Region;
				}
				else
				{
					emuImg.SourceTexture = texture;
					emuImg.SourceRegion = default;
				}
				bool flipX = cbg.width < 0;
				bool flipY = cbg.height < 0;
				int w = cbg.width != 0 ? System.Math.Abs(cbg.width) : (cbg.Img.DestBaseSize.Width > 0 ? cbg.Img.DestBaseSize.Width : texture.GetWidth());
				int h = cbg.height != 0 ? System.Math.Abs(cbg.height) : (cbg.Img.DestBaseSize.Height > 0 ? cbg.Img.DestBaseSize.Height : texture.GetHeight());
				emuImg.DrawOffset = GetSpriteHtmlDrawOffset(cbg.Img, cbg.Img.Name, w, h);
				emuImg.DrawSize = GetSpriteHtmlDrawSize(cbg.Img, cbg.Img.Name, w, h);
				emuImg.Position = GetCbgLayerPosition(cbg, currentScrollY);
				emuImg.Size = new Vector2(w, h);
				emuImg.FlipX = flipX;
				emuImg.FlipY = flipY;
				emuImg.Modulate = new Godot.Color(1, 1, 1, cbg.opacity);
				emuImg.SetColorMatrix(cbg.colorMatrix);
				emuImg.Visible = true;
				renderedCbgLayers.Add(cbg);
				nodeIndex++;
			}
		}
		finally
		{
			activeTexturePinCollector = previousTexturePinCollector;
			cbgTexturePins = newCbgTexturePins;
		}
		lastCbgScrollVertical = currentScrollY;
		TrimCbgNodes(nodeIndex);
	}

	// Convert emuera CBG z-depth rules into a Godot position. Positive z-depth
	// layers follow content scroll, while other depths remain screen-relative.
	Vector2 GetCbgLayerPosition(MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage cbg, int currentScrollY)
	{
		int y = cbg.y;
		if (cbg.followScroll)
		{
			if (cbg.initialScrollY == int.MinValue)
				cbg.initialScrollY = currentScrollY;
			y -= currentScrollY - cbg.initialScrollY;
		}
		return new Vector2(cbg.x, y);
	}

	// Current vertical scroll used by CBG positioning and animation culling.
	int GetCurrentContentScrollY()
	{
		return scrollContainer != null ? scrollContainer.ScrollVertical : 0;
	}

	// Keep scroll-following CBG layers in sync without rebuilding their nodes.
	void RefreshCbgFollowScrollPositions()
	{
		if (renderedCbgLayers.Count == 0 || cbgNodes.Count == 0)
			return;
		int currentScrollY = GetCurrentContentScrollY();
		if (currentScrollY == lastCbgScrollVertical)
			return;
		int count = System.Math.Min(renderedCbgLayers.Count, cbgNodes.Count);
		for (int i = 0; i < count; i++)
		{
			var cbg = renderedCbgLayers[i];
			if (cbg != null && cbg.followScroll && cbgNodes[i] != null)
				cbgNodes[i].Position = GetCbgLayerPosition(cbg, currentScrollY);
		}
		lastCbgScrollVertical = currentScrollY;
	}

	EmueraImage GetOrCreateCbgNode(int index)
	{
		while (cbgNodes.Count <= index)
		{
			var node = new EmueraImage();
			node.MouseFilter = MouseFilterEnum.Ignore;
			cbgNodes.Add(node);
			cbgContainer.AddChild(node);
		}
		return cbgNodes[index];
	}

	// Compatibility overload for legacy callers that pass a loop flag.
	public void PlaySoundFile(string path, bool loop, int channel)
	{
		PlaySoundFile(path, loop ? -1 : 1, channel);
	}

	// Play a sound effect on a logical emuera channel. repeat < 0 means loop,
	// repeat > 0 means replay a fixed number of times.
	public void PlaySoundFile(string path, int repeat, int channel)
	{
		bool loop = repeat < 0;
		var stream = LoadAudioStream(path, loop);
		if (stream == null)
		{
			GenericUtils.NotifySoundPlaybackFailed(channel, path);
			return;
		}
		while (soundPlayers.Count <= channel)
		{
			int newChannel = soundPlayers.Count;
			var newPlayer = new AudioStreamPlayer();
			newPlayer.Finished += () => OnSoundPlayerFinished(newChannel);
			soundPlayers.Add(newPlayer);
			soundRepeatRemaining.Add(0);
			AddChild(newPlayer);
		}
		AudioStreamPlayer player = soundPlayers[channel];
		player.Stop();
		player.Stream = stream;
		player.VolumeDb = LinearToDb(soundVolume);
		soundRepeatRemaining[channel] = loop ? -1 : Math.Max(repeat, 1);
		player.Play();
		GenericUtils.NotifySoundPlaybackStarted(channel, path, GetAudioStreamLengthMs(stream));
	}

	// Replay or stop a channel when Godot reports that playback finished.
	void OnSoundPlayerFinished(int channel)
	{
		if (channel < 0 || channel >= soundPlayers.Count || channel >= soundRepeatRemaining.Count)
			return;
		int remaining = soundRepeatRemaining[channel];
		if (remaining < 0)
			return;
		remaining--;
		soundRepeatRemaining[channel] = remaining;
		if (remaining > 0)
			soundPlayers[channel].Play();
	}

	// Stop all sound effect channels without touching BGM.
	public void StopSounds()
	{
		for (int i = 0; i < soundPlayers.Count; i++)
		{
			soundRepeatRemaining[i] = 0;
			soundPlayers[i].Stop();
		}
	}

	// Stop one sound effect channel if it exists.
	public void StopSoundChannel(int channel)
	{
		if (channel < 0 || channel >= soundPlayers.Count)
			return;
		if (channel < soundRepeatRemaining.Count)
			soundRepeatRemaining[channel] = 0;
		soundPlayers[channel].Stop();
	}

	// Pause/resume one sound channel for emuera SOUNDSTOP/SOUNDPLAY semantics.
	public void PauseSoundChannel(int channel, bool paused)
	{
		if (channel < 0 || channel >= soundPlayers.Count)
			return;
		soundPlayers[channel].StreamPaused = paused;
	}

	// Adjust playback speed for one sound effect channel.
	public void SetSoundChannelSpeed(int channel, float speed)
	{
		if (channel < 0 || channel >= soundPlayers.Count)
			return;
		soundPlayers[channel].PitchScale = Mathf.Max(0.01f, speed);
	}

	// Start BGM, replacing any existing track.
	public void PlayBgmFile(string path)
	{
		var stream = LoadAudioStream(path, true);
		if (stream == null)
		{
			GenericUtils.NotifyBgmPlaybackFailed(path);
			return;
		}
		if (bgmPlayer == null)
		{
			bgmPlayer = new AudioStreamPlayer();
			AddChild(bgmPlayer);
		}
		bgmPlayer.Stop();
		bgmPlayer.Stream = stream;
		bgmPlayer.VolumeDb = LinearToDb(bgmVolume);
		bgmPlayer.Play();
		GenericUtils.NotifyBgmPlaybackStarted(path, GetAudioStreamLengthMs(stream));
	}

	// Stop the active BGM track.
	public void StopBgm()
	{
		bgmPlayer?.Stop();
	}

	// Pause or resume BGM without releasing the stream.
	public void PauseBgm(bool paused)
	{
		if (bgmPlayer == null)
			return;
		bgmPlayer.StreamPaused = paused;
	}

	// Android can suspend or throttle apps aggressively in the background. Keep
	// audio state reversible so a script-paused channel stays paused after resume.
	public void SetApplicationPaused(bool paused)
	{
		if (applicationPauseActive == paused)
			return;
		applicationPauseActive = paused;
		if (paused)
		{
			// Preserve each logical channel's script-controlled pause state before
			// forcing an Android lifecycle pause, so resume does not accidentally
			// unpause audio that the era script had already paused.
			bgmPausedBeforeApplicationPause = bgmPlayer != null && bgmPlayer.StreamPaused;
			if (bgmPlayer != null)
				bgmPlayer.StreamPaused = true;
			soundPausedBeforeApplicationPause.Clear();
			for (int i = 0; i < soundPlayers.Count; i++)
			{
				var player = soundPlayers[i];
				bool wasPaused = player != null && player.StreamPaused;
				soundPausedBeforeApplicationPause.Add(wasPaused);
				if (player != null)
					player.StreamPaused = true;
			}
			StopContentInertia();
			return;
		}

		if (bgmPlayer != null)
			bgmPlayer.StreamPaused = bgmPausedBeforeApplicationPause;
		for (int i = 0; i < soundPlayers.Count && i < soundPausedBeforeApplicationPause.Count; i++)
		{
			var player = soundPlayers[i];
			if (player != null)
				player.StreamPaused = soundPausedBeforeApplicationPause[i];
		}
		soundPausedBeforeApplicationPause.Clear();
	}

	// Adjust BGM pitch/tempo through Godot's pitch scale.
	public void SetBgmSpeed(float speed)
	{
		if (bgmPlayer == null)
			return;
		bgmPlayer.PitchScale = Mathf.Max(0.01f, speed);
	}

	// Set global sound effect volume from emuera's 0-100 scale.
	public void SetSoundVolume(int volume)
	{
		soundVolume = NormalizeEraVolume(volume);
		foreach (var player in soundPlayers)
			player.VolumeDb = LinearToDb(soundVolume);
	}

	// Set BGM volume from emuera's 0-100 scale.
	public void SetBgmVolume(int volume)
	{
		bgmVolume = NormalizeEraVolume(volume);
		if (bgmPlayer != null)
			bgmPlayer.VolumeDb = LinearToDb(bgmVolume);
	}

	// Load a Godot AudioStream from an emuera file path. This stays synchronous
	// because sound commands expect immediate playback, so callers should avoid
	// using very large audio files in hot loops on mobile.
	AudioStream LoadAudioStream(string path, bool loop)
	{
		if (string.IsNullOrEmpty(path))
			return null;
		path = uEmuera.Utils.ResolveExistingFilePath(path);
		if (!uEmuera.Utils.FileExists(path))
		{
			GenericUtils.Warn(EmueraLogCategory.Audio, () => $"[AUDIO] File not found: {path}");
			return null;
		}
		string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
		switch (ext)
		{
			case ".wav":
				var wav = AudioStreamWav.LoadFromFile(path);
				if (wav != null)
					wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
				return wav;
			case ".ogg":
				var ogg = AudioStreamOggVorbis.LoadFromFile(path);
				if (ogg != null)
					ogg.Loop = loop;
				return ogg;
			case ".mp3":
				var mp3 = AudioStreamMP3.LoadFromFile(path);
				if (mp3 != null)
					mp3.Loop = loop;
				else
					GenericUtils.Warn(EmueraLogCategory.Audio, () => $"[AUDIO] Failed to load MP3: {path}");
				return mp3;
			default:
				GenericUtils.Warn(EmueraLogCategory.Audio, () => $"[AUDIO] Unsupported audio extension \"{ext}\": {path}");
				return null;
		}
	}

	// Convert Godot stream length to milliseconds for status callbacks.
	static long GetAudioStreamLengthMs(AudioStream stream)
	{
		if (stream == null)
			return 0;
		double length = stream.GetLength();
		if (length <= 0 || double.IsNaN(length) || double.IsInfinity(length))
			return 0;
		return (long)(length * 1000.0);
	}

	// Convert emuera integer volume to Godot linear volume.
	static float NormalizeEraVolume(int volume)
	{
		if (volume <= 0)
			return 0.0f;
		if (volume >= 100)
			return 1.0f;
		return volume / 100.0f;
	}

	// Godot audio buses use dB, with a hard mute floor for zero volume.
	static float LinearToDb(float linear)
	{
		if (linear <= 0.0001f)
			return -80.0f;
		return Mathf.LinearToDb(linear);
	}

	// Drop unused CBG nodes after a background refresh with fewer layers.
	void TrimCbgNodes(int keepCount)
	{
		for (int i = cbgNodes.Count - 1; i >= keepCount; i--)
		{
			var node = cbgNodes[i];
			cbgNodes.RemoveAt(i);
			SafeQueueFree(node);
		}
	}

	// Update the active emuera button generation and rebuild quick buttons if the
	// currently displayed quick-button cache no longer matches.
	public void SetLastButtonGeneration(int generation)
	{
		bool shouldAutoShowQuick = quickAutoHiddenUntilNextButtons
			&& generation >= 0
			&& generation != quickAutoHiddenGeneration;
		lastButtonGeneration = generation;
		RefreshQuickInputGate();

		if (quickButtons != null && (quickButtons.IsShow || shouldAutoShowQuick))
		{
			if (quickRenderedGeneration == lastButtonGeneration && quickRenderedRevision == displayRevision)
			{
				if (shouldAutoShowQuick)
				{
					quickAutoHiddenUntilNextButtons = false;
					quickAutoHiddenGeneration = int.MinValue;
					quickButtons.ShowPad();
					UpdateSystemButtonVisuals();
				}
				return;
			}

			quickButtons.Clear();
			quickRenderedGeneration = lastButtonGeneration;
			quickRenderedRevision = displayRevision;

			if (lastButtonGeneration < 0)
				return;

			var lineGroups = new List<(int lineNo, List<(string text, Godot.Color color, string code)> buttons)>();
			foreach (var kvp in lineObjects)
			{
				var line = kvp.Value;
				var lineButtons = new List<(string text, Godot.Color color, string code)>();
				CollectQuickButtons(line, lineButtons);
				if (lineButtons.Count > 0)
					lineGroups.Add((kvp.Key, lineButtons));
			}

			lineGroups.Sort((a, b) => a.lineNo.CompareTo(b.lineNo));

			if (lineGroups.Count == 0)
				return;

			if (shouldAutoShowQuick)
			{
				quickAutoHiddenUntilNextButtons = false;
				quickAutoHiddenGeneration = int.MinValue;
				quickButtons.ShowPad();
				quickButtons.SetInputEnabled(true);
				UpdateSystemButtonVisuals();
			}

			for (int i = 0; i < lineGroups.Count; i++)
			{
				foreach (var btn in lineGroups[i].buttons)
				{
					quickButtons.AddButton(btn.text, btn.color, btn.code, lastButtonGeneration);
				}
				if (i < lineGroups.Count - 1)
					quickButtons.ShiftLine();
			}
		}
	}

	// Recursively collect command buttons from visible console lines and nested
	// divs for the quick-button overlay.
	void CollectQuickButtons(ConsoleDisplayLine line, List<(string text, Godot.Color color, string code)> output)
	{
		if (line?.Buttons == null || output == null)
			return;
		foreach (var btn in line.Buttons)
		{
			if (btn.IsButton && btn.Generation == lastButtonGeneration)
			{
				string text = btn.ToString().Trim();
				if (string.IsNullOrEmpty(text))
					text = btn.Title ?? "";
				output.Add((text, GetQuickButtonColor(btn), btn.Inputs));
			}
			foreach (var part in btn.StrArray)
			{
				if (part is ConsoleDivPart div && div.Children != null)
				{
					foreach (var childLine in div.Children)
						CollectQuickButtons(childLine, output);
				}
			}
		}
	}

	// Submit input from the quick-button overlay using the same generation guard
	// as inline console buttons.
	public void SubmitQuickButtonInput(string input, long generation)
	{
		if (quickInputGateActive && quickInputGateGeneration == generation && quickInputGateRevision == displayRevision)
			return;

		HideQuickUntilNextButtons(generation);

		OnButtonPressed(input, generation);
	}

	// Hide quick buttons after one is pressed until a new button generation is
	// rendered. This prevents double-submits during core processing.
	void HideQuickUntilNextButtons(long generation)
	{
		if (generation < lastButtonGeneration)
			return;

		quickInputGateActive = true;
		quickInputGateGeneration = generation;
		quickInputGateRevision = displayRevision;
		quickInputGateTick = Time.GetTicksMsec();
		quickAutoHiddenUntilNextButtons = true;
		quickAutoHiddenGeneration = (int)generation;
		quickButtons?.SetInputEnabled(true);
		quickButtons?.HidePad();
		UpdateSystemButtonVisuals();
	}

	// Re-apply quick-button sizing after settings change.
	public void RefreshQuickButtonSettings()
	{
		quickButtons?.RefreshSizing();
	}

	// Re-enable quick-button input when display revision or button generation has
	// advanced past the submitted prompt.
	void RefreshQuickInputGate()
	{
		if (!quickInputGateActive)
		{
			quickButtons?.SetInputEnabled(true);
			return;
		}

		if (quickInputGateGeneration != lastButtonGeneration || quickInputGateRevision != displayRevision)
		{
			quickInputGateActive = false;
			quickInputGateGeneration = -1;
			quickInputGateRevision = -1;
			quickInputGateTick = 0;
			quickButtons?.SetInputEnabled(true);
		}
	}

	// Fallback unlock for cases where the core finishes without changing visible
	// button generation.
	void RestoreQuickInputGate()
	{
		quickInputGateActive = false;
		quickInputGateGeneration = -1;
		quickInputGateRevision = -1;
		quickInputGateTick = 0;
		quickButtons?.SetInputEnabled(true);
	}

	// Use the command button's final colored part as the quick-button text color.
	Godot.Color GetQuickButtonColor(ConsoleButtonString button)
	{
		if (button.StrArray != null && button.StrArray.Length > 0)
		{
			if (button.StrArray[button.StrArray.Length - 1] is AConsoleColoredPart coloredPart)
			{
				var c = coloredPart.pColor;
				return new Godot.Color(c.r, c.g, c.b, c.a);
			}
		}
		return new Godot.Color(Config.ForeColor.r, Config.ForeColor.g, Config.ForeColor.b, Config.ForeColor.a);
	}

	// Apply emuera background color to the full viewport.
	public void SetBackgroundColor(uEmuera.Drawing.Color color)
	{
		if (bgRect != null)
			bgRect.Color = new Godot.Color(color.r, color.g, color.b, color.a);
	}

	// Toggle the processing label while the worker thread is busy.
	public void ShowIsInProcess(bool show)
	{
		if (inProcessLabel != null)
			inProcessLabel.Visible = show;
	}

	// Show/hide the input pad and sync its input type from the console.
	public void ShowInput(bool show)
	{
		if (inputpad == null)
			return;
		if (show)
		{
			inputpad.ShowPad();
			var console = GlobalStatic.Console;
			if (console != null)
				inputpad.UpdateInputType(console.InputType);
		}
		else
		{
			inputpad.HidePad();
		}
		UpdateSystemButtonVisuals();
	}

	// Expose input-pad visibility to legacy callers.
	public bool IsInputVisible()
	{
		return inputpad != null && inputpad.IsShow;
	}

	// Submit an inline or quick command button to the core. Old generations are
	// treated as a plain advance, matching emuera's stale-button behavior.
	void OnButtonPressed(string input, long generation, bool skip = false)
	{
		RememberCurrentContentScroll();
		TraceScroll("button_pressed", $"input={GenericUtils.ClipTrace(input, 64)} gen={generation} lastGen={lastButtonGeneration} skip={skip}");
		GenericUtils.StartScrollTraceCoreWindow($"button input={GenericUtils.ClipTrace(input, 64)} gen={generation} skip={skip}");
		if (generation < lastButtonGeneration)
		{
			// Old button clicked - send empty input (acts as skip/advance)
			TraceScroll("button_pressed_old_generation", $"gen={generation} lastGen={lastButtonGeneration}");
			EmueraThread.instance.Input("", false, skip);
			return;
		}
		EmueraThread.instance.Input(input, true, skip, 1);
	}

	// Return to the first scene after confirming the emuera worker is idle.
	void OnBackPressed()
	{
		if (EmueraThread.instance.Running())
		{
			ShowMessageBox(
				MultiLanguage.Get("[Wait]", "Wait"),
				MultiLanguage.Get("[WaitContent]", "Please wait for processing to finish!"));
			return;
		}
		ShowConfirmDialog(
			MultiLanguage.Get("[BackMenu]", "Back to Menu"),
			MultiLanguage.Get("[BackMenuContent]", "Return to menu?"),
			() =>
			{
				EmueraThread.instance.End();
				GetTree().ChangeSceneToFile("res://first_window.tscn");
			});
	}

	// Restart the current game scene after user confirmation.
	void OnRestartPressed()
	{
		if (EmueraThread.instance.Running())
		{
			ShowMessageBox(
				MultiLanguage.Get("[Wait]", "Wait"),
				MultiLanguage.Get("[WaitContent]", "Please wait for processing to finish!"));
			return;
		}
		ShowConfirmDialog(
			MultiLanguage.Get("[ReloadGame]", "Reload Game"),
			MultiLanguage.Get("[ReloadGameContent]", "Reload the game?"),
			() =>
			{
				EmueraThread.instance.End();
				CallDeferred(nameof(RestartScene));
			});
	}

	// Deferred scene reload target.
	void RestartScene()
	{
		GetTree().ReloadCurrentScene();
	}

	// ERB command hook for script-driven restart.
	public void RequestRestartFromErb()
	{
		EmueraThread.instance.End();
		CallDeferred(nameof(RestartScene));
	}

	// Open the option dialog overlay.
	void OnOptionsPressed()
	{
		optionWindow?.ShowPopup();
	}

	// Toggle the input pad and hide mutually exclusive overlays.
	void OnInputTogglePressed()
	{
		if (inputpad.IsShow)
		{
			inputpad.HidePad();
		}
		else
		{
			quickButtons?.HidePad();
			scalepad?.HidePad();
			inputpad.ShowPad();
		}
		UpdateSystemButtonVisuals();
	}

	// Toggle quick buttons and rebuild them for the current button generation.
	void OnQuickTogglePressed()
	{
		if (quickButtons.IsShow)
		{
			quickAutoHiddenUntilNextButtons = false;
			quickAutoHiddenGeneration = int.MinValue;
			quickButtons.HidePad();
		}
		else
		{
			quickAutoHiddenUntilNextButtons = false;
			quickAutoHiddenGeneration = int.MinValue;
			inputpad?.HidePad();
			scalepad?.HidePad();
			quickButtons.ShowPad();
			SetLastButtonGeneration(lastButtonGeneration);
		}
		UpdateSystemButtonVisuals();
	}

	// Toggle automatic click-to-advance while the console waits for input.
	void OnAutoSkipTogglePressed()
	{
		autoClickSkipEnabled = !autoClickSkipEnabled;
		lastAutoClickSkipTick = 0;
		UpdateSystemButtonVisuals();
	}

	// Show an informational modal with only an OK button.
	public void ShowMessageBox(string title, string message)
	{
		msgBoxTitle.Text = title;
		msgBoxMessage.Text = message;
		msgBoxConfirmCallback = null;
		msgBoxCancelCallback = null;
		msgBoxCancelBtn.Visible = false;
		msgBox.PopupCentered();
	}

	// Show a confirmation modal and invoke callbacks after the user responds.
	public void ShowConfirmDialog(string title, string message, System.Action onConfirm, System.Action onCancel = null)
	{
		msgBoxTitle.Text = title;
		msgBoxMessage.Text = message;
		msgBoxConfirmCallback = onConfirm;
		msgBoxCancelCallback = onCancel;
		msgBoxCancelBtn.Visible = true;
		msgBox.PopupCentered();
	}

	// Confirm button handler for the shared modal.
	void OnMsgConfirm()
	{
		msgBox.Hide();
		msgBoxConfirmCallback?.Invoke();
		msgBoxConfirmCallback = null;
		msgBoxCancelCallback = null;
	}

	// Cancel button handler for the shared modal.
	void OnMsgCancel()
	{
		msgBox.Hide();
		msgBoxCancelCallback?.Invoke();
		msgBoxConfirmCallback = null;
		msgBoxCancelCallback = null;
	}

	// Save emuera output log beside the game executable/content path.
	void OnSaveLogPressed()
	{
		var path = MinorShift.Emuera.Program.ExeDir;
		var time = System.DateTime.Now;
		string fname = time.ToString("yyyyMMdd-HHmmss");
		path = System.IO.Path.Combine(path, fname + ".log");
		bool result = false;
		var console = GlobalStatic.Console;
		if (console != null)
			result = console.OutputLog(path);
		string diagnosticPath = GenericUtils.GetDefaultDiagnosticLogPath(fname);
		bool diagnosticResult = GenericUtils.ExportDiagnosticLog(diagnosticPath, out string diagnosticError);

		ShowMessageBox(
			MultiLanguage.Get("[SaveLog]", "Save Log"),
			result
				? $"{MultiLanguage.Get("[SavePath]", "Path")}:\n{path}\nDiagnostic:\n{(diagnosticResult ? diagnosticPath : diagnosticError)}"
				: MultiLanguage.Get("[Failure]", "Failure"));
	}

	// Ask the core to return to the title screen after confirmation.
	void OnGotoTitlePressed()
	{
		if (EmueraThread.instance.Running())
		{
			ShowMessageBox(
				MultiLanguage.Get("[Wait]", "Wait"),
				MultiLanguage.Get("[WaitContent]", "Please wait for processing to finish!"));
			return;
		}
		ShowConfirmDialog(
			MultiLanguage.Get("[BackTitle]", "Back to Title"),
			MultiLanguage.Get("[BackTitleContent]", "Return to title screen?"),
			() =>
			{
				GlobalStatic.Console?.GotoTitle();
			});
	}

	// Quit the Godot app after confirmation.
	void OnExitPressed()
	{
		if (EmueraThread.instance.Running())
		{
			ShowMessageBox(
				MultiLanguage.Get("[Wait]", "Wait"),
				MultiLanguage.Get("[WaitContent]", "Please wait for processing to finish!"));
			return;
		}
		ShowConfirmDialog(
			MultiLanguage.Get("[Exit]", "Exit"),
			MultiLanguage.Get("[ExitContent]", "Exit the game?"),
			() =>
			{
				GetTree().Quit();
			});
	}

	// Toggle scale controls and hide other overlays to avoid overlapping touch
	// targets on phone screens.
	void OnScaleTogglePressed()
	{
		if (scalepad.IsShow)
		{
			scalepad.HidePad();
		}
		else
		{
			inputpad?.HidePad();
			quickButtons?.HidePad();
			scalepad.ShowPad();
		}
		UpdateSystemButtonVisuals();
	}

	// Reflect overlay/auto-skip state in the menu icon tint.
	void UpdateSystemButtonVisuals()
	{
		SetSystemButtonActive(inputMenuButton, inputpad != null && inputpad.IsShow);
		SetSystemButtonActive(quickMenuButton, quickButtons != null && quickButtons.IsShow);
		SetSystemButtonActive(autoSkipMenuButton, autoClickSkipEnabled);
		SetSystemButtonActive(scaleMenuButton, scalepad != null && scalepad.IsShow);
	}

	// Apply active/inactive tint to one menu icon.
	static void SetSystemButtonActive(TextureButton button, bool active)
	{
		if (button == null)
			return;
		button.SelfModulate = active ? ActiveSystemButtonColor : NormalSystemButtonColor;
	}

	// Expand or collapse the top-right system menu.
	void OnMenuTogglePressed()
	{
		menuExpanded = !menuExpanded;
		if (menuExpandedBar != null && menuExpandedBar.GetParent() is Control panel)
			panel.Visible = menuExpanded;
	}

	// React to orientation/resolution changes. Android exports can resize when
	// system UI or rotation changes.
	void OnViewportSizeChanged()
	{
		Size = GetViewportRect().Size;
		ContentWidth = (int)Size.X;
		ContentHeight = (int)Size.Y;
		QueueScaleBoundsUpdate();
	}

	// Public scale entry point used by Scalepad. It keeps the viewport center
	// anchored so zooming does not jump to the top-left.
	public void SetContentScale(float scale)
	{
		if (scrollContainer != null)
			SetContentScaleKeepingFocus(scale, scrollContainer.GetGlobalRect().GetCenter(), false);
		else
			SetContentScale(scale, false, true);
	}

	// Internal scale setter used by fallback paths where focus preservation is
	// not possible.
	void SetContentScale(float scale, bool requestScrollToBottom, bool queueBoundsUpdate)
	{
		ApplyContentScaleValue(scale);
		ApplyContentScaleTransform();
		if (queueBoundsUpdate)
			QueueScaleBoundsUpdate();
		if (requestScrollToBottom)
			RequestScrollToBottom();
	}

	// Store clamped scale and sync dependent UI.
	void ApplyContentScaleValue(float scale)
	{
		contentScale = ClampContentScale(scale);
		scalepad?.SyncScale(contentScale);
		if (scrollContainer == null)
			return;

		ConfigureContentScrollContainer();
	}

	// Apply the visual scale to console rows and CBG layers. The root minimum
	// size is handled separately by UpdateScaleBounds.
	void ApplyContentScaleTransform()
	{
		var scaleVector = new Vector2(contentScale, contentScale);
		if (lineContainer != null)
			lineContainer.Scale = scaleVector;
		if (cbgContainer != null)
			cbgContainer.Scale = scaleVector;
	}

	// Re-apply font size to existing generated controls after Config changes.
	public void RefreshFontSize()
	{
		int size = FontSize;
// Update all existing lines
		foreach (var node in lineContainer.GetChildren())
		{
			if (node is Control lineCtrl)
			{
				foreach (var child in lineCtrl.GetChildren())
				{
					if (child is Control ctrl)
					{
						ctrl.AddThemeFontSizeOverride("font_size", size);
					}
				}
			}
		}
		// Update auxiliary UI
		inputpad?.ApplyFont(mainFont, size);
		quickButtons?.ApplyFont(mainFont, size);
		scalepad?.ApplyFont(mainFont, size);
		inProcessLabel?.AddThemeFontSizeOverride("font_size", size);
		ApplyFont(msgBoxTitle);
		ApplyFont(msgBoxMessage);
		ApplyFont(msgBoxConfirmBtn);
		ApplyFont(msgBoxCancelBtn);
	}

	// Per-frame maintenance. The expensive parts are guarded by flags, and the
	// always-on pieces are O(1) so Android frame time remains predictable.
	public override void _Process(double delta)
	{
		ProcessPendingContentPinchZoom();
		ProcessContentInertia((float)delta);
		ProcessContentScrollCorrection();
		SyncContentVerticalScrollRange();
		PublishAudioPlaybackPositions();
		RefreshCbgFollowScrollPositions();
		RefreshCbgAnimationPauseState();
		RefreshQuickInputGate();
		if (quickInputGateActive && Time.GetTicksMsec() - quickInputGateTick >= QuickInputGateFallbackMs && !EmueraThread.instance.Running())
		{
			RestoreQuickInputGate();
		}

		if (!autoClickSkipEnabled)
			return;
		var console = GlobalStatic.Console;
		if (console == null || (!console.IsWaitingEnterKey && !console.IsWaitAnyKey))
			return;
		ulong now = Time.GetTicksMsec();
		if (now - lastAutoClickSkipTick < 80)
			return;
		lastAutoClickSkipTick = now;
		TraceScroll("auto_skip_input");
		GenericUtils.StartScrollTraceCoreWindow("auto_skip");
		EmueraThread.instance.Input("", false, true);
	}

	// Pause animated CBG sprites when outside the visible viewport to save mobile
	// CPU/GPU work.
	void RefreshCbgAnimationPauseState()
	{
		if (renderedCbgLayers.Count == 0 || cbgNodes.Count == 0 || cbgContainer == null)
			return;
		var viewRect = cbgContainer.GetGlobalRect();
		int count = System.Math.Min(renderedCbgLayers.Count, cbgNodes.Count);
		for (int i = 0; i < count; i++)
		{
			var sprite = renderedCbgLayers[i].Img;
			if (sprite is MinorShift.Emuera.Content.SpriteAnime anime)
			{
				var node = cbgNodes[i];
				if (node == null)
					continue;
				var nodeRect = node.GetGlobalRect();
				bool visible = nodeRect.Intersects(viewRect);
				if (visible)
					anime.ResumeAnimation();
				else
					anime.PauseAnimation();
			}
		}
	}

	// Publish audio playback positions to the bridge for status queries.
	void PublishAudioPlaybackPositions()
	{
		if (bgmPlayer != null)
		{
			double total = bgmPlayer.Stream?.GetLength() ?? 0.0;
			GenericUtils.NotifyBgmPlaybackPosition(bgmPlayer.GetPlaybackPosition(), total, bgmPlayer.Playing && !bgmPlayer.StreamPaused);
		}
		for (int i = 0; i < soundPlayers.Count; i++)
		{
			var player = soundPlayers[i];
			if (player == null)
				continue;
			double total = player.Stream?.GetLength() ?? 0.0;
			GenericUtils.NotifySoundPlaybackPosition(i, player.GetPlaybackPosition(), total, player.Playing && !player.StreamPaused);
		}
	}

	// Continue pointer tracking outside the original Control when a drag started
	// inside the console.
	public override void _Input(InputEvent @event)
	{
		if (contentDragActive)
		{
			if (contentDragStartedOnButton && !contentDragMoved && IsPointerRelease(@event))
			{
				CallDeferred(nameof(ResetButtonTapDragStateIfStillPending));
				return;
			}
			HandleContentPointerInput(@event, false);
		}
	}

	// Root-level GUI input fallback.
	public override void _GuiInput(InputEvent @event)
	{
		HandleContentPointerInput(@event, true);
	}

	// ScrollContainer/scaled root GUI input handler.
	void OnContentGuiInput(InputEvent @event)
	{
		HandleContentPointerInput(@event, true);
	}

	// Inline command button GUI input handler. Button identity is passed through
	// so release can submit the correct generation even after layout changes.
	void OnContentButtonGuiInput(InputEvent @event, Control btn, string input, long generation)
	{
		HandleContentPointerInput(@event, true, btn, input, generation);
	}

	// Unified pointer handler for mouse, touch emulation, inline buttons, drag
	// scrolling, inertia, and click-to-advance.
	bool HandleContentPointerInput(InputEvent @event, bool acceptEvent, Control button = null, string input = null, long generation = 0)
	{
		if (HandleContentTouchGesture(@event, acceptEvent))
			return true;

		if (!TryGetPointer(@event, out var pointerPosition, out var pressed, out var released, out var motion))
			return false;

		UpdatePointerPosition(pointerPosition);

		if (scrollContainer == null || (!contentDragActive && !scrollContainer.GetGlobalRect().HasPoint(pointerPosition)))
			return false;

		if (pressed && contentDragActive && button == null)
		{
			if (acceptEvent)
				AcceptEvent();
			else
				GetViewport().SetInputAsHandled();
			return true;
		}

		if (pressed)
		{
			MinorShift._Library.WinInput.PulseVirtualKey(0x01);
			StopContentInertia();
			contentDragActive = true;
			contentDragMoved = false;
			contentDragStartedOnButton = button != null;
			contentDragButton = button;
			contentDragButtonInput = input;
			contentDragButtonGeneration = generation;
			contentDragStartPosition = pointerPosition;
			contentDragLastPosition = pointerPosition;
			contentLastDragTick = Time.GetTicksMsec();
			lastScrollTraceDragTick = contentLastDragTick;
			TraceScroll("pointer_press", $"button={contentDragStartedOnButton} input={GenericUtils.ClipTrace(input, 64)} gen={generation} pos=({Mathf.RoundToInt(pointerPosition.X)},{Mathf.RoundToInt(pointerPosition.Y)}) accept={acceptEvent}");
			if (contentDragStartedOnButton)
			{
				if (acceptEvent)
					AcceptEvent();
				else
					GetViewport().SetInputAsHandled();
				return true;
			}
			return false;
		}

		if (!contentDragActive)
			return false;

		if (motion)
		{
			var totalDelta = pointerPosition - contentDragStartPosition;
			if (!contentDragMoved && totalDelta.Length() >= ScrollDragThreshold)
			{
				contentDragMoved = true;
				contentScrollInteractionSerial++;
				TraceScroll("drag_start", $"total=({Mathf.RoundToInt(totalDelta.X)},{Mathf.RoundToInt(totalDelta.Y)}) threshold={ScrollDragThreshold}");
			}
			if (contentDragMoved)
			{
				var rawScrollDelta = contentDragLastPosition - pointerPosition;
				var appliedDelta = ScrollContentBy(rawScrollDelta);
				UpdateContentScrollVelocity(rawScrollDelta, appliedDelta);
				ulong now = Time.GetTicksMsec();
				if (now - lastScrollTraceDragTick >= ScrollTraceDragIntervalMs)
				{
					lastScrollTraceDragTick = now;
					TraceScroll("drag_move", $"raw=({Mathf.RoundToInt(rawScrollDelta.X)},{Mathf.RoundToInt(rawScrollDelta.Y)}) applied=({Mathf.RoundToInt(appliedDelta.X)},{Mathf.RoundToInt(appliedDelta.Y)})");
				}
				contentDragLastPosition = pointerPosition;
				if (acceptEvent)
					AcceptEvent();
				else
					GetViewport().SetInputAsHandled();
				return true;
			}
			contentDragLastPosition = pointerPosition;
			return false;
		}

		if (!released)
			return false;

		bool handled = false;
		bool restoreQuickInputGate = false;
		string pressedButtonInput = null;
		long pressedButtonGeneration = 0;
		Control pressedButtonControl = null;
		bool advanceTap = false;
		if (contentDragMoved)
		{
			StartContentInertia();
			handled = true;
		}
		else if (contentDragStartedOnButton)
		{
			pressedButtonInput = contentDragButtonInput;
			pressedButtonGeneration = contentDragButtonGeneration;
			pressedButtonControl = contentDragButton;
			handled = true;
		}
		else if (!contentDragStartedOnButton)
		{
			var console = GlobalStatic.Console;
			advanceTap = console != null && (console.IsWaitingEnterKey || console.IsWaitAnyKey);
			handled = advanceTap;
			restoreQuickInputGate = advanceTap;
		}

		TraceScroll("pointer_release", $"moved={contentDragMoved} button={contentDragStartedOnButton} pressedInput={GenericUtils.ClipTrace(pressedButtonInput, 64)} advance={advanceTap} handled={handled}");
		ResetContentDragState();
		if (pressedButtonInput != null)
		{
			UpdatePointerPositionForButton(pressedButtonControl, pointerPosition);
			if (quickButtons != null && quickButtons.IsShow)
				HideQuickUntilNextButtons(pressedButtonGeneration);
			OnButtonPressed(pressedButtonInput, pressedButtonGeneration);
		}
		else if (advanceTap)
			TryAdvanceTap(acceptEvent);
		if (restoreQuickInputGate)
			RestoreQuickInputGate();
		if (handled)
		{
			if (acceptEvent)
				AcceptEvent();
			else
				GetViewport().SetInputAsHandled();
		}
		return handled;
	}

	// Detect multi-touch gestures before normal drag/tap handling. Single touch
	// is allowed to fall through as ordinary pointer input.
	bool HandleContentTouchGesture(InputEvent @event, bool acceptEvent)
	{
		if (scrollContainer == null)
			return false;

		if (contentTouchGestureActive && (@event is InputEventMouseButton || @event is InputEventMouseMotion))
		{
			ConsumeContentPointerEvent(acceptEvent);
			return true;
		}

		if (@event is InputEventScreenTouch touch)
			return HandleContentScreenTouch(touch, acceptEvent);
		if (@event is InputEventScreenDrag drag)
			return HandleContentScreenDrag(drag, acceptEvent);
		return false;
	}

	// Track touch press/release state for pinch gestures.
	bool HandleContentScreenTouch(InputEventScreenTouch touch, bool acceptEvent)
	{
		if (touch.Pressed)
		{
			var rect = scrollContainer.GetGlobalRect();
			if (!rect.HasPoint(touch.Position) && contentTouchPositions.Count == 0 && !contentTouchGestureActive)
				return false;

			contentTouchPositions[touch.Index] = touch.Position;
			if (contentTouchPositions.Count >= ContentPinchTouchCount)
			{
				BeginContentTouchGesture();
				ConsumeContentPointerEvent(acceptEvent);
				return true;
			}

			if (contentTouchGestureActive)
			{
				ConsumeContentPointerEvent(acceptEvent);
				return true;
			}
			return false;
		}

		if (!contentTouchPositions.ContainsKey(touch.Index))
		{
			if (!contentTouchGestureActive)
				return false;
			ConsumeContentPointerEvent(acceptEvent);
			return true;
		}

		contentTouchPositions.Remove(touch.Index);
		if (!contentTouchGestureActive)
			return false;

		if (contentTouchPositions.Count >= ContentPinchTouchCount)
			BeginContentPinch();
		else if (contentTouchPositions.Count == 0)
			EndContentTouchGesture();
		else
		{
			contentPinchActive = false;
			contentPinchDirty = false;
		}

		ConsumeContentPointerEvent(acceptEvent);
		return true;
	}

	// Update multi-touch positions during pinch gestures.
	bool HandleContentScreenDrag(InputEventScreenDrag drag, bool acceptEvent)
	{
		if (!contentTouchPositions.ContainsKey(drag.Index))
		{
			if (!contentTouchGestureActive)
				return false;
			ConsumeContentPointerEvent(acceptEvent);
			return true;
		}

		contentTouchPositions[drag.Index] = drag.Position;
		if (!contentTouchGestureActive && contentTouchPositions.Count < ContentPinchTouchCount)
			return false;

		if (!contentTouchGestureActive)
			BeginContentTouchGesture();
		else if (!contentPinchActive || contentTouchPositions.Count != contentPinchTouchCount)
			BeginContentPinch();
		else
			contentPinchDirty = true;

		ConsumeContentPointerEvent(acceptEvent);
		return true;
	}

	// Switch from ordinary drag/tap handling to multi-touch gesture mode.
	void BeginContentTouchGesture()
	{
		if (!contentTouchGestureActive)
		{
			contentTouchGestureActive = true;
			contentScrollInteractionSerial++;
			StopContentInertia();
			ResetContentDragState();
			TraceScroll("touch_gesture_begin", $"touches={contentTouchPositions.Count}");
		}
		BeginContentPinch();
	}

	// Initialize pinch measurements if two valid touches are active and pinch
	// zoom is enabled.
	void BeginContentPinch()
	{
		contentPinchActive = false;
		contentPinchDirty = false;
		contentPinchTouchCount = contentTouchPositions.Count;
		if (!ContentPinchZoomEnabled)
			return;

		if (contentPinchTouchCount != ContentPinchTouchCount)
			return;

		if (!TryGetContentTouchMetrics(out _, out _, out var spread) || spread < ContentPinchMinSpread)
			return;

		contentPinchStartSpread = spread;
		contentPinchStartScale = contentScale;
		contentPinchActive = true;
	}

	// Apply deferred pinch zoom once per frame instead of on every raw drag event.
	void ProcessPendingContentPinchZoom()
	{
		if (!contentPinchDirty)
			return;
		contentPinchDirty = false;
		UpdateContentPinchZoom();
	}

	// Convert pinch spread ratio into a scale value while keeping the pinch center
	// visually anchored.
	void UpdateContentPinchZoom()
	{
		if (!contentPinchActive)
			return;
		if (!TryGetContentTouchMetrics(out var count, out var center, out var spread))
			return;
		if (count != ContentPinchTouchCount)
		{
			contentPinchActive = false;
			return;
		}
		if (count != contentPinchTouchCount)
		{
			BeginContentPinch();
			return;
		}
		if (spread < ContentPinchMinSpread || contentPinchStartSpread < ContentPinchMinSpread)
			return;

		float ratio = spread / contentPinchStartSpread;
		if (Mathf.Abs(ratio - 1.0f) < ContentPinchScaleDeadZone)
			return;

		float targetScale = ClampContentScale(contentPinchStartScale * ratio);
		bool atLowerLimit = targetScale <= ContentScaleMin + ContentScaleEpsilon && ratio < 1.0f;
		bool atUpperLimit = targetScale >= ContentScaleMax - ContentScaleEpsilon && ratio > 1.0f;
		if (Mathf.Abs(targetScale - contentScale) < ContentScaleEpsilon)
		{
			if (atLowerLimit || atUpperLimit)
				RebaseContentPinch(spread);
			return;
		}

		SetContentScaleKeepingFocus(targetScale, center);
		if (atLowerLimit || atUpperLimit)
			RebaseContentPinch(spread);
		UpdatePointerPosition(center);
	}

	// Clamp requested zoom to the supported mobile range.
	static float ClampContentScale(float scale)
	{
		return Mathf.Clamp(scale, ContentScaleMin, ContentScaleMax);
	}

	// Reset pinch baseline when the gesture hits a zoom limit.
	void RebaseContentPinch(float spread)
	{
		contentPinchStartSpread = Mathf.Max(spread, ContentPinchMinSpread);
		contentPinchStartScale = contentScale;
	}

	// Return center/spread for the active two-finger gesture.
	bool TryGetContentTouchMetrics(out int count, out Vector2 center, out float spread)
	{
		count = contentTouchPositions.Count;
		center = Vector2.Zero;
		spread = 0.0f;
		if (count != ContentPinchTouchCount)
			return false;

		using var enumerator = contentTouchPositions.Values.GetEnumerator();
		if (!enumerator.MoveNext())
			return false;
		var first = enumerator.Current;
		if (!enumerator.MoveNext())
			return false;
		var second = enumerator.Current;
		center = (first + second) * 0.5f;
		spread = first.DistanceTo(second);
		return true;
	}

	// Change scale while preserving the content point under focusGlobalPosition.
	void SetContentScaleKeepingFocus(float scale, Vector2 focusGlobalPosition, bool trackGestureFocus = true)
	{
		if (scrollContainer == null)
		{
			SetContentScale(scale, false, true);
			return;
		}

		var rect = scrollContainer.GetGlobalRect();
		var localFocus = focusGlobalPosition - rect.Position;
		localFocus.X = Mathf.Clamp(localFocus.X, 0.0f, rect.Size.X);
		localFocus.Y = Mathf.Clamp(localFocus.Y, 0.0f, rect.Size.Y);

		float previousScale = Mathf.Max(contentScale, 0.001f);
		var previousScroll = desiredContentScrollValid
			? new Vector2(desiredContentScrollHorizontal, desiredContentScrollVertical)
			: new Vector2(scrollContainer.ScrollHorizontal, scrollContainer.ScrollVertical);
		var contentFocus = (previousScroll + localFocus) / previousScale;
		TraceScroll("scale_focus_begin", $"from={contentScale:0.###} to={ClampContentScale(scale):0.###} focus=({Mathf.RoundToInt(localFocus.X)},{Mathf.RoundToInt(localFocus.Y)})");

		ApplyContentScaleValue(scale);
		UpdateScaleBounds(false);
		ApplyContentScaleTransform();
		RestoreContentScaleFocus(contentFocus, localFocus);
		if (trackGestureFocus)
		{
			scaleFocusContentPoint = contentFocus;
			scaleFocusLocalPoint = localFocus;
			contentPinchFocusValid = true;
		}
	}

	// Restore scroll after a scale change so the same content point remains under
	// the same screen coordinate.
	void RestoreContentScaleFocus(Vector2 contentFocus, Vector2 localFocus)
	{
		if (scrollContainer == null)
			return;

		var nextScroll = contentFocus * contentScale - localFocus;
		int targetHorizontal = Mathf.Clamp(Mathf.RoundToInt(nextScroll.X), 0, GetMaxContentHorizontalScroll());
		int targetVertical = Mathf.Clamp(Mathf.RoundToInt(nextScroll.Y), 0, GetMaxContentVerticalScroll());
		scrollContainer.ScrollHorizontal = targetHorizontal;
		scrollContainer.ScrollVertical = targetVertical;
		RememberDesiredContentScroll(targetHorizontal, targetVertical);
		TraceScroll("scale_focus_restore", $"target=({targetHorizontal},{targetVertical})");
	}

	// Finish a multi-touch gesture and lock in the final focused scroll position.
	void EndContentTouchGesture()
	{
		if (contentPinchFocusValid)
		{
			UpdateScaleBounds(true);
			RestoreContentScaleFocus(scaleFocusContentPoint, scaleFocusLocalPoint);
		}
		TraceScroll("touch_gesture_end", $"focus={contentPinchFocusValid}");
		ResetContentTouchGestureState();
	}

	// Clear all multi-touch tracking state.
	void ResetContentTouchGestureState()
	{
		contentTouchGestureActive = false;
		contentPinchActive = false;
		contentPinchDirty = false;
		contentPinchFocusValid = false;
		contentPinchStartSpread = 0.0f;
		contentPinchStartScale = contentScale;
		contentPinchTouchCount = 0;
		contentTouchPositions.Clear();
	}

	// Mark an event handled from either GUI input or raw input context.
	void ConsumeContentPointerEvent(bool acceptEvent)
	{
		if (acceptEvent)
			AcceptEvent();
		else
			GetViewport().SetInputAsHandled();
	}

	// Update the emuera pointer position in unscaled console coordinates.
	void UpdatePointerPosition(Vector2 globalPosition)
	{
		if (scrollContainer == null)
		{
			GenericUtils.SetPointerPosition(globalPosition.X, globalPosition.Y);
			return;
		}

		var rect = scrollContainer.GetGlobalRect();
		var contentPosition = globalPosition - rect.Position;
		contentPosition += new Vector2(scrollContainer.ScrollHorizontal, scrollContainer.ScrollVertical);
		if (contentScale > 0.001f)
			contentPosition /= contentScale;
		GenericUtils.SetPointerPosition(contentPosition.X, contentPosition.Y);
	}

	// Use the button center for command submission so the core receives a stable
	// pointer position even if the finger releases slightly outside the button.
	void UpdatePointerPositionForButton(Control button, Vector2 fallbackGlobalPosition)
	{
		if (button == null || !GodotObject.IsInstanceValid(button))
		{
			UpdatePointerPosition(fallbackGlobalPosition);
			return;
		}

		if (button.Size.X <= 0 || button.Size.Y <= 0 || lineContainer == null || !GodotObject.IsInstanceValid(lineContainer))
		{
			UpdatePointerPosition(fallbackGlobalPosition);
			return;
		}

		var globalCenter = button.GetGlobalTransformWithCanvas() * (button.Size * 0.5f);
		var contentCenter = lineContainer.GetGlobalTransformWithCanvas().AffineInverse() * globalCenter;
		GenericUtils.SetPointerPosition(contentCenter.X, contentCenter.Y);
	}

	// Apply user-configured sensitivity to a scroll delta.
	Vector2 ScrollContentBy(Vector2 delta)
	{
		if (scrollContainer == null)
			return Vector2.Zero;
		delta *= ContentDragSensitivity;
		return ApplyContentScrollDelta(delta);
	}

	// Clamp and apply scroll changes, then remember the target for correction
	// after Godot's next layout pass.
	Vector2 ApplyContentScrollDelta(Vector2 delta)
	{
		if (scrollContainer == null)
			return Vector2.Zero;

		int oldHorizontal = scrollContainer.ScrollHorizontal;
		int oldVertical = scrollContainer.ScrollVertical;
		int nextHorizontal = Mathf.Clamp(oldHorizontal + Mathf.RoundToInt(delta.X), 0, GetMaxContentHorizontalScroll());
		int nextVertical = Mathf.Clamp(oldVertical + Mathf.RoundToInt(delta.Y), 0, GetMaxContentVerticalScroll());
		scrollContainer.ScrollHorizontal = nextHorizontal;
		scrollContainer.ScrollVertical = nextVertical;
		RememberDesiredContentScroll(nextHorizontal, nextVertical);
		var applied = new Vector2(nextHorizontal - oldHorizontal, nextVertical - oldVertical);
		if (applied.LengthSquared() > 0.01f && !contentDragActive && !contentInertiaActive)
			TraceScroll("scroll_delta", $"delta=({Mathf.RoundToInt(delta.X)},{Mathf.RoundToInt(delta.Y)}) applied=({Mathf.RoundToInt(applied.X)},{Mathf.RoundToInt(applied.Y)})");
		return applied;
	}

	// Correct transient ScrollContainer snaps caused by internal layout updates.
	// This is especially important after button rendering on Android.
	void ProcessContentScrollCorrection()
	{
		if (scrollContainer == null || !desiredContentScrollValid || pendingScroll || contentDragActive || contentInertiaActive)
			return;
		if (scrollContainer.ScrollHorizontal == desiredContentScrollHorizontal && scrollContainer.ScrollVertical == desiredContentScrollVertical)
			return;

		var limit = GetContentScrollLimit();
		int targetHorizontal = Mathf.Clamp(desiredContentScrollHorizontal, 0, limit.X);
		int targetVertical = Mathf.Clamp(desiredContentScrollVertical, 0, limit.Y);
		if (targetHorizontal == scrollContainer.ScrollHorizontal && targetVertical == scrollContainer.ScrollVertical)
			return;

		int oldHorizontal = scrollContainer.ScrollHorizontal;
		int oldVertical = scrollContainer.ScrollVertical;
		scrollContainer.ScrollHorizontal = targetHorizontal;
		scrollContainer.ScrollVertical = targetVertical;
		RememberDesiredContentScroll(targetHorizontal, targetVertical);
		TraceScroll("scroll_correction", $"from=({oldHorizontal},{oldVertical}) to=({targetHorizontal},{targetVertical}) limit=({limit.X},{limit.Y})");
	}

	// Estimate drag velocity for inertial scrolling.
	void UpdateContentScrollVelocity(Vector2 rawScrollDelta, Vector2 appliedDelta)
	{
		ulong now = Time.GetTicksMsec();
		if (contentLastDragTick == 0)
		{
			contentLastDragTick = now;
			return;
		}

		if (rawScrollDelta.LengthSquared() <= 0.01f)
			return;

		float elapsed = Mathf.Max((now - contentLastDragTick) / 1000.0f, 1.0f / 120.0f);
		if (appliedDelta.LengthSquared() <= 0.01f)
		{
			contentScrollVelocity = Vector2.Zero;
			contentLastDragTick = now;
			return;
		}

		var instantVelocity = rawScrollDelta * ContentDragSensitivity / elapsed;
		if (instantVelocity.Length() > ContentInertiaMaxVelocity)
			instantVelocity = instantVelocity.Normalized() * ContentInertiaMaxVelocity;

		contentScrollVelocity = contentScrollVelocity.Lerp(instantVelocity, 0.78f);
		contentLastDragTick = now;
	}

	// Start inertial scrolling using a speed-dependent boost/deceleration curve.
	void StartContentInertia()
	{
		float releaseSpeed = contentScrollVelocity.Length();
		float fastRatio = Mathf.Clamp(
			(releaseSpeed - ContentInertiaMinVelocity) / (ContentInertiaFastVelocity - ContentInertiaMinVelocity),
			0.0f,
			1.0f);
		float releaseBoost = Mathf.Lerp(ContentInertiaMinReleaseBoost, ContentInertiaMaxReleaseBoost, fastRatio);
		contentInertiaDeceleration = Mathf.Lerp(ContentInertiaSlowDeceleration, ContentInertiaFastDeceleration, fastRatio);
		contentScrollVelocity *= releaseBoost;
		if (contentScrollVelocity.Length() > ContentInertiaMaxVelocity)
			contentScrollVelocity = contentScrollVelocity.Normalized() * ContentInertiaMaxVelocity;
		if (contentScrollVelocity.Length() >= ContentInertiaMinVelocity)
		{
			contentInertiaActive = true;
			TraceScroll("inertia_start", $"speed={Mathf.RoundToInt(contentScrollVelocity.Length())} decel={Mathf.RoundToInt(contentInertiaDeceleration)}");
		}
		else
			StopContentInertia();
	}

	// Stop inertial scrolling and clear fractional remainder.
	void StopContentInertia()
	{
		bool shouldLog = contentInertiaActive || contentScrollVelocity.LengthSquared() > 0.01f || contentInertiaRemainder.LengthSquared() > 0.01f;
		if (shouldLog)
			TraceScroll("inertia_stop", $"speed={Mathf.RoundToInt(contentScrollVelocity.Length())}");
		contentInertiaActive = false;
		contentScrollVelocity = Vector2.Zero;
		contentInertiaRemainder = Vector2.Zero;
		contentLastDragTick = 0;
	}

	// Advance inertial scrolling each frame.
	void ProcessContentInertia(float delta)
	{
		if (!contentInertiaActive || contentDragActive || scrollContainer == null)
			return;

		var desiredDelta = contentScrollVelocity * delta + contentInertiaRemainder;
		var roundedDelta = new Vector2(Mathf.Round(desiredDelta.X), Mathf.Round(desiredDelta.Y));
		contentInertiaRemainder = desiredDelta - roundedDelta;
		if (roundedDelta.LengthSquared() > 0.01f)
		{
			var appliedDelta = ApplyContentScrollDelta(roundedDelta);
			if (appliedDelta.LengthSquared() <= 0.01f)
			{
				StopContentInertia();
				return;
			}
		}

		float speed = contentScrollVelocity.Length();
		speed = Mathf.MoveToward(speed, 0, contentInertiaDeceleration * delta);
		if (speed <= ContentInertiaStopVelocity)
		{
			StopContentInertia();
			return;
		}
		contentScrollVelocity = contentScrollVelocity.Normalized() * speed;
	}

	// Current horizontal scroll limit from calculated content bounds.
	int GetMaxContentHorizontalScroll()
	{
		if (scrollContainer == null)
			return 0;
		return GetContentScrollLimit().X;
	}

	// Current vertical scroll limit from calculated content bounds.
	int GetMaxContentVerticalScroll()
	{
		if (scrollContainer == null)
			return 0;
		return GetContentScrollLimit().Y;
	}

	// Calculate scroll limits from viewport size, scaled content size, and the
	// current Godot root size.
	Vector2I GetContentScrollLimit()
	{
		if (scrollContainer == null)
			return Vector2I.Zero;

		var scrollSize = scrollContainer.Size;
		var contentSize = CalculateLineContentSize();
		var layoutSize = CalculateContentLayoutSize(contentSize);
		float safeScale = GetSafeContentScale();
		var scaledSize = CalculateScaledContentRootSize(layoutSize, scrollSize, safeScale);
		if (scaledContentRoot != null)
		{
			scaledSize.X = Mathf.Max(scaledSize.X, Mathf.Max(scaledContentRoot.CustomMinimumSize.X, scaledContentRoot.Size.X));
			scaledSize.Y = Mathf.Max(scaledSize.Y, Mathf.Max(scaledContentRoot.CustomMinimumSize.Y, scaledContentRoot.Size.Y));
		}
		return new Vector2I(
			Mathf.CeilToInt(Mathf.Max(0.0f, scaledSize.X - scrollSize.X)),
			Mathf.CeilToInt(Mathf.Max(0.0f, scaledSize.Y - scrollSize.Y)));
	}

	// Submit an empty input when the console is waiting for enter/any key.
	bool TryAdvanceTap(bool acceptEvent)
	{
		var console = GlobalStatic.Console;
		if (console == null || (!console.IsWaitingEnterKey && !console.IsWaitAnyKey))
			return false;

		uint nowTick = MinorShift._Library.WinmmTimer.TickCount;
		bool skipFlag = (nowTick - lastClickTick < 200);
		TraceScroll("advance_tap", $"skip={skipFlag}");
		GenericUtils.StartScrollTraceCoreWindow($"advance_tap skip={skipFlag}");
		EmueraThread.instance.Input("", false, skipFlag);
		lastClickTick = nowTick;
		return true;
	}

	// Clear ordinary drag/tap tracking state.
	void ResetContentDragState()
	{
		contentDragActive = false;
		contentDragMoved = false;
		contentDragStartedOnButton = false;
		contentDragButton = null;
		contentDragButtonInput = null;
		contentDragButtonGeneration = 0;
	}

	// Defensive cleanup for a button press that was consumed by raw input before
	// the button release handler could run.
	void ResetButtonTapDragStateIfStillPending()
	{
		if (contentDragActive && contentDragStartedOnButton && !contentDragMoved)
		{
			TraceScroll("button_tap_drag_reset");
			ResetContentDragState();
		}
	}

	// Identify left mouse or screen-touch release events.
	static bool IsPointerRelease(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
			return !mb.Pressed;
		if (@event is InputEventScreenTouch touch)
			return !touch.Pressed;
		return false;
	}

	// Normalize Godot mouse and screen touch/drag events into one pointer shape.
	static bool TryGetPointer(InputEvent @event, out Vector2 position, out bool pressed, out bool released, out bool motion)
	{
		position = Vector2.Zero;
		pressed = false;
		released = false;
		motion = false;

		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			position = mb.GlobalPosition;
			pressed = mb.Pressed;
			released = !mb.Pressed;
			return true;
		}
		if (@event is InputEventMouseMotion mm)
		{
			position = mm.GlobalPosition;
			motion = true;
			return true;
		}
		if (@event is InputEventScreenTouch touch)
		{
			position = touch.Position;
			pressed = touch.Pressed;
			released = !touch.Pressed;
			return true;
		}
		if (@event is InputEventScreenDrag drag)
		{
			position = drag.Position;
			motion = true;
			return true;
		}
		return false;
	}

	// Keyboard fallback for desktop testing and for Android devices with hardware
	// keyboards. Pointer events are also handled here when they were not captured
	// by GUI controls.
	public override void _UnhandledInput(InputEvent @event)
	{
		if (HandleContentPointerInput(@event, false))
			return;

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.KpEnter)
				MinorShift._Library.WinInput.PulseVirtualKey(0x0D);
			else if (keyEvent.Keycode == Key.Escape)
				MinorShift._Library.WinInput.PulseVirtualKey(0x1B);
			if (inputpad != null && inputpad.IsShow)
				return;
			var console = GlobalStatic.Console;
			if (console != null && EmueraThread.instance != null)
			{
				if (console.IsWaitAnyKey)
				{
					EmueraThread.instance.Input("", false);
					GetViewport().SetInputAsHandled();
				}
				else if (console.IsWaitingEnterKey && (keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.KpEnter))
				{
					EmueraThread.instance.Input("", false);
					GetViewport().SetInputAsHandled();
				}
			}
		}
	}
}

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
	// provides a stable scaled scroll area. In the legacy backend lineContainer
	// holds one Control per console line; in the Canvas backend it only hosts
	// complex overlay rows that still need the old node renderer.
	ScrollContainer scrollContainer;
	Control scaledContentRoot;
	Control lineContainer;
	VBoxContainer htmlIslandContainer;
	ConsoleRenderSurface consoleRenderSurface;
	ConsoleRenderBackend consoleRenderBackend = ConsoleRenderBackend.Canvas;

	// Overlay and tool UI. These are Canvas/Control overlays above the console and
	// should not own emuera state directly.
	HBoxContainer menuBar;
	Inputpad inputpad;
	QuickButtons quickButtons;
	Scalepad scalepad;
	ColorRect bgRect;
	Control cbgContainer;
	OptionWindow optionWindow;
	UiDiagnosticOverlay uiDiagnosticOverlay;

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
	// lineSizes/lineNumbers 记录当前保留行；Canvas 额外维护一份 prefix 布局快照，
	// 让滚动、绘制、命中和 overlay 对齐不再每次从第一行累加。
	Dictionary<int, ConsoleDisplayLine> lineObjects = new Dictionary<int, ConsoleDisplayLine>();
	Dictionary<int, Control> lineControls = new Dictionary<int, Control>();
	Dictionary<int, ConsoleButtonHit[]> canvasLineButtonHits = new Dictionary<int, ConsoleButtonHit[]>();
	Dictionary<int, List<CanvasImageOverlay>> canvasImageOverlayNodes = new Dictionary<int, List<CanvasImageOverlay>>();
	Dictionary<int, List<CanvasDivOverlay>> canvasDivOverlayNodes = new Dictionary<int, List<CanvasDivOverlay>>();
	HashSet<int> canvasRowsWithPositionedNodes = new HashSet<int>();
	HashSet<int> canvasRowsWithEscapedOverlays = new HashSet<int>();
	HashSet<int> canvasLastVisibilityRows = new HashSet<int>();
	List<int> canvasVisibilityTargetRows = new List<int>();
	HashSet<int> canvasVisibilityTargetRowSet = new HashSet<int>();
	List<int> canvasCurrentVisibilityRows = new List<int>();
	List<CanvasOverlayKey> canvasAnimatedImageOverlayKeys = new List<CanvasOverlayKey>();
	Dictionary<int, Vector2> lineSizes = new Dictionary<int, Vector2>();
	SortedSet<int> lineNumbers = new SortedSet<int>();
	struct ConsoleLineLayoutEntry
	{
		public int LineNo;
		public float Top;
		public Vector2 Size;
		public float Bottom => Top + Size.Y;
	}
	struct CanvasOverlayKey : IEquatable<CanvasOverlayKey>
	{
		public int LineNo;
		public int Index;

		public CanvasOverlayKey(int lineNo, int index)
		{
			LineNo = lineNo;
			Index = index;
		}

		public bool Equals(CanvasOverlayKey other)
		{
			return LineNo == other.LineNo && Index == other.Index;
		}

		public override bool Equals(object obj)
		{
			return obj is CanvasOverlayKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(LineNo, Index);
		}
	}
	List<ConsoleLineLayoutEntry> lineLayoutEntries = new List<ConsoleLineLayoutEntry>();
	Dictionary<int, int> lineLayoutIndexByLineNo = new Dictionary<int, int>();
	bool lineLayoutDirty = false;
	bool canvasOverlayRowsDirty = false;
	// Texture pins mirror presentation lifetime: console rows own line pins and
	// CBG owns background pins. activeTexturePinCollector is scoped to the current
	// render pass so GetSpriteTexture can remain a pure conversion helper.
	Dictionary<int, List<SpriteManager.TextureInfo>> lineTexturePins = new Dictionary<int, List<SpriteManager.TextureInfo>>();
	List<SpriteManager.TextureInfo> cbgTexturePins = new List<SpriteManager.TextureInfo>();
	List<SpriteManager.TextureInfo> activeTexturePinCollector;
	HashSet<int> asyncTexturePendingLineNos = new HashSet<int>();
	Dictionary<int, ConsoleDisplayLine> pendingAsyncLineUpdates = new Dictionary<int, ConsoleDisplayLine>();
	int activeRenderLineNo = -1;
	long observedTextureLoadVersion = 0;
	bool renderingCbgTextures = false;
	bool renderingHtmlIslandTextures = false;
	bool pendingCbgAsyncTextureRefresh = false;
	bool pendingHtmlIslandAsyncTextureRefresh = false;

	// Texture lookup failures are memoized to avoid repeated recursive file scans
	// on Android storage where I/O stalls are very visible.
	HashSet<string> failedTextureSearches = new HashSet<string>();

	// CBG nodes are reused instead of recreated whenever possible. This reduces
	// CanvasItem churn and texture upload pressure during rapid script updates.
	List<EmueraImage> cbgNodes = new List<EmueraImage>();
	List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage> renderedCbgLayers = new List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage>();
	List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage> lastCbgSourceLayers = new List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage>();
	ConsoleDisplayLine[] lastHtmlIslandLines = null;

	struct CbgRenderEntry
	{
		public MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage Layer;
		public Texture2D SourceTexture;
		public Rect2 SourceRegion;
		public Vector2 Position;
		public Vector2 Size;
		public Vector2 DrawOffset;
		public Vector2 DrawSize;
		public bool FlipX;
		public bool FlipY;
		public Color Modulate;
	}

	// Batched display updates defer expensive follow-up work until a group of
	// lines has been applied.
	bool batchingDisplayLines = false;
	float totalLineHeight = 0;
	float widestLineWidth = 0;

	// Visible line cap. Android memory pressure is the main constraint here, so
	// old Controls are trimmed in batches instead of letting the scene tree grow
	// without bound.
	public const int DefaultMaxVisibleLines = 360;
	const int DefaultMobileMaxVisibleLines = 240;
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
	bool quickAutoHiddenWasVisible = false;
	int quickAutoHiddenGeneration = int.MinValue;
	ulong quickAutoHiddenTick = 0;
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
	bool contentDragButtonContentCenterValid = false;
	Vector2 contentDragButtonContentCenter = Vector2.Zero;
	ulong contentLastDragTick = 0;
	bool contentInertiaActive = false;
	float contentInertiaDeceleration = 900.0f;
	int contentScrollInteractionSerial = 0;
	string canvasVisualButtonInput;
	long canvasVisualButtonGeneration = long.MinValue;

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
	const string ConsoleRenderBackendKey = "ConsoleRenderBackend";
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
				// Android 上 Canvas 后端已经避免了“每行一个节点”的主要成本，但保留行越多，
				// lineObjects、纹理 pin、按钮快照和 overlay 索引仍会增加内存压力。默认值按手机端更保守，
				// 用户显式写入 user://settings.cfg 后仍完全尊重用户设置。
				configuredMaxVisibleLines = ClampMaxVisibleLines(
					(int)cfg.GetValue(SettingsSection, MaxVisibleLinesKey, GetDefaultMaxVisibleLines()));
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

	static int GetDefaultMaxVisibleLines()
	{
		return OS.HasFeature("mobile") ? DefaultMobileMaxVisibleLines : DefaultMaxVisibleLines;
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

	ConsoleRenderBackend LoadConsoleRenderBackend()
	{
		var cfg = new ConfigFile();
		cfg.Load(SettingsPath);
		string value = cfg.GetValue(SettingsSection, ConsoleRenderBackendKey, "canvas").AsString();
		return string.Equals(value, "controls", StringComparison.OrdinalIgnoreCase)
			? ConsoleRenderBackend.Controls
			: ConsoleRenderBackend.Canvas;
	}

	bool UseCanvasRenderBackend => consoleRenderBackend == ConsoleRenderBackend.Canvas;

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
		consoleRenderBackend = LoadConsoleRenderBackend();

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

		if (UseCanvasRenderBackend)
		{
			consoleRenderSurface = new ConsoleRenderSurface(this);
			consoleRenderSurface.Name = "ConsoleRenderSurface";
			consoleRenderSurface.MouseFilter = MouseFilterEnum.Pass;
			consoleRenderSurface.ClipContents = true;
			scaledContentRoot.AddChild(consoleRenderSurface);

			lineContainer = new Control();
			lineContainer.Name = "ConsoleOverlayRows";
			lineContainer.MouseFilter = MouseFilterEnum.Pass;
		}
		else
		{
			var rows = new VBoxContainer();
			rows.AddThemeConstantOverride("separation", 0);
			lineContainer = rows;
			lineContainer.Name = "ConsoleControlRows";
			lineContainer.MouseFilter = MouseFilterEnum.Pass;
		}
		lineContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lineContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		lineContainer.ClipContents = false;
		scaledContentRoot.AddChild(lineContainer);

		htmlIslandContainer = new VBoxContainer();
		htmlIslandContainer.MouseFilter = MouseFilterEnum.Pass;
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
		ClearCanvasVisualButton();
		if (lineContainer != null)
		{
			foreach(var child in lineContainer.GetChildren())
				SafeQueueFree(child);
		}
		ResetLineIndexes();
		consoleRenderSurface?.MarkDirty();
		failedTextureSearches.Clear();
		asyncTexturePendingLineNos.Clear();
		pendingAsyncLineUpdates.Clear();
		pendingCbgAsyncTextureRefresh = false;
		pendingHtmlIslandAsyncTextureRefresh = false;
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

	// 控制台布局必须使用 emuera 配置行高，不能让 Godot 字体 fallback 的真实 metrics
	// 反向撑高行盒。字体只参与 ConsoleTextPart 的绘制，并在固定行盒内裁剪。
	int EffectiveLineHeight => Config.LineHeight > 0 ? Config.LineHeight : FontSize;

	// HTML div 内部子行的推进距离必须与 v24/snake 核心一致，使用脚本配置行高。
	// Godot 实际字体行高不能参与 div 子内容流式排版。
	int HtmlDivLineHeight => EffectiveLineHeight;

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
		if (control is ConsoleTextPart textPart)
			textPart.SetFixedSize(size);
		control.CustomMinimumSize = size;
		control.Size = size;
	}

	// Render a text fragment in emuera's fixed half/full-width grid. Godot Label
	// uses real glyph advance, which makes CJK/box-drawing maps drift on Android.
	Control CreateTextPart(string text, EmuColor color, EmuFont font, float width)
	{
		var textPart = new ConsoleTextPart(mainFont, FontSize, color.ToGodotColor(), font?.Bold == true, text);
		SetFixedControlSize(textPart, new Vector2(width, EffectiveLineHeight));
		return textPart;
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

	const int EscapedConsolePartZIndex = 2;

	// The following helpers compute absolute row bounds from every part in a
	// ConsoleDisplayLine. Images follow the SkiaSharp core behavior: the display
	// line still occupies one text row, while overflow is drawn above later rows.
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

	int GetPartBottom(AConsoleDisplayPart part, bool reserveImageOverflow, int lineHeight = -1)
	{
		int baseLineHeight = lineHeight > 0 ? lineHeight : EffectiveLineHeight;
		if (part == null)
			return baseLineHeight;
		if (part is ConsoleImagePart image)
		{
			// SkiaSharp 核心按固定 LineHeight 推进行号，再把越界图片作为 escaped part 覆盖绘制。
			// Godot 行布局也不能被图片撑高，否则会在图片后产生大量空白行；按钮命中范围才需要单独保留图片高度。
			if (reserveImageOverflow && (image.Display == DisplayMode.Relative || image.Display == DisplayMode.AbsoluteLeftTop))
				return GetImagePartBottom(image, baseLineHeight);
			return baseLineHeight;
		}
		if (part is ConsoleDivPart)
			return baseLineHeight;
		return System.Math.Max(baseLineHeight, part.Bottom);
	}

	int GetImagePartBottom(ConsoleImagePart image, int lineHeight = -1)
	{
		int baseLineHeight = lineHeight > 0 ? lineHeight : EffectiveLineHeight;
		if (image == null)
			return baseLineHeight;
		int imageBottom = image.dest_rect.Y + System.Math.Abs(image.dest_rect.Height);
		if (imageBottom <= image.dest_rect.Y)
		{
			// 防御性：当 dest_rect.Height 因异常变为 0 时，渲染路径会回退到纹理自然高度，
			// 按钮命中范围也应使用同一高度，避免图片可见但触摸区域过小。
			int naturalHeight = TryGetImageNaturalHeight(image);
			if (naturalHeight > 0)
				imageBottom = image.dest_rect.Y + naturalHeight;
		}
		return System.Math.Max(baseLineHeight, imageBottom);
	}

	// 从 ConsoleImagePart 关联的精灵或纹理缓存中获取自然高度，用于 dest_rect.Height 异常时的回退。
	int TryGetImageNaturalHeight(ConsoleImagePart image)
	{
		if (image?.Image is ASpriteSingle single && single.BaseImage?.Bitmap != null)
		{
			int h = System.Math.Abs(single.SrcRectangle.Height);
			if (h > 0)
				return h;
		}
		if (!string.IsNullOrEmpty(image?.ResourceName))
		{
			SpriteManager.TryGetTextureInfoCached(image.ResourceName, image.ResourceName, out var ti);
			if (ti != null && ti.height > 0)
				return ti.height;
		}
		return 0;
	}

	bool ImageEscapesLine(ConsoleImagePart image)
	{
		if (image == null)
			return false;
		if (image.Display == DisplayMode.Absolute || image.Display == DisplayMode.AbsoluteLeftBottom)
			return true;
		if (image.Display == DisplayMode.AbsoluteLeftTop)
			return true;
		int top = image.dest_rect.Y;
		int bottom = GetImagePartBottom(image);
		return top < 0 || bottom > EffectiveLineHeight;
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

	int GetButtonBottom(ConsoleButtonString button, bool reserveImageOverflow = false, int lineHeight = -1)
	{
		int baseLineHeight = lineHeight > 0 ? lineHeight : EffectiveLineHeight;
		int bottom = baseLineHeight;
		if (button?.StrArray == null)
			return bottom;
		foreach (var part in button.StrArray)
			bottom = System.Math.Max(bottom, GetPartBottom(part, reserveImageOverflow, baseLineHeight));
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
		var focusColor = Config.FocusColor.ToGodotColor();
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
		if (CanRenderLineOnCanvas(line))
		{
			AddCanvasLine(line, isUpdate);
			return;
		}

		int lineHeight = GetLineBottom(line);
		var lineControl = new Control();
		lineControl.MouseFilter = MouseFilterEnum.Pass;
		lineControl.ClipContents = false;
		int maxLineRight = 0;

		// Collect every TextureInfo touched while building this row. The pins are
		// committed only after the row is inserted, which keeps replacement/update
		// flows balanced even when rendering throws before registration.
		var previousTexturePinCollector = activeTexturePinCollector;
		int previousRenderLineNo = activeRenderLineNo;
		var newTexturePins = new List<SpriteManager.TextureInfo>();
		activeTexturePinCollector = newTexturePins;
		activeRenderLineNo = line.LineNo;
		try
		{
			AddLineBackground(line, lineControl, lineHeight);
			foreach(var button in line.Buttons)
			{
				if(button.IsButton)
				{
					int buttonTop = GetButtonTop(button);
					int buttonHeight = GetButtonBottom(button, true) - buttonTop;
					if (buttonHeight <= 0)
						buttonHeight = EffectiveLineHeight;
					var btn = BuildConsoleButton(button, buttonTop, buttonHeight);
					if (buttonTop < 0 || buttonHeight > EffectiveLineHeight)
						btn.ZIndex = EscapedConsolePartZIndex;
					lineControl.AddChild(btn);
					if (GenericUtils.IsUiLayoutTraceEnabled("button"))
						QueueUiLayoutTrace(btn, "button", "", button.PointX, buttonTop, button.Width, buttonHeight);

					int btnRight = Mathf.CeilToInt(btn.Position.X + btn.Size.X);
					if (btnRight > maxLineRight) maxLineRight = btnRight;
				}
				else
				{
					foreach(var part in button.StrArray)
					{
						AddPartToContainer(part, lineControl, 0);
					}
					int right = GetButtonVisualRight(button, -1, false);
					if (right > maxLineRight) maxLineRight = right;
				}
			}
		}
		catch
		{
			SafeQueueFree(lineControl);
			ReleaseTexturePinList(newTexturePins);
			asyncTexturePendingLineNos.Remove(line.LineNo);
			throw;
		}
		finally
		{
			activeTexturePinCollector = previousTexturePinCollector;
			activeRenderLineNo = previousRenderLineNo;
		}

		// PRINT_IMAGE 会用后续的 <br> 行为大图预留显示高度。即使这些行没有可见子节点，
		// 也必须按 emuera 的逻辑行高参与布局，否则日结动画后的图片和怀孕口上会被后续输出挤压或遮住。
		int fixedLineHeight = lineHeight;
		var lineSize = new Vector2(maxLineRight, fixedLineHeight);
		SetFixedControlSize(lineControl, lineSize);
		bool asyncTexturePendingDuringRender = asyncTexturePendingLineNos.Contains(line.LineNo);
		bool hasExistingLine = lineObjects.ContainsKey(line.LineNo) || lineControls.ContainsKey(line.LineNo);
		if (ShouldDeferLineReplacementForAsyncTexture(line, isUpdate, hasExistingLine, asyncTexturePendingDuringRender))
		{
			SafeQueueFree(lineControl);
			ReleaseTexturePinList(newTexturePins);
			return;
		}

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
		if (asyncTexturePendingDuringRender)
			asyncTexturePendingLineNos.Add(line.LineNo);
		else
			pendingAsyncLineUpdates.Remove(line.LineNo);
		displayRevision++;
		if (UseCanvasRenderBackend)
			NotifyConsoleRenderContentChanged();

		// Enforce node cap to prevent unbounded memory growth
		if (!batchingDisplayLines && GetRetainedLineCount() > MaxVisibleLines)
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

	bool ShouldDeferLineReplacementForAsyncTexture(ConsoleDisplayLine line, bool isUpdate, bool hasExistingLine, bool asyncTexturePending)
	{
		if (line == null || !isUpdate || !hasExistingLine || !asyncTexturePending)
			return false;

		// 刷新已有行时，如果新图片仍在异步解码，先保留旧行画面。
		// 否则旧节点会被空白临时行替换，玩家会看到图片闪白；纹理完成后再用挂起的新行做真正替换。
		pendingAsyncLineUpdates[line.LineNo] = line;
		asyncTexturePendingLineNos.Add(line.LineNo);
		return true;
	}

	// 企业级说明：普通行与 HTML/Div 子行共用同一按钮构建入口，避免触摸命中、焦点、样式和内容裁剪规则在移动端产生分叉。
	Panel BuildConsoleButton(ConsoleButtonString button, int buttonTop, int buttonHeight)
	{
		if (buttonHeight <= 0)
			buttonHeight = EffectiveLineHeight;
		Rect2 hitRect = GetButtonVisualBounds(button, buttonTop, buttonHeight, button.PointX, button.PointX);
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
		btn.SetMeta("button_input", inputs);
		btn.SetMeta("generation", generation);

		var contentBox = new Control();
		contentBox.MouseFilter = MouseFilterEnum.Ignore;
		contentBox.ClipContents = false;
		contentBox.Position = new Vector2(button.PointX - hitRect.Position.X, -hitRect.Position.Y);
		btn.AddChild(contentBox);

		foreach (var part in button.StrArray)
			AddPartToContainer(part, contentBox, button.PointX);

		foreach (var child in contentBox.GetChildren())
		{
			if (child is Control c)
				c.MouseFilter = MouseFilterEnum.Ignore;
		}

		SetFixedControlSize(contentBox, hitRect.Size);
		btn.CustomMinimumSize = hitRect.Size;
		btn.Position = hitRect.Position;
		btn.Size = hitRect.Size;
		return btn;
	}

	Rect2 GetButtonVisualBounds(ConsoleButtonString button, int buttonTop, int buttonHeight, int renderRelX, int renderOriginX)
	{
		float left = button.PointX;
		float top = buttonTop;
		float right = button.PointX + System.Math.Max(button.Width, 1);
		float bottom = buttonTop + System.Math.Max(buttonHeight, 1);
		bool hasVisualPart = false;
		if (button?.StrArray != null)
		{
			foreach (var part in button.StrArray)
				hasVisualPart |= ExpandPartVisualBounds(part, renderRelX, renderOriginX, ref left, ref top, ref right, ref bottom);
		}

		// v24/snake 的 div 自带矩形命中；Godot 版必须把这些非文本宽度合并进 Panel，
		// 否则 <button><div>...</div></button> 会因为按钮流式宽度为 0 而只能在 quick 面板点击。
		if (!hasVisualPart && right <= left)
			right = left + 1;
		if (bottom <= top)
			bottom = top + System.Math.Max(buttonHeight, EffectiveLineHeight);
		return new Rect2(new Vector2(left, top), new Vector2(right - left, bottom - top));
	}

	ConsoleButtonHit[] BuildCanvasLineButtonHits(ConsoleDisplayLine line)
	{
		if (line?.Buttons == null)
			return null;
		List<ConsoleButtonHit> hits = null;
		foreach (var button in line.Buttons)
		{
			if (button == null || !button.IsButton)
				continue;
			int buttonTop = GetButtonTop(button);
			int buttonHeight = GetButtonBottom(button, true) - buttonTop;
			if (buttonHeight <= 0)
				buttonHeight = EffectiveLineHeight;
			var bounds = GetButtonVisualBounds(button, buttonTop, buttonHeight, button.PointX, button.PointX);
			if (bounds.Size.X <= 0 || bounds.Size.Y <= 0)
				continue;
			hits ??= new List<ConsoleButtonHit>();
			hits.Add(new ConsoleButtonHit
			{
				Rect = bounds,
				Input = button.Inputs,
				Generation = button.Generation,
				ContentCenter = bounds.Position + bounds.Size * 0.5f,
			});
		}
		return hits == null ? null : hits.ToArray();
	}

	int GetButtonVisualRight(ConsoleButtonString button, int rowHeight = -1, bool asControlButton = true)
	{
		int buttonTop = GetButtonTop(button);
		int buttonBottom = GetButtonBottom(button, true, rowHeight);
		int buttonHeight = buttonBottom - buttonTop;
		if (buttonHeight <= 0)
			buttonHeight = rowHeight > 0 ? rowHeight : EffectiveLineHeight;
		int renderRelX = asControlButton ? button.PointX : 0;
		int renderOriginX = asControlButton ? button.PointX : 0;
		var bounds = GetButtonVisualBounds(button, buttonTop, buttonHeight, renderRelX, renderOriginX);
		return Mathf.CeilToInt(bounds.Position.X + bounds.Size.X);
	}

	bool ExpandPartVisualBounds(AConsoleDisplayPart part, int relX, int originX, ref float left, ref float top, ref float right, ref float bottom)
	{
		if (part == null)
			return false;
		Rect2 rect;
		if (part is ConsoleStyledString css)
		{
			if (string.IsNullOrEmpty(css.Str))
				return false;
			rect = new Rect2(css.PointX - relX, 0, System.Math.Max(css.Width, 1), EffectiveLineHeight);
		}
		else if (part is ConsoleDivPart div)
		{
			rect = new Rect2(GetHtmlDivPosition(div, relX), new Vector2(System.Math.Max(div.DivWidth, 1), System.Math.Max(div.DivHeight, 1)));
		}
		else if (part is ConsoleImagePart image)
		{
			Vector2 pos = GetHtmlImagePosition(image, relX);
			Vector2 size = GetImageRenderSize(image);
			rect = new Rect2(pos, size);
		}
		else if (part is ConsoleRectangleShapePart rectShape)
		{
			rect = new Rect2(rectShape.PointX - relX, rectShape.Top,
				System.Math.Max(rectShape.Width, 1),
				System.Math.Max(rectShape.Bottom - rectShape.Top, 1));
		}
		else
		{
			rect = new Rect2(part.PointX - relX, part.Top,
				System.Math.Max(part.Width, 1),
				System.Math.Max(part.Bottom - part.Top, EffectiveLineHeight));
		}

		rect.Position = new Vector2(rect.Position.X + originX, rect.Position.Y);
		left = System.Math.Min(left, rect.Position.X);
		top = System.Math.Min(top, rect.Position.Y);
		right = System.Math.Max(right, rect.Position.X + rect.Size.X);
		bottom = System.Math.Max(bottom, rect.Position.Y + rect.Size.Y);
		return true;
	}

	Vector2 GetImageRenderSize(ConsoleImagePart image)
	{
		int w = System.Math.Abs(image.dest_rect.Width);
		int h = System.Math.Abs(image.dest_rect.Height);
		if (w > 0 && h > 0)
			return new Vector2(w, h);

		if (TryGetKnownImageTextureSize(image, out int textureWidth, out int textureHeight))
		{
			if (w > 0)
			{
				h = textureWidth > 0 ? System.Math.Max(1, textureHeight * w / textureWidth) : w;
				return new Vector2(w, h);
			}
			if (h > 0)
			{
				w = textureHeight > 0 ? System.Math.Max(1, textureWidth * h / textureHeight) : h;
				return new Vector2(w, h);
			}
			return new Vector2(System.Math.Max(textureWidth, 1), System.Math.Max(textureHeight, 1));
		}

		int fallback = System.Math.Max(EffectiveLineHeight, 1);
		return new Vector2(System.Math.Max(w, fallback), System.Math.Max(h, fallback));
	}

	static bool TryGetKnownImageTextureSize(ConsoleImagePart image, out int width, out int height)
	{
		width = 0;
		height = 0;
		ASprite sprite = image.Image;
		if (sprite == null && !string.IsNullOrEmpty(image.ResourceName))
			sprite = AppContents.GetSprite(image.ResourceName);

		if (sprite?.DestBaseSize.Width > 0 && sprite.DestBaseSize.Height > 0)
		{
			width = sprite.DestBaseSize.Width;
			height = sprite.DestBaseSize.Height;
			return true;
		}
		if (sprite is ASpriteSingle single)
		{
			int srcW = System.Math.Abs(single.SrcRectangle.Width);
			int srcH = System.Math.Abs(single.SrcRectangle.Height);
			if (srcW > 0 && srcH > 0)
			{
				width = srcW;
				height = srcH;
				return true;
			}
		}
		return false;
	}

	void QueueUiLayoutTrace(Control control, string kind, string resourceName, int targetX, int targetY, int targetW, int targetH)
	{
		if (!GenericUtils.IsUiLayoutTraceEnabled(kind))
			return;
		// 企业级说明：本方法在行节点注册完成前调用，而注册成功会推进一次 displayRevision。
		// 记录“预期稳定修订号”可区分当前行自身完成注册和后续 UPDATE 批次替换旧节点。
		int expectedDisplayRevision = displayRevision + 1;
		TraceUiLayoutAfterLayout(control, kind, resourceName ?? "", targetX, targetY, targetW, targetH,
			expectedDisplayRevision, GenericUtils.UiFrameGeneration);
	}

	async void TraceUiLayoutAfterLayout(Control control, string kind, string resourceName,
		int targetX, int targetY, int targetW, int targetH,
		int expectedDisplayRevision, int queuedUiFrameGeneration)
	{
		// 企业级说明：Godot 容器的最终 Control rect 可能在当前帧结束后才稳定。
		// UI 几何诊断只在调试开关开启时排队到下一帧采样，不阻塞主线程，也不修改布局行为。
		var tree = GetTree();
		if (tree != null)
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);

		if (control == null || !GodotObject.IsInstanceValid(control) || control.IsQueuedForDeletion())
		{
			// 企业级说明：LOAD/UPDATE 会在 Android 上分帧重建大量控制台行。
			// 如果等待布局帧期间显示修订号已经推进，当前采样对象属于旧批次，
			// 节点失效是正常生命周期结束，不能记录为立绘未加载或实际矩形缺失。
			if (displayRevision != expectedDisplayRevision)
				return;
			if (GenericUtils.IsUiLayoutMismatchTraceEnabled())
				GenericUtils.UiLayoutTrace("UI_LAYOUT.MISSING_ACTUAL",
					() => kind + " actual rect missing",
					() => BuildUiLayoutData(kind, resourceName, targetX, targetY, targetW, targetH, 0, 0, 0, 0)
						+ $" reason=node_missing expected_display_revision={expectedDisplayRevision} queued_render_batch_id={queuedUiFrameGeneration}");
			return;
		}

		int actualX = Mathf.RoundToInt(control.Position.X);
		int actualY = Mathf.RoundToInt(control.Position.Y);
		int actualW = Mathf.RoundToInt(control.Size.X);
		int actualH = Mathf.RoundToInt(control.Size.Y);
		EmitUiLayoutTrace(kind, resourceName, targetX, targetY, targetW, targetH, actualX, actualY, actualW, actualH);
		EmitUiOverlayTrace(control, kind, targetX, targetY, targetW, targetH, actualX, actualY, actualW, actualH);
	}

	void EmitUiLayoutTrace(string kind, string resourceName,
		int targetX, int targetY, int targetW, int targetH,
		int actualX, int actualY, int actualW, int actualH)
	{
		int dx = targetX - actualX;
		int dy = targetY - actualY;
		int dw = targetW - actualW;
		int dh = targetH - actualH;
		string dataFactory() => BuildUiLayoutData(kind, resourceName, targetX, targetY, targetW, targetH, actualX, actualY, actualW, actualH);

		if (GenericUtils.IsUiLayoutTargetRectTraceEnabled())
			GenericUtils.UiLayoutTrace("UI_LAYOUT.TARGET.RECORDED", () => kind + " target rect", dataFactory);
		if (GenericUtils.IsUiLayoutActualRectTraceEnabled())
			GenericUtils.UiLayoutTrace("UI_LAYOUT.ACTUAL.RECORDED", () => kind + " actual rect", dataFactory);

		int threshold = GenericUtils.UiLayoutMismatchThresholdPx;
		if (GenericUtils.IsUiLayoutMismatchTraceEnabled()
			&& (Mathf.Abs(dx) > threshold || Mathf.Abs(dy) > threshold || Mathf.Abs(dw) > threshold || Mathf.Abs(dh) > threshold))
		{
			GenericUtils.UiLayoutTrace("UI_LAYOUT.MISMATCH", () => kind + " mismatch", dataFactory);
		}
	}

	static string BuildUiLayoutData(string kind, string resourceName,
		int targetX, int targetY, int targetW, int targetH,
		int actualX, int actualY, int actualW, int actualH)
	{
		string resource = string.IsNullOrEmpty(resourceName) ? "" : " resource=" + resourceName;
		return $"kind={kind}{resource} target=({targetX},{targetY},{targetW},{targetH}) actual=({actualX},{actualY},{actualW},{actualH}) delta=({targetX - actualX},{targetY - actualY},{targetW - actualW},{targetH - actualH})";
	}

	void EmitUiOverlayTrace(Control control, string kind,
		int targetX, int targetY, int targetW, int targetH,
		int actualX, int actualY, int actualW, int actualH)
	{
		if (control == null || !GenericUtils.IsUiOverlayEnabled())
			return;
		if (kind == "button" && !GenericUtils.UiOverlayButtonRectEnabled)
			return;
		if (kind == "image" && !GenericUtils.UiOverlayImageRectEnabled)
			return;

		var overlay = EnsureUiDiagnosticOverlay();
		if (overlay == null)
			return;

		// 企业级说明：overlay 只复用 UI_LAYOUT 已经采样到的矩形，不主动遍历 UI 树。
		// 默认关闭时没有节点、没有绘制、没有额外 I/O；开启后也不修改布局或触摸命中，只画临时线框。
		var actualRect = ConvertGlobalRectToOverlay(control.GetGlobalRect());
		var targetRect = ConvertTargetRectToOverlay(control, targetX, targetY, targetW, targetH, actualX, actualY, actualW, actualH);
		int threshold = GenericUtils.UiLayoutMismatchThresholdPx;
		bool mismatch = Mathf.Abs(targetX - actualX) > threshold
			|| Mathf.Abs(targetY - actualY) > threshold
			|| Mathf.Abs(targetW - actualW) > threshold
			|| Mathf.Abs(targetH - actualH) > threshold;

		if (GenericUtils.UiOverlayTargetRectEnabled)
			overlay.AddRect(targetRect, new Color(0.2f, 0.55f, 1.0f, 0.95f));
		if (GenericUtils.UiOverlayActualRectEnabled)
			overlay.AddRect(actualRect, new Color(0.35f, 1.0f, 0.45f, 0.95f));
		if (mismatch && GenericUtils.UiOverlayMismatchEnabled)
			overlay.AddRect(actualRect, new Color(1.0f, 0.2f, 0.35f, 1.0f));
	}

	UiDiagnosticOverlay EnsureUiDiagnosticOverlay()
	{
		if (!GenericUtils.IsUiOverlayEnabled())
			return null;
		if (uiDiagnosticOverlay == null || !GodotObject.IsInstanceValid(uiDiagnosticOverlay))
		{
			uiDiagnosticOverlay = new UiDiagnosticOverlay();
			uiDiagnosticOverlay.Name = "UiDiagnosticOverlay";
			uiDiagnosticOverlay.MouseFilter = MouseFilterEnum.Ignore;
			uiDiagnosticOverlay.ZIndex = 4096;
			uiDiagnosticOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
			AddChild(uiDiagnosticOverlay);
		}
		uiDiagnosticOverlay.Visible = true;
		uiDiagnosticOverlay.MaxRects = GenericUtils.UiOverlayMaxDrawnRects;
		return uiDiagnosticOverlay;
	}

	void RefreshUiDiagnosticOverlay()
	{
		if (uiDiagnosticOverlay == null || !GodotObject.IsInstanceValid(uiDiagnosticOverlay))
			return;
		bool enabled = GenericUtils.IsUiOverlayEnabled();
		uiDiagnosticOverlay.Visible = enabled;
		uiDiagnosticOverlay.MaxRects = GenericUtils.UiOverlayMaxDrawnRects;
		if (!enabled)
			uiDiagnosticOverlay.ClearRects();
	}

	Rect2 ConvertTargetRectToOverlay(Control control,
		int targetX, int targetY, int targetW, int targetH,
		int actualX, int actualY, int actualW, int actualH)
	{
		Rect2 actualGlobal = control.GetGlobalRect();
		float scaleX = actualW != 0 ? actualGlobal.Size.X / actualW : 1.0f;
		float scaleY = actualH != 0 ? actualGlobal.Size.Y / actualH : 1.0f;
		var targetGlobal = new Rect2(
			actualGlobal.Position + new Vector2((targetX - actualX) * scaleX, (targetY - actualY) * scaleY),
			new Vector2(targetW * scaleX, targetH * scaleY));
		return ConvertGlobalRectToOverlay(targetGlobal);
	}

	Rect2 ConvertGlobalRectToOverlay(Rect2 globalRect)
	{
		if (uiDiagnosticOverlay == null || !GodotObject.IsInstanceValid(uiDiagnosticOverlay))
			return globalRect;
		return new Rect2(globalRect.Position - uiDiagnosticOverlay.GetGlobalRect().Position, globalRect.Size);
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

		int overflow = GetRetainedLineCount() - MaxVisibleLines;
		if (overflow > 0)
			RemoveTopLines(System.Math.Max(LineTrimBatch, overflow));

		FlushCanvasOverlayRowsIfNeeded();
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

		int overflow = GetRetainedLineCount() - MaxVisibleLines;
		if (overflow > 0)
		{
			RemoveTopLines(System.Math.Max(LineTrimBatch, overflow));
			changed = true;
		}

		if (changed || update)
		{
			FlushCanvasOverlayRowsIfNeeded();
			RefreshQuickInputGate();
			QueueDisplayFollowUp();
		}

		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("apply_text_changes", () => $"removeBottom={removeBottomCount} add={lines?.Count ?? 0} changed={changed} update={update} lastGen={lastButtonGeneration} maxLine={GetMaxLineNo()}");
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
		bg.Color = c.ToGodotColor();
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
		lastHtmlIslandLines = lines;
		pendingHtmlIslandAsyncTextureRefresh = false;

		bool previousHtmlIslandRender = renderingHtmlIslandTextures;
		renderingHtmlIslandTextures = true;
		try
		{
			foreach (var line in lines)
			{
				if (line == null)
					continue;
				int lineHeight = GetLineBottom(line);
				var lineControl = new Control();
				lineControl.MouseFilter = MouseFilterEnum.Pass;
				lineControl.ClipContents = false;
				AddLineBackground(line, lineControl, lineHeight);
				foreach (var button in line.Buttons)
				{
					if (button.IsButton)
					{
						int buttonTop = GetButtonTop(button);
						int buttonHeight = GetButtonBottom(button, true) - buttonTop;
						if (buttonHeight <= 0)
							buttonHeight = EffectiveLineHeight;
						var btn = BuildConsoleButton(button, buttonTop, buttonHeight);
						if (buttonTop < 0 || buttonHeight > EffectiveLineHeight)
							btn.ZIndex = EscapedConsolePartZIndex;
						lineControl.AddChild(btn);
					}
					else
					{
						foreach (var part in button.StrArray)
							AddPartToContainer(part, lineControl, 0);
					}
				}
				SetFixedControlSize(lineControl, new Vector2(GetLineRight(line), lineHeight));
				htmlIslandContainer.AddChild(lineControl);
			}
		}
		finally
		{
			renderingHtmlIslandTextures = previousHtmlIslandRender;
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
		lastHtmlIslandLines = null;
		pendingHtmlIslandAsyncTextureRefresh = false;
		displayRevision++;
		RefreshQuickInputGate();
	}

	// Register one row in all lookup tables and aggregate metrics used by the
	// manual layout calculator.
	void RegisterLine(int lineNo, ConsoleDisplayLine line, Control control, Vector2 size)
	{
		lineObjects[lineNo] = line;
		lineControls[lineNo] = control;
		if (UseCanvasRenderBackend && control == null)
		{
			var hits = BuildCanvasLineButtonHits(line);
			if (hits != null && hits.Length > 0)
				canvasLineButtonHits[lineNo] = hits;
			else
				canvasLineButtonHits.Remove(lineNo);
		}
		else
		{
			canvasLineButtonHits.Remove(lineNo);
		}
		lineSizes[lineNo] = size;
		lineNumbers.Add(lineNo);
		totalLineHeight += size.Y;
		if (size.X > widestLineWidth)
			widestLineWidth = size.X;
		UpdateCanvasPositionedNodeIndexForLine(lineNo);
		MarkLineLayoutDirty();
	}

	int GetRetainedLineCount()
	{
		return lineNumbers.Count;
	}

	// Remove one row from lookup tables and subtract its cached contribution from
	// the aggregate layout metrics.
	void UnregisterLine(int lineNo)
	{
		ReleaseLineTexturePins(lineNo);
		ReleaseCanvasImageOverlays(lineNo);
		ReleaseCanvasDivOverlays(lineNo);
		ClearCanvasOverlayIndexesForLine(lineNo);
		asyncTexturePendingLineNos.Remove(lineNo);
		pendingAsyncLineUpdates.Remove(lineNo);
		lineObjects.Remove(lineNo);
		lineControls.Remove(lineNo);
		canvasLineButtonHits.Remove(lineNo);
		canvasRowsWithPositionedNodes.Remove(lineNo);
		bool removedNumber = lineNumbers.Remove(lineNo);
		if (!lineSizes.TryGetValue(lineNo, out var size))
		{
			if (removedNumber)
				MarkLineLayoutDirty();
			return;
		}
		lineSizes.Remove(lineNo);
		totalLineHeight = System.Math.Max(0, totalLineHeight - size.Y);
		if (size.X >= widestLineWidth)
			RecalculateWidestLineWidth();
		MarkLineLayoutDirty();
	}

	// Reset every line cache after a full clear.
	void ResetLineIndexes()
	{
		ResetLineTexturePins();
		ResetCanvasImageOverlays();
		ResetCanvasDivOverlays();
		ClearCanvasOverlayIndexes();
		lineObjects.Clear();
		lineControls.Clear();
		pendingAsyncLineUpdates.Clear();
		canvasLineButtonHits.Clear();
		canvasRowsWithPositionedNodes.Clear();
		lineSizes.Clear();
		lineNumbers.Clear();
		lineLayoutEntries.Clear();
		lineLayoutIndexByLineNo.Clear();
		lineLayoutDirty = false;
		canvasOverlayRowsDirty = false;
		totalLineHeight = 0;
		widestLineWidth = 0;
	}

	void RegisterCanvasImageOverlays(int lineNo, List<CanvasImageOverlay> nodes)
	{
		if (nodes == null || nodes.Count == 0)
			return;
		canvasImageOverlayNodes[lineNo] = nodes;
		RebuildCanvasOverlayIndexesForLine(lineNo);
		UpdateCanvasPositionedNodeIndexForLine(lineNo);
	}

	void RegisterCanvasDivOverlays(int lineNo, List<CanvasDivOverlay> nodes)
	{
		if (nodes == null || nodes.Count == 0)
			return;
		canvasDivOverlayNodes[lineNo] = nodes;
		RebuildCanvasOverlayIndexesForLine(lineNo);
		UpdateCanvasPositionedNodeIndexForLine(lineNo);
	}

	void ReleaseCanvasImageOverlays(int lineNo)
	{
		if (!canvasImageOverlayNodes.TryGetValue(lineNo, out var nodes))
			return;
		canvasImageOverlayNodes.Remove(lineNo);
		RebuildCanvasOverlayIndexesForLine(lineNo);
		UpdateCanvasPositionedNodeIndexForLine(lineNo);
		ReleaseCanvasImageOverlayList(nodes);
	}

	void ResetCanvasImageOverlays()
	{
		foreach (var nodes in canvasImageOverlayNodes.Values)
			ReleaseCanvasImageOverlayList(nodes);
		canvasImageOverlayNodes.Clear();
		RebuildCanvasOverlayIndexes();
	}

	void ReleaseCanvasDivOverlays(int lineNo)
	{
		if (!canvasDivOverlayNodes.TryGetValue(lineNo, out var nodes))
			return;
		canvasDivOverlayNodes.Remove(lineNo);
		RebuildCanvasOverlayIndexesForLine(lineNo);
		UpdateCanvasPositionedNodeIndexForLine(lineNo);
		ReleaseCanvasDivOverlayList(nodes);
	}

	void UpdateCanvasPositionedNodeIndexForLine(int lineNo)
	{
		// Canvas 热路径只需要移动仍由 Godot 节点承载的行：整行 fallback Control、
		// 复杂图片 overlay 或 div overlay。普通纯文本/形状行继续只由 Canvas 自绘。
		if (HasCanvasPositionedNodesForLine(lineNo))
			canvasRowsWithPositionedNodes.Add(lineNo);
		else
			canvasRowsWithPositionedNodes.Remove(lineNo);
	}

	bool HasCanvasPositionedNodesForLine(int lineNo)
	{
		return HasCanvasLineControlForPositioning(lineNo)
			|| HasCanvasOverlaysForLine(lineNo);
	}

	bool HasCanvasLineControlForPositioning(int lineNo)
	{
		return lineControls.TryGetValue(lineNo, out var control)
			&& control != null;
	}

	void ResetCanvasDivOverlays()
	{
		foreach (var nodes in canvasDivOverlayNodes.Values)
			ReleaseCanvasDivOverlayList(nodes);
		canvasDivOverlayNodes.Clear();
		RebuildCanvasOverlayIndexes();
	}

	void RebuildCanvasOverlayIndexesForLine(int lineNo)
	{
		ClearCanvasOverlayMetadataForLine(lineNo);
		if (canvasImageOverlayNodes.TryGetValue(lineNo, out var imageNodes))
			IndexCanvasImageOverlays(lineNo, imageNodes);
		if (canvasDivOverlayNodes.TryGetValue(lineNo, out var divNodes))
			IndexCanvasDivOverlays(lineNo, divNodes);
	}

	void RebuildCanvasOverlayIndexes()
	{
		canvasRowsWithEscapedOverlays.Clear();
		canvasAnimatedImageOverlayKeys.Clear();
		foreach (var item in canvasImageOverlayNodes)
			IndexCanvasImageOverlays(item.Key, item.Value);
		foreach (var item in canvasDivOverlayNodes)
			IndexCanvasDivOverlays(item.Key, item.Value);
	}

	void IndexCanvasImageOverlays(int lineNo, List<CanvasImageOverlay> nodes)
	{
		if (nodes == null)
			return;
		for (int i = 0; i < nodes.Count; i++)
		{
			if (nodes[i].EscapesLine)
				canvasRowsWithEscapedOverlays.Add(lineNo);
			if (nodes[i].IsAnimation)
				canvasAnimatedImageOverlayKeys.Add(new CanvasOverlayKey(lineNo, i));
		}
	}

	void IndexCanvasDivOverlays(int lineNo, List<CanvasDivOverlay> nodes)
	{
		if (nodes == null)
			return;
		for (int i = 0; i < nodes.Count; i++)
		{
			if (nodes[i].EscapesLine)
				canvasRowsWithEscapedOverlays.Add(lineNo);
		}
	}

	void ClearCanvasOverlayMetadataForLine(int lineNo)
	{
		canvasRowsWithEscapedOverlays.Remove(lineNo);
		for (int i = canvasAnimatedImageOverlayKeys.Count - 1; i >= 0; i--)
		{
			if (canvasAnimatedImageOverlayKeys[i].LineNo == lineNo)
				canvasAnimatedImageOverlayKeys.RemoveAt(i);
		}
	}

	void ClearCanvasOverlayIndexesForLine(int lineNo)
	{
		ClearCanvasOverlayMetadataForLine(lineNo);
		canvasLastVisibilityRows.Remove(lineNo);
		canvasVisibilityTargetRowSet.Remove(lineNo);
		canvasVisibilityTargetRows.Remove(lineNo);
		canvasCurrentVisibilityRows.Remove(lineNo);
	}

	void ClearCanvasOverlayIndexes()
	{
		canvasRowsWithEscapedOverlays.Clear();
		canvasRowsWithPositionedNodes.Clear();
		canvasLastVisibilityRows.Clear();
		canvasVisibilityTargetRows.Clear();
		canvasVisibilityTargetRowSet.Clear();
		canvasCurrentVisibilityRows.Clear();
		canvasAnimatedImageOverlayKeys.Clear();
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
		ReleaseTexturePinList(pins);
		lineTexturePins.Remove(lineNo);
	}

	void ResetLineTexturePins()
	{
		foreach (var pins in lineTexturePins.Values)
			ReleaseTexturePinList(pins);
		lineTexturePins.Clear();
	}

	void MarkLineLayoutDirty()
	{
		lineLayoutDirty = true;
		canvasOverlayRowsDirty = true;
	}

	void EnsureLineLayout()
	{
		if (!lineLayoutDirty)
			return;

		// Canvas 热路径只读这份紧凑快照。行增删仍集中在 lineNumbers/lineSizes，
		// 这里按需重建，避免批量输出时每行都重新计算所有后续 Y 坐标。
		lineLayoutEntries.Clear();
		lineLayoutIndexByLineNo.Clear();
		float y = 0;
		foreach (int lineNo in lineNumbers)
		{
			if (!lineSizes.TryGetValue(lineNo, out var size))
				continue;
			int index = lineLayoutEntries.Count;
			lineLayoutEntries.Add(new ConsoleLineLayoutEntry
			{
				LineNo = lineNo,
				Top = y,
				Size = size,
			});
			lineLayoutIndexByLineNo[lineNo] = index;
			y += size.Y;
		}
		lineLayoutDirty = false;
	}

	float GetLineTopByLayout(int lineNo)
	{
		EnsureLineLayout();
		if (lineLayoutIndexByLineNo.TryGetValue(lineNo, out int index)
			&& index >= 0
			&& index < lineLayoutEntries.Count)
			return lineLayoutEntries[index].Top;
		return totalLineHeight;
	}

	bool TryGetVisibleCanvasLineLayoutRange(Rect2 visible, out int firstIndex, out int lastIndex)
	{
		EnsureLineLayout();
		firstIndex = 0;
		lastIndex = -1;
		if (lineLayoutEntries.Count == 0)
			return false;

		// 先二分到首个与可视区相交的行，再向后走当前视口范围。
		// 视口内行数通常远小于 backlog 行数，滚动和命中表重建都能稳定在可视行成本。
		float top = visible.Position.Y;
		float bottom = visible.Position.Y + visible.Size.Y;
		int low = 0;
		int high = lineLayoutEntries.Count - 1;
		int first = lineLayoutEntries.Count;
		while (low <= high)
		{
			int mid = low + ((high - low) >> 1);
			if (lineLayoutEntries[mid].Bottom >= top)
			{
				first = mid;
				high = mid - 1;
			}
			else
			{
				low = mid + 1;
			}
		}

		if (first >= lineLayoutEntries.Count)
			return false;

		int last = first;
		while (last + 1 < lineLayoutEntries.Count && lineLayoutEntries[last + 1].Top <= bottom)
			last++;
		firstIndex = first;
		lastIndex = last;
		return true;
	}

	void FlushCanvasOverlayRowsIfNeeded()
	{
		if (!canvasOverlayRowsDirty || batchingDisplayLines)
			return;
		if (!UseCanvasRenderBackend)
		{
			canvasOverlayRowsDirty = false;
			return;
		}
		RefreshCanvasOverlayRows();
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
		if (!GenericUtils.IsScrollTraceActive)
			return;
		string suffix = string.IsNullOrEmpty(detail) ? "" : " " + detail;
		GenericUtils.ScrollTrace("ui", $"{action}{suffix} {GetScrollTraceState()}");
	}

	void TraceScroll(string action, Func<string> detailFactory)
	{
		// 企业级说明：滚动追踪覆盖触摸、惯性和 UI 自动滚动路径。
		// Android/APK 默认关闭时必须避免构造 detail 字符串，降低触摸帧中的 GC 压力。
		if (!GenericUtils.IsScrollTraceActive)
			return;
		TraceScroll(action, detailFactory != null ? detailFactory() : null);
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
		int lineCount = GetRetainedLineCount();
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
			if (GenericUtils.IsScrollTraceActive)
				TraceScroll("scroll_bottom_request_merge", () => $"deadline={pendingScrollDeadlineTick}");
			return;
		}
		pendingScroll = true;
		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("scroll_bottom_request", () => $"deadline={pendingScrollDeadlineTick}");
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
					if (GenericUtils.IsScrollTraceActive)
						TraceScroll("scroll_bottom_max", () => $"max={maxScroll} stableDeadline={stableDeadline}");
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
			if (GenericUtils.IsScrollTraceActive)
				TraceScroll("scroll_bottom_end", () => $"reason={endReason}");
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
			(consoleRenderSurface != null && (consoleRenderSurface.CustomMinimumSize != layoutSize || consoleRenderSurface.Size != layoutSize)) ||
			scaledContentRoot.CustomMinimumSize != scaledSize ||
			scaledContentRoot.Size != scaledSize;

		lineContainer.CustomMinimumSize = layoutSize;
		lineContainer.Position = Vector2.Zero;
		lineContainer.Size = layoutSize;
		if (consoleRenderSurface != null)
		{
			consoleRenderSurface.CustomMinimumSize = layoutSize;
			consoleRenderSurface.Position = Vector2.Zero;
			consoleRenderSurface.Size = layoutSize;
		}
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
				if (GenericUtils.IsScrollTraceActive)
					TraceScroll("scale_bounds_scroll_set", () => $"allowShrink={allowShrink} from=({oldHorizontal},{oldVertical}) to=({targetHorizontal},{targetVertical}) limit=({limit.X},{limit.Y})");
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
		if (!UseCanvasRenderBackend && visibleRows > 1 && lineContainer is VBoxContainer rows)
			height += (visibleRows - 1) * rows.GetThemeConstant("separation");
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
						if (TryGetDisplayTextureInfo(resName, tryPath, out var ti))
						{
							texture = ti.texture;
							break;
						}
						if (RequestAsyncTextureForCurrentRender(resName, tryPath))
							break;
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
							if (TryGetDisplayTextureInfo(resName, found, out var ti))
							{
								GenericUtils.Info(EmueraLogCategory.Sprite, () => $"[IMG] Found \"{resName}\" via subdirectory search: {found}");
								texture = ti.texture;
								break;
							}
							if (RequestAsyncTextureForCurrentRender(resName, found))
							{
								GenericUtils.Info(EmueraLogCategory.Sprite, () => $"[IMG] Found \"{resName}\" via subdirectory search: {found}");
								break;
							}
						}
					}
				}
				if (texture == null && !HasPendingAsyncTextureForCurrentRender())
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
				if (ImageEscapesLine(cip))
					emuImg.ZIndex = EscapedConsolePartZIndex;
				// Inline images are absolutely positioned inside a fixed-height Emuera line.
				// Giving them a minimum size lets Godot containers add blank vertical space.
				emuImg.CustomMinimumSize = Vector2.Zero;
				container.AddChild(emuImg);
				if (GenericUtils.IsImageDebugEnabled("render_rect"))
					GenericUtils.ImageTrace("IMAGE.RENDER.TARGET", () => "image render target",
						() => $"resource={cip.ResourceName} target=({Mathf.RoundToInt(emuImg.Position.X)},{Mathf.RoundToInt(emuImg.Position.Y)},{w},{imgH})");
				if (GenericUtils.IsUiLayoutTraceEnabled("image"))
					QueueUiLayoutTrace(emuImg, "image", cip.ResourceName, Mathf.RoundToInt(emuImg.Position.X), Mathf.RoundToInt(emuImg.Position.Y), w, imgH);
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
				colorRect.Color = sc.ToGodotColor();
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
		return EffectiveLineHeight;
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
			bg.Color = c.ToGodotColor();
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
		int childLineHeight = HtmlDivLineHeight;
		foreach (var childLine in div.Children)
		{
			AddDisplayLineToContainer(childLine, content, y, childLineHeight);
			// 原核心在 div 内按固定文本行高推进；div、图片等外溢部件由父级裁剪/覆盖绘制处理，
			// 不参与普通文本流的行高扩张。
			y += childLineHeight;
		}

		return wrapper;
	}

	// Render a child ConsoleDisplayLine into an existing container at yOffset.
	// Used by nested divs and island output.
	int AddDisplayLineToContainer(ConsoleDisplayLine line, Control container, int yOffset, int rowHeight = -1)
	{
		if (line == null)
			return 0;
		if (rowHeight <= 0)
			rowHeight = EffectiveLineHeight;
		var row = new Control();
		row.MouseFilter = MouseFilterEnum.Pass;
		row.ClipContents = false;
		row.Position = new Vector2(0, yOffset);

		foreach (var button in line.Buttons)
		{
			if (button.IsButton)
			{
				int buttonTop = GetButtonTop(button);
				int buttonHeight = GetButtonBottom(button, true, rowHeight) - buttonTop;
				if (buttonHeight <= 0)
					buttonHeight = rowHeight;
				var btn = BuildConsoleButton(button, buttonTop, buttonHeight);
				if (buttonTop < 0 || buttonHeight > rowHeight)
					btn.ZIndex = EscapedConsolePartZIndex;
				row.AddChild(btn);
			}
			else
			{
				foreach (var part in button.StrArray)
					AddPartToContainer(part, row, 0);
			}
		}

		int maxHeight = rowHeight;
		SetFixedControlSize(row, new Vector2(GetLineRight(line, rowHeight), maxHeight));
		container.AddChild(row);
		return maxHeight;
	}

	// Compute the right edge of a console line for manual minimum-size tracking.
	int GetLineRight(ConsoleDisplayLine line, int rowHeight = -1)
	{
		int right = 0;
		if (line?.Buttons == null)
			return right;
		foreach (var button in line.Buttons)
		{
			if (button == null)
				continue;
			right = System.Math.Max(right, GetButtonVisualRight(button, rowHeight));
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
			var ti = bt.CachedTextureInfo;
			if (ti == null)
			{
				if (bt.RequestTextureInfoAsync())
					TrackAsyncTextureRequestForCurrentRender();
				return null;
			}
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
				var ti = GetDisplayTextureInfoForBitmap(bmp);
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
					var ti = GetDisplayTextureInfoForBitmap(bmp);
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

	SpriteManager.TextureInfo GetDisplayTextureInfoForBitmap(uEmuera.Drawing.Bitmap bmp)
	{
		if (bmp == null)
			return null;
		if (bmp is uEmuera.Drawing.BitmapTexture bt)
		{
			var ti = bt.CachedTextureInfo;
			if (ti == null && bt.RequestTextureInfoAsync())
				TrackAsyncTextureRequestForCurrentRender();
			return ti;
		}

		// 动态生成或兼容层来源的 Bitmap 需要立即读取像素，继续同步解析；
		// 只有文件 backed 的 BitmapTexture 用非阻塞显示路径。
		var sync = SpriteManager.GetTextureInfo(bmp.path, bmp.path);
		if (sync == null && !string.IsNullOrEmpty(bmp.filename))
			sync = SpriteManager.GetTextureInfo(bmp.filename, bmp.path);
		return sync;
	}

	bool TryGetDisplayTextureInfo(string name, string filename, out SpriteManager.TextureInfo ti)
	{
		if (SpriteManager.TryGetTextureInfoCached(name, filename, out ti))
		{
			TrackTexturePin(ti);
			return true;
		}
		return false;
	}

	bool RequestAsyncTextureForCurrentRender(string name, string filename)
	{
		if (!SpriteManager.RequestTextureInfoAsync(name, filename))
			return false;
		TrackAsyncTextureRequestForCurrentRender();
		return true;
	}

	void TrackAsyncTextureRequestForCurrentRender()
	{
		// Android 外部存储 I/O 与图片解码是主要帧尖峰来源。
		// 异步完成后只重建请求来源，避免把整个控制台重新生成一遍。
		if (activeRenderLineNo >= 0)
			asyncTexturePendingLineNos.Add(activeRenderLineNo);
		else if (renderingCbgTextures)
			pendingCbgAsyncTextureRefresh = true;
		else if (renderingHtmlIslandTextures)
			pendingHtmlIslandAsyncTextureRefresh = true;
	}

	bool HasPendingAsyncTextureForCurrentRender()
	{
		if (activeRenderLineNo >= 0 && asyncTexturePendingLineNos.Contains(activeRenderLineNo))
			return true;
		return (renderingCbgTextures && pendingCbgAsyncTextureRefresh)
			|| (renderingHtmlIslandTextures && pendingHtmlIslandAsyncTextureRefresh);
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
		if (count <= 0)
			return;

		int removeCount = System.Math.Min(count, lineNumbers.Count);
		if (removeCount <= 0)
			return;

		var lineNos = new List<int>(removeCount);
		foreach (int lineNo in lineNumbers)
		{
			lineNos.Add(lineNo);
			if (lineNos.Count >= removeCount)
				break;
		}
		RemoveLinesByNumber(lineNos);
	}

	// Re-apply the row cap after the user changes MaxVisibleLines.
	public void TrimVisibleLinesToLimit()
	{
		int overflow = GetRetainedLineCount() - MaxVisibleLines;
		if (overflow > 0)
			RemoveTopLines(overflow);
	}

	// Remove recent rows when the core overwrites or updates the bottom output.
	public void RemoveBottomLines(int count)
	{
		if (count <= 0)
			return;

		int removeCount = System.Math.Min(count, lineNumbers.Count);
		if (removeCount <= 0)
			return;

		var lineNos = new List<int>(removeCount);
		foreach (int lineNo in lineNumbers.Reverse())
		{
			lineNos.Add(lineNo);
			if (lineNos.Count >= removeCount)
				break;
		}
		RemoveLinesByNumber(lineNos);
	}

	void RemoveLinesByNumber(List<int> lineNos)
	{
		if (lineNos == null || lineNos.Count == 0)
			return;

		GenericUtils.ClearPointingButton();
		bool removedAny = false;
		for (int i = 0; i < lineNos.Count; i++)
		{
			int lineNo = lineNos[i];
			lineControls.TryGetValue(lineNo, out var control);
			UnregisterLine(lineNo);
			if (control != null && GodotObject.IsInstanceValid(control))
				SafeQueueFree(control);
			removedAny = true;
		}
		if (removedAny)
		{
			displayRevision++;
			NotifyConsoleRenderContentChanged();
			RefreshQuickInputGate();
		}
	}

	void RemoveLineChildren(List<Node> children)
	{
		if (children == null || children.Count == 0)
			return;

		GenericUtils.ClearPointingButton();
		bool removedAny = false;
		for (int i = 0; i < children.Count; i++)
		{
			var child = children[i];
			if (child == null || !GodotObject.IsInstanceValid(child))
				continue;
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

	void ProcessAsyncTextureRefreshes()
	{
		long version = SpriteManager.TextureLoadVersion;
		if (version == observedTextureLoadVersion)
			return;
		observedTextureLoadVersion = version;

		bool changed = false;
		if (asyncTexturePendingLineNos.Count > 0)
		{
			var lineNos = new List<int>(asyncTexturePendingLineNos);
			asyncTexturePendingLineNos.Clear();
			lineNos.Sort();

			bool previousBatching = batchingDisplayLines;
			batchingDisplayLines = true;
			try
			{
				for (int i = 0; i < lineNos.Count; i++)
				{
					int lineNo = lineNos[i];
					if (pendingAsyncLineUpdates.TryGetValue(lineNo, out var pendingLine)
						|| lineObjects.TryGetValue(lineNo, out pendingLine))
					{
						AddLine(pendingLine, true);
						changed = true;
					}
				}
			}
			finally
			{
				batchingDisplayLines = previousBatching;
			}
		}

		if (pendingHtmlIslandAsyncTextureRefresh)
		{
			var lines = lastHtmlIslandLines;
			pendingHtmlIslandAsyncTextureRefresh = false;
			if (lines != null)
			{
				SetHtmlIsland(lines);
				changed = true;
			}
		}

		if (pendingCbgAsyncTextureRefresh)
		{
			pendingCbgAsyncTextureRefresh = false;
			if (lastCbgSourceLayers.Count > 0)
			{
				RefreshCBG(lastCbgSourceLayers);
				changed = true;
			}
		}

		if (changed)
		{
			FlushCanvasOverlayRowsIfNeeded();
			RefreshQuickInputGate();
			QueueScaleBoundsUpdate();
		}
	}

	// Refresh client background graphics from the core. Nodes are reused by index
	// so animated/background-heavy scenes avoid repeated allocation.
	internal void RefreshCBG(List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage> list)
	{
		if (cbgContainer == null)
			return;

		if (list == null || list.Count == 0)
		{
			ReleaseCbgTexturePins();
			lastCbgSourceLayers.Clear();
			pendingCbgAsyncTextureRefresh = false;
			TrimCbgNodes(0);
			return;
		}

		lastCbgSourceLayers = new List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage>(list);
		pendingCbgAsyncTextureRefresh = false;
		int currentScrollY = GetCurrentContentScrollY();
		// Treat one CBG refresh as an ownership transaction. GetSpriteTexture pins
		// into this temporary collector, then the collector becomes cbgTexturePins
		// only after all visible layers have been rebuilt.
		var previousTexturePinCollector = activeTexturePinCollector;
		var newCbgTexturePins = new List<SpriteManager.TextureInfo>();
		var entries = new List<CbgRenderEntry>();
		activeTexturePinCollector = newCbgTexturePins;
		bool previousCbgRender = renderingCbgTextures;
		renderingCbgTextures = true;
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

				var entry = new CbgRenderEntry
				{
					Layer = cbg,
				};
				if (texture is AtlasTexture atlas)
				{
					entry.SourceTexture = atlas.Atlas;
					entry.SourceRegion = atlas.Region;
				}
				else
				{
					entry.SourceTexture = texture;
					entry.SourceRegion = default;
				}
				bool flipX = cbg.width < 0;
				bool flipY = cbg.height < 0;
				int w = cbg.width != 0 ? System.Math.Abs(cbg.width) : (cbg.Img.DestBaseSize.Width > 0 ? cbg.Img.DestBaseSize.Width : texture.GetWidth());
				int h = cbg.height != 0 ? System.Math.Abs(cbg.height) : (cbg.Img.DestBaseSize.Height > 0 ? cbg.Img.DestBaseSize.Height : texture.GetHeight());
				entry.DrawOffset = GetSpriteHtmlDrawOffset(cbg.Img, cbg.Img.Name, w, h);
				entry.DrawSize = GetSpriteHtmlDrawSize(cbg.Img, cbg.Img.Name, w, h);
				entry.Position = GetCbgLayerPosition(cbg, currentScrollY);
				entry.Size = new Vector2(w, h);
				entry.FlipX = flipX;
				entry.FlipY = flipY;
				entry.Modulate = new Godot.Color(1, 1, 1, cbg.opacity);
				entries.Add(entry);
			}
		}
		catch
		{
			ReleaseTexturePinList(newCbgTexturePins);
			throw;
		}
		finally
		{
			renderingCbgTextures = previousCbgRender;
			activeTexturePinCollector = previousTexturePinCollector;
		}

		if (pendingCbgAsyncTextureRefresh && cbgNodes.Count > 0)
		{
			// 刷新背景层时如果新纹理还没就绪，保留旧 CBG 节点和旧 pin。
			// 否则本帧会把旧背景裁掉，玩家会看到白/空背景，等异步完成后再提交新背景。
			ReleaseTexturePinList(newCbgTexturePins);
			return;
		}

		ReleaseCbgTexturePins();
		renderedCbgLayers.Clear();
		for (int i = 0; i < entries.Count; i++)
		{
			var entry = entries[i];
			var emuImg = GetOrCreateCbgNode(i);
			emuImg.SourceTexture = entry.SourceTexture;
			emuImg.SourceRegion = entry.SourceRegion;
			emuImg.DrawOffset = entry.DrawOffset;
			emuImg.DrawSize = entry.DrawSize;
			emuImg.Position = entry.Position;
			emuImg.Size = entry.Size;
			emuImg.FlipX = entry.FlipX;
			emuImg.FlipY = entry.FlipY;
			emuImg.Modulate = entry.Modulate;
			emuImg.SetColorMatrix(entry.Layer.colorMatrix);
			emuImg.Visible = true;
			renderedCbgLayers.Add(entry.Layer);
		}
		cbgTexturePins = newCbgTexturePins;
		lastCbgScrollVertical = currentScrollY;
		TrimCbgNodes(entries.Count);
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
		player.PitchScale = 1.0f;
		player.StreamPaused = false;
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
		{
			soundPlayers[channel].Play();
			GenericUtils.NotifySoundPlaybackRepeated(channel);
			return;
		}
		GenericUtils.NotifySoundPlaybackFinished(channel);
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
		bgmPlayer.PitchScale = 1.0f;
		bgmPlayer.StreamPaused = false;
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
		// 企业级说明：音量边界由 GenericUtils 统一维护，避免核心状态与 Godot 播放器出现 0-100 规则漂移。
		return GenericUtils.ClampEraVolume(volume) / 100.0f;
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
			&& quickAutoHiddenWasVisible
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
					ClearQuickAutoHiddenState();
					quickButtons.ShowPad();
					UpdateSystemButtonVisuals();
				}
				return;
			}

			quickButtons.BeginBatch();
			try
			{
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
					ClearQuickAutoHiddenState();
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
			finally
			{
				quickButtons.EndBatch();
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
		quickAutoHiddenWasVisible = quickButtons != null && quickButtons.IsShow;
		quickAutoHiddenGeneration = (int)generation;
		quickAutoHiddenTick = quickInputGateTick;
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
		RestoreAutoHiddenQuickButtonsIfCurrent();
	}

	void ClearQuickAutoHiddenState()
	{
		quickAutoHiddenUntilNextButtons = false;
		quickAutoHiddenWasVisible = false;
		quickAutoHiddenGeneration = int.MinValue;
		quickAutoHiddenTick = 0;
	}

	void RestoreAutoHiddenQuickButtonsIfCurrent()
	{
		if (!quickAutoHiddenUntilNextButtons || !quickAutoHiddenWasVisible || quickButtons == null)
			return;
		if (quickAutoHiddenGeneration != lastButtonGeneration)
			return;

		// 企业级说明：部分 ERB 流程会在同一按钮代内完成处理并继续等待输入。
		// 快捷按钮此前为了防止连点被临时隐藏；核心空闲后必须恢复面板，否则手机端会失去“移动”等唯一触摸入口。
		ClearQuickAutoHiddenState();
		quickButtons.ShowPad();
		quickButtons.SetInputEnabled(true);
		SetLastButtonGeneration(lastButtonGeneration);
		UpdateSystemButtonVisuals();
	}

	// Use the command button's final colored part as the quick-button text color.
	Godot.Color GetQuickButtonColor(ConsoleButtonString button)
	{
		if (button.StrArray != null && button.StrArray.Length > 0)
		{
			if (button.StrArray[button.StrArray.Length - 1] is AConsoleColoredPart coloredPart)
			{
				var c = coloredPart.pColor;
				return c.ToGodotColor();
			}
		}
		return Config.ForeColor.ToGodotColor();
	}

	// Apply emuera background color to the full viewport.
	public void SetBackgroundColor(uEmuera.Drawing.Color color)
	{
		if (bgRect != null)
			bgRect.Color = color.ToGodotColor();
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
		if (GenericUtils.IsScrollTraceActive)
		{
			TraceScroll("button_pressed", () => $"input={GenericUtils.ClipTrace(input, 64)} gen={generation} lastGen={lastButtonGeneration} skip={skip}");
			GenericUtils.StartScrollTraceCoreWindow(() => $"button input={GenericUtils.ClipTrace(input, 64)} gen={generation} skip={skip}");
		}
		if (generation < lastButtonGeneration)
		{
			// Old button clicked - send empty input (acts as skip/advance)
			if (GenericUtils.IsScrollTraceActive)
				TraceScroll("button_pressed_old_generation", () => $"gen={generation} lastGen={lastButtonGeneration}");
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
		ClearQuickAutoHiddenState();
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
			ClearQuickAutoHiddenState();
			quickButtons.HidePad();
		}
		else
		{
			ClearQuickAutoHiddenState();
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
		path = System.IO.Path.Combine(path, $"emuera_{fname}.log");
		bool result = false;
		var console = GlobalStatic.Console;
		if (console != null)
			result = console.OutputLog(path);
		string diagnosticPath = GenericUtils.GetDefaultDiagnosticLogPath(fname);
		bool diagnosticResult = GenericUtils.ExportDiagnosticLog(diagnosticPath, out string diagnosticError);
		string diagnosticDisplayPath = diagnosticResult
			? GenericUtils.ResolveDiagnosticPathForDisplay(diagnosticPath)
			: diagnosticError;

		ShowMessageBox(
			MultiLanguage.Get("[SaveLog]", "Save Log"),
			$"{MultiLanguage.Get("[SavePath]", "Path")}:\n"
			+ $"emuera: {(result ? path : MultiLanguage.Get("[Failure]", "Failure"))}\n"
			+ $"gemuera: {diagnosticDisplayPath}");
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
		ClearQuickAutoHiddenState();
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
		if (consoleRenderSurface != null)
			consoleRenderSurface.Scale = scaleVector;
		if (lineContainer != null)
			lineContainer.Scale = scaleVector;
		if (htmlIslandContainer != null)
			htmlIslandContainer.Scale = scaleVector;
		if (cbgContainer != null)
			cbgContainer.Scale = scaleVector;
	}

	// Re-apply font size to existing generated controls after Config changes.
	public void RefreshFontSize()
	{
		int size = FontSize;
// Update all existing lines
		if (lineContainer != null)
		{
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
		}
		consoleRenderSurface?.MarkDirty();
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
		consoleRenderSurface?.SyncScrollRedraw();
		PublishAudioPlaybackPositions();
		RefreshCbgFollowScrollPositions();
		RefreshCbgAnimationPauseState();
		RefreshCanvasImageAnimations();
		RefreshQuickInputGate();
		RefreshUiDiagnosticOverlay();
		ProcessAsyncTextureRefreshes();
		if (quickInputGateActive && Time.GetTicksMsec() - quickInputGateTick >= QuickInputGateFallbackMs && !EmueraThread.instance.Running())
		{
			RestoreQuickInputGate();
		}
		else if (!quickInputGateActive
			&& quickAutoHiddenUntilNextButtons
			&& quickAutoHiddenWasVisible
			&& quickAutoHiddenTick > 0
			&& Time.GetTicksMsec() - quickAutoHiddenTick >= QuickInputGateFallbackMs
			&& !EmueraThread.instance.Running())
		{
			RestoreAutoHiddenQuickButtonsIfCurrent();
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
				// Android/Godot 上 release 不一定回到最初的 Panel.GuiInput。
				// 主画面 HTML_PRINT 按钮必须由 root 兜底提交，否则只剩 quick 面板能点击。
				if (!HandleContentPointerInput(@event, false))
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

	void SetCanvasVisualButton(string input, long generation)
	{
		input ??= "";
		if (canvasVisualButtonGeneration == generation
			&& string.Equals(canvasVisualButtonInput ?? "", input, StringComparison.Ordinal))
			return;
		canvasVisualButtonInput = input;
		canvasVisualButtonGeneration = generation;
		QueueCanvasVisualRedraw();
	}

	void ClearCanvasVisualButton()
	{
		if (canvasVisualButtonGeneration == long.MinValue && string.IsNullOrEmpty(canvasVisualButtonInput))
			return;
		canvasVisualButtonInput = null;
		canvasVisualButtonGeneration = long.MinValue;
		QueueCanvasVisualRedraw();
	}

	void QueueCanvasVisualRedraw()
	{
		if (!UseCanvasRenderBackend || consoleRenderSurface == null || !GodotObject.IsInstanceValid(consoleRenderSurface))
			return;
		consoleRenderSurface.QueueRedraw();
	}

	bool IsCanvasButtonVisuallySelected(ConsoleButtonString button)
	{
		if (button == null || !button.IsButton)
			return false;
		if (canvasVisualButtonGeneration == long.MinValue)
			return false;
		return button.Generation == canvasVisualButtonGeneration
			&& string.Equals(button.Inputs ?? "", canvasVisualButtonInput ?? "", StringComparison.Ordinal);
	}

	bool IsContentBackLogView()
	{
		if (scrollContainer == null)
			return false;
		return scrollContainer.ScrollVertical < GetMaxContentVerticalScroll();
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

		if (motion && !contentDragActive)
		{
			if (TryFindConsoleButtonAtGlobalPosition(pointerPosition, out _, out var hoverInput, out var hoverGeneration, out _, out _))
				SetCanvasVisualButton(hoverInput, hoverGeneration);
			else
				ClearCanvasVisualButton();
			return false;
		}

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
			bool hitContentCenterValid = false;
			Vector2 hitContentCenter = Vector2.Zero;
			if (button == null && TryFindConsoleButtonAtGlobalPosition(pointerPosition, out var hitButton, out var hitInput, out var hitGeneration, out hitContentCenterValid, out hitContentCenter))
			{
				button = hitButton;
				input = hitInput;
				generation = hitGeneration;
			}

			MinorShift._Library.WinInput.PulseVirtualKey(0x01);
			StopContentInertia();
			contentDragActive = true;
			contentDragMoved = false;
			contentDragStartedOnButton = button != null || !string.IsNullOrEmpty(input);
			contentDragButton = button;
			contentDragButtonInput = input;
			contentDragButtonGeneration = generation;
			contentDragButtonContentCenterValid = hitContentCenterValid;
			contentDragButtonContentCenter = hitContentCenter;
			if (contentDragStartedOnButton)
				SetCanvasVisualButton(input, generation);
			else
				ClearCanvasVisualButton();
			contentDragStartPosition = pointerPosition;
			contentDragLastPosition = pointerPosition;
			contentLastDragTick = Time.GetTicksMsec();
			lastScrollTraceDragTick = contentLastDragTick;
			if (GenericUtils.IsScrollTraceActive)
				TraceScroll("pointer_press", () => $"button={contentDragStartedOnButton} input={GenericUtils.ClipTrace(input, 64)} gen={generation} pos=({Mathf.RoundToInt(pointerPosition.X)},{Mathf.RoundToInt(pointerPosition.Y)}) accept={acceptEvent}");
			if (GenericUtils.IsTouchTraceEnabled("pointer"))
				GenericUtils.TouchTrace("TOUCH.POINTER.PRESS", () => "pointer press",
					() => $"button={contentDragStartedOnButton} pos=({Mathf.RoundToInt(pointerPosition.X)},{Mathf.RoundToInt(pointerPosition.Y)}) accept={acceptEvent}");
			CaptureInputReplayEvent(contentDragStartedOnButton ? "button_press" : "touch_press",
				input, pointerPosition, contentDragStartedOnButton);
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
				ClearCanvasVisualButton();
				if (GenericUtils.IsScrollTraceActive)
					TraceScroll("drag_start", () => $"total=({Mathf.RoundToInt(totalDelta.X)},{Mathf.RoundToInt(totalDelta.Y)}) threshold={ScrollDragThreshold}");
				if (GenericUtils.IsTouchTraceEnabled("drag"))
					GenericUtils.TouchTrace("TOUCH.DRAG.START", () => "drag start",
						() => $"total=({Mathf.RoundToInt(totalDelta.X)},{Mathf.RoundToInt(totalDelta.Y)}) threshold={ScrollDragThreshold}");
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
					if (GenericUtils.IsScrollTraceActive)
						TraceScroll("drag_move", () => $"raw=({Mathf.RoundToInt(rawScrollDelta.X)},{Mathf.RoundToInt(rawScrollDelta.Y)}) applied=({Mathf.RoundToInt(appliedDelta.X)},{Mathf.RoundToInt(appliedDelta.Y)})");
					if (GenericUtils.IsTouchTraceEnabled("drag"))
						GenericUtils.TouchTrace("TOUCH.DRAG.MOVE", () => "drag move",
							() => $"raw=({Mathf.RoundToInt(rawScrollDelta.X)},{Mathf.RoundToInt(rawScrollDelta.Y)}) applied=({Mathf.RoundToInt(appliedDelta.X)},{Mathf.RoundToInt(appliedDelta.Y)})");
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
		bool pressedButtonContentCenterValid = false;
		Vector2 pressedButtonContentCenter = Vector2.Zero;
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
			pressedButtonContentCenterValid = contentDragButtonContentCenterValid;
			pressedButtonContentCenter = contentDragButtonContentCenter;
			handled = true;
		}
		else if (!contentDragStartedOnButton)
		{
			var console = GlobalStatic.Console;
			advanceTap = console != null && (console.IsWaitingEnterKey || console.IsWaitAnyKey);
			handled = advanceTap;
			restoreQuickInputGate = advanceTap;
		}

		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("pointer_release", () => $"moved={contentDragMoved} button={contentDragStartedOnButton} pressedInput={GenericUtils.ClipTrace(pressedButtonInput, 64)} advance={advanceTap} handled={handled}");
		if (GenericUtils.IsTouchTraceEnabled("pointer"))
			GenericUtils.TouchTrace("TOUCH.POINTER.RELEASE", () => "pointer release",
				() => $"moved={contentDragMoved} button={contentDragStartedOnButton} advance={advanceTap} handled={handled}");
		CaptureInputReplayEvent(contentDragStartedOnButton ? "button_release" : "touch_release",
			pressedButtonInput, pointerPosition, handled);
		ResetContentDragState();
		if (pressedButtonInput != null)
		{
			if (pressedButtonContentCenterValid)
				UpdatePointerPositionForContentPoint(pressedButtonContentCenter);
			else
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

	// Android 上触摸事件有时只到达 ScrollContainer/root，绕过按钮 Panel.GuiInput。
	// 这里按当前渲染树反向命中一次，保持主视图按钮和 quick 按钮的输入路径一致。
	bool TryFindConsoleButtonAtGlobalPosition(Vector2 globalPosition, out Control button, out string input, out long generation, out bool contentCenterValid, out Vector2 contentCenter)
	{
		button = null;
		input = null;
		generation = 0;
		contentCenterValid = false;
		contentCenter = Vector2.Zero;
		if (scaledContentRoot == null || !GodotObject.IsInstanceValid(scaledContentRoot))
			return false;
		if (UseCanvasRenderBackend
			&& consoleRenderSurface != null
			&& GodotObject.IsInstanceValid(consoleRenderSurface)
			&& consoleRenderSurface.TryHitGlobal(globalPosition, out var hit))
		{
			input = hit.Input;
			generation = hit.Generation;
			contentCenterValid = true;
			contentCenter = hit.ContentCenter;
			return true;
		}
		if (TryFindConsoleButtonAtGlobalPosition(scaledContentRoot, globalPosition, out button, out input, out generation))
			return true;
		return false;
	}

	bool TryFindConsoleButtonAtGlobalPosition(Node node, Vector2 globalPosition, out Control button, out string input, out long generation)
	{
		button = null;
		input = null;
		generation = 0;
		if (node == null || !GodotObject.IsInstanceValid(node))
			return false;
		if (node is Control parentControl && !parentControl.Visible)
			return false;

		var children = node.GetChildren();
		for (int i = children.Count - 1; i >= 0; i--)
		{
			if (TryFindConsoleButtonAtGlobalPosition(children[i], globalPosition, out button, out input, out generation))
				return true;
		}

		if (node is not Control control || !control.HasMeta("button_input"))
			return false;
		if (!control.GetGlobalRect().HasPoint(globalPosition))
			return false;

		input = control.GetMeta("button_input").As<string>();
		generation = control.HasMeta("generation") ? control.GetMeta("generation").AsInt64() : 0;
		button = control;
		return !string.IsNullOrEmpty(input);
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
			CaptureInputReplayEvent("screen_drag", "", drag.Position, true);
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
		CaptureInputReplayEvent("screen_drag", "", drag.Position, true);
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
			if (GenericUtils.IsScrollTraceActive)
				TraceScroll("touch_gesture_begin", () => $"touches={contentTouchPositions.Count}");
			if (GenericUtils.IsTouchTraceEnabled("pinch"))
				GenericUtils.TouchTrace("TOUCH.PINCH.START", () => "pinch start",
					() => $"touches={contentTouchPositions.Count}");
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
		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("scale_focus_begin", () => $"from={contentScale:0.###} to={ClampContentScale(scale):0.###} focus=({Mathf.RoundToInt(localFocus.X)},{Mathf.RoundToInt(localFocus.Y)})");

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
		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("scale_focus_restore", () => $"target=({targetHorizontal},{targetVertical})");
	}

	// Finish a multi-touch gesture and lock in the final focused scroll position.
	void EndContentTouchGesture()
	{
		if (contentPinchFocusValid)
		{
			UpdateScaleBounds(true);
			RestoreContentScaleFocus(scaleFocusContentPoint, scaleFocusLocalPoint);
		}
		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("touch_gesture_end", () => $"focus={contentPinchFocusValid}");
		if (GenericUtils.IsTouchTraceEnabled("pinch"))
			GenericUtils.TouchTrace("TOUCH.PINCH.END", () => "pinch end",
				() => $"focus={contentPinchFocusValid}");
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

	void CaptureInputReplayEvent(string kind, string input, Vector2 globalPosition, bool consumed)
	{
		if (!GenericUtils.IsInputReplayCaptureEnabled)
			return;
		GenericUtils.CaptureInputReplay(kind, input, globalPosition,
			GetContentReplayPosition(globalPosition), BuildInputReplayWaitState(), consumed);
	}

	Vector2 GetContentReplayPosition(Vector2 globalPosition)
	{
		if (scrollContainer == null)
			return globalPosition;
		var rect = scrollContainer.GetGlobalRect();
		var contentPosition = globalPosition - rect.Position;
		contentPosition += new Vector2(scrollContainer.ScrollHorizontal, scrollContainer.ScrollVertical);
		if (contentScale > 0.001f)
			contentPosition /= contentScale;
		return contentPosition;
	}

	string BuildInputReplayWaitState()
	{
		var console = GlobalStatic.Console;
		if (console == null)
			return "none";
		return "input=" + console.IsWaitingInput
			+ ",enter=" + console.IsWaitingEnterKey
			+ ",any=" + console.IsWaitAnyKey
			+ ",something=" + console.IsWaitingInputSomething;
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

		if (button.Size.X <= 0 || button.Size.Y <= 0 || scaledContentRoot == null || !GodotObject.IsInstanceValid(scaledContentRoot))
		{
			UpdatePointerPosition(fallbackGlobalPosition);
			return;
		}

		var globalCenter = button.GetGlobalTransformWithCanvas() * (button.Size * 0.5f);
		// HTML island 和普通行不是同一个直接父容器；统一换算到 scaledContentRoot，
		// 再除以当前缩放，才能得到 emuera 核心期望的未缩放控制台坐标。
		var contentCenter = scaledContentRoot.GetGlobalTransformWithCanvas().AffineInverse() * globalCenter;
		if (contentScale > 0.001f)
			contentCenter /= contentScale;
		GenericUtils.SetPointerPosition(contentCenter.X, contentCenter.Y);
	}

	void UpdatePointerPositionForContentPoint(Vector2 contentPoint)
	{
		GenericUtils.SetPointerPosition(contentPoint.X, contentPoint.Y);
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
			if (GenericUtils.IsScrollTraceActive)
				TraceScroll("scroll_delta", () => $"delta=({Mathf.RoundToInt(delta.X)},{Mathf.RoundToInt(delta.Y)}) applied=({Mathf.RoundToInt(applied.X)},{Mathf.RoundToInt(applied.Y)})");
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
		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("scroll_correction", () => $"from=({oldHorizontal},{oldVertical}) to=({targetHorizontal},{targetVertical}) limit=({limit.X},{limit.Y})");
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
			if (GenericUtils.IsScrollTraceActive)
				TraceScroll("inertia_start", () => $"speed={Mathf.RoundToInt(contentScrollVelocity.Length())} decel={Mathf.RoundToInt(contentInertiaDeceleration)}");
			if (GenericUtils.IsTouchTraceEnabled("inertia"))
				GenericUtils.TouchTrace("TOUCH.INERTIA.START", () => "inertia start",
					() => $"speed={Mathf.RoundToInt(contentScrollVelocity.Length())} decel={Mathf.RoundToInt(contentInertiaDeceleration)}");
		}
		else
			StopContentInertia();
	}

	// Stop inertial scrolling and clear fractional remainder.
	void StopContentInertia()
	{
		bool shouldLog = contentInertiaActive || contentScrollVelocity.LengthSquared() > 0.01f || contentInertiaRemainder.LengthSquared() > 0.01f;
		if (shouldLog)
		{
			if (GenericUtils.IsScrollTraceActive)
				TraceScroll("inertia_stop", () => $"speed={Mathf.RoundToInt(contentScrollVelocity.Length())}");
			if (GenericUtils.IsTouchTraceEnabled("inertia"))
				GenericUtils.TouchTrace("TOUCH.INERTIA.STOP", () => "inertia stop",
					() => $"speed={Mathf.RoundToInt(contentScrollVelocity.Length())}");
		}
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
		if (GenericUtils.IsScrollTraceActive)
		{
			TraceScroll("advance_tap", () => $"skip={skipFlag}");
			GenericUtils.StartScrollTraceCoreWindow(() => $"advance_tap skip={skipFlag}");
		}
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
		contentDragButtonContentCenterValid = false;
		contentDragButtonContentCenter = Vector2.Zero;
		ClearCanvasVisualButton();
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
			int windowsKeyData = ToWindowsKeyData(keyEvent);
			var console = GlobalStatic.Console;
			if (console != null && EmueraThread.instance != null)
			{
				if (windowsKeyData == (0x44 | 0x00020000))
				{
					console.ToggleHotkeyState(out string message);
					GenericUtils.InputTrace("INPUT.HOTKEY.TOGGLE", () => message ?? "");
					GetViewport().SetInputAsHandled();
					return;
				}
				if (console.TryEvaluateHotkey(windowsKeyData, out long hotkeyInput))
				{
					// HOTKEY.ERB は WinForms の KeyData 値を前提にした簡易インタプリタ。
					// Godot 版ではハードウェアキーボード入力だけをここで数値入力へ変換し、
					// タッチ・クイックボタンの入力経路には影響させない。
					EmueraThread.instance.Input(hotkeyInput.ToString(), true);
					GetViewport().SetInputAsHandled();
					return;
				}
				if (inputpad != null && inputpad.IsShow)
					return;
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

	static int ToWindowsKeyData(InputEventKey keyEvent)
	{
		int keyCode = ToWindowsKeyCode(keyEvent.Keycode);
		int modifiers = 0;
		if (keyEvent.ShiftPressed)
			modifiers |= 0x00010000;
		if (keyEvent.CtrlPressed)
			modifiers |= 0x00020000;
		if (keyEvent.AltPressed)
			modifiers |= 0x00040000;
		return keyCode | modifiers;
	}

	static int ToWindowsKeyCode(Key key)
	{
		if (key >= Key.A && key <= Key.Z)
			return 0x41 + (int)(key - Key.A);
		if (key >= Key.Key0 && key <= Key.Key9)
			return 0x30 + (int)(key - Key.Key0);
		if (key >= Key.F1 && key <= Key.F12)
			return 0x70 + (int)(key - Key.F1);
		if (key >= Key.Kp0 && key <= Key.Kp9)
			return 0x60 + (int)(key - Key.Kp0);
		return key switch
		{
			Key.Enter or Key.KpEnter => 0x0D,
			Key.Escape => 0x1B,
			Key.Space => 0x20,
			Key.Tab => 0x09,
			Key.Backspace => 0x08,
			Key.Left => 0x25,
			Key.Up => 0x26,
			Key.Right => 0x27,
			Key.Down => 0x28,
			Key.Home => 0x24,
			Key.End => 0x23,
			Key.Pageup => 0x21,
			Key.Pagedown => 0x22,
			Key.Insert => 0x2D,
			Key.Delete => 0x2E,
			Key.Quoteleft => 0xC0,
			_ => (int)key,
		};
	}

	sealed partial class ConsoleTextPart : Control
	{
		readonly Font font;
		readonly int fontSize;
		readonly Color color;
		readonly bool bold;
		readonly string text;
		Vector2 fixedSize;

		public ConsoleTextPart(Font font, int fontSize, Color color, bool bold, string text)
		{
			this.font = font;
			this.fontSize = fontSize > 0 ? fontSize : 18;
			this.color = color;
			this.bold = bold;
			this.text = uEmuera.Utils.StripZeroWidth(text) ?? "";
			MouseFilter = MouseFilterEnum.Ignore;
			ClipContents = true;
		}

		public void SetFixedSize(Vector2 size)
		{
			fixedSize = size;
			UpdateMinimumSize();
			QueueRedraw();
		}

		public override Vector2 _GetMinimumSize()
		{
			return fixedSize;
		}

		public override void _Draw()
		{
			if (font == null || string.IsNullOrEmpty(text))
				return;

			float baseline = GetTextBaseline(font, fontSize, Size.Y);
			if (!ShouldUseGridDrawing(text))
			{
				DrawPlainText(baseline);
				return;
			}

			float exactX = 0.0f;
			float drawX = 0.0f;
			for (int i = 0; i < text.Length; i++)
			{
				bool half = uEmuera.Utils.CheckHalfSize(text[i]);
				// 布局宽度由 Utils.GetDisplayLength 决定，奇数字号下半角字符会按累计整数截断。
				// 绘制也用同一格点推进，避免 Button 和 Label 之间出现 0.5px 累计偏移。
				float nextExactX = exactX + GetCellWidth(half);
				float nextDrawX = (int)nextExactX;
				float cellWidth = nextDrawX - drawX;
				DrawGridChar(text[i], drawX, baseline, cellWidth);
				exactX = nextExactX;
				drawX = nextDrawX;
			}
		}

		void DrawPlainText(float baseline)
		{
			float drawWidth = System.Math.Max(1.0f, Size.X);
			DrawString(font, new Vector2(0, baseline), text, HorizontalAlignment.Left, drawWidth, fontSize, color);
			if (bold)
				DrawString(font, new Vector2(1.0f, baseline), text, HorizontalAlignment.Left, System.Math.Max(1.0f, drawWidth - 1.0f), fontSize, color);
		}

		static float GetTextBaseline(Font font, int fontSize, float height)
		{
			float fontHeight = font.GetHeight(fontSize);
			float ascent = font.GetAscent(fontSize);
			return Mathf.Round((height - fontHeight) * 0.5f + ascent);
		}

		float GetCellWidth(bool half)
		{
			return half ? fontSize / 2.0f : fontSize;
		}

		static bool ShouldUseGridDrawing(string value)
		{
			// 普通文字按整段字体 advance 绘制，以贴近 v24/snake 的 GDI/SkiaSharp 横向间距。
			// 地图、表格、箱线和空白对齐仍走固定半角/全角格点，避免移动端布局漂移。
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
			return (c >= '\u2500' && c <= '\u257F') // Box Drawing
				|| (c >= '\u2580' && c <= '\u259F') // Block Elements
				|| (c >= '\u25A0' && c <= '\u25FF') // Geometric Shapes
				|| (c >= '\u2800' && c <= '\u28FF') // Braille Patterns
				|| c == '\u3000';
		}

		void DrawGridChar(char value, float x, float baseline, float cellWidth)
		{
			// 每个字符仍按 emuera 的网格起点绘制，但不能再按单元格宽度裁剪字形。
			// Godot 字体 fallback 下，DRAWLINE/箱线字符的实际 glyph 往往宽于半角格；
			// 若逐格裁剪会出现横线缺失。片段边界继续由本 Control 的 ClipContents 统一限制。
			string glyph = value.ToString();
			float drawWidth = System.Math.Max(1.0f, System.Math.Max(cellWidth, Size.X - x));
			DrawString(font, new Vector2(x, baseline), glyph, HorizontalAlignment.Left, drawWidth, fontSize, color);
			if (bold)
				DrawString(font, new Vector2(x + 1.0f, baseline), glyph, HorizontalAlignment.Left, System.Math.Max(1.0f, drawWidth - 1.0f), fontSize, color);
		}
	}

	sealed partial class UiDiagnosticOverlay : Control
	{
		const ulong RectLifetimeMs = 2500;
		readonly List<OverlayRect> rects = new List<OverlayRect>(128);

		public int MaxRects { get; set; } = 128;

		struct OverlayRect
		{
			public Rect2 Rect;
			public Color Color;
			public ulong ExpireTick;
		}

		public override void _Ready()
		{
			MouseFilter = MouseFilterEnum.Ignore;
			SetProcess(true);
		}

		public void AddRect(Rect2 rect, Color color)
		{
			if (rect.Size.X <= 0 || rect.Size.Y <= 0)
				return;
			int maxRects = System.Math.Max(1, MaxRects);
			while (rects.Count >= maxRects)
				rects.RemoveAt(0);
			rects.Add(new OverlayRect
			{
				Rect = rect,
				Color = color,
				ExpireTick = Time.GetTicksMsec() + RectLifetimeMs
			});
			QueueRedraw();
		}

		public void ClearRects()
		{
			if (rects.Count == 0)
				return;
			rects.Clear();
			QueueRedraw();
		}

		public override void _Process(double delta)
		{
			if (rects.Count == 0)
				return;
			ulong now = Time.GetTicksMsec();
			bool changed = false;
			for (int i = rects.Count - 1; i >= 0; i--)
			{
				if (rects[i].ExpireTick <= now)
				{
					rects.RemoveAt(i);
					changed = true;
				}
			}
			if (changed)
				QueueRedraw();
		}

		public override void _Draw()
		{
			for (int i = 0; i < rects.Count; i++)
				DrawRect(rects[i].Rect, rects[i].Color, false, 2.0f);
		}
	}
}

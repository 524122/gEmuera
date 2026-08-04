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
	Dictionary<int, Vector2> lineVisualExtents = new Dictionary<int, Vector2>();
	Dictionary<int, long> lineMaxButtonGeneration = new Dictionary<int, long>();
	Dictionary<int, List<long>> lineButtonGenerations = new Dictionary<int, List<long>>();
	Dictionary<long, SortedSet<int>> buttonGenerationLineNumbers = new Dictionary<long, SortedSet<int>>();
	HashSet<int> canvasRowsWithPositionedNodes = new HashSet<int>();
	HashSet<int> canvasRowsWithEscapedOverlays = new HashSet<int>();
	HashSet<int> viewportAnchoredRelativeDivLineNos = new HashSet<int>();
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
	const string ViewportAnchoredRelativeDivMeta = "viewport_anchor_relative_div";
	const string ViewportAnchoredRelativeDivRowMeta = "viewport_anchor_relative_div_row";
	const string ViewportAnchoredRelativeDivXMeta = "viewport_anchor_relative_div_x";
	const string ViewportAnchoredRelativeDivYMeta = "viewport_anchor_relative_div_y";
	const string ViewportAnchoredRelativeDivHeightMeta = "viewport_anchor_relative_div_height";
	const string InlineLineBackgroundDivMeta = "inline_line_background_div";
	// Texture pins mirror presentation lifetime: console rows own line pins and
	// CBG owns background pins. activeTexturePinCollector is scoped to the current
	// render pass so GetSpriteTexture can remain a pure conversion helper.
	Dictionary<int, List<SpriteManager.TextureInfo>> lineTexturePins = new Dictionary<int, List<SpriteManager.TextureInfo>>();
	List<SpriteManager.TextureInfo> cbgTexturePins = new List<SpriteManager.TextureInfo>();
	List<SpriteManager.TextureInfo> htmlIslandTexturePins = new List<SpriteManager.TextureInfo>();
	List<SpriteManager.TextureInfo> activeTexturePinCollector;
	readonly Dictionary<string, Font> consoleFontCache = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> missingConsoleFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	HashSet<int> asyncTexturePendingLineNos = new HashSet<int>();
	Dictionary<int, ConsoleDisplayLine> pendingAsyncLineUpdates = new Dictionary<int, ConsoleDisplayLine>();
	struct PureImageFallbackLine
	{
		public ConsoleDisplayLine Line;
		public int OriginalLineNo;
		public ulong RemovedAtMs;
	}
	List<PureImageFallbackLine> recentPureImageFallbackLines = new List<PureImageFallbackLine>();
	bool renderingAsyncImageFallbackLine = false;
	int activeRenderLineNo = -1;
	long observedTextureLoadVersion = 0;
	bool renderingCbgTextures = false;
	bool renderingHtmlIslandTextures = false;
	bool pendingCbgAsyncTextureRefresh = false;
	bool cbgTextureUnavailableDuringRender = false;
	bool pendingHtmlIslandAsyncTextureRefresh = false;

	// Texture lookup failures are memoized to avoid repeated recursive file scans
	// on Android storage where I/O stalls are very visible.
	HashSet<string> failedTextureSearches = new HashSet<string>();
	Dictionary<string, string> resolvedTextureSearchPaths = new Dictionary<string, string>();
	ulong lastCanvasAnimationRefreshMs = 0;
	const int MaxPureImageFallbackLines = 64;
	const ulong PureImageFallbackTtlMs = 2000;
	static readonly string[] CanvasImageFallbackExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tga" };

	// CBG nodes are reused instead of recreated whenever possible. This reduces
	// CanvasItem churn and texture upload pressure during rapid script updates.
	List<EmueraImage> cbgNodes = new List<EmueraImage>();
	List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage> renderedCbgLayers = new List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage>();
	List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage> lastCbgSourceLayers = new List<MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage>();
	ConsoleDisplayLine[] lastHtmlIslandLines = null;
	struct GraphicsImageTextureCacheEntry
	{
		public long Revision;
		public Texture2D Texture;
	}
	Dictionary<int, GraphicsImageTextureCacheEntry> graphicsImageTextureCache = new Dictionary<int, GraphicsImageTextureCacheEntry>();
	const ulong GraphicsImageDisplayStableDelayMs = 24;

	struct CbgRenderEntry
	{
		public MinorShift.Emuera.GameView.EmueraConsole.ClientBackGroundImage Layer;
		public Texture2D SourceTexture;
		public string AnimatedWebpPath;
		public Rect2 SourceRegion;
		public Vector2 Position;
		public Vector2 Size;
		public Vector2 DrawOffset;
		public Vector2 DrawSize;
		public bool FlipX;
		public bool FlipY;
		public Color Modulate;
	}

	struct SpriteAnimeFrameLayoutInfo
	{
		public bool IsValid;
		public int OffsetX;
		public int OffsetY;
		public int SourceWidth;
		public int SourceHeight;
	}

	// Batched display updates defer expensive follow-up work until a group of
	// lines has been applied.
	bool batchingDisplayLines = false;
	float totalLineHeight = 0;
	float widestLineWidth = 0;
	float widestVisualLineWidth = 0;
	float visualLayoutContentHeight = 0;

	// Visible line cap. Android memory pressure is the main constraint here, so
	// old Controls are trimmed in batches instead of letting the scene tree grow
	// without bound.
	public const int DefaultMaxVisibleLines = 360;
	const int DefaultSnakeDesktopMaxVisibleLines = 1500;
	const int DefaultMobileMaxVisibleLines = 240;
	const int DefaultSnakeMobileMaxVisibleLines = 600;
	public const int MinMaxVisibleLines = 120;
	public const int MaxMaxVisibleLines = 3000;
	static int MaxVisibleLines => ConfiguredMaxVisibleLines;
	static int LineTrimBatch => System.Math.Max(40, System.Math.Min(200, MaxVisibleLines / 6));
	const int ButtonGenerationScanMaxDepth = 8;

	// Button generation and quick-button cache state. emuera reuses button text
	// across waits, so generation guards prevent an old visible button from
	// submitting into a newer input prompt.
	Font mainFont;
	int lastButtonGeneration = -1;
	int displayRevision = 0;
	int quickRenderedGeneration = int.MinValue;
	int quickRenderedRevision = -1;
	string quickRenderedSignature = "";
	bool quickInputGateActive = false;
	bool quickAutoHiddenUntilNextButtons = false;
	bool quickAutoHiddenWasVisible = false;
	int quickAutoHiddenGeneration = int.MinValue;
	ulong quickAutoHiddenTick = 0;
	long quickInputGateGeneration = -1;
	int quickInputGateRevision = -1;
	ulong quickInputGateTick = 0;
	long maxRenderedButtonGeneration = long.MinValue;
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
	ulong contentLastDragTick = 0;
	bool contentInertiaActive = false;
	float contentInertiaDeceleration = 900.0f;
	int contentScrollInteractionSerial = 0;
	internal int ContentScrollInteractionSerial => contentScrollInteractionSerial;
	string canvasVisualButtonInput;
	long canvasVisualButtonGeneration = long.MinValue;

	// 虚拟鼠标状态：桌面端用真实鼠标按键直接传递；Android 端由 VirtualCursor
	// 接管单指手势（移动光标+长按=右键+短按=左键），不再有“预选按键”状态。
	int contentDragMouseVk = 0x01;       // 本次拖拽/点击使用的实际 VK
	VirtualCursor virtualCursor;

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
	bool pendingKeepChoicesVisible = false;
	int pendingKeepChoicesInteractionSerial = 0;
	const int KeepChoicesVisiblePaddingPx = 12;
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
	const int AndroidAsyncTextureRefreshLineBudget = 4;
	const int DesktopAsyncTextureRefreshLineBudget = 12;
	const float ContentInertiaMinVelocity = 80.0f;
	const float ContentInertiaFastVelocity = 4500.0f;
	const float ContentInertiaMaxVelocity = 14000.0f;
	const float ContentInertiaMinReleaseBoost = 1.2f;
	const float ContentInertiaMaxReleaseBoost = 3.0f;
	const float ContentInertiaSlowDeceleration = 1800.0f;
	const float ContentInertiaFastDeceleration = 520.0f;
	const float ContentInertiaStopVelocity = 6.0f;
	const int SystemButtonTouchSize = 48;
	const int MinDynamicContentWidth = 320;
	const float SafeAreaEpsilon = 0.5f;
	const int ContentHorizontalScrollTolerancePx = 1;
	const string SettingsPath = "user://settings.cfg";
	const string SettingsSection = "Display";
	const string ContentDragSensitivityKey = "ContentDragSensitivity";
	const string ButtonDragSensitivityKey = "ButtonDragSensitivity";
	const string MaxVisibleLinesKey = "MaxVisibleLines";
	const string ContentPinchZoomEnabledKey = "ContentPinchZoomEnabled";
	const string ConsoleRenderBackendKey = "ConsoleRenderBackend";
	// M0 runner 专用的显示后端覆盖值，不写入用户设置，避免影响正常启动。
	static string m0RunnerDisplayBackendOverride;
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
	TextureButton mouseMenuButton;
	bool autoClickSkipEnabled = false;
	ulong lastAutoClickSkipTick = 0;
	static readonly Color ActiveSystemButtonColor = new Color(1.0f, 0.86f, 0.15f, 1.0f);
	static readonly Color NormalSystemButtonColor = new Color(1, 1, 1, 1);

	public static int ContentWidth { get; private set; }
	public static int ContentHeight { get; private set; }
	public static int ContentSafeWidth { get; private set; }
	public static int ContentSafeHeight { get; private set; }
	public static Rect2 ContentSafeRect { get; private set; } = new Rect2(Vector2.Zero, Vector2.Zero);

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
				int rawValue = (int)cfg.GetValue(SettingsSection, MaxVisibleLinesKey, GetDefaultMaxVisibleLines());
				configuredMaxVisibleLines = ClampMaxVisibleLines(MigrateDefaultMaxVisibleLines(rawValue));
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
		if (OS.HasFeature("mobile"))
			return IsExtendedDisplayProfile() ? DefaultSnakeMobileMaxVisibleLines : DefaultMobileMaxVisibleLines;
		if (IsExtendedDisplayProfile())
			return DefaultSnakeDesktopMaxVisibleLines;
		return DefaultMaxVisibleLines;
	}

	static int MigrateDefaultMaxVisibleLines(int value)
	{
		// TW/Snake 就寝后会一次输出大量角色信息。旧默认会直接裁掉上方内容，
		// 因此把旧默认视为未显式调过；Android 只做保守提升，避免手机端内存压力过大。
		if (!IsExtendedDisplayProfile())
			return value;
		if (OS.HasFeature("mobile") && value == DefaultMobileMaxVisibleLines)
			return DefaultSnakeMobileMaxVisibleLines;
		if (!OS.HasFeature("mobile") && value == DefaultMaxVisibleLines)
			return DefaultSnakeDesktopMaxVisibleLines;
		return value;
	}

	static bool IsExtendedDisplayProfile()
	{
		return Program.Compatibility.UsesExtendedDisplayHistory;
	}

	// emuera still exposes the console viewport through Config.WindowY. Keeping
	// this helper isolated makes it easier to replace later with actual viewport
	// measurements if the core API changes.
	static int GetContentViewportHeight()
	{
		return Config.WindowY;
	}

	public static Rect2 GetSafeViewportRect(Viewport viewport)
	{
		Vector2 viewportSize = viewport?.GetVisibleRect().Size ?? Vector2.Zero;
		if (viewportSize.X <= 0 || viewportSize.Y <= 0)
		{
			Vector2I windowSize = DisplayServer.WindowGetSize();
			viewportSize = new Vector2(System.Math.Max(1, windowSize.X), System.Math.Max(1, windowSize.Y));
		}

		var fullRect = new Rect2(Vector2.Zero, viewportSize);
		if (!OS.HasFeature("mobile"))
			return fullRect;

		Rect2I displaySafeArea;
		try
		{
			displaySafeArea = DisplayServer.GetDisplaySafeArea();
		}
		catch
		{
			return fullRect;
		}

		if (displaySafeArea.Size.X <= 0 || displaySafeArea.Size.Y <= 0)
			return fullRect;

		Vector2I window = DisplayServer.WindowGetSize();
		Vector2 sourceSize = new Vector2(window.X, window.Y);
		if (sourceSize.X <= 0 || sourceSize.Y <= 0)
			sourceSize = viewportSize;

		float scaleX = viewportSize.X / sourceSize.X;
		float scaleY = viewportSize.Y / sourceSize.Y;
		var safe = new Rect2(
			new Vector2(displaySafeArea.Position.X * scaleX, displaySafeArea.Position.Y * scaleY),
			new Vector2(displaySafeArea.Size.X * scaleX, displaySafeArea.Size.Y * scaleY));
		return ClampRectToViewport(safe, viewportSize, fullRect);
	}

	static Rect2 ClampRectToViewport(Rect2 rect, Vector2 viewportSize, Rect2 fallback)
	{
		float left = Mathf.Clamp(rect.Position.X, 0, viewportSize.X);
		float top = Mathf.Clamp(rect.Position.Y, 0, viewportSize.Y);
		float right = Mathf.Clamp(rect.Position.X + rect.Size.X, left, viewportSize.X);
		float bottom = Mathf.Clamp(rect.Position.Y + rect.Size.Y, top, viewportSize.Y);
		if (right - left < 1 || bottom - top < 1)
			return fallback;
		return new Rect2(left, top, right - left, bottom - top);
	}

	static bool RectAlmostEqual(Rect2 a, Rect2 b)
	{
		return Mathf.Abs(a.Position.X - b.Position.X) <= SafeAreaEpsilon
			&& Mathf.Abs(a.Position.Y - b.Position.Y) <= SafeAreaEpsilon
			&& Mathf.Abs(a.Size.X - b.Size.X) <= SafeAreaEpsilon
			&& Mathf.Abs(a.Size.Y - b.Size.Y) <= SafeAreaEpsilon;
	}

	bool RefreshViewportMetrics()
	{
		Size = GetViewportRect().Size;
		ContentWidth = Mathf.RoundToInt(Size.X);
		ContentHeight = Mathf.RoundToInt(Size.Y);
		Rect2 nextSafeRect = GetSafeViewportRect(GetViewport());
		bool changed = !RectAlmostEqual(ContentSafeRect, nextSafeRect);
		ContentSafeRect = nextSafeRect;
		ContentSafeWidth = Mathf.RoundToInt(nextSafeRect.Size.X);
		ContentSafeHeight = Mathf.RoundToInt(nextSafeRect.Size.Y);
		return changed;
	}

	static void ApplySafeRect(Control control, Rect2 rect)
	{
		if (control == null)
			return;
		control.SetAnchorsPreset(LayoutPreset.TopLeft);
		control.Position = rect.Position;
		control.Size = rect.Size;
		control.CustomMinimumSize = rect.Size;
	}

	bool ApplySafeAreaLayout(bool forceCoreWidthRefresh = false)
	{
		bool safeChanged = RefreshViewportMetrics();
		Rect2 safeRect = ContentSafeRect;

		ApplySafeRect(rootContent, safeRect);
		ApplySafeRect(cbgContainer, safeRect);
		ApplySystemMenuSafeArea(safeRect);
		inputpad?.RefreshSafeAreaLayout();
		scalepad?.RefreshSafeAreaLayout();
		quickButtons?.RefreshSafeAreaLayout();

		bool widthChanged = ApplyAndroidDynamicWindowWidth(forceCoreWidthRefresh || safeChanged);
		if (safeChanged || widthChanged)
			QueueScaleBoundsUpdate();
		return safeChanged || widthChanged;
	}

	void ApplySystemMenuSafeArea(Rect2 safeRect)
	{
		if (menuRoot == null)
			return;

		Vector2 viewportSize = GetViewportRect().Size;
		float rightInset = Mathf.Max(0, viewportSize.X - (safeRect.Position.X + safeRect.Size.X));
		float topInset = Mathf.Max(0, safeRect.Position.Y);
		menuRoot.OffsetRight = -4 - rightInset;
		menuRoot.OffsetTop = 4 + topInset;
	}

	bool ApplyAndroidDynamicWindowWidth(bool requestRefresh)
	{
		if (OS.GetName() != "Android" || Config.WindowX <= 0 || ContentSafeWidth <= 0)
			return false;

		int targetWidth = System.Math.Max(MinDynamicContentWidth, ContentSafeWidth);
		if (System.Math.Abs(Config.WindowX - targetWidth) <= 1)
			return false;

		int previousWidth = Config.WindowX;
		Config.UpdateWindowWidth(targetWidth);
		GenericUtils.Info($"[UI] Android dynamic content width: {previousWidth} -> {targetWidth}, safe={ContentSafeWidth}x{ContentSafeHeight}, viewport={ContentWidth}x{ContentHeight}");
		if (requestRefresh)
			GlobalStatic.MainWindow?.Refresh();
		return true;
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

	public static bool ConfigureM0RunnerDisplayBackend(string value, out string errorMessage)
	{
		errorMessage = "";
		if (!string.Equals(value, "controls", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(value, "canvas", StringComparison.OrdinalIgnoreCase))
		{
			errorMessage = "m0_display_backend_must_be_controls_or_canvas";
			return false;
		}
		m0RunnerDisplayBackendOverride = string.Equals(value, "controls", StringComparison.OrdinalIgnoreCase)
			? "controls"
			: "canvas";
		return true;
	}

	ConsoleRenderBackend LoadConsoleRenderBackend()
	{
		if (!string.IsNullOrEmpty(m0RunnerDisplayBackendOverride))
			return string.Equals(m0RunnerDisplayBackendOverride, "controls", StringComparison.Ordinal)
				? ConsoleRenderBackend.Controls
				: ConsoleRenderBackend.Canvas;
		var cfg = new ConfigFile();
		cfg.Load(SettingsPath);
		string value = cfg.GetValue(SettingsSection, ConsoleRenderBackendKey, "canvas").AsString();
		return string.Equals(value, "controls", StringComparison.OrdinalIgnoreCase)
			? ConsoleRenderBackend.Controls
			: ConsoleRenderBackend.Canvas;
	}

	bool UseCanvasRenderBackend => consoleRenderBackend == ConsoleRenderBackend.Canvas;

	public gEmuera.M0.LegacyDisplayObservation CaptureM0LegacyDisplayObservation(string requestedBackend)
	{
		return BuildM0LegacyDisplayObservation(requestedBackend);
	}

	// Build the UI tree entirely in code because the emulator surface is dynamic:
	// lines, buttons, overlays, and background images are all generated from ERB
	// output rather than fixed scene resources.
	public override void _Ready()
	{
		instance = this;
		RefreshViewportMetrics();
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
		// CBG 背景层（cbgContainer，ZIndex=10）必须位于控制台前景之下：eraTW 系列把
		// 立绘/文字/行内图片放在 rootContent 内（累计 z=0），若低于背景层会被半透明
		// 背景图蒙罩（"低亮/无衬底"模式下背景带 alpha，立绘看起来跟着变透明）。
		// 这里把整个内容层抬到 20（>10），保持内部相对顺序不变，与 Snake 语义一致：
		// 背景色 → CBG 背景图 → 前景文字/图片。
		rootContent.ZIndex = 20;
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
		mouseMenuButton = AddIconButton("res://Icons/mouse.svg", OnVirtualCursorTogglePressed);

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

		virtualCursor = new VirtualCursor();
		AddChild(virtualCursor);

		inputpad = new Inputpad();
		AddChild(inputpad);

		scalepad = new Scalepad();
		AddChild(scalepad);

		optionWindow = new OptionWindow();
		AddChild(optionWindow);

		// Main console viewport. Horizontal scrolling is enabled only while the
		// scaled content is wider than the safe viewport; vertical range remains
		// Godot-owned.
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
		cbgContainer.ClipContents = true;
		cbgContainer.ZIndex = 10;
		AddChild(cbgContainer);

		ApplySafeAreaLayout(true);
	}

	// Clear all generated console state. This is used for title changes/reloads
	// and must reset both Godot nodes and the O(1) lookup indexes.
	public void Clear()
	{
		GenericUtils.ClearPointingButton();
		ClearCanvasVisualButton();
		DisposeAndroidSpriteAnimeFrameTextures();
		if (lineContainer != null)
		{
			foreach(var child in lineContainer.GetChildren())
				SafeQueueFree(child);
		}
		ResetLineIndexes();
		consoleRenderSurface?.MarkDirty();
		failedTextureSearches.Clear();
		resolvedTextureSearchPaths.Clear();
		lastCanvasAnimationRefreshMs = 0;
		asyncTexturePendingLineNos.Clear();
		pendingAsyncLineUpdates.Clear();
		pendingCbgAsyncTextureRefresh = false;
		pendingHtmlIslandAsyncTextureRefresh = false;
		displayRevision++;
		RefreshQuickInputGate();
		quickRenderedRevision = -1;
		quickRenderedSignature = "";
		if (quickButtons != null && quickButtons.IsShow)
			quickButtons.Clear();
	}

	// 仅供已停止 legacy worker 的 M0 会话切换调用，释放会话级 Godot 资源而不改变普通重载语义。
	internal void ClearForCanarySessionTransition()
	{
		Clear();
		ClearHtmlIsland();
		ClearCbgSessionState();
		ClearSessionAudioState();
		ClearGraphicsImageTextureCache();
		ClearSessionFontCache();
	}

	public override void _ExitTree()
	{
		GetViewport().SizeChanged -= OnViewportSizeChanged;
		ResetLineTexturePins();
		ReleaseCbgTexturePins();
		DisposeAndroidSpriteAnimeFrameTextures();
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
	Control CreateTextPart(string text, EmuColor color, EmuFont font, float width, EmuColor? selectedColor = null, FontVerticalAlign? verticalAlign = null)
	{
		int actualFontSize = font != null ? Math.Max(1, Mathf.RoundToInt(font.Size)) : FontSize;
		var textPart = new ConsoleTextPart(
			ResolveConsoleFont(font),
			actualFontSize,
			color.ToGodotColor(),
			(selectedColor ?? color).ToGodotColor(),
			font?.Bold == true,
			text,
			verticalAlign);
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
		string normalized = NormalizeConsoleFontName(requested);
		if (string.IsNullOrEmpty(normalized))
			return true;
		return IsBundledConsoleFontName(normalized);
	}

	static bool ShouldUseMainConsoleFont(string requested)
	{
		string normalized = NormalizeConsoleFontName(requested);
		if (string.IsNullOrEmpty(normalized))
			return true;
		string configFont = NormalizeConsoleFontName(Config.FontName);
		return normalized.Equals(configFont, System.StringComparison.OrdinalIgnoreCase)
			|| IsBundledConsoleFontName(normalized);
	}

	static string NormalizeConsoleFontName(string requested)
	{
		if (string.IsNullOrWhiteSpace(requested))
			return "";
		string normalized = requested.Trim();
		if (normalized.StartsWith("@", System.StringComparison.Ordinal))
			normalized = normalized.Substring(1);
		return normalized;
	}

	static bool IsBundledConsoleFontName(string normalized)
	{
		return normalized.Equals("MS Gothic", System.StringComparison.OrdinalIgnoreCase)
			|| normalized.Equals("MS UI Gothic", System.StringComparison.OrdinalIgnoreCase)
			|| normalized.Equals("\uFF2D\uFF33 \u30B4\u30B7\u30C3\u30AF", System.StringComparison.OrdinalIgnoreCase);
	}

	Font ResolveConsoleFont(EmuFont font)
	{
		string requested = font?.FontFamily?.Name;
		// 主控制台字体仍走原本的内置/系统字体策略；HTML 片段字体必须按游戏目录加载，
		// 否则 eraFL 的 game-icons 私有区码点会被主字体误绘成普通汉字或方块。
		if (ShouldUseMainConsoleFont(requested))
			return mainFont;

		string key = NormalizeConsoleFontName(requested);
		if (consoleFontCache.TryGetValue(key, out var cached))
			return cached ?? mainFont;
		if (missingConsoleFonts.Contains(key))
			return mainFont;

		Font loaded = LoadGameFontByName(key);
		if (loaded != null)
		{
			consoleFontCache[key] = loaded;
			return loaded;
		}

		missingConsoleFonts.Add(key);
		return mainFont;
	}

	Font LoadGameFontByName(string fontName)
	{
		foreach (string path in BuildGameFontPathCandidates(fontName))
		{
			string resolved = uEmuera.Utils.ResolveExistingFilePath(path);
			if (!uEmuera.Utils.FileExists(resolved))
				continue;
			var fontFile = new FontFile();
			var error = fontFile.LoadDynamicFont(resolved);
			if (error == Error.Ok)
				return fontFile;
			GenericUtils.Warn(EmueraLogCategory.UI, () => $"[FONT] Failed to load font \"{fontName}\" from {uEmuera.Utils.GetRelativePathFromGameDir(resolved)}: {error}");
		}
		return null;
	}

	IEnumerable<string> BuildGameFontPathCandidates(string fontName)
	{
		if (string.IsNullOrWhiteSpace(fontName))
			yield break;

		string trimmed = fontName.Trim();
		string fileName = trimmed;
		if (!fileName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
		{
			foreach (string ext in new[] { ".ttf", ".otf" })
			{
				foreach (string dir in new[] { "font", "Font", "fonts", "Fonts" })
				{
					if (!string.IsNullOrEmpty(Program.ExeDir))
						yield return System.IO.Path.Combine(Program.ExeDir, dir, trimmed + ext);
					if (!string.IsNullOrEmpty(Program.ContentDir))
						yield return System.IO.Path.Combine(Program.ContentDir, dir, trimmed + ext);
				}
			}
			yield break;
		}

		foreach (string dir in new[] { "font", "Font", "fonts", "Fonts" })
		{
			if (!string.IsNullOrEmpty(Program.ExeDir))
				yield return System.IO.Path.Combine(Program.ExeDir, dir, fileName);
			if (!string.IsNullOrEmpty(Program.ContentDir))
				yield return System.IO.Path.Combine(Program.ContentDir, dir, fileName);
		}
	}

	const int EscapedConsolePartZIndex = 2;
	const int HtmlDivZIndexBase = 1024;

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
		{
			// eraFL 的 UI 容器通常先用 HTML_PRINT ...,1 输出相对定位 div，
			// 再用 NEWLINE(n) 显式保留窗口高度。这里不能再按 div.Bottom
			// 撑高逻辑行，否则会把游戏脚本自己预留的高度重复计算一遍。
			return baseLineHeight;
		}
		return System.Math.Max(baseLineHeight, part.Bottom);
	}

	int GetPartVisualBottom(AConsoleDisplayPart part, int lineHeight = -1)
	{
		int baseLineHeight = lineHeight > 0 ? lineHeight : EffectiveLineHeight;
		if (part == null)
			return baseLineHeight;
		if (part is ConsoleImagePart image)
		{
			if (image.Display == DisplayMode.Relative || image.Display == DisplayMode.AbsoluteLeftTop)
				return GetImagePartBottom(image, baseLineHeight);
			return baseLineHeight;
		}
		if (part is ConsoleDivPart div)
		{
			// div 不参与逻辑文本流撑高，但可视内容仍可能越过所在行。
			// ScrollContainer 的子内容尺寸必须覆盖这部分溢出，否则状态页下半块会被父容器裁掉。
			if (div.Display == DisplayMode.Relative || div.Display == DisplayMode.AbsoluteLeftTop)
				return System.Math.Max(baseLineHeight, div.Y + div.DivHeight);
			return baseLineHeight;
		}
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

	int GetButtonVisualBottom(ConsoleButtonString button, int lineHeight = -1)
	{
		int baseLineHeight = lineHeight > 0 ? lineHeight : EffectiveLineHeight;
		int bottom = baseLineHeight;
		if (button?.StrArray == null)
			return bottom;
		foreach (var part in button.StrArray)
			bottom = System.Math.Max(bottom, GetPartVisualBottom(part, baseLineHeight));
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

	int GetLineVisualBottom(ConsoleDisplayLine line)
	{
		int bottom = EffectiveLineHeight;
		if (line?.Buttons == null)
			return bottom;
		foreach (var button in line.Buttons)
			bottom = System.Math.Max(bottom, GetButtonVisualBottom(button));
		return bottom;
	}

	int GetLineVisualRight(ConsoleDisplayLine line)
	{
		int right = 0;
		if (line?.Buttons == null)
			return right;
		foreach (var button in line.Buttons)
			right = System.Math.Max(right, GetButtonVisualRight(button));
		return right;
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
			maxLineRight = System.Math.Max(maxLineRight, AddInlineLineBackgroundDivs(line, lineControl, 0));
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
		else if (lineObjects.ContainsKey(line.LineNo))
		{
			UnregisterLine(line.LineNo);
		}

		lineContainer.AddChild(lineControl);
		if (insertIndex >= 0)
			lineContainer.MoveChild(lineControl, System.Math.Min(insertIndex, lineContainer.GetChildCount() - 1));

		lineControl.SetMeta("line_no", line.LineNo);
		RegisterLine(line.LineNo, line, lineControl, lineSize);
		RegisterLineTexturePins(line.LineNo, newTexturePins);
		GenericUtils.RecordDisplayFallbackLineBuild(hasExistingLine);
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

	bool RefreshRenderedLineDataOnly(ConsoleDisplayLine line)
	{
		if (line == null || !lineObjects.ContainsKey(line.LineNo))
			return false;

		// 动态地图独立刷新时，底部选项的视觉内容通常不变，只是按钮 generation 前进。
		// 这里只替换行数据和命中信息，不重建 Control/Canvas 节点，避免选项闪烁。
		lineObjects[line.LineNo] = line;
		if (lineSizes.TryGetValue(line.LineNo, out var existingSize))
		{
			if (UpdateLineDerivedCaches(line.LineNo, line, existingSize))
				MarkLineLayoutDirty();
		}
		if (UseCanvasRenderBackend)
		{
			var hits = BuildCanvasLineButtonHits(line);
			if (hits != null && hits.Length > 0)
				canvasLineButtonHits[line.LineNo] = hits;
			else
				canvasLineButtonHits.Remove(line.LineNo);
			consoleRenderSurface?.MarkHitRectsDirtyOnly();
		}

		if (lineControls.TryGetValue(line.LineNo, out var control) && control != null && GodotObject.IsInstanceValid(control))
			UpdateRenderedButtonMetadata(control, line);
		return true;
	}

	void UpdateRenderedButtonMetadata(Control root, ConsoleDisplayLine line)
	{
		var buttonData = new List<(string input, long generation)>();
		CollectRenderedButtonData(line, buttonData);
		if (buttonData.Count == 0)
			return;

		var renderedButtons = new List<Control>();
		CollectRenderedButtonControls(root, renderedButtons);
		int count = System.Math.Min(buttonData.Count, renderedButtons.Count);
		for (int i = 0; i < count; i++)
		{
			var control = renderedButtons[i];
			if (control == null || !GodotObject.IsInstanceValid(control))
				continue;
			control.SetMeta("button_input", buttonData[i].input ?? "");
			control.SetMeta("generation", buttonData[i].generation);
		}
	}

	void CollectRenderedButtonData(ConsoleDisplayLine line, List<(string input, long generation)> output)
	{
		if (line?.Buttons == null || output == null)
			return;
		foreach (var button in line.Buttons)
		{
			if (button == null)
				continue;
			if (button.IsButton)
				output.Add((button.Inputs, button.Generation));
			if (button.StrArray == null)
				continue;
			foreach (var part in button.StrArray)
			{
				if (part is ConsoleDivPart div && div.Children != null)
				{
					foreach (var childLine in div.Children)
						CollectRenderedButtonData(childLine, output);
				}
			}
		}
	}

	void CollectRenderedButtonControls(Node node, List<Control> output)
	{
		if (node == null || output == null)
			return;
		if (node is Control control && control.HasMeta("button_input"))
			output.Add(control);
		foreach (var child in node.GetChildren())
			CollectRenderedButtonControls(child, output);
	}

	bool ShouldDeferLineReplacementForAsyncTexture(ConsoleDisplayLine line, bool isUpdate, bool hasExistingLine, bool asyncTexturePending)
	{
		if (line == null || !asyncTexturePending)
			return false;
		if (renderingAsyncImageFallbackLine)
			return false;

		if (isUpdate && hasExistingLine)
		{
			// 刷新已有行时，如果新图片仍在异步解码，先保留旧行画面。
			// 否则旧节点会被空白临时行替换，玩家会看到图片闪白；纹理完成后再用挂起的新行做真正替换。
			pendingAsyncLineUpdates[line.LineNo] = line;
			asyncTexturePendingLineNos.Add(line.LineNo);
			return true;
		}

		if (!hasExistingLine && IsPureImageLine(line))
		{
			// 标题和 XRay 这类纯图片 HTML 行在首次提交时如果先显示 spacer，会在 REDRAW 恢复时闪出空/白块。
			// 纯图片行没有可读文本需要抢先显示，因此等纹理就绪后再按原 LineNo 补回。
			if (!renderingAsyncImageFallbackLine)
				TryRenderRecentPureImageFallback(line.LineNo);
			pendingAsyncLineUpdates[line.LineNo] = line;
			asyncTexturePendingLineNos.Add(line.LineNo);
			return true;
		}

		return false;
	}

	void RememberPureImageFallbackLine(int lineNo)
	{
		if (!lineObjects.TryGetValue(lineNo, out var line) || !IsPureImageLine(line))
			return;

		ulong now = Time.GetTicksMsec();
		PruneExpiredPureImageFallbackLines(now);
		recentPureImageFallbackLines.Add(new PureImageFallbackLine
		{
			Line = line,
			OriginalLineNo = line.LineNo,
			RemovedAtMs = now,
		});
		while (recentPureImageFallbackLines.Count > MaxPureImageFallbackLines)
			recentPureImageFallbackLines.RemoveAt(0);
	}

	bool TryRenderRecentPureImageFallback(int targetLineNo)
	{
		ulong now = Time.GetTicksMsec();
		PruneExpiredPureImageFallbackLines(now);
		for (int pass = 0; pass < 2; pass++)
		{
			for (int i = recentPureImageFallbackLines.Count - 1; i >= 0; i--)
			{
				var fallback = recentPureImageFallbackLines[i];
				if (fallback.Line == null)
				{
					recentPureImageFallbackLines.RemoveAt(i);
					continue;
				}
				if (pass == 0 && fallback.OriginalLineNo != targetLineNo)
					continue;

				recentPureImageFallbackLines.RemoveAt(i);
				// CLEARLINE/REDRAW 会先删除旧图片行，再输出同位置的新图片行。
				// 标题这类连续图片行通常会复用 LineNo，优先按原 LineNo 匹配，避免 36 条标题切片顺序倒置。
				// 新图异步解码期间，把刚删除的旧纯图片行挂到新 LineNo；该旧行已经从显示表移除，
				// 不能在注册后恢复旧 LineNo，否则后续 Canvas/Control 替换会拿到不一致的行号。
				// 纹理就绪后 pendingAsyncLineUpdates 会用真实新行替换它。
				renderingAsyncImageFallbackLine = true;
				// Keep the fallback registered under the target LineNo until the real image line is ready.
				renderingAsyncImageFallbackLine = true;
				try
				{
					fallback.Line.LineNo = targetLineNo;
					AddLine(fallback.Line, true);
					return true;
				}
				finally
				{
					renderingAsyncImageFallbackLine = false;
				}
			}
		}
		return false;
	}

	void PruneExpiredPureImageFallbackLines(ulong now)
	{
		for (int i = recentPureImageFallbackLines.Count - 1; i >= 0; i--)
		{
			var fallback = recentPureImageFallbackLines[i];
			if (fallback.Line == null || now - fallback.RemovedAtMs > PureImageFallbackTtlMs)
				recentPureImageFallbackLines.RemoveAt(i);
		}
	}

	static bool IsPureImageLine(ConsoleDisplayLine line)
	{
		if (!TryClassifyPureImageLine(line, 0, out bool hasImage))
			return false;
		return hasImage;
	}

	static bool TryClassifyPureImageLine(ConsoleDisplayLine line, int depth, out bool hasImage)
	{
		hasImage = false;
		if (line?.Buttons == null || line.Buttons.Length == 0)
			return false;
		for (int i = 0; i < line.Buttons.Length; i++)
		{
			var button = line.Buttons[i];
			if (button?.StrArray == null)
				continue;
			for (int j = 0; j < button.StrArray.Length; j++)
			{
				if (!TryClassifyPureImagePart(button.StrArray[j], depth, out bool partHasImage))
					return false;
				hasImage |= partHasImage;
			}
		}
		return true;
	}

	static bool TryClassifyPureImagePart(AConsoleDisplayPart part, int depth, out bool hasImage)
	{
		hasImage = false;
		if (part == null)
			return true;
		if (part is ConsoleImagePart)
		{
			hasImage = true;
			return true;
		}
		if (part is ConsoleSpacePart)
			return true;
		if (part is ConsoleStyledString styled)
			return string.IsNullOrWhiteSpace(styled.Str);
		if (part is ConsoleDivPart div)
		{
			// eraFL 的底图/立绘常写成 <div><img ...></div>。
			// 异步纹理首帧未就绪时，需要把这种包装图片也按纯图片行延后提交，
			// 否则 Canvas 会先提交空 div/spacer，之后容易表现为“有框没图”。
			if (depth >= 4 || div.Children == null || div.Children.Length == 0)
				return true;
			for (int i = 0; i < div.Children.Length; i++)
			{
				if (!TryClassifyPureImageLine(div.Children[i], depth + 1, out bool childHasImage))
					return false;
				hasImage |= childHasImage;
			}
			return true;
		}
		return false;
	}

	// 企业级说明：普通行与 HTML/Div 子行共用同一按钮构建入口，避免触摸命中、焦点、样式和内容裁剪规则在移动端产生分叉。
	Panel BuildConsoleButton(ConsoleButtonString button, int buttonTop, int buttonHeight, bool allowEscapedPartZ = true)
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
		btn.GuiInput += inputEvent => OnContentButtonGuiInput(inputEvent, btn, GetRenderedButtonInput(btn), GetRenderedButtonGeneration(btn));
		btn.MouseEntered += () => SetRenderedButtonHover(btn, true);
		btn.MouseExited += () => SetRenderedButtonHover(btn, false);
		btn.SetMeta("button_input", inputs);
		btn.SetMeta("generation", generation);

		var contentBox = new Control();
		contentBox.MouseFilter = MouseFilterEnum.Ignore;
		contentBox.ClipContents = false;
		contentBox.Position = new Vector2(button.PointX - hitRect.Position.X, -hitRect.Position.Y);
		btn.AddChild(contentBox);

		foreach (var part in button.StrArray)
			AddPartToContainer(part, contentBox, button.PointX, allowEscapedPartZ);

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

	static string GetRenderedButtonInput(Control button)
	{
		if (button == null || !button.HasMeta("button_input"))
			return "";
		try
		{
			return button.GetMeta("button_input").As<string>() ?? "";
		}
		catch
		{
			return "";
		}
	}

	static long GetRenderedButtonGeneration(Control button)
	{
		if (button == null || !button.HasMeta("generation"))
			return 0;
		try
		{
			return button.GetMeta("generation").AsInt64();
		}
		catch
		{
			return 0;
		}
	}

	void SetRenderedButtonHover(Control button, bool selected)
	{
		if (button == null || !GodotObject.IsInstanceValid(button))
			return;

		string input = GetRenderedButtonInput(button);
		long generation = GetRenderedButtonGeneration(button);
		SetControlButtonSelected(button, selected);
		if (selected)
		{
			SetCanvasVisualButton(input, generation);
			GenericUtils.SetPointingButton(input, generation);
		}
		else
		{
			ClearCanvasVisualButtonIfMatches(input, generation);
			GenericUtils.ClearPointingButton(generation);
		}
	}

	static void SetControlButtonSelected(Node node, bool selected)
	{
		if (node == null || !GodotObject.IsInstanceValid(node))
			return;
		if (node is ConsoleTextPart textPart)
			textPart.SetSelected(selected);
		else if (node is ConsoleColorRectPart colorRectPart)
			colorRectPart.SetSelected(selected);
		else if (node is EmueraImage imagePart)
			imagePart.SetSelected(selected);
		foreach (var child in node.GetChildren())
			SetControlButtonSelected(child, selected);
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
			if (!rectShape.HasRenderableRect)
				return false;
			rect = new Rect2(rectShape.PointX - relX + rectShape.RenderX, rectShape.RenderY,
				System.Math.Max(rectShape.RenderWidth, 1),
				System.Math.Max(rectShape.RenderHeight, 1));
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
	internal void ApplyTextChanges(int removeBottomCount, IReadOnlyList<(ConsoleDisplayLine Line, bool Update)> lines,
		bool update, int lastButtonGeneration, bool scrollToBottom = true, IReadOnlyList<ConsoleDisplayLine> dataOnlyLines = null)
	{
		ApplyTextChanges(removeBottomCount, lines, update, lastButtonGeneration,
			scrollToBottom ? EmueraDisplayScrollMode.FollowBottom : EmueraDisplayScrollMode.PreserveViewport,
			dataOnlyLines);
	}

	internal void ApplyTextChanges(int removeBottomCount, IReadOnlyList<(ConsoleDisplayLine Line, bool Update)> lines,
		bool update, int lastButtonGeneration, EmueraDisplayScrollMode scrollMode, IReadOnlyList<ConsoleDisplayLine> dataOnlyLines = null)
	{
		bool sampleDisplayBridge = GenericUtils.IsPerformanceSamplingEnabled;
		ulong applyStartUsec = sampleDisplayBridge ? Time.GetTicksUsec() : 0;
		bool scrollToBottom = scrollMode == EmueraDisplayScrollMode.FollowBottom;
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

			if (dataOnlyLines != null && dataOnlyLines.Count > 0)
			{
				for (int i = 0; i < dataOnlyLines.Count; i++)
					changed |= RefreshRenderedLineDataOnly(dataOnlyLines[i]);
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
			QueueDisplayFollowUp(scrollMode);
		}

		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("apply_text_changes", () => $"removeBottom={removeBottomCount} add={lines?.Count ?? 0} dataOnly={dataOnlyLines?.Count ?? 0} changed={changed} update={update} scrollMode={scrollMode} autoScroll={scrollToBottom} lastGen={lastButtonGeneration} maxLine={GetMaxLineNo()}");
		SetLastButtonGeneration(lastButtonGeneration);
		if (sampleDisplayBridge)
			GenericUtils.RecordDisplayApplyBatch((Time.GetTicksUsec() - applyStartUsec) / 1000.0);
	}

	void QueueDisplayFollowUp(bool scrollToBottom = true)
	{
		QueueDisplayFollowUp(scrollToBottom ? EmueraDisplayScrollMode.FollowBottom : EmueraDisplayScrollMode.PreserveViewport);
	}

	void QueueDisplayFollowUp(EmueraDisplayScrollMode scrollMode)
	{
		bool scrollToBottom = scrollMode == EmueraDisplayScrollMode.FollowBottom;
		if (!scrollToBottom)
		{
			CancelPendingScrollToBottom();
			RememberCurrentContentScroll();
		}
		QueueScaleBoundsUpdate();
		if (scrollToBottom)
			RequestScrollToBottom();
		else if (scrollMode == EmueraDisplayScrollMode.KeepChoicesVisible)
			RequestKeepChoicesVisible();
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

	int AddInlineLineBackgroundDivs(ConsoleDisplayLine line, Control lineControl, int relX)
	{
		if (line?.Buttons == null || lineControl == null)
			return 0;

		int right = 0;
		foreach (var button in line.Buttons)
		{
			if (button?.StrArray == null)
				continue;
			foreach (var part in button.StrArray)
			{
				if (part is not ConsoleDivPart div || !IsInlineLineBackgroundDiv(div))
					continue;

				var wrapper = BuildDivControl(div, relX);
				// eraTW/snake 的角色列表在行尾输出负 xpos 的空 div 作为条纹背景。
				// 这类背景必须先作为本行底图加入，再绘制文字/按钮；不能依赖负 ZIndex，
				// 否则在 Godot 的 Control/Panel 层级中可能被父节点或兄弟节点压到不可见。
				wrapper.ZIndex = 0;
				wrapper.MouseFilter = MouseFilterEnum.Ignore;
				wrapper.SetMeta(InlineLineBackgroundDivMeta, true);
				lineControl.AddChild(wrapper);
				right = System.Math.Max(right, Mathf.CeilToInt(wrapper.Position.X + wrapper.Size.X));
			}
		}
		return right;
	}

	internal void SetHtmlIsland(ConsoleDisplayLine[] lines)
	{
		if (htmlIslandContainer == null || lines == null)
		{
			ClearHtmlIsland();
			return;
		}
		lastHtmlIslandLines = lines;
		pendingHtmlIslandAsyncTextureRefresh = false;

		var previousTexturePinCollector = activeTexturePinCollector;
		var newHtmlIslandTexturePins = new List<SpriteManager.TextureInfo>();
		var newControls = new List<Control>();
		activeTexturePinCollector = newHtmlIslandTexturePins;
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
				AddInlineLineBackgroundDivs(line, lineControl, 0);
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
					newControls.Add(lineControl);
				}
		}
		catch
		{
			ReleaseControlList(newControls);
			ReleaseTexturePinList(newHtmlIslandTexturePins);
			throw;
		}
		finally
		{
			renderingHtmlIslandTextures = previousHtmlIslandRender;
			activeTexturePinCollector = previousTexturePinCollector;
		}

		if (pendingHtmlIslandAsyncTextureRefresh)
		{
			// HTML island 常用于整块标题/差分图。异步纹理未就绪时先保留旧 island；
			// 没有旧 island 时也不提交透明占位，等纹理完成后一次性替换，避免白块闪烁。
			ReleaseControlList(newControls);
			ReleaseTexturePinList(newHtmlIslandTexturePins);
			return;
		}

		ClearHtmlIslandControls();
		ReleaseHtmlIslandTexturePins();
		for (int i = 0; i < newControls.Count; i++)
			htmlIslandContainer.AddChild(newControls[i]);
		htmlIslandTexturePins = newHtmlIslandTexturePins;
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
		ClearHtmlIslandControls();
		ReleaseHtmlIslandTexturePins();
		lastHtmlIslandLines = null;
		pendingHtmlIslandAsyncTextureRefresh = false;
		displayRevision++;
		RefreshQuickInputGate();
	}

	void ClearCbgSessionState()
	{
		ReleaseCbgTexturePins();
		renderedCbgLayers.Clear();
		lastCbgSourceLayers.Clear();
		pendingCbgAsyncTextureRefresh = false;
		lastCbgScrollVertical = int.MinValue;
		TrimCbgNodes(0);
	}

	void ClearGraphicsImageTextureCache()
	{
		var releasedTextures = new HashSet<Texture2D>();
		foreach (var entry in graphicsImageTextureCache.Values)
		{
			if (entry.Texture != null && releasedTextures.Add(entry.Texture))
				entry.Texture.Dispose();
		}
		graphicsImageTextureCache.Clear();
	}

	void ClearSessionFontCache()
	{
		foreach (var font in consoleFontCache.Values)
		{
			if (font != null && font != mainFont)
				font.Dispose();
		}
		consoleFontCache.Clear();
		missingConsoleFonts.Clear();
	}

	void ClearSessionAudioState()
	{
		applicationPauseActive = false;
		bgmPausedBeforeApplicationPause = false;
		soundPausedBeforeApplicationPause.Clear();
		if (bgmPlayer != null)
		{
			bgmPlayer.Stop();
			bgmPlayer.Stream = null;
			bgmPlayer.StreamPaused = false;
		}
		for (int i = 0; i < soundPlayers.Count; i++)
		{
			var player = soundPlayers[i];
			if (player == null)
				continue;
			player.Stop();
			player.Stream = null;
			player.StreamPaused = false;
			player.PitchScale = 1.0f;
		}
		for (int i = 0; i < soundRepeatRemaining.Count; i++)
			soundRepeatRemaining[i] = 0;
		soundVolume = 1.0f;
		bgmVolume = 1.0f;
	}

	void ClearHtmlIslandControls()
	{
		if (htmlIslandContainer == null)
			return;
		foreach (var child in htmlIslandContainer.GetChildren())
			SafeQueueFree(child);
	}

	void ReleaseControlList(List<Control> controls)
	{
		if (controls == null)
			return;
		for (int i = 0; i < controls.Count; i++)
			SafeQueueFree(controls[i]);
		controls.Clear();
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
		UpdateViewportAnchoredRelativeDivLineIndex(lineNo, control);
		lineSizes[lineNo] = size;
		lineNumbers.Add(lineNo);
		totalLineHeight += size.Y;
		if (size.X > widestLineWidth)
			widestLineWidth = size.X;
		UpdateLineDerivedCaches(lineNo, line, size);
		UpdateCanvasPositionedNodeIndexForLine(lineNo);
		MarkLineLayoutDirty();
	}

	int GetRetainedLineCount()
	{
		return lineNumbers.Count;
	}

	bool UpdateLineDerivedCaches(int lineNo, ConsoleDisplayLine line, Vector2 size)
	{
		bool hadOldVisual = lineVisualExtents.TryGetValue(lineNo, out var oldVisual);
		bool oldVisualWasMax = false;
		if (hadOldVisual)
			oldVisualWasMax = oldVisual.X >= widestVisualLineWidth;

		RemoveLineButtonGenerationIndex(lineNo);

		var visualExtents = ComputeLineVisualExtents(line, size);
		lineVisualExtents[lineNo] = visualExtents;
		if (visualExtents.X > widestVisualLineWidth)
			widestVisualLineWidth = visualExtents.X;

		UpdateLineButtonGenerationIndex(lineNo, line);

		if (oldVisualWasMax && visualExtents.X < widestVisualLineWidth)
			RecalculateVisualLineMetrics();
		return !hadOldVisual
			|| System.Math.Abs(oldVisual.X - visualExtents.X) > 0.5f
			|| System.Math.Abs(oldVisual.Y - visualExtents.Y) > 0.5f;
	}

	Vector2 ComputeLineVisualExtents(ConsoleDisplayLine line, Vector2 fallbackSize)
	{
		float right = fallbackSize.X;
		float bottom = fallbackSize.Y;
		if (line?.Buttons == null)
			return new Vector2(right, bottom);
		foreach (var button in line.Buttons)
		{
			if (button == null)
				continue;
			right = Mathf.Max(right, GetButtonVisualRight(button));
			bottom = Mathf.Max(bottom, GetButtonVisualBottom(button));
		}
		return new Vector2(right, bottom);
	}

	void UpdateLineButtonGenerationIndex(int lineNo, ConsoleDisplayLine line)
	{
		var generations = new List<long>();
		CollectLineButtonGenerations(line, generations, 0);
		if (generations.Count == 0)
		{
			lineMaxButtonGeneration[lineNo] = long.MinValue;
			return;
		}

		long lineMax = long.MinValue;
		for (int i = 0; i < generations.Count; i++)
		{
			long generation = generations[i];
			lineMax = System.Math.Max(lineMax, generation);
			if (!buttonGenerationLineNumbers.TryGetValue(generation, out var rows))
			{
				rows = new SortedSet<int>();
				buttonGenerationLineNumbers[generation] = rows;
			}
			rows.Add(lineNo);
		}
		lineButtonGenerations[lineNo] = generations;
		lineMaxButtonGeneration[lineNo] = lineMax;
		if (lineMax > maxRenderedButtonGeneration)
			maxRenderedButtonGeneration = lineMax;
	}

	void RemoveLineButtonGenerationIndex(int lineNo)
	{
		if (!lineButtonGenerations.TryGetValue(lineNo, out var generations))
		{
			lineMaxButtonGeneration.Remove(lineNo);
			return;
		}

		bool removedMax = false;
		for (int i = 0; i < generations.Count; i++)
		{
			long generation = generations[i];
			if (generation >= maxRenderedButtonGeneration)
				removedMax = true;
			if (!buttonGenerationLineNumbers.TryGetValue(generation, out var rows))
				continue;
			rows.Remove(lineNo);
			if (rows.Count == 0)
				buttonGenerationLineNumbers.Remove(generation);
		}
		lineButtonGenerations.Remove(lineNo);
		lineMaxButtonGeneration.Remove(lineNo);
		if (removedMax)
			RecalculateMaxRenderedButtonGeneration();
	}

	void RecalculateMaxRenderedButtonGeneration()
	{
		maxRenderedButtonGeneration = long.MinValue;
		foreach (long generation in buttonGenerationLineNumbers.Keys)
		{
			if (generation > maxRenderedButtonGeneration)
				maxRenderedButtonGeneration = generation;
		}
	}

	void CollectLineButtonGenerations(ConsoleDisplayLine line, List<long> output, int depth)
	{
		if (line?.Buttons == null || output == null || depth > ButtonGenerationScanMaxDepth)
			return;
		foreach (var button in line.Buttons)
		{
			if (button == null)
				continue;
			if (button.IsButton && !output.Contains(button.Generation))
				output.Add(button.Generation);
			if (button.StrArray == null)
				continue;
			foreach (var part in button.StrArray)
			{
				if (part is ConsoleDivPart div && div.Children != null)
				{
					foreach (var childLine in div.Children)
						CollectLineButtonGenerations(childLine, output, depth + 1);
				}
			}
		}
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
		viewportAnchoredRelativeDivLineNos.Remove(lineNo);
		canvasRowsWithPositionedNodes.Remove(lineNo);
		bool removedVisualWidest = false;
		if (lineVisualExtents.TryGetValue(lineNo, out var visualExtents))
			removedVisualWidest = visualExtents.X >= widestVisualLineWidth;
		lineVisualExtents.Remove(lineNo);
		RemoveLineButtonGenerationIndex(lineNo);
		bool removedNumber = lineNumbers.Remove(lineNo);
		if (!lineSizes.TryGetValue(lineNo, out var size))
		{
			if (removedVisualWidest)
				RecalculateVisualLineMetrics();
			if (removedNumber)
				MarkLineLayoutDirty();
			return;
		}
		lineSizes.Remove(lineNo);
		totalLineHeight = System.Math.Max(0, totalLineHeight - size.Y);
		if (size.X >= widestLineWidth)
			RecalculateWidestLineWidth();
		if (removedVisualWidest)
			RecalculateVisualLineMetrics();
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
		recentPureImageFallbackLines.Clear();
		canvasLineButtonHits.Clear();
		viewportAnchoredRelativeDivLineNos.Clear();
		canvasRowsWithPositionedNodes.Clear();
		lineVisualExtents.Clear();
		lineMaxButtonGeneration.Clear();
		lineButtonGenerations.Clear();
		buttonGenerationLineNumbers.Clear();
		lineSizes.Clear();
		lineNumbers.Clear();
		lineLayoutEntries.Clear();
		lineLayoutIndexByLineNo.Clear();
		lineLayoutDirty = false;
		canvasOverlayRowsDirty = false;
		totalLineHeight = 0;
		widestLineWidth = 0;
		widestVisualLineWidth = 0;
		visualLayoutContentHeight = 0;
		maxRenderedButtonGeneration = long.MinValue;
	}

	void RegisterCanvasImageOverlays(int lineNo, List<CanvasImageOverlay> nodes)
	{
		if (nodes == null || nodes.Count == 0)
			return;
		canvasImageOverlayNodes[lineNo] = nodes;
		RebuildCanvasOverlayIndexesForLine(lineNo);
		UpdateCanvasPositionedNodeIndexForLine(lineNo);
		canvasOverlayRowsDirty = true;
	}

	void RegisterCanvasDivOverlays(int lineNo, List<CanvasDivOverlay> nodes)
	{
		if (nodes == null || nodes.Count == 0)
			return;
		canvasDivOverlayNodes[lineNo] = nodes;
		RebuildCanvasOverlayIndexesForLine(lineNo);
		UpdateCanvasPositionedNodeIndexForLine(lineNo);
		canvasOverlayRowsDirty = true;
	}

	void ReleaseCanvasImageOverlays(int lineNo)
	{
		if (!canvasImageOverlayNodes.TryGetValue(lineNo, out var nodes))
			return;
		canvasImageOverlayNodes.Remove(lineNo);
		RebuildCanvasOverlayIndexesForLine(lineNo);
		UpdateCanvasPositionedNodeIndexForLine(lineNo);
		canvasOverlayRowsDirty = true;
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
		canvasOverlayRowsDirty = true;
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

	void UpdateViewportAnchoredRelativeDivLineIndex(int lineNo, Control control)
	{
		if (control != null && control.HasMeta(ViewportAnchoredRelativeDivRowMeta))
			viewportAnchoredRelativeDivLineNos.Add(lineNo);
		else
			viewportAnchoredRelativeDivLineNos.Remove(lineNo);
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
		float visualBottom = 0;
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
			if (lineVisualExtents.TryGetValue(lineNo, out var visualExtents))
				visualBottom = Mathf.Max(visualBottom, y + visualExtents.Y);
			else
				visualBottom = Mathf.Max(visualBottom, y + size.Y);
			y += size.Y;
		}
		// 可视溢出必须按真实行顶点计算。仅用 totalLineHeight + maxOverflow 会把靠前地图行
		// 的大 div 高度叠到整段历史输出末尾，造成滚到底部后一大片黑屏。
		visualLayoutContentHeight = Mathf.Max(y, visualBottom);
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

	void RefreshViewportAnchoredRelativeDivRows()
	{
		if (viewportAnchoredRelativeDivLineNos.Count == 0)
			return;
		EnsureLineLayout();
		foreach (int lineNo in viewportAnchoredRelativeDivLineNos)
		{
			if (!lineControls.TryGetValue(lineNo, out var control)
				|| control == null
				|| !GodotObject.IsInstanceValid(control))
				continue;
			RefreshViewportAnchoredRelativeDivs(control, GetLineTopByLayout(lineNo));
		}
	}

	void ReleaseCbgTexturePins()
	{
		// CBG is refreshed as a whole layer set, so all previous background pins are
		// released before collecting the next frame's visible textures.
		for (int i = 0; i < cbgTexturePins.Count; i++)
			SpriteManager.UnpinTextureInfo(cbgTexturePins[i]);
		cbgTexturePins.Clear();
	}

	void ReleaseHtmlIslandTexturePins()
	{
		for (int i = 0; i < htmlIslandTexturePins.Count; i++)
			SpriteManager.UnpinTextureInfo(htmlIslandTexturePins[i]);
		htmlIslandTexturePins.Clear();
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

	void RecalculateWidestVisualLineWidth()
	{
		RecalculateVisualLineMetrics();
	}

	void RecalculateVisualLineMetrics()
	{
		widestVisualLineWidth = 0;
		foreach (var kvp in lineVisualExtents)
		{
			var visualExtents = kvp.Value;
			if (visualExtents.X > widestVisualLineWidth)
				widestVisualLineWidth = visualExtents.X;
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

	string BuildDynamicMapScrollState()
	{
		if (scrollContainer == null)
			return "scroll=null";
		var limit = GetContentScrollLimit();
		var scrollSize = scrollContainer.Size;
		var rootSize = scaledContentRoot != null ? scaledContentRoot.Size : Vector2.Zero;
		return "x=" + scrollContainer.ScrollHorizontal
			+ ",y=" + scrollContainer.ScrollVertical
			+ ",maxY=" + GetMaxContentVerticalScroll()
			+ ",limitY=" + limit.Y
			+ ",pending=" + pendingScroll
			+ ",desiredY=" + desiredContentScrollVertical
			+ ",desiredValid=" + desiredContentScrollValid
			+ ",drag=" + contentDragActive
			+ ",inertia=" + contentInertiaActive
			+ ",view=" + Mathf.RoundToInt(scrollSize.X) + "x" + Mathf.RoundToInt(scrollSize.Y)
			+ ",root=" + Mathf.RoundToInt(rootSize.X) + "x" + Mathf.RoundToInt(rootSize.Y);
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
		bool traceDynamicScroll = GenericUtils.IsDynamicMapScrollTraceEnabled
			&& GenericUtils.ShouldTraceDynamicMap(false);
		if (pendingScroll)
		{
			if (traceDynamicScroll)
			{
				GenericUtils.DynamicMapTrace("DYNAMIC_MAP.UI.SCROLL_REQUEST_MERGE",
					() => "dynamic map scroll-to-bottom request merged",
					() => "deadline=" + pendingScrollDeadlineTick
						+ " state=" + BuildDynamicMapScrollState());
			}
			if (GenericUtils.IsScrollTraceActive)
				TraceScroll("scroll_bottom_request_merge", () => $"deadline={pendingScrollDeadlineTick}");
			return;
		}
		pendingScroll = true;
		if (traceDynamicScroll)
		{
			GenericUtils.DynamicMapTrace("DYNAMIC_MAP.UI.SCROLL_REQUEST",
				() => "dynamic map scroll-to-bottom requested",
				() => "deadline=" + pendingScrollDeadlineTick
					+ " state=" + BuildDynamicMapScrollState());
		}
		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("scroll_bottom_request", () => $"deadline={pendingScrollDeadlineTick}");
		CallDeferred(nameof(DeferredScrollToBottom));
	}

	void CancelPendingScrollToBottom()
	{
		if (!pendingScroll)
			return;
		pendingScroll = false;
		pendingScrollLastMax = int.MinValue;
		pendingScrollStableSinceTick = 0;
		pendingScrollDeadlineTick = 0;
		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("scroll_bottom_cancel");
	}

	void RequestKeepChoicesVisible()
	{
		if (scrollContainer == null)
			return;
		pendingKeepChoicesInteractionSerial = contentScrollInteractionSerial;
		if (pendingKeepChoicesVisible)
			return;
		pendingKeepChoicesVisible = true;
		CallDeferred(nameof(DeferredKeepChoicesVisible));
	}

	async void DeferredKeepChoicesVisible()
	{
		try
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (scrollContainer == null || contentDragActive || contentInertiaActive)
				return;
			if (pendingKeepChoicesInteractionSerial != contentScrollInteractionSerial)
				return;

			UpdateScaleBounds();

			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (scrollContainer == null || contentDragActive || contentInertiaActive)
				return;
			if (pendingKeepChoicesInteractionSerial != contentScrollInteractionSerial)
				return;

			EnsureCurrentChoicesVisible();
		}
		finally
		{
			pendingKeepChoicesVisible = false;
		}
	}

	void EnsureCurrentChoicesVisible()
	{
		if (scrollContainer == null)
			return;
		if (!TryGetLastCurrentChoiceLineBounds(out float lineTop, out float lineBottom))
			return;

		float scale = GetSafeContentScale();
		int currentY = scrollContainer.ScrollVertical;
		int viewportHeight = Mathf.RoundToInt(scrollContainer.Size.Y);
		int choiceTop = Mathf.RoundToInt(lineTop * scale);
		int choiceBottom = Mathf.RoundToInt(lineBottom * scale) + KeepChoicesVisiblePaddingPx;
		int visibleBottom = currentY + viewportHeight;
		if (choiceBottom <= visibleBottom)
			return;

		var limit = GetContentScrollLimit();
		int targetY = Mathf.Clamp(choiceBottom - viewportHeight, 0, limit.Y);
		if (targetY <= currentY)
			return;

		scrollContainer.ScrollVertical = targetY;
		RememberDesiredContentScroll(scrollContainer.ScrollHorizontal, targetY);
		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("keep_choices_visible", () => $"line=({Mathf.RoundToInt(lineTop)},{Mathf.RoundToInt(lineBottom)}) choice=({choiceTop},{choiceBottom}) from={currentY} to={targetY} limit={limit.Y}");
	}

	bool TryGetLastCurrentChoiceLineBounds(out float top, out float bottom)
	{
		top = 0;
		bottom = 0;
		EnsureLineLayout();
		foreach (int lineNo in lineNumbers.Reverse())
		{
			if (!lineObjects.TryGetValue(lineNo, out var line))
				continue;
			if (!LineHasCurrentGenerationButton(line, 0))
				continue;
			if (!lineLayoutIndexByLineNo.TryGetValue(lineNo, out int index)
				|| index < 0
				|| index >= lineLayoutEntries.Count)
				continue;
			var entry = lineLayoutEntries[index];
			top = entry.Top;
			bottom = entry.Bottom;
			return true;
		}
		return false;
	}

	bool LineHasCurrentGenerationButton(ConsoleDisplayLine line, int depth)
	{
		if (line?.Buttons == null || depth > 4)
			return false;
		foreach (var button in line.Buttons)
		{
			if (button == null)
				continue;
			if (button.IsButton && (lastButtonGeneration < 0 || button.Generation == lastButtonGeneration))
				return true;
			if (button.StrArray == null)
				continue;
			foreach (var part in button.StrArray)
			{
				if (part is ConsoleDivPart div && div.Children != null)
				{
					for (int i = 0; i < div.Children.Length; i++)
					{
						if (LineHasCurrentGenerationButton(div.Children[i], depth + 1))
							return true;
					}
				}
			}
		}
		return false;
	}

	// Retry bottom scrolling until content height has remained stable long enough
	// or a user interaction starts. This prevents output batches from ending one
	// frame before the final buttons are laid out.
	async void DeferredScrollToBottom()
	{
		string endReason = "loop_end";
		bool loggedDragWait = false;
		bool traceDynamicScroll = GenericUtils.IsDynamicMapScrollTraceEnabled
			&& GenericUtils.ShouldTraceDynamicMap(false);
		if (traceDynamicScroll)
		{
			GenericUtils.DynamicMapTrace("DYNAMIC_MAP.UI.SCROLL_START",
				() => "dynamic map deferred scroll start",
				() => "state=" + BuildDynamicMapScrollState());
		}
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
					if (traceDynamicScroll)
					{
						GenericUtils.DynamicMapTrace("DYNAMIC_MAP.UI.SCROLL_MAX",
							() => "dynamic map scroll max changed",
							() => "max=" + maxScroll
								+ " stable_deadline=" + stableDeadline
								+ " state=" + BuildDynamicMapScrollState());
					}
				}

				scrollContainer.ScrollVertical = maxScroll;
				ClampContentHorizontalScroll();
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
			if (traceDynamicScroll)
			{
				GenericUtils.DynamicMapTrace("DYNAMIC_MAP.UI.SCROLL_END",
					() => "dynamic map deferred scroll end",
					() => "reason=" + endReason
						+ " state=" + BuildDynamicMapScrollState());
			}
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

		UpdateContentHorizontalScrollMode();
		scrollContainer.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		HideContentScrollBar(scrollContainer.GetHScrollBar());
		HideContentScrollBar(scrollContainer.GetVScrollBar());
		ClampContentHorizontalScroll();
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
			int targetHorizontal = NormalizeContentHorizontalScroll(Mathf.Clamp(previousHorizontal, 0, limit.X));
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
		UpdateContentHorizontalScrollMode();
		ClampContentHorizontalScroll();
	}

	int NormalizeContentHorizontalScroll(int horizontal)
	{
		int maxScroll = GetMaxContentHorizontalScroll();
		if (maxScroll <= 0)
			return 0;
		return Mathf.Clamp(horizontal, 0, maxScroll);
	}

	void ClampContentHorizontalScroll()
	{
		if (scrollContainer == null)
			return;

		int targetHorizontal = NormalizeContentHorizontalScroll(scrollContainer.ScrollHorizontal);
		if (scrollContainer.ScrollHorizontal != targetHorizontal)
			scrollContainer.ScrollHorizontal = targetHorizontal;
	}

	bool IsContentHorizontalScrollAvailable()
	{
		return GetContentScrollLimit().X > 0;
	}

	void UpdateContentHorizontalScrollMode()
	{
		if (scrollContainer == null)
			return;

		scrollContainer.HorizontalScrollMode = IsContentHorizontalScrollAvailable()
			? ScrollContainer.ScrollMode.ShowNever
			: ScrollContainer.ScrollMode.Disabled;
	}

	// Capture the live viewport as the desired scroll target before sending input
	// to the core or before a layout-changing action.
	void RememberCurrentContentScroll()
	{
		if (scrollContainer == null)
			return;
		RememberDesiredContentScroll(NormalizeContentHorizontalScroll(scrollContainer.ScrollHorizontal), scrollContainer.ScrollVertical);
	}

	// Store the scroll target that ProcessContentScrollCorrection will preserve
	// across Godot layout passes.
	void RememberDesiredContentScroll(int horizontal, int vertical)
	{
		desiredContentScrollHorizontal = NormalizeContentHorizontalScroll(horizontal);
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
		// 动态地图会保留历史输出，不能在每次打开地图/缩放/滚动时重新扫描全部行。
		// 行注册与 data-only 刷新时缓存可视边界；布局 dirty 时只重建一次 prefix 快照。
		EnsureLineLayout();
		float width = Mathf.Max(Config.DrawableWidth, Mathf.Max(widestLineWidth, widestVisualLineWidth));
		float height = Mathf.Max(totalLineHeight, visualLayoutContentHeight);
		int visibleRows = lineNumbers.Count;
		if (!UseCanvasRenderBackend && visibleRows > 1 && lineContainer is VBoxContainer rows)
			height += (visibleRows - 1) * rows.GetThemeConstant("separation");
		return new Vector2(width, height);
	}

	public float GetCurrentVisualContentWidth()
	{
		return CalculateLineContentSize().X;
	}

	// Convert one emuera display part into Godot Controls under container.
	// relX is used for button/div-local coordinates.
	int AddPartToContainer(AConsoleDisplayPart part, Control container, int relX, bool allowEscapedPartZ = true)
	{
		if (part is ConsoleDivPart inlineBackgroundDiv && IsInlineLineBackgroundDiv(inlineBackgroundDiv))
			return EffectiveLineHeight;
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
			var text = CreateTextPart(css.Str, css.pColor, css.Font, w, css.pButtonColor, css.VerticalAlign);
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

			string animatedWebpPath = GetAnimatedWebpSourcePath(sprite);
			// 行内 <img> 与 CBG 是两条独立渲染链。动画 WebP 不能等待 Godot
			// 的静态 WebP 解码结果，否则 GetSpriteTexture 返回空时会退化成 spacer，
			// 导致图片节点和 Control 内的逐帧贴图路径都不会被创建。
			SpriteAnimeFrameLayoutInfo animeFrameLayout = default;
			var texture = animatedWebpPath == null ? GetSpriteTexture(sprite, out animeFrameLayout) : null;
			if (texture == null && animatedWebpPath == null && ShouldUseRawImageResourceFallback(cip.ResourceName, sprite))
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
			if (texture != null || animatedWebpPath != null)
			{
				int sourceWidth = texture?.GetWidth() ?? sprite?.DestBaseSize.Width ?? 1;
				int sourceHeight = texture?.GetHeight() ?? sprite?.DestBaseSize.Height ?? 1;
				if (sourceWidth <= 0)
					sourceWidth = 1;
				if (sourceHeight <= 0)
					sourceHeight = 1;
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
					imgH = sourceHeight * w / sourceWidth;
				}
				else if (cip.dest_rect.Height > 0)
				{
					// Height specified, compute width from aspect ratio (matches constructor logic)
					imgH = cip.dest_rect.Height;
					w = sourceWidth * imgH / sourceHeight;
				}
				else
				{
					// No dimensions specified, use natural size
					w = sourceWidth;
					imgH = sourceHeight;
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
				emuImg.DrawOffset = GetSpriteHtmlDrawOffset(sprite, cip.ResourceName, w, imgH, animeFrameLayout);
				emuImg.DrawSize = GetSpriteHtmlDrawSize(sprite, cip.ResourceName, w, imgH, animeFrameLayout);
				emuImg.Position = GetHtmlImagePosition(cip, relX);
				emuImg.Size = new Vector2(w, imgH);
				emuImg.FlipX = cip.FlipX;
				emuImg.FlipY = cip.FlipY;
				emuImg.SetColorMatrix(cip.ColorMatrix);
				var normalSource = new EmueraImage.ImageSourceState(
					emuImg.SourceTexture,
					emuImg.SourceRegion,
					animatedWebpPath,
					emuImg.DrawOffset,
					emuImg.DrawSize);
				EmueraImage.ImageSourceState selectedSource = default;
				CanvasImageRenderInfo selectedInfo = default;
				bool hasSelectedSource = !string.IsNullOrEmpty(cip.ButtonResourceName)
					&& TryResolveCanvasImage(cip, relX, true, out selectedInfo);
				if (hasSelectedSource)
				{
					selectedSource = new EmueraImage.ImageSourceState(
						selectedInfo.SourceTexture,
						selectedInfo.SourceRegion,
						selectedInfo.AnimatedWebpPath,
						selectedInfo.DrawOffset,
						selectedInfo.DrawSize);
				}
				emuImg.ConfigureButtonSources(normalSource, selectedSource, hasSelectedSource);
				if (allowEscapedPartZ && ImageEscapesLine(cip))
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
				if (!rectShape.HasRenderableRect)
					return rectShape.Bottom;
				var colorRect = new ConsoleColorRectPart(rectShape.pColor.ToGodotColor(), rectShape.pButtonColor.ToGodotColor());
				SetFixedControlSize(colorRect, new Vector2(rectShape.RenderWidth, rectShape.RenderHeight));
				colorRect.Position = new Vector2(rectShape.PointX - relX + rectShape.RenderX, rectShape.RenderY);
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
		if (wrapper.HasMeta(ViewportAnchoredRelativeDivMeta))
			container.SetMeta(ViewportAnchoredRelativeDivRowMeta, true);
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
		wrapper.ZIndex = GetGodotZIndexForHtmlDiv(div);
		if (ShouldAnchorRelativeDivToViewport(div))
			MarkViewportAnchoredRelativeDiv(wrapper, div);

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

	void MarkViewportAnchoredRelativeDiv(Control wrapper, ConsoleDivPart div)
	{
		if (wrapper == null || div == null)
			return;
		wrapper.SetMeta(ViewportAnchoredRelativeDivMeta, true);
		wrapper.SetMeta(ViewportAnchoredRelativeDivXMeta, div.X);
		wrapper.SetMeta(ViewportAnchoredRelativeDivYMeta, div.Y);
		wrapper.SetMeta(ViewportAnchoredRelativeDivHeightMeta, div.DivHeight);
	}

	void RefreshViewportAnchoredRelativeDivs(Control root, float lineY)
	{
		if (root == null || !GodotObject.IsInstanceValid(root))
			return;
		RefreshViewportAnchoredRelativeDiv(root, lineY);
		foreach (var child in root.GetChildren())
		{
			if (child is Control childControl)
				RefreshViewportAnchoredRelativeDivs(childControl, lineY);
		}
	}

	void RefreshViewportAnchoredRelativeDiv(Control control, float lineY)
	{
		if (control == null || !control.HasMeta(ViewportAnchoredRelativeDivMeta))
			return;
		int x = (int)control.GetMeta(ViewportAnchoredRelativeDivXMeta).AsInt64();
		int y = (int)control.GetMeta(ViewportAnchoredRelativeDivYMeta).AsInt64();
		int height = (int)control.GetMeta(ViewportAnchoredRelativeDivHeightMeta).AsInt64();
		// eraTW/snake 泡茶菜单把 MOUSEY()-DIV_HEIGHT 写成普通 relative div。
		// fallback Control 行本身已经按历史输出 lineY 放置，因此这里必须减掉 lineY，
		// 否则浮层会继续跟着原输出行走，而不是贴到当前可视窗口里的鼠标上方。
		control.Position = GetViewportAnchoredRelativeDivPosition(x, y, height) - new Vector2(0, lineY);
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

		AddInlineLineBackgroundDivs(line, row, 0);

		foreach (var button in line.Buttons)
		{
			if (button.IsButton)
			{
				int buttonTop = GetButtonTop(button);
				int buttonHeight = GetButtonBottom(button, true, rowHeight) - buttonTop;
				if (buttonHeight <= 0)
					buttonHeight = rowHeight;
				var btn = BuildConsoleButton(button, buttonTop, buttonHeight, allowEscapedPartZ: false);
				row.AddChild(btn);
			}
			else
			{
				foreach (var part in button.StrArray)
					// div 内部已经由外层 div 的 depth 和裁剪框控制层级；子图片不能再用 escaped ZIndex
					// 抬到兄弟 div 之上，否则 eraFL 状态栏的背景图会盖住后续文字 div。
					AddPartToContainer(part, row, 0, allowEscapedPartZ: false);
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
		if (border == null || boxW <= 0 || boxH <= 0)
			return;
		int defaultColor = Config.ForeColor.ToArgb();
		AddBorderRect(wrapper, boxX, boxY, boxW, BoxValue(border, BoxDirection.Top), ColorValue(borderColor, BoxDirection.Top, defaultColor));
		AddBorderRect(wrapper, boxX + boxW - BoxValue(border, BoxDirection.Right), boxY, BoxValue(border, BoxDirection.Right), boxH, ColorValue(borderColor, BoxDirection.Right, defaultColor));
		AddBorderRect(wrapper, boxX, boxY + boxH - BoxValue(border, BoxDirection.Bottom), boxW, BoxValue(border, BoxDirection.Bottom), ColorValue(borderColor, BoxDirection.Bottom, defaultColor));
		AddBorderRect(wrapper, boxX, boxY, BoxValue(border, BoxDirection.Left), boxH, ColorValue(borderColor, BoxDirection.Left, defaultColor));
	}

	// Add one border rectangle if the side has positive thickness.
	void AddBorderRect(Control wrapper, float x, float y, float w, float h, int color)
	{
		if (w <= 0 || h <= 0)
			return;
		var rect = new ColorRect();
		rect.MouseFilter = MouseFilterEnum.Ignore;
		uint argb = unchecked((uint)color);
		rect.Color = new Godot.Color(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, ((argb >> 24) & 0xFF) / 255f);
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
	static int ColorValue(int[] values, int index, int fallback)
	{
		if (values == null || index < 0 || index >= values.Length)
			return fallback;
		return values[index];
	}

	// Resolve div coordinates into the target container's local coordinate space.
	Vector2 GetHtmlDivPosition(ConsoleDivPart div, int relX)
	{
		switch (div.Display)
		{
			case DisplayMode.Absolute:
			case DisplayMode.AbsoluteLeftBottom:
				return new Vector2(div.X, ResolveBottomOriginHtmlY(div.Y, div.DivHeight));
			case DisplayMode.AbsoluteLeftTop:
				return new Vector2(div.X, div.Y);
			default:
				return new Vector2(div.PointX - relX + div.X, div.Y);
		}
	}

	float ResolveBottomOriginHtmlY(int y, int height)
	{
		// Emuera 的 MOUSEY/CLIENTHEIGHT 坐标是底边为 0、向上为负。
		// v24/snake 的浮层会直接把这个负数作为 div 的上边缘；正数 left-bottom
		// 旧用法仍按“距底部高度”处理，避免破坏已有资源。
		if (y < 0)
			return GetContentViewportHeight() + y;
		return GetContentViewportHeight() - y - height;
	}

	bool ShouldAnchorRelativeDivToViewport(ConsoleDivPart div)
	{
		if (div == null || div.Display != DisplayMode.Relative || !div.IsRelative)
			return false;
		// eraTW/snake 的泡茶子菜单没有显式 display='absolute'，但脚本用
		// MOUSEY()-DIV_HEIGHT 得到的是 Emuera 底边原点坐标。它的下边缘仍为负数，
		// 且 depth=-1；普通行内 relative div 不满足这个形态约束，继续按 lineY 相对定位。
		return div.Depth < 0 && div.Y < 0 && div.Y + div.DivHeight < 0;
	}

	Vector2 GetViewportAnchoredRelativeDivPosition(ConsoleDivPart div)
	{
		if (div == null)
			return Vector2.Zero;
		return GetViewportAnchoredRelativeDivPosition(div.X, div.Y, div.DivHeight);
	}

	Vector2 GetViewportAnchoredRelativeDivPosition(int x, int y, int height)
	{
		var viewportPosition = new Vector2(x, ResolveBottomOriginHtmlY(y, height));
		if (scrollContainer == null)
			return viewportPosition;
		var scroll = new Vector2(NormalizeContentHorizontalScroll(scrollContainer.ScrollHorizontal), scrollContainer.ScrollVertical);
		if (contentScale > 0.001f)
			scroll /= contentScale;
		return scroll + viewportPosition;
	}

	// HTML 的 depth 数值越大越靠后；Godot 的 ZIndex 需要整体抬到 Canvas 绘制面之上。
	// eraFL 的房间框使用 depth=1，若直接映射为负数会被 Canvas 背景盖住，只剩无 depth 的局部遮罩可见。
	static int GetGodotZIndexForHtmlDepth(int depth)
	{
		return System.Math.Max(1, HtmlDivZIndexBase - depth);
	}

	int GetGodotZIndexForHtmlDiv(ConsoleDivPart div)
	{
		// eraTW/snake 的角色列表会在每一行文本之后追加一个 depth=1 的空 div 作为条纹背景。
		// 这类 div 必须压在本行文字和 PRINT_RECT 槽条后面；否则 Godot 的子节点绘制顺序会让背景盖住整行数据。
		// 大尺寸/带子内容/带盒模型的状态栏与浮层 div 仍走正向基准，保留 2026-07-07 修复的跨行覆盖语义。
		if (IsInlineLineBackgroundDiv(div))
			return -1;
		return GetGodotZIndexForHtmlDepth(div?.Depth ?? 0);
	}

	bool IsInlineLineBackgroundDiv(ConsoleDivPart div)
	{
		if (div == null
			|| !div.IsRelative
			|| div.Display != DisplayMode.Relative
			|| div.Depth <= 0
			|| !div.BackgroundColor.HasValue
			|| div.StyledBox != null
			|| div.Y != 0
			|| div.DivHeight <= 0)
			return false;
		if (div.Children != null && div.Children.Length > 0)
			return false;

		int inlineHeightLimit = System.Math.Max(EffectiveLineHeight, Config.FontSize) + 2;
		return div.DivHeight <= inlineHeightLimit;
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
		// v24/snake 原核心只有在 DestBasePosition 非零时，才把裁剪图块映射到
		// DestBaseSize 表示的基准画布内部。仅仅 src 尺寸和 DestBaseSize 不同，
		// 仍应把裁剪图块拉伸到调用方给出的目标矩形；否则立绘身体/表情这类
		// 共用基准画布的资源会被错误缩小并分离显示。
		return TryGetSpriteHtmlBasePosition(single, resourceName, out _);
	}

	static Vector2 GetSpriteHtmlDrawOffset(ASprite sprite, string resourceName, int width, int height,
		SpriteAnimeFrameLayoutInfo animeFrameLayout)
	{
		if (width == 0 || height == 0)
			return Vector2.Zero;
		if (sprite == null)
			return Vector2.Zero;

		if (sprite is SpriteAnime && animeFrameLayout.IsValid)
		{
			if (sprite.DestBaseSize.Width == 0 || sprite.DestBaseSize.Height == 0)
				return Vector2.Zero;
			// SpriteAnime 的帧偏移是相对动画基准画布的位置。必须与动画自身的
			// DestBasePosition 合并后缩放，才能保持原生 GraphicsDraw 的布局语义。
			return new Vector2(
				(sprite.DestBasePosition.X + animeFrameLayout.OffsetX) * width / (float)sprite.DestBaseSize.Width,
				(sprite.DestBasePosition.Y + animeFrameLayout.OffsetY) * height / (float)sprite.DestBaseSize.Height);
		}

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

	static Vector2 GetSpriteHtmlDrawSize(ASprite sprite, string resourceName, int width, int height,
		SpriteAnimeFrameLayoutInfo animeFrameLayout)
	{
		if (width == 0 || height == 0)
			return new Vector2(width, height);
		if (sprite is SpriteAnime && animeFrameLayout.IsValid
			&& sprite.DestBaseSize.Width > 0 && sprite.DestBaseSize.Height > 0
			&& animeFrameLayout.SourceWidth > 0 && animeFrameLayout.SourceHeight > 0)
		{
			return new Vector2(
				animeFrameLayout.SourceWidth * width / (float)sprite.DestBaseSize.Width,
				animeFrameLayout.SourceHeight * height / (float)sprite.DestBaseSize.Height);
		}
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
	Texture2D GetSpriteTexture(ASprite sprite, out SpriteAnimeFrameLayoutInfo animeFrameLayout)
	{
		animeFrameLayout = default;
		if (sprite == null)
			return null;

		// SpriteAnime.Bitmap 返回当前帧所属的整张源图集。动画必须先解析当前帧，
		// 否则下方 BitmapTexture 快速路径会直接返回整张图集并跳过 srcRect 裁剪。
		if (sprite is SpriteAnime anime)
		{
			AbstractImage baseImage;
			uEmuera.Drawing.Rectangle srcRect;
			uEmuera.Drawing.Point offset;
			if (anime.GetCurrentFrameInfo(out baseImage, out srcRect, out offset))
			{
				// 同一次渲染只读取一次当前帧，避免后台计时推进时纹理裁剪、偏移和尺寸跨帧。
				animeFrameLayout = new SpriteAnimeFrameLayoutInfo
				{
					IsValid = true,
					OffsetX = offset.X,
					OffsetY = offset.Y,
					SourceWidth = srcRect.Width,
					SourceHeight = srcRect.Height,
				};
				if (baseImage is GraphicsImage gImg && gImg.godotImage != null)
				{
					var texture = GetGraphicsImageDisplayTexture(gImg);
					if (texture == null)
						return null;
					if (srcRect.X == 0 && srcRect.Y == 0 &&
						srcRect.Width == texture.GetWidth() &&
						srcRect.Height == texture.GetHeight())
					{
						return texture;
					}
					return CreateAtlasTextureForDisplay(sprite, texture, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
				}
				if (baseImage?.Bitmap is uEmuera.Drawing.BitmapTexture androidBitmap
					&& UseAndroidCroppedAtlasTexture)
				{
					// Android 只上传当前小帧。不能在这里读取 ti.texture，否则会先创建整张
					// 8000px 图集的 ImageTexture，再由 AtlasTexture 裁剪，仍会触发实机限制。
					return GetAndroidSpriteAnimeFrameTexture(anime, androidBitmap, srcRect);
				}
				if (baseImage?.Bitmap != null)
				{
					var bmp = baseImage.Bitmap;
					var ti = GetDisplayTextureInfoForBitmap(bmp);
					if (ti != null && !ti.IsPlaceholder && ti.texture != null)
					{
						TrackTexturePin(ti);
						return ti.GetAtlasTexture(
							BuildAtlasCacheKey(sprite, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height),
							new Rect2(srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height));
					}
				}
			}
			return null;
		}

		if (sprite.Bitmap is uEmuera.Drawing.BitmapTexture bt)
		{
			var ti = bt.CachedTextureInfo;
			if (ti == null)
			{
				if (bt.RequestTextureInfoAsync())
					TrackAsyncTextureRequestForCurrentRender();
				return null;
			}
			if (ti.IsPlaceholder)
			{
				if (bt.RequestTextureInfoAsync())
					TrackAsyncTextureRequestForCurrentRender();
				else if (renderingCbgTextures)
					cbgTextureUnavailableDuringRender = true;
				return null;
			}
			if (sprite is ASpriteSingle androidStaticSprite
				&& TryGetAndroidStaticAtlasRegionTexture(androidStaticSprite, bt,
					androidStaticSprite.SrcRectangle, out var croppedAtlasTexture))
			{
				// Android 大图集的 CSV 坐标基于原始尺寸；不能先缩小整图再交给 AtlasTexture。
				// 命中时直接返回 CPU 裁出的图块，SourceRegion 保持默认值即可完整显示该小纹理。
				return croppedAtlasTexture;
			}
			if (ti.texture == null)
			{
				// ti 存在但 texture 为 null：ImageTexture lazy create 失败或 image 解码未完成。
				// 追踪当前行为 pending，确保 ProcessAsyncTextureRefreshes 会重试。
				TrackAsyncTextureRequestForCurrentRender();
				if (renderingCbgTextures)
					cbgTextureUnavailableDuringRender = true;
				return null;
			}
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
				var texture = GetGraphicsImageDisplayTexture(gImg);
				if (texture == null)
					return null;
				if (srcRect.X == 0 && srcRect.Y == 0 &&
					srcRect.Width == texture.GetWidth() &&
					srcRect.Height == texture.GetHeight())
				{
					return texture;
				}
				return CreateAtlasTextureForDisplay(sprite, texture, srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height);
			}
			if (singleSprite.BaseImage?.Bitmap != null)
			{
				var bmp = singleSprite.BaseImage.Bitmap;
				var ti = GetDisplayTextureInfoForBitmap(bmp);
				if (ti != null && !ti.IsPlaceholder && ti.texture != null)
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

		return null;
	}

	// CSV 的普通 .webp 资源通常仍按静态图片处理。只有完整引用、带 ANIM chunk 的
	// 文件才交给 EmueraImage 的逐帧贴图路径，避免裁剪 sprite 或普通 WebP 改变原有显示语义。
	static string GetAnimatedWebpSourcePath(ASprite sprite)
	{
		if (sprite is not ASpriteSingle single
			|| single.Bitmap is not uEmuera.Drawing.BitmapTexture bitmap
			|| single.SrcRectangle.X != 0 || single.SrcRectangle.Y != 0
			|| single.SrcRectangle.Width != bitmap.Width || single.SrcRectangle.Height != bitmap.Height)
			return null;
		return AnimatedWebpSpriteFrames.IsAnimatedWebp(bitmap.path) ? bitmap.path : null;
	}

	Texture2D GetGraphicsImageDisplayTexture(GraphicsImage image)
	{
		if (image == null)
			return null;
		long revision = image.DisplayRevision;
		if (graphicsImageTextureCache.TryGetValue(image.ID, out var cached)
			&& cached.Revision == revision
			&& cached.Texture != null)
		{
			return cached.Texture;
		}

		// TW/Snake 部分角色会把立绘先清空到 GraphicsImage，再连续绘制差分层。
		// UI 若在中间态上传贴图，就会看到角色白一下再恢复；因此动态图像需要稳定一个短窗口再提交。
		if (!image.TryCreateDisplaySnapshot(GraphicsImageDisplayStableDelayMs, out var snapshot, out revision, out bool retrySoon))
		{
			if (retrySoon)
				TrackAsyncTextureRequestForCurrentRender();
			if (renderingCbgTextures)
				cbgTextureUnavailableDuringRender = true;
			return cached.Texture;
		}

		try
		{
			var texture = Godot.ImageTexture.CreateFromImage(snapshot);
			graphicsImageTextureCache[image.ID] = new GraphicsImageTextureCacheEntry
			{
				Revision = revision,
				Texture = texture,
			};
			return texture;
		}
		finally
		{
			snapshot?.Dispose();
		}
	}

	static AtlasTexture CreateAtlasTextureForDisplay(ASprite sprite, Texture2D texture, int x, int y, int width, int height)
	{
		if (texture == null)
			return null;
		var atlas = new AtlasTexture();
		atlas.Atlas = texture;
		atlas.Region = new Rect2(x, y, width, height);
		return atlas;
	}

	SpriteManager.TextureInfo GetDisplayTextureInfoForBitmap(uEmuera.Drawing.Bitmap bmp)
	{
		if (bmp == null)
			return null;
		if (bmp is uEmuera.Drawing.BitmapTexture bt)
		{
			var ti = bt.CachedTextureInfo;
			if (ti == null && bt.RequestTextureInfoAsync())
			{
				TrackAsyncTextureRequestForCurrentRender();
				return null;
			}
			if (ti != null && ti.IsPlaceholder)
			{
				if (bt.RequestTextureInfoAsync())
					TrackAsyncTextureRequestForCurrentRender();
				else if (renderingCbgTextures)
					cbgTextureUnavailableDuringRender = true;
				return null;
			}
			if (ti != null && ti.texture == null)
			{
				if (renderingCbgTextures)
					cbgTextureUnavailableDuringRender = true;
				return null;
			}
			return ti;
		}

		// 动态生成或兼容层来源的 Bitmap 需要立即读取像素，继续同步解析；
		// 只有文件 backed 的 BitmapTexture 用非阻塞显示路径。
		var sync = SpriteManager.GetTextureInfo(bmp.path, bmp.path);
		if (sync == null && !string.IsNullOrEmpty(bmp.filename))
			sync = SpriteManager.GetTextureInfo(bmp.filename, bmp.path);
		if (sync != null && sync.IsPlaceholder)
		{
			if (renderingCbgTextures)
				cbgTextureUnavailableDuringRender = true;
			return null;
		}
		if (sync != null && sync.texture == null)
		{
			if (renderingCbgTextures)
				cbgTextureUnavailableDuringRender = true;
			return null;
		}
		return sync;
	}

	bool TryGetDisplayTextureInfo(string name, string filename, out SpriteManager.TextureInfo ti)
	{
		if (SpriteManager.TryGetTextureInfoCached(name, filename, out ti))
		{
			if (ti.IsPlaceholder || ti.texture == null)
			{
				ti = null;
				return false;
			}
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
		RemoveLinesByNumber(lineNos, true);
	}

	void RemoveLinesByNumber(List<int> lineNos, bool rememberPureImageFallback = false)
	{
		if (lineNos == null || lineNos.Count == 0)
			return;

		GenericUtils.ClearPointingButton();
		bool removedAny = false;
		for (int i = 0; i < lineNos.Count; i++)
		{
			int lineNo = lineNos[i];
			lineControls.TryGetValue(lineNo, out var control);
			if (rememberPureImageFallback)
				RememberPureImageFallbackLine(lineNo);
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
		if (version == observedTextureLoadVersion
			&& asyncTexturePendingLineNos.Count == 0
			&& !pendingHtmlIslandAsyncTextureRefresh
			&& !pendingCbgAsyncTextureRefresh)
			return;
		observedTextureLoadVersion = version;

		bool changed = false;
		if (asyncTexturePendingLineNos.Count > 0)
		{
			var lineNos = new List<int>(asyncTexturePendingLineNos);
			lineNos.Sort();
			int refreshLimit = System.Math.Min(lineNos.Count, GetAsyncTextureRefreshLineBudget());

			bool previousBatching = batchingDisplayLines;
			batchingDisplayLines = true;
			try
			{
				for (int i = 0; i < refreshLimit; i++)
				{
					int lineNo = lineNos[i];
					asyncTexturePendingLineNos.Remove(lineNo);
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

	static int GetAsyncTextureRefreshLineBudget()
	{
		// Android 端把异步补图拆成小批次，避免多张图片同帧触发节点重建和纹理上传尖峰；
		// 高端机只会多等几个渲染帧，低端骁龙 660 这类设备能明显降低卡顿风险。
		return OS.GetName() == "Android" ? AndroidAsyncTextureRefreshLineBudget : DesktopAsyncTextureRefreshLineBudget;
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
		bool previousCbgUnavailable = cbgTextureUnavailableDuringRender;
		renderingCbgTextures = true;
		cbgTextureUnavailableDuringRender = false;
		try
		{
			foreach (var cbg in list)
			{
				if (cbg.zdepth == 0)
					continue;
				if (cbg.Img == null || !cbg.Img.IsCreated)
					continue;

				string animatedWebpPath = GetAnimatedWebpSourcePath(cbg.Img);
				// 动画 WebP 由后台解码器提供帧纹理，并由 EmueraImage 逐帧替换自身贴图。不能先等待
				// Image.LoadWebpFromBuffer 的静态首帧，否则 Godot 不支持该文件时会在
				// 此处提前跳过整层，导致动画样例只剩右侧的静态对照图。
				SpriteAnimeFrameLayoutInfo animeFrameLayout = default;
				var texture = animatedWebpPath == null ? GetSpriteTexture(cbg.Img, out animeFrameLayout) : null;
				if (texture == null && animatedWebpPath == null)
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
				int sourceWidth = cbg.Img.DestBaseSize.Width > 0 ? cbg.Img.DestBaseSize.Width : texture?.GetWidth() ?? 1;
				int sourceHeight = cbg.Img.DestBaseSize.Height > 0 ? cbg.Img.DestBaseSize.Height : texture?.GetHeight() ?? 1;
				int w = cbg.width != 0 ? System.Math.Abs(cbg.width) : sourceWidth;
				int h = cbg.height != 0 ? System.Math.Abs(cbg.height) : sourceHeight;
				entry.DrawOffset = GetSpriteHtmlDrawOffset(cbg.Img, cbg.Img.Name, w, h, animeFrameLayout);
				entry.DrawSize = GetSpriteHtmlDrawSize(cbg.Img, cbg.Img.Name, w, h, animeFrameLayout);
				entry.AnimatedWebpPath = animatedWebpPath;
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
			cbgTextureUnavailableDuringRender = previousCbgUnavailable;
			ReleaseTexturePinList(newCbgTexturePins);
			throw;
		}
		finally
		{
			renderingCbgTextures = previousCbgRender;
			activeTexturePinCollector = previousTexturePinCollector;
		}

		bool preservePreviousCbgForUnavailableTexture = cbgTextureUnavailableDuringRender;
		cbgTextureUnavailableDuringRender = previousCbgUnavailable;
		if (pendingCbgAsyncTextureRefresh || preservePreviousCbgForUnavailableTexture)
		{
			// 刷新背景层时如果新纹理还没就绪，保留旧 CBG 节点和旧 pin。
			// 初次显示时也不提交半成品图层，等异步完成后再一次性提交，避免标题/差分图白块闪烁。
			ReleaseTexturePinList(newCbgTexturePins);
			return;
		}

		ReleaseCbgTexturePins();
		renderedCbgLayers.Clear();
		for (int i = 0; i < entries.Count; i++)
		{
			var entry = entries[i];
			var emuImg = GetOrCreateCbgNode(i);
			ApplyCbgRenderEntry(emuImg, entry);
			renderedCbgLayers.Add(entry.Layer);
		}
		cbgTexturePins = newCbgTexturePins;
		lastCbgScrollVertical = currentScrollY;
		TrimCbgNodes(entries.Count);
	}

	// CBG 的节点按索引复用。即使动态图层要求重新检查纹理，也不要给属性未变的节点
	// 重复写入相同值，否则每个 setter 都会让 Godot 重新标记 CanvasItem 并放大地图刷新成本。
	static void ApplyCbgRenderEntry(EmueraImage image, CbgRenderEntry entry)
	{
		if (image == null)
			return;
		if (image.SourceTexture != entry.SourceTexture)
			image.SourceTexture = entry.SourceTexture;
		if (image.SourceRegion != entry.SourceRegion)
			image.SourceRegion = entry.SourceRegion;
		if (image.DrawOffset != entry.DrawOffset)
			image.DrawOffset = entry.DrawOffset;
		if (image.DrawSize != entry.DrawSize)
			image.DrawSize = entry.DrawSize;
		if (image.Position != entry.Position)
			image.Position = entry.Position;
		if (image.Size != entry.Size)
			image.Size = entry.Size;
		if (image.FlipX != entry.FlipX)
			image.FlipX = entry.FlipX;
		if (image.FlipY != entry.FlipY)
			image.FlipY = entry.FlipY;
		if (image.Modulate != entry.Modulate)
			image.Modulate = entry.Modulate;
		image.SetColorMatrix(entry.Layer?.colorMatrix);
		image.SetAnimatedWebpSource(entry.AnimatedWebpPath);
		if (!image.Visible)
			image.Visible = true;
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
		bool traceDynamicButtons = GenericUtils.IsDynamicMapButtonTraceEnabled
			&& GenericUtils.ShouldTraceDynamicMap(false);
		if (traceDynamicButtons)
		{
			GenericUtils.DynamicMapTrace("DYNAMIC_MAP.UI.BUTTONS.REQUEST",
				() => "dynamic map quick-button generation requested",
				() => "previous_generation=" + lastButtonGeneration
					+ " next_generation=" + generation
					+ " quick_exists=" + (quickButtons != null)
					+ " quick_show=" + (quickButtons != null && quickButtons.IsShow)
					+ " auto_show=" + shouldAutoShowQuick
					+ " rendered_generation=" + quickRenderedGeneration
					+ " rendered_revision=" + quickRenderedRevision
					+ " display_revision=" + displayRevision);
		}
		lastButtonGeneration = generation;
		RefreshQuickInputGate();

		if (quickButtons != null && (quickButtons.IsShow || shouldAutoShowQuick))
		{
			if (quickRenderedGeneration == lastButtonGeneration && quickRenderedRevision == displayRevision)
			{
				if (traceDynamicButtons)
				{
					GenericUtils.DynamicMapTrace("DYNAMIC_MAP.UI.BUTTONS.CACHED",
						() => "dynamic map quick buttons reused",
						() => "generation=" + lastButtonGeneration
							+ " display_revision=" + displayRevision);
				}
				if (shouldAutoShowQuick)
				{
					ClearQuickAutoHiddenState();
					quickButtons.ShowPad();
					UpdateSystemButtonVisuals();
				}
				return;
			}

			if (lastButtonGeneration < 0)
			{
				quickButtons.BeginBatch();
				try
				{
					quickButtons.Clear();
					quickRenderedGeneration = lastButtonGeneration;
					quickRenderedRevision = displayRevision;
					quickRenderedSignature = "";
					if (traceDynamicButtons)
					{
						GenericUtils.DynamicMapTrace("DYNAMIC_MAP.UI.BUTTONS.SKIP",
							() => "dynamic map quick buttons skipped",
							() => "reason=negative_generation generation=" + lastButtonGeneration
								+ " display_revision=" + displayRevision);
					}
				}
				finally
				{
					quickButtons.EndBatch();
				}
				return;
			}

			var lineGroups = CollectCurrentGenerationQuickButtonGroups();

			if (lineGroups.Count == 0)
			{
				quickButtons.BeginBatch();
				try
				{
					quickButtons.Clear();
					quickRenderedGeneration = lastButtonGeneration;
					quickRenderedRevision = displayRevision;
					quickRenderedSignature = "";
					if (traceDynamicButtons)
					{
						GenericUtils.DynamicMapTrace("DYNAMIC_MAP.UI.BUTTONS.EMPTY",
							() => "dynamic map quick buttons empty",
							() => "generation=" + lastButtonGeneration
								+ " display_revision=" + displayRevision
								+ " retained_lines=" + GetRetainedLineCount());
					}
				}
				finally
				{
					quickButtons.EndBatch();
				}
				return;
			}

			string nextSignature = BuildQuickButtonGroupsSignature(lineGroups);
			if (quickRenderedSignature == nextSignature)
			{
				bool generationChanged = quickRenderedGeneration != lastButtonGeneration;
				if (generationChanged)
					quickButtons.UpdateButtonGeneration(lastButtonGeneration);
				quickRenderedGeneration = lastButtonGeneration;
				quickRenderedRevision = displayRevision;
				if (traceDynamicButtons)
				{
					GenericUtils.DynamicMapTrace("DYNAMIC_MAP.UI.BUTTONS.CACHED_SIGNATURE",
						() => "dynamic map quick buttons reused by signature",
						() => "generation=" + lastButtonGeneration
							+ " display_revision=" + displayRevision
							+ " groups=" + lineGroups.Count
							+ " generation_updated=" + generationChanged);
				}
				if (shouldAutoShowQuick)
				{
					ClearQuickAutoHiddenState();
					quickButtons.ShowPad();
					quickButtons.SetInputEnabled(true);
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
				quickRenderedSignature = nextSignature;
				if (shouldAutoShowQuick)
				{
					ClearQuickAutoHiddenState();
					quickButtons.ShowPad();
					quickButtons.SetInputEnabled(true);
					UpdateSystemButtonVisuals();
				}

				if (traceDynamicButtons)
				{
					GenericUtils.DynamicMapTrace("DYNAMIC_MAP.UI.BUTTONS.REBUILD",
						() => "dynamic map quick buttons rebuilt",
						() => "generation=" + lastButtonGeneration
							+ " display_revision=" + displayRevision
							+ " groups=" + lineGroups.Count
							+ " buttons=" + BuildDynamicMapQuickGroupsSummary(lineGroups));
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

	List<(int lineNo, List<(string text, Godot.Color color, string code)> buttons)> CollectCurrentGenerationQuickButtonGroups()
	{
		var lineGroups = new List<(int lineNo, List<(string text, Godot.Color color, string code)> buttons)>();
		if (lastButtonGeneration < 0 || lastButtonGeneration > maxRenderedButtonGeneration)
			return lineGroups;
		if (!buttonGenerationLineNumbers.TryGetValue(lastButtonGeneration, out var rows) || rows == null)
			return lineGroups;

		// 动态地图会累积大量历史行；当前等待代的按钮只可能出现在 generation 索引命中的行里。
		// 这里避免每次打开地图/刷新快捷按钮时遍历全部 retained line。
		foreach (int lineNo in rows)
		{
			if (!lineObjects.TryGetValue(lineNo, out var line))
				continue;
			var lineButtons = new List<(string text, Godot.Color color, string code)>();
			CollectQuickButtons(line, lineButtons);
			if (lineButtons.Count > 0)
				lineGroups.Add((lineNo, lineButtons));
		}
		return lineGroups;
	}

	string BuildQuickButtonGroupsSignature(List<(int lineNo, List<(string text, Godot.Color color, string code)> buttons)> lineGroups)
	{
		if (lineGroups == null || lineGroups.Count == 0)
			return "";
		var sb = new System.Text.StringBuilder(lineGroups.Count * 48);
		for (int i = 0; i < lineGroups.Count; i++)
		{
			var group = lineGroups[i];
			// 动态地图刷新可能删除尾部再重绘，绝对行号会变化；快捷按钮是否需要重建只取决于可见按钮组的顺序和内容。
			sb.Append(i).Append(':');
			if (group.buttons != null)
			{
				for (int j = 0; j < group.buttons.Count; j++)
				{
					var button = group.buttons[j];
					sb.Append(button.code ?? "")
						.Append('=')
						.Append(button.text ?? "")
						.Append('#')
						.Append(button.color.ToString())
						.Append(';');
				}
			}
			sb.Append('|');
		}
		return sb.ToString();
	}

	string BuildDynamicMapQuickGroupsSummary(List<(int lineNo, List<(string text, Godot.Color color, string code)> buttons)> lineGroups)
	{
		if (lineGroups == null || lineGroups.Count == 0)
			return "none";
		int maxGroups = Math.Min(12, lineGroups.Count);
		var sb = new System.Text.StringBuilder(maxGroups * 64);
		for (int i = 0; i < maxGroups; i++)
		{
			if (sb.Length > 0)
				sb.Append('|');
			var group = lineGroups[i];
			sb.Append(group.lineNo).Append(':');
			int maxButtons = Math.Min(4, group.buttons?.Count ?? 0);
			for (int j = 0; j < maxButtons; j++)
			{
				if (j > 0)
					sb.Append(',');
				var button = group.buttons[j];
				sb.Append(GenericUtils.ClipTrace(button.code, 24).Replace(' ', '_'))
					.Append('=')
					.Append(GenericUtils.ClipTrace(button.text, 24).Replace(' ', '_'));
			}
			if ((group.buttons?.Count ?? 0) > maxButtons)
				sb.Append(",...");
		}
		if (lineGroups.Count > maxGroups)
			sb.Append("|...");
		return sb.ToString();
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

	// ------------------------------------------------------------------
	// VirtualCursor 转发入口：把私有的坐标换算/命中测试/点击提交/hover 高亮
	// 方法暴露给 VirtualCursor，使其可以脱离真实 InputEvent 驱动同一套判定链路。
	// ------------------------------------------------------------------

	// 反向坐标换算：content-local 坐标 → 屏幕全局坐标（供光标可视化定位）。
	// 是 UpdatePointerPosition 换算公式的镜像：
	//   content = (global - scrollRect.Position + scrollOffset) / contentScale
	//   global  = content * contentScale - scrollOffset + scrollRect.Position
	public Vector2 VirtualCursorContentToGlobal(Vector2 contentPosition)
	{
		if (scrollContainer == null)
			return contentPosition;
		var rect = scrollContainer.GetGlobalRect();
		var scrollOffset = new Vector2(NormalizeContentHorizontalScroll(scrollContainer.ScrollHorizontal), scrollContainer.ScrollVertical);
		var scaled = contentPosition * (contentScale > 0.001f ? contentScale : 1.0f);
		return scaled - scrollOffset + rect.Position;
	}

	// 正向坐标换算：屏幕全局坐标 → content-local 坐标（供虚拟光标存储坐标状态）。
	// 与 UpdatePointerPosition 逻辑一致。
	public Vector2 VirtualCursorGlobalToContent(Vector2 globalPosition)
	{
		if (scrollContainer == null)
			return globalPosition;
		var rect = scrollContainer.GetGlobalRect();
		var contentPosition = globalPosition - rect.Position;
		contentPosition += new Vector2(NormalizeContentHorizontalScroll(scrollContainer.ScrollHorizontal), scrollContainer.ScrollVertical);
		if (contentScale > 0.001f)
			contentPosition /= contentScale;
		return contentPosition;
	}

	// 当前可视内容区域（全局坐标），供 VirtualCursor clamp 光标移动范围。
	public Rect2 VirtualCursorGetContentViewportRect()
	{
		return scrollContainer != null ? scrollContainer.GetGlobalRect() : new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size);
	}

	// 虚拟光标重新显示或移动时，视觉坐标和脚本读取的 MOUSEX/MOUSEY 必须一起更新。
	// 否则新内容会按旧指针位置计算 HTML div，造成视觉光标和游戏内浮层不一致。
	public void VirtualCursorSynchronizePosition(Vector2 globalPosition)
	{
		UpdatePointerPosition(globalPosition);
		VirtualCursorUpdateHover(globalPosition);
	}

	// 命中测试：光标当前全局坐标下是否有按钮，命中则驱动 Canvas 高亮（通道B）+
	// 原生 hover 字段（通道A，ERB MOUSEBUTTON() 读取），未命中则清空两条通道。
	public bool VirtualCursorUpdateHover(Vector2 globalPosition)
	{
		if (TryFindConsoleButtonAtGlobalPosition(globalPosition, out _, out var hoverInput, out var hoverGeneration,
			out _, out _))
		{
			SetCanvasVisualButton(hoverInput, hoverGeneration);
			GenericUtils.SetPointingButton(hoverInput, hoverGeneration);
			return true;
		}
		ClearCanvasVisualButton();
		GenericUtils.ClearPointingButton();
		return false;
	}

	// 在光标当前全局坐标提交一次点击：命中按钮则走 OnButtonPressed，否则走
	// TryAdvanceTap（空白区域点击，用于 eraFL 地图/状态切换等场景）。
	public void VirtualCursorCommitClick(Vector2 globalPosition, int mouseVk)
	{
		UpdatePointerPosition(globalPosition);
		if (TryFindConsoleButtonAtGlobalPosition(globalPosition, out var hitButton, out var hitInput, out var hitGeneration,
			out var contentCenterValid, out _))
		{
			if (contentCenterValid)
				UpdatePointerPosition(globalPosition);
			else
				UpdatePointerPositionForButton(hitButton, globalPosition);
			if (quickButtons != null && quickButtons.IsShow)
				HideQuickUntilNextButtons(hitGeneration);
			OnButtonPressed(hitInput, hitGeneration, mouseVk: mouseVk);
		}
		else
		{
			TryAdvanceTap(true, mouseVk);
		}
	}

	public void VirtualCursorBeginSlide(Vector2 globalPosition)
	{
		if (scrollContainer == null)
			return;

		StopContentInertia();
		contentDragActive = true;
		contentDragMoved = true;
		contentDragStartedOnButton = false;
		contentDragButton = null;
		contentDragButtonInput = null;
		contentDragButtonGeneration = 0;
		contentDragButtonContentCenterValid = false;
		contentDragMouseVk = 0x01;
		contentDragStartPosition = globalPosition;
		contentDragLastPosition = globalPosition;
		contentScrollVelocity = Vector2.Zero;
		contentInertiaRemainder = Vector2.Zero;
		contentLastDragTick = Time.GetTicksMsec();
		lastScrollTraceDragTick = contentLastDragTick;
		contentScrollInteractionSerial++;
		ClearCanvasVisualButton();
		UpdatePointerPosition(globalPosition);
		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("virtual_cursor_slide_begin", () => $"pos=({Mathf.RoundToInt(globalPosition.X)},{Mathf.RoundToInt(globalPosition.Y)})");
	}

	public void VirtualCursorSlideBy(Vector2 pointerDelta)
	{
		if (scrollContainer == null)
			return;

		if (!contentDragActive)
			VirtualCursorBeginSlide(VirtualCursorGetContentViewportRect().GetCenter());

		var rawScrollDelta = -pointerDelta;
		var appliedDelta = ScrollContentBy(rawScrollDelta);
		UpdateContentScrollVelocity(rawScrollDelta, appliedDelta);
		contentDragLastPosition += pointerDelta;
		if (GenericUtils.IsScrollTraceActive && appliedDelta.LengthSquared() > 0.01f)
			TraceScroll("virtual_cursor_slide_move", () => $"raw=({Mathf.RoundToInt(rawScrollDelta.X)},{Mathf.RoundToInt(rawScrollDelta.Y)}) applied=({Mathf.RoundToInt(appliedDelta.X)},{Mathf.RoundToInt(appliedDelta.Y)})");
	}

	public void VirtualCursorEndSlide(bool startInertia)
	{
		if (!contentDragActive)
			return;

		if (startInertia)
			StartContentInertia();
		else
			StopContentInertia();
		ResetContentDragState();
		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("virtual_cursor_slide_end", () => $"inertia={startInertia}");
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
	void OnButtonPressed(string input, long generation, bool skip = false, int mouseVk = 0x01)
	{
		RememberCurrentContentScroll();
		if (GenericUtils.IsScrollTraceActive)
		{
			TraceScroll("button_pressed", () => $"input={GenericUtils.ClipTrace(input, 64)} gen={generation} lastGen={lastButtonGeneration} skip={skip} vk={mouseVk}");
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
		EmueraThread.instance.Input(input, true, skip, mouseVk);
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
			virtualCursor?.Disable();
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
			virtualCursor?.Disable();
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
			virtualCursor?.Disable();
			scalepad.ShowPad();
		}
		UpdateSystemButtonVisuals();
	}

	// Toggle 虚拟光标模式；与 输入/快捷/缩放 面板保持同一套互斥逻辑，避免叠加遮挡触控区域。
	void OnVirtualCursorTogglePressed()
	{
		ClearQuickAutoHiddenState();
		if (virtualCursor != null && virtualCursor.IsEnabled)
		{
			virtualCursor.Disable();
		}
		else
		{
			inputpad?.HidePad();
			quickButtons?.HidePad();
			scalepad?.HidePad();
			virtualCursor?.Enable();
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
		SetSystemButtonActive(mouseMenuButton, virtualCursor != null && virtualCursor.IsEnabled);
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
		ApplySafeAreaLayout();
		virtualCursor?.RefreshViewportBounds();
		QueueScaleBoundsUpdate();
	}

	// Public scale entry point used by Scalepad. It keeps the viewport center
	// anchored so zooming does not jump to the top-left.
	public void SetContentScale(float scale)
	{
		if (scrollContainer != null)
			SetContentScaleKeepingFocus(scale, scrollContainer.GetGlobalRect().GetCenter(), false, true);
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
		AnimatedWebpSpriteFrames.ProcessPendingFrameUploads(OS.HasFeature("mobile"));
		ProcessPendingContentPinchZoom();
		ProcessContentInertia((float)delta);
		ProcessContentScrollCorrection();
		SyncContentVerticalScrollRange();
		consoleRenderSurface?.SyncScrollRedraw();
		PublishAudioPlaybackPositions();
		RefreshCbgFollowScrollPositions();
		RefreshCbgAnimationPauseState();
		RefreshCanvasImageAnimations();
		CleanupAndroidSpriteAnimeFrameTextures();
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
			var node = cbgNodes[i];
			if (node == null)
				continue;
			var nodeRect = node.GetGlobalRect();
			bool visible = nodeRect.Intersects(viewRect);
			if (sprite is MinorShift.Emuera.Content.SpriteAnime anime)
			{
				if (visible)
					anime.ResumeAnimation();
				else
					anime.PauseAnimation();
			}
			node.SetAnimatedWebpPaused(!visible);
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
		string previousInput = canvasVisualButtonInput;
		long previousGeneration = canvasVisualButtonGeneration;
		canvasVisualButtonInput = input;
		canvasVisualButtonGeneration = generation;
		RefreshCanvasImageOverlaySelection(previousInput, previousGeneration, input, generation);
		QueueCanvasVisualRedraw();
	}

	void ClearCanvasVisualButton()
	{
		if (canvasVisualButtonGeneration == long.MinValue && string.IsNullOrEmpty(canvasVisualButtonInput))
			return;
		string previousInput = canvasVisualButtonInput;
		long previousGeneration = canvasVisualButtonGeneration;
		canvasVisualButtonInput = null;
		canvasVisualButtonGeneration = long.MinValue;
		RefreshCanvasImageOverlaySelection(previousInput, previousGeneration, null, long.MinValue);
		QueueCanvasVisualRedraw();
	}

	void ClearCanvasVisualButtonIfMatches(string input, long generation)
	{
		if (canvasVisualButtonGeneration != generation)
			return;
		if (!string.Equals(canvasVisualButtonInput ?? "", input ?? "", StringComparison.Ordinal))
			return;
		ClearCanvasVisualButton();
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
	bool HandleContentPointerInput(InputEvent @event, bool acceptEvent, Control button = null, string input = null,
		long generation = 0)
	{
		if (HandleContentTouchGesture(@event, acceptEvent))
			return true;

		// 虚拟光标模式开启时，单指按下/拖动/释放全部交给 VirtualCursor 处理
		// （移动光标 + 短按=左键/长按=右键），不进入下面的滚动/点击判定分支。
		// 双指缩放优先级不受影响，因为已经在 HandleContentTouchGesture 判断之后。
		if (virtualCursor != null && virtualCursor.IsEnabled
			&& virtualCursor.HandleGesture(@event, acceptEvent))
			return true;

		if (!TryGetPointer(@event, out var pointerPosition, out var pressed, out var released, out var motion, out var eventMouseVk))
			return false;

		UpdatePointerPosition(pointerPosition);

		if (scrollContainer == null || (!contentDragActive && !scrollContainer.GetGlobalRect().HasPoint(pointerPosition)))
			return false;

		if (motion && !contentDragActive)
		{
			if (TryFindConsoleButtonAtGlobalPosition(pointerPosition, out _, out var hoverInput, out var hoverGeneration,
				out _, out _))
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
			if (button == null && TryFindConsoleButtonAtGlobalPosition(pointerPosition, out var hitButton, out var hitInput,
				out var hitGeneration, out hitContentCenterValid, out _))
			{
				button = hitButton;
				input = hitInput;
				generation = hitGeneration;
			}

			// 桌面:用事件的真实 VK；Android 触控(eventMouseVk<0):默认左键。
			// 右/中键在虚拟光标模式下改由 HandleVirtualCursorGesture 独立提交，
			// 不会经过这条常规按钮按下路径。
			int effectiveVk = eventMouseVk >= 0 ? eventMouseVk : 0x01;
			MinorShift._Library.WinInput.PulseVirtualKey(effectiveVk);
			contentDragMouseVk = effectiveVk;
			StopContentInertia();
			contentDragActive = true;
			contentDragMoved = false;
			contentDragStartedOnButton = button != null || !string.IsNullOrEmpty(input);
			contentDragButton = button;
			contentDragButtonInput = input;
			contentDragButtonGeneration = generation;
			contentDragButtonContentCenterValid = hitContentCenterValid;
			if (contentDragStartedOnButton)
			{
				SetCanvasVisualButton(input, generation);
				SetControlButtonSelected(button, true);
			}
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
			// 右/中键不滚动，无论移动多少像素都不标记为 drag。
			if (!contentDragMoved && contentDragMouseVk == 0x01 && totalDelta.Length() >= ScrollDragThreshold)
			{
				contentDragMoved = true;
				contentScrollInteractionSerial++;
				SetControlButtonSelected(contentDragButton, false);
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
		int pressedButtonMouseVk = contentDragMouseVk;
		Control pressedButtonControl = null;
		bool pressedButtonContentCenterValid = false;
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
			handled = true;
		}
		else if (!contentDragStartedOnButton)
		{
			var console = GlobalStatic.Console;
			bool isNonLeftClick = pressedButtonMouseVk != 0x01;
			// 左クリック: 通常は EnterKey/AnyKey、eraFL では INPUTS の空白入力も advance。
			// 右/中クリック: INPUT 待ち中も送信し、ゲーム側が RESULT:1 で用途を判定する。
			advanceTap = console != null && (
				console.IsWaitingEnterKey || console.IsWaitAnyKey ||
				(isNonLeftClick && console.IsWaitingInput) ||
				ShouldSubmitBlankLeftClickDefault(console, pressedButtonMouseVk) ||
				ShouldSubmitEraFlBlankPointerString(console, pressedButtonMouseVk));
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
				UpdatePointerPosition(pointerPosition);
			else
				UpdatePointerPositionForButton(pressedButtonControl, pointerPosition);
			if (quickButtons != null && quickButtons.IsShow)
				HideQuickUntilNextButtons(pressedButtonGeneration);
			OnButtonPressed(pressedButtonInput, pressedButtonGeneration, mouseVk: pressedButtonMouseVk);
		}
		else if (advanceTap)
			TryAdvanceTap(acceptEvent, pressedButtonMouseVk);
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
	bool TryFindConsoleButtonAtGlobalPosition(Vector2 globalPosition, out Control button, out string input, out long generation,
		out bool contentCenterValid, out Vector2 contentCenter)
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
	void SetContentScaleKeepingFocus(float scale, Vector2 focusGlobalPosition, bool trackGestureFocus = true, bool allowShrink = false)
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
			: new Vector2(NormalizeContentHorizontalScroll(scrollContainer.ScrollHorizontal), scrollContainer.ScrollVertical);
		var contentFocus = (previousScroll + localFocus) / previousScale;
		if (GenericUtils.IsScrollTraceActive)
			TraceScroll("scale_focus_begin", () => $"from={contentScale:0.###} to={ClampContentScale(scale):0.###} focus=({Mathf.RoundToInt(localFocus.X)},{Mathf.RoundToInt(localFocus.Y)})");

		ApplyContentScaleValue(scale);
		UpdateScaleBounds(allowShrink);
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
		int targetHorizontal = NormalizeContentHorizontalScroll(Mathf.Clamp(Mathf.RoundToInt(nextScroll.X), 0, GetMaxContentHorizontalScroll()));
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
		contentPosition += new Vector2(NormalizeContentHorizontalScroll(scrollContainer.ScrollHorizontal), scrollContainer.ScrollVertical);
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

	// Update MOUSEX/MOUSEY in client coordinates, matching WinForms PointToClient.
	// 滚动条位置不能混入这里，否则 ERB 会把“历史输出全文坐标”误当成窗口坐标，
	// v24/snake 的鼠标浮层会触发底部溢出保护并贴到底部。
	void UpdatePointerPosition(Vector2 globalPosition)
	{
		if (scrollContainer == null)
		{
			GenericUtils.SetPointerPosition(globalPosition.X, globalPosition.Y);
			return;
		}

		var pointerPosition = GetViewportPointerPositionFromGlobal(globalPosition);
		GenericUtils.SetPointerPosition(pointerPosition.X, pointerPosition.Y);
	}

	Vector2 GetViewportPointerPositionFromGlobal(Vector2 globalPosition)
	{
		if (scrollContainer == null)
			return globalPosition;
		var rect = scrollContainer.GetGlobalRect();
		var contentPosition = globalPosition - rect.Position;
		if (contentScale > 0.001f)
			contentPosition /= contentScale;
		return contentPosition;
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

		if (button.Size.X <= 0 || button.Size.Y <= 0)
		{
			UpdatePointerPosition(fallbackGlobalPosition);
			return;
		}

		var globalCenter = button.GetGlobalTransformWithCanvas() * (button.Size * 0.5f);
		UpdatePointerPosition(globalCenter);
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

		if (!IsContentHorizontalScrollAvailable())
			delta.X = 0;
		int oldHorizontal = scrollContainer.ScrollHorizontal;
		int oldVertical = scrollContainer.ScrollVertical;
		int nextHorizontal = NormalizeContentHorizontalScroll(Mathf.Clamp(oldHorizontal + Mathf.RoundToInt(delta.X), 0, GetMaxContentHorizontalScroll()));
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
		int targetHorizontal = NormalizeContentHorizontalScroll(Mathf.Clamp(desiredContentScrollHorizontal, 0, limit.X));
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
		if (!IsContentHorizontalScrollAvailable())
		{
			rawScrollDelta.X = 0;
			appliedDelta.X = 0;
			contentScrollVelocity.X = 0;
			contentInertiaRemainder.X = 0;
		}

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
		if (!IsContentHorizontalScrollAvailable())
		{
			contentScrollVelocity.X = 0;
			contentInertiaRemainder.X = 0;
		}

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

		if (!IsContentHorizontalScrollAvailable())
		{
			contentScrollVelocity.X = 0;
			contentInertiaRemainder.X = 0;
		}

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
			GetHorizontalScrollLimit(scaledSize, scrollSize),
			Mathf.CeilToInt(Mathf.Max(0.0f, scaledSize.Y - scrollSize.Y)));
	}

	int GetHorizontalScrollLimit(Vector2 scaledSize, Vector2 scrollSize)
	{
		float overflow = scaledSize.X - scrollSize.X;
		if (overflow <= ContentHorizontalScrollTolerancePx)
			return 0;
		return Mathf.CeilToInt(overflow);
	}

	// Submit an empty input for enter/any-key waits and profile-approved pointer input loops.
	// mouseVk: 以 from_button=true 送入时由 EmueraThread 写入对应的 RESULT_ARRAY[1] 鼠标码。
	bool TryAdvanceTap(bool acceptEvent, int mouseVk = 0x01)
	{
		var console = GlobalStatic.Console;
		bool isNonLeft = mouseVk != 0x01;
		// 左键：通常只推进 EnterKey/AnyKey；eraFL 的 INPUTS 鼠标循环额外允许空白输入。
		// 右/中键：INPUT 等待也送入，让游戏通过 RESULT:1 区分技能、快捷键等用途。
		if (console == null)
			return false;
		bool submitBlankLeftDefault = ShouldSubmitBlankLeftClickDefault(console, mouseVk);
		bool submitEraFlBlankString = ShouldSubmitEraFlBlankPointerString(console, mouseVk);
		if (!console.IsWaitingEnterKey && !console.IsWaitAnyKey && !(isNonLeft && console.IsWaitingInput)
			&& !submitBlankLeftDefault && !submitEraFlBlankString)
			return false;

		uint nowTick = MinorShift._Library.WinmmTimer.TickCount;
		bool skipFlag = (nowTick - lastClickTick < 200);
		if (GenericUtils.IsScrollTraceActive)
		{
			TraceScroll("advance_tap", () => $"skip={skipFlag} vk={mouseVk}");
			GenericUtils.StartScrollTraceCoreWindow(() => $"advance_tap skip={skipFlag} vk={mouseVk}");
		}
		// 右键/中键を空エリアでタップした場合は from_button=true で送信。
		// これにより EmueraThread の IsWaitingInputSomething ガードを通過し、
		// RESULT_ARRAY[1] に正しいボタンコードが書き込まれる。
		bool fromButton = mouseVk != 0x01 || submitBlankLeftDefault || submitEraFlBlankString;
		EmueraThread.instance.Input("", fromButton, skipFlag, fromButton ? mouseVk : 0);
		lastClickTick = nowTick;
		return true;
	}

	static bool ShouldSubmitEraFlBlankPointerString(MinorShift.Emuera.GameView.EmueraConsole console, int mouseVk)
	{
		return Program.Compatibility.EraFl.ShouldSubmitBlankPointerStringInput(
			mouseVk,
			console != null && console.IsWaitingInput
				&& console.InputType == MinorShift.Emuera.GameProc.InputType.StrValue);
	}

	static bool ShouldSubmitBlankLeftClickDefault(MinorShift.Emuera.GameView.EmueraConsole console, int mouseVk)
	{
		// eraTW/snake 的泡茶展开面板用 INPUT -1 等待下一次点击。
		// 原生空白左键会提交默认值 -1 并写入 RESULT:1=1，从而关闭浮层；
		// 普通数值输入没有默认值时仍不吞空白点击，避免误提交。
		return mouseVk == 0x01
			&& console != null
			&& console.IsWaitingDefaultableIntValue;
	}

	// Clear ordinary drag/tap tracking state.
	void ResetContentDragState()
	{
		SetControlButtonSelected(contentDragButton, false);
		contentDragActive = false;
		contentDragMoved = false;
		contentDragStartedOnButton = false;
		contentDragButton = null;
		contentDragButtonInput = null;
		contentDragButtonGeneration = 0;
		contentDragButtonContentCenterValid = false;
		contentDragMouseVk = 0x01;
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

	// Identify left mouse, right/middle mouse, or screen-touch release events.
	static bool IsPointerRelease(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb &&
			(mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right || mb.ButtonIndex == MouseButton.Middle))
			return !mb.Pressed;
		if (@event is InputEventScreenTouch touch)
			return !touch.Pressed;
		return false;
	}

	// Normalize Godot mouse and screen touch/drag events into one pointer shape.
	// mouseVk: 0x01 左键 / 0x02 右键 / 0x04 中键 / -1 触控（虚拟光标模式关闭时按左键处理）
	static bool TryGetPointer(InputEvent @event, out Vector2 position, out bool pressed, out bool released, out bool motion, out int mouseVk)
	{
		position = Vector2.Zero;
		pressed = false;
		released = false;
		motion = false;
		mouseVk = -1;

		if (@event is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Left) mouseVk = 0x01;
			else if (mb.ButtonIndex == MouseButton.Right) mouseVk = 0x02;
			else if (mb.ButtonIndex == MouseButton.Middle) mouseVk = 0x04;
			else return false;
			position = mb.GlobalPosition;
			pressed = mb.Pressed;
			released = !mb.Pressed;
			return true;
		}
		if (@event is InputEventMouseMotion mm)
		{
			position = mm.GlobalPosition;
			motion = true;
			mouseVk = 0x01; // motion 不区分按键，沿用左键
			return true;
		}
		if (@event is InputEventScreenTouch touch)
		{
			position = touch.Position;
			pressed = touch.Pressed;
			released = !touch.Pressed;
			mouseVk = -1; // 触控事件本身不带按键语义，由虚拟光标模式的手势独立判定
			return true;
		}
		if (@event is InputEventScreenDrag drag)
		{
			position = drag.Position;
			motion = true;
			mouseVk = -1;
			return true;
		}
		return false;
	}

	// 兼容旧调用点（不需要 mouseVk）的5参数版本。
	static bool TryGetPointer(InputEvent @event, out Vector2 position, out bool pressed, out bool released, out bool motion)
	{
		return TryGetPointer(@event, out position, out pressed, out released, out motion, out _);
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

	static bool TryGetSolidBlockElementRect(char value, float cellWidth, float lineHeight, float fontHeight, out Rect2 rect)
	{
		// 不再把 U+2580-U+259F Block Elements 合成为 Godot 矩形。
		// eraTW 的老虎机等 AA 画面依赖 MS Gothic 字形本身的细缝、抗锯齿和纵横比例；
		// 这里如果手动画 Rect，布局虽然对齐，但会把字形变成硬边色块，和原生 Emuera 差异很大。
		// 横向占位仍由 Utils.CheckHalfSize 决定，因此 PRINT_COLORBAR/ASCII Art 的列推进保持不变。
		rect = default;
		return false;
	}

	static bool IsBlockElementChar(char value)
	{
		return value >= '\u2580' && value <= '\u259F';
	}

	sealed partial class ConsoleColorRectPart : ColorRect
	{
		readonly Color normalColor;
		readonly Color selectedColor;
		bool selected;

		public ConsoleColorRectPart(Color normalColor, Color selectedColor)
		{
			this.normalColor = normalColor;
			this.selectedColor = selectedColor;
			Color = normalColor;
			MouseFilter = MouseFilterEnum.Ignore;
		}

		public void SetSelected(bool value)
		{
			if (selected == value)
				return;
			selected = value;
			Color = selected ? selectedColor : normalColor;
		}
	}

	sealed partial class ConsoleTextPart : Control
	{
		readonly Font font;
		readonly int fontSize;
		readonly FontVerticalAlign? verticalAlign;
		readonly Color color;
		readonly Color selectedColor;
		readonly bool bold;
		readonly string text;
		Vector2 fixedSize;
		bool selected;

		public ConsoleTextPart(Font font, int fontSize, Color color, Color selectedColor, bool bold, string text, FontVerticalAlign? verticalAlign = null)
		{
			this.font = font;
			this.fontSize = fontSize > 0 ? fontSize : 18;
			this.color = color;
			this.selectedColor = selectedColor;
			this.bold = bold;
			this.verticalAlign = verticalAlign;
			this.text = uEmuera.Utils.StripZeroWidth(text) ?? "";
			MouseFilter = MouseFilterEnum.Ignore;
			ClipContents = true;
		}

		public void SetSelected(bool value)
		{
			if (selected == value)
				return;
			selected = value;
			QueueRedraw();
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

			float fontHeight = font.GetHeight(fontSize);
			float baseline = GetTextBaseline(font, fontSize, Size.Y, fontHeight, verticalAlign);
			Color drawColor = selected ? selectedColor : color;
			if (selected && Config.UseButtonFocusBackgroundColor && !string.IsNullOrWhiteSpace(text))
				DrawRect(new Rect2(Vector2.Zero, Size), new Color(50.0f / 255.0f, 50.0f / 255.0f, 50.0f / 255.0f, 1.0f));
			if (!ShouldUseGridDrawing(text))
			{
				DrawPlainText(baseline, drawColor);
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
				DrawGridChar(text[i], drawX, 0, baseline, cellWidth, fontHeight, drawColor);
				exactX = nextExactX;
				drawX = nextDrawX;
			}
		}

		void DrawPlainText(float baseline, Color drawColor)
		{
			float drawWidth = System.Math.Max(1.0f, Size.X);
			DrawString(font, new Vector2(0, baseline), text, HorizontalAlignment.Left, drawWidth, fontSize, drawColor);
			if (bold)
				DrawString(font, new Vector2(1.0f, baseline), text, HorizontalAlignment.Left, System.Math.Max(1.0f, drawWidth - 1.0f), fontSize, drawColor);
		}

		static float GetTextBaseline(Font font, int fontSize, float height, float fontHeight = -1.0f, FontVerticalAlign? verticalAlign = null)
		{
			if (fontHeight < 0.0f)
				fontHeight = font.GetHeight(fontSize);
			float freeSpace = height - fontHeight;
			float alignmentOffset = verticalAlign switch
			{
				FontVerticalAlign.Top => 0.0f,
				FontVerticalAlign.Middle => freeSpace * 0.5f,
				FontVerticalAlign.Bottom => freeSpace,
				_ => freeSpace * 0.5f,
			};
			float ascent = font.GetAscent(fontSize);
			return Mathf.Round(alignmentOffset + ascent);
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

		void DrawGridChar(char value, float x, float lineTop, float baseline, float cellWidth, float fontHeight, Color drawColor)
		{
			if (TryGetSolidBlockElementRect(value, cellWidth, Size.Y, fontHeight, out var blockRect))
			{
				DrawRect(new Rect2(x + blockRect.Position.X, lineTop + blockRect.Position.Y, blockRect.Size.X, blockRect.Size.Y), drawColor);
				return;
			}
			// 每个字符仍按 emuera 的网格起点绘制。箱线字符不能按单元格宽度裁剪字形，
			// Godot 字体 fallback 下，DRAWLINE/箱线字符的实际 glyph 往往宽于半角格；
			// 若逐格裁剪会出现横线缺失。片段边界继续由本 Control 的 ClipContents 统一限制。
			// 但 U+2580-U+259F 块字符在 TW 老虎机中被当作连续半角像素块使用，若也放宽到整段宽度，
			// 一个 ▉ 会横向盖到后续 ; 背景格，所以下面仅对 Block Elements 收紧到当前格宽。
			string glyph = value.ToString();
			float drawWidth = IsBlockElementChar(value)
				? System.Math.Max(1.0f, cellWidth)
				: System.Math.Max(1.0f, System.Math.Max(cellWidth, Size.X - x));
			DrawString(font, new Vector2(x, baseline), glyph, HorizontalAlignment.Left, drawWidth, fontSize, drawColor);
			if (bold)
				DrawString(font, new Vector2(x + 1.0f, baseline), glyph, HorizontalAlignment.Left, System.Math.Max(1.0f, drawWidth - 1.0f), fontSize, drawColor);
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

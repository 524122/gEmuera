using Godot;
using GEmuera.Core.Compatibility;
using System.Collections.Generic;
using System.IO;

public partial class FirstWindow : Control
{
	const string ProjectGitHubUrl = "https://github.com/wwwXiaoHan17/gEmuera";
	const string FeedbackQqGroup = "413675556";
	const string LauncherSettingsPath = "user://launcher.cfg";
	const string LauncherSettingsSection = "launcher";
	const string LauncherLastGamePathKey = "last_game_path";
	const string LauncherLastCoreProfileKey = "last_core_profile";
	const string LauncherAdvancedCompatibilityKey = "advanced_compatibility";
	const string LauncherManualCoreProfileKey = "manual_core_profile";
	const string CompatibilityDirectoryName = "compat";
	const string SnakeDirectoryName = "snake";
	const int LauncherScrollBarWidth = 24;
	const int LauncherBaseMarginLeft = 20;
	const int LauncherBaseMarginTop = 22;
	const int LauncherBaseMarginRight = 20;
	const int LauncherBaseMarginBottom = 22;
	// 启动器只做固定深度的目录枚举。上限既防止异常目录拖慢 Android 首屏，
	// 也避免把扫描器重新演化成递归的游戏内容探测器。
	const int MaxLauncherGameEntries = 512;
	const int MaxCompatibilityProfilesPerRoot = 32;
	const int MaxGamesPerCompatibilityProfile = 64;
	// 扫描根下允许的额外嵌套深度：emuera/snake/归档目录/游戏（深度 1 的
	// 嵌套）。再深的归档结构不扫描，避免把游戏内部子目录误识别为独立游戏，
	// 也避免大目录树扫描拖慢启动器。
	const int MaxLauncherScanDepth = 2;
	const int MaxScanMessages = 6;
	public const string CoreProfileV24Pure = "v24pure";
	public const string CoreProfileSnake = "snake";
	public const string CoreProfileEraFl = "erafl";
	// 保留旧配置值，避免升级时无法读取 launcher.cfg；启动器不再执行自动探测。
	public const string CoreProfileAutomatic = "auto";

	enum LauncherGameCategory
	{
		V24Pure,
		Snake
	}

	enum LauncherGameSource
	{
		V24Root,
		SnakeRoot,
		CompatibilityDirectory
	}

	/// <summary>
	/// 启动器拥有游戏路径、目录路由得出的 profile 与展示来源；ItemList 只保存显示/选中状态。
	/// 不能再由当前标签或游戏名反推 profile，否则 compat/&lt;profile&gt; 会被错误地按 v24 启动。
	/// </summary>
	sealed record LauncherGameEntry(
		string DisplayName,
		string GameRoot,
		string ProfileId,
		LauncherGameSource Source);

	enum LauncherTab
	{
		V24Pure,
		Snake,
		Announcement
	}

	public static string SelectedGamePath { get; private set; }
	public static string SelectedCoreProfileName { get; private set; } = CoreProfileV24Pure;
	public static bool AdvancedCompatibilityEnabled { get; private set; }
	public static string ManualCoreProfileName { get; private set; } = CoreProfileV24Pure;
	static readonly CompatibilityProfileCatalog DirectoryRouteProfileCatalog = CreateDirectoryRouteProfileCatalog();

	/// <summary>
	/// M0 baseline runner-only session injection. The normal launcher never calls this method.
	/// Unlike SetSelectedGamePath, this does not persist launcher.cfg and therefore cannot
	/// change the next interactive startup.
	/// </summary>
	public static bool ConfigureM0RunnerSession(string path, string coreProfileName, out string errorMessage)
	{
		errorMessage = "";
		if (!IsUsableEraGameDirectory(path))
		{
			errorMessage = "invalid_era_game_directory";
			return false;
		}

		if (!TryNormalizeCoreProfileName(coreProfileName, out string normalizedProfileName))
		{
			errorMessage = "unsupported_compatibility_profile";
			return false;
		}

		SelectedGamePath = path.TrimEnd('/', '\\');
		SelectedCoreProfileName = normalizedProfileName;
		return true;
	}

	ItemList gameList;
	Button startButton;
	Label statusLabel;
	Label categoryHintLabel;
	Label announcementStatusLabel;
	CheckButton advancedCompatibilityToggle;
	OptionButton compatibilityProfileOption;
	MarginContainer launcherMargin;
	Button v24TabButton;
	Button snakeTabButton;
	Button announcementTabButton;
	Control gameTabContent;
	Control announcementTabContent;
	Tween tabFadeTween;
	LauncherGameCategory currentCategory = LauncherGameCategory.V24Pure;
	LauncherTab currentTab = LauncherTab.V24Pure;
	readonly List<LauncherGameEntry> gameEntries = new();
	bool androidPermissionCheckPending = false;
	bool androidPermissionResultReceived = false;

	public override void _Ready()
	{
		FrameRateHelper.Apply();
		ResolutionHelper.Apply();

		// 企业级说明：Android APK 首屏可能停留在启动器和权限流程，尚未进入 EmueraMain。
		// 这里提前初始化诊断系统，确保冷启动、权限失败和游戏路径选择都能写入 breadcrumb。
		GenericUtils.SetMainThread();
		GenericUtils.InitializeLogging();

		BuildLauncherUi();
		ApplyLauncherSafeArea();
		GetViewport().SizeChanged += OnViewportSizeChanged;

		if (OS.GetName() == "Android")
		{
			GetTree().OnRequestPermissionsResult += OnPermissionsResult;
			androidPermissionCheckPending = true;
			statusLabel.Text = "正在请求文件权限...\nRequesting file permissions...";
			OS.RequestPermissions();
			GetTree().CreateTimer(2.5).Timeout += OnPermissionRequestFallbackTimeout;
		}
		else
		{
			ScanGames();
		}
	}

	void BuildLauncherUi()
	{
		var background = new ColorRect();
		background.Color = new Color(0.075f, 0.083f, 0.088f);
		background.MouseFilter = MouseFilterEnum.Ignore;
		background.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(background);

		launcherMargin = new MarginContainer();
		launcherMargin.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(launcherMargin);

		var root = new VBoxContainer();
		root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		root.SizeFlagsVertical = SizeFlags.ExpandFill;
		root.AddThemeConstantOverride("separation", 10);
		launcherMargin.AddChild(root);

		root.AddChild(CreateHeader());
		root.AddChild(CreateLauncherTabs());

		statusLabel = new Label();
		statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		statusLabel.AddThemeColorOverride("font_color", new Color(0.78f, 0.84f, 0.9f));
		statusLabel.AddThemeFontSizeOverride("font_size", 14);
		root.AddChild(statusLabel);
	}

	void OnViewportSizeChanged()
	{
		ApplyLauncherSafeArea();
	}

	void ApplyLauncherSafeArea()
	{
		if (launcherMargin == null)
			return;

		Rect2 safeRect = EmueraContent.GetSafeViewportRect(GetViewport());
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		int left = LauncherBaseMarginLeft + Mathf.RoundToInt(System.Math.Max(0, safeRect.Position.X));
		int top = LauncherBaseMarginTop + Mathf.RoundToInt(System.Math.Max(0, safeRect.Position.Y));
		int right = LauncherBaseMarginRight + Mathf.RoundToInt(System.Math.Max(0, viewportSize.X - (safeRect.Position.X + safeRect.Size.X)));
		int bottom = LauncherBaseMarginBottom + Mathf.RoundToInt(System.Math.Max(0, viewportSize.Y - (safeRect.Position.Y + safeRect.Size.Y)));

		launcherMargin.AddThemeConstantOverride("margin_left", left);
		launcherMargin.AddThemeConstantOverride("margin_top", top);
		launcherMargin.AddThemeConstantOverride("margin_right", right);
		launcherMargin.AddThemeConstantOverride("margin_bottom", bottom);
	}

	Control CreateHeader()
	{
		var header = new HBoxContainer();
		header.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddThemeConstantOverride("separation", 12);

		var titleBlock = new VBoxContainer();
		titleBlock.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddChild(titleBlock);

		var title = new Label();
		title.Text = MultiLanguage.Get("FirstWindow.Title", "gEmuera(Emuera for Godot)");
		title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		title.AddThemeFontSizeOverride("font_size", 28);
		title.AddThemeColorOverride("font_color", new Color(0.96f, 0.98f, 1.0f));
		titleBlock.AddChild(title);

		return header;
	}

	Control CreateLauncherTabs()
	{
		var panel = CreatePanel(new Color(0.105f, 0.118f, 0.118f), new Color(0.2f, 0.25f, 0.28f), true);
		panel.SizeFlagsVertical = SizeFlags.ExpandFill;

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		panel.AddChild(margin);

		var body = new HBoxContainer();
		body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		body.SizeFlagsVertical = SizeFlags.ExpandFill;
		body.AddThemeConstantOverride("separation", 12);
		margin.AddChild(body);

		body.AddChild(CreateTabRail());

		var contentStack = new Control();
		contentStack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		contentStack.SizeFlagsVertical = SizeFlags.ExpandFill;
		body.AddChild(contentStack);

		gameTabContent = CreateGameContent();
		gameTabContent.SetAnchorsPreset(LayoutPreset.FullRect);
		contentStack.AddChild(gameTabContent);

		announcementTabContent = CreateAnnouncementContent();
		announcementTabContent.SetAnchorsPreset(LayoutPreset.FullRect);
		announcementTabContent.Visible = false;
		contentStack.AddChild(announcementTabContent);

		UpdateTabButtonStyles();
		return panel;
	}

	Control CreateTabRail()
	{
		var rail = new VBoxContainer();
		rail.CustomMinimumSize = new Vector2(88, 0);
		rail.SizeFlagsVertical = SizeFlags.ExpandFill;
		rail.AddThemeConstantOverride("separation", 8);

		v24TabButton = CreateRailButton("v24", () => SelectLauncherTab(LauncherTab.V24Pure));
		snakeTabButton = CreateRailButton("snake", () => SelectLauncherTab(LauncherTab.Snake));
		announcementTabButton = CreateRailButton(MultiLanguage.Get("FirstWindow.NoticeButton", "公告"), () => SelectLauncherTab(LauncherTab.Announcement));

		rail.AddChild(v24TabButton);
		rail.AddChild(snakeTabButton);
		rail.AddChild(announcementTabButton);

		var spacer = new Control();
		spacer.SizeFlagsVertical = SizeFlags.ExpandFill;
		rail.AddChild(spacer);

		return rail;
	}

	Button CreateRailButton(string text, System.Action pressed)
	{
		var button = new Button();
		button.Text = text;
		button.CustomMinimumSize = new Vector2(88, 58);
		button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		button.AddThemeFontSizeOverride("font_size", text.Length > 4 ? 15 : 17);
		button.Pressed += pressed;
		button.ButtonDown += () => AnimateButtonScale(button, 0.97f);
		button.ButtonUp += () => AnimateButtonScale(button, 1.0f);
		button.MouseExited += () => AnimateButtonScale(button, 1.0f);
		return button;
	}

	void SelectLauncherTab(LauncherTab tab)
	{
		if (currentTab == tab && tab != LauncherTab.Announcement)
			return;

		currentTab = tab;
		if (tab == LauncherTab.Snake)
			currentCategory = LauncherGameCategory.Snake;
		else if (tab == LauncherTab.V24Pure)
			currentCategory = LauncherGameCategory.V24Pure;

		bool showingAnnouncement = tab == LauncherTab.Announcement;
		gameTabContent.Visible = !showingAnnouncement;
		announcementTabContent.Visible = showingAnnouncement;
		UpdateTabButtonStyles();

		if (showingAnnouncement)
			FadeInContent(announcementTabContent);
		else
		{
			UpdateCategoryHint();
			ScanGames();
			FadeInContent(gameTabContent);
		}
	}

	void UpdateTabButtonStyles()
	{
		ApplyRailButtonStyle(v24TabButton, currentTab == LauncherTab.V24Pure);
		ApplyRailButtonStyle(snakeTabButton, currentTab == LauncherTab.Snake);
		ApplyRailButtonStyle(announcementTabButton, currentTab == LauncherTab.Announcement);
	}

	void ApplyRailButtonStyle(Button button, bool active)
	{
		if (button == null)
			return;

		button.AddThemeStyleboxOverride("normal", CreateRailButtonStyle(active, false));
		button.AddThemeStyleboxOverride("hover", CreateRailButtonStyle(true, false));
		button.AddThemeStyleboxOverride("pressed", CreateRailButtonStyle(active, true));
		button.AddThemeStyleboxOverride("focus", CreateRailButtonStyle(true, false));
		button.AddThemeColorOverride("font_color", active ? new Color(0.98f, 0.98f, 0.94f) : new Color(0.74f, 0.79f, 0.8f));
		button.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.97f, 0.86f));
		button.AddThemeColorOverride("font_pressed_color", new Color(0.98f, 0.95f, 0.82f));
	}

	StyleBoxFlat CreateRailButtonStyle(bool active, bool pressed)
	{
		var style = new StyleBoxFlat();
		style.BgColor = active ? new Color(0.22f, 0.27f, 0.25f) : new Color(0.12f, 0.14f, 0.14f);
		style.BorderColor = active ? new Color(0.52f, 0.58f, 0.42f) : new Color(0.18f, 0.21f, 0.21f);
		style.SetBorderWidthAll(active ? 1 : 0);
		style.SetCornerRadiusAll(8);
		style.ContentMarginLeft = 8;
		style.ContentMarginRight = 8;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;
		style.ShadowColor = active ? new Color(0, 0, 0, 0.35f) : new Color(0, 0, 0, 0.2f);
		style.ShadowSize = pressed ? 1 : active ? 5 : 2;
		style.ShadowOffset = new Vector2(0, pressed ? 1 : 3);
		return style;
	}

	void AnimateButtonScale(Button button, float targetScale)
	{
		if (button == null)
			return;

		button.PivotOffset = button.Size * 0.5f;
		var tween = CreateTween();
		tween.BindNode(button);
		tween.SetTrans(Tween.TransitionType.Cubic);
		tween.SetEase(Tween.EaseType.Out);
		tween.TweenProperty(button, "scale", new Vector2(targetScale, targetScale), 0.08);
	}

	void FadeInContent(Control content)
	{
		if (content == null)
			return;
		if (tabFadeTween != null && tabFadeTween.IsRunning())
			tabFadeTween.Kill();

		content.Modulate = new Color(1, 1, 1, 0.72f);
		tabFadeTween = CreateTween();
		tabFadeTween.BindNode(content);
		tabFadeTween.SetTrans(Tween.TransitionType.Cubic);
		tabFadeTween.SetEase(Tween.EaseType.Out);
		tabFadeTween.TweenProperty(content, "modulate:a", 1.0, 0.16);
	}

	Control CreateNoticeTab()
	{
		var content = CreateDialogTab(MultiLanguage.Get("FirstWindow.NoticeTitle", "公告"));

		var body = CreateDialogText(MultiLanguage.Get("FirstWindow.NoticeBody",
			"游戏放置说明:\n\n"
			+ "新版蛇 TW 请放入 snake 文件夹下。详细路径: Android 为 /storage/emulated/0/emuera/snake/你的游戏文件夹；Windows 或编辑器测试时，为程序目录或项目目录下的 snake/你的游戏文件夹。放好后从左侧 snake 标签启动，会使用 snake 核心。\n\n"
			+ "旧版蛇 TW 和其他 era 游戏请放入 emuera 文件夹下。详细路径: Android 为 /storage/emulated/0/emuera/你的游戏文件夹；Windows 或编辑器测试时，为程序目录或项目目录下的你的游戏文件夹。放好后从左侧 v24 标签启动。\n\n"
			+ "如果出现 v24 无法启动、解析报错、资源路径异常等情况，可以把同一个游戏文件夹移动到 snake 文件夹下，再从 snake 标签启动，尝试放入 snake 核心。\n\n"
			+ "eraFL 或其他独立 profile 请放入 compat/<profile>/游戏文件夹，例如 compat/erafl/eraFL0.48；它们仍显示在 v24 标签，但会按目录自动选择对应模块。启动器不会读取游戏内容自动识别类型。高级兼容模式只用于诊断或临时覆盖。\n\n"
			+ "每个游戏文件夹内通常需要包含 ERB 文件夹，并至少包含 CSV、DAT 或 resources 其中之一。"));
		content.AddChild(body);

		return content;
	}

	Control CreateFeedbackTab()
	{
		var content = CreateDialogTab(MultiLanguage.Get("FirstWindow.FeedbackTab", "反馈"));

		content.AddChild(CreateDialogText(MultiLanguage.Get("FirstWindow.FeedbackBody", "遇到 bug 可以进群反馈，反馈时尽量带上游戏名、操作步骤和报错截图。")));

		var qqField = new LineEdit();
		qqField.Text = FeedbackQqGroup;
		qqField.Editable = false;
		qqField.SelectAllOnFocus = true;
		qqField.CustomMinimumSize = new Vector2(120, 36);
		qqField.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		qqField.AddThemeFontSizeOverride("font_size", 16);
		qqField.TooltipText = MultiLanguage.Get("FirstWindow.QQTooltip", "QQ群号，可选中复制");
		content.AddChild(qqField);

		var copyButton = new Button();
		copyButton.Text = MultiLanguage.Get("FirstWindow.CopyQQ", "复制群号");
		copyButton.CustomMinimumSize = new Vector2(0, 40);
		copyButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		copyButton.Pressed += CopyFeedbackGroup;
		content.AddChild(copyButton);

		announcementStatusLabel = new Label();
		announcementStatusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		announcementStatusLabel.AddThemeFontSizeOverride("font_size", 13);
		announcementStatusLabel.AddThemeColorOverride("font_color", new Color(0.67f, 0.76f, 0.84f));
		content.AddChild(announcementStatusLabel);

		return content;
	}

	Control CreateProjectTab()
	{
		var content = CreateDialogTab(MultiLanguage.Get("FirstWindow.ProjectTab", "项目"));

		content.AddChild(CreateDialogText(MultiLanguage.Get("FirstWindow.Author", "Author: 恋雨朦胧/xiao_han17")));

		var githubButton = new LinkButton();
		githubButton.Text = MultiLanguage.Get("FirstWindow.GitHub", $"GitHub: {ProjectGitHubUrl}");
		githubButton.TooltipText = ProjectGitHubUrl;
		githubButton.AddThemeFontSizeOverride("font_size", 16);
		githubButton.Pressed += () => OS.ShellOpen(ProjectGitHubUrl);
		content.AddChild(githubButton);

		return content;
	}

	VBoxContainer CreateDialogTab(string name)
	{
		var content = new VBoxContainer();
		content.Name = name;
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		content.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		content.AddThemeConstantOverride("separation", 12);
		return content;
	}

	Label CreateDialogText(string text)
	{
		var label = new Label();
		label.Text = text;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.AddThemeFontSizeOverride("font_size", 15);
		label.AddThemeColorOverride("font_color", new Color(0.88f, 0.92f, 0.96f));
		return label;
	}

	Control CreateAnnouncementContent()
	{
		var scroll = new ScrollContainer();
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		ApplyWideVerticalScrollbar(scroll.GetVScrollBar());

		var content = new VBoxContainer();
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		content.AddThemeConstantOverride("separation", 14);
		scroll.AddChild(content);

		content.AddChild(CreateSectionTitle(MultiLanguage.Get("FirstWindow.NoticeTitle", "公告")));
		content.AddChild(CreateNoticeTab());
		content.AddChild(CreateFeedbackTab());
		content.AddChild(CreateProjectTab());

		return scroll;
	}

	Control CreateGameContent()
	{
		var content = new VBoxContainer();
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		content.SizeFlagsVertical = SizeFlags.ExpandFill;
		content.AddThemeConstantOverride("separation", 12);

		content.AddChild(CreateSectionTitle(MultiLanguage.Get("FirstWindow.SelectGame", "选择游戏")));

		categoryHintLabel = new Label();
		categoryHintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		categoryHintLabel.AddThemeFontSizeOverride("font_size", 13);
		categoryHintLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.78f, 0.86f));
		content.AddChild(categoryHintLabel);
		UpdateCategoryHint();
		content.AddChild(CreateCompatibilitySettings());

		gameList = new ItemList();
		gameList.SizeFlagsVertical = SizeFlags.ExpandFill;
		gameList.CustomMinimumSize = new Vector2(0, 260);
		gameList.AddThemeFontSizeOverride("font_size", 20);
		gameList.AddThemeConstantOverride("v_separation", 12);
		gameList.AddThemeConstantOverride("line_separation", 8);
		ApplyWideVerticalScrollbar(gameList.GetVScrollBar());
		gameList.ItemSelected += OnGameSelected;
		gameList.ItemActivated += OnGameActivated;
		content.AddChild(gameList);

		startButton = new Button();
		startButton.Text = MultiLanguage.Get("FirstWindow.Start", "Start");
		startButton.Disabled = true;
		startButton.CustomMinimumSize = new Vector2(0, 52);
		startButton.AddThemeFontSizeOverride("font_size", 18);
		ApplyPrimaryButtonStyle(startButton);
		startButton.Pressed += OnStartPressed;
		content.AddChild(startButton);

		return content;
	}

	Control CreateCompatibilitySettings()
	{
		LoadCompatibilitySettings();

		var settings = new VBoxContainer();
		settings.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		settings.AddThemeConstantOverride("separation", 6);

		advancedCompatibilityToggle = new CheckButton();
		advancedCompatibilityToggle.Text = MultiLanguage.Get(
			"FirstWindow.AdvancedCompatibility",
			"高级兼容模式");
		advancedCompatibilityToggle.TooltipText = MultiLanguage.Get(
			"FirstWindow.AdvancedCompatibilityTooltip",
			"开启后可手动覆盖本次启动的兼容 profile；关闭时严格使用游戏目录路由。");
		advancedCompatibilityToggle.ButtonPressed = AdvancedCompatibilityEnabled;
		advancedCompatibilityToggle.CustomMinimumSize = new Vector2(0, 44);
		advancedCompatibilityToggle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		advancedCompatibilityToggle.AddThemeFontSizeOverride("font_size", 15);
		advancedCompatibilityToggle.Toggled += OnAdvancedCompatibilityToggled;
		settings.AddChild(advancedCompatibilityToggle);

		compatibilityProfileOption = new OptionButton();
		compatibilityProfileOption.TooltipText = MultiLanguage.Get(
			"FirstWindow.CompatibilityProfileTooltip",
			"选择后将在下一次启动游戏时生效。");
		compatibilityProfileOption.CustomMinimumSize = new Vector2(180, 44);
		compatibilityProfileOption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		compatibilityProfileOption.AddThemeFontSizeOverride("font_size", 15);
		compatibilityProfileOption.AddItem(CoreProfileV24Pure);
		compatibilityProfileOption.AddItem(CoreProfileSnake);
		compatibilityProfileOption.AddItem(CoreProfileEraFl);
		compatibilityProfileOption.Select(GetManualProfileOptionIndex());
		compatibilityProfileOption.ItemSelected += OnManualProfileSelected;
		compatibilityProfileOption.Visible = AdvancedCompatibilityEnabled;
		settings.AddChild(compatibilityProfileOption);

		return settings;
	}

	void OnAdvancedCompatibilityToggled(bool enabled)
	{
		AdvancedCompatibilityEnabled = enabled;
		if (!enabled)
			ManualCoreProfileName = CoreProfileV24Pure;
		SaveCompatibilitySettings();
		if (compatibilityProfileOption != null)
			compatibilityProfileOption.Visible = enabled;
		UpdateSelectedGameCompatibilityHint();
	}

	void OnManualProfileSelected(long index)
	{
		ManualCoreProfileName = index switch
		{
			0 => CoreProfileV24Pure,
			1 => CoreProfileSnake,
			2 => CoreProfileEraFl,
			_ => CoreProfileV24Pure,
		};
		SaveCompatibilitySettings();
		UpdateSelectedGameCompatibilityHint();
	}

	static void LoadCompatibilitySettings()
	{
		var config = new ConfigFile();
		if (config.Load(LauncherSettingsPath) != Error.Ok)
			return;

		AdvancedCompatibilityEnabled = config.GetValue(
			LauncherSettingsSection,
			LauncherAdvancedCompatibilityKey,
			false).AsBool();
		ManualCoreProfileName = NormalizeManualCoreProfileName(config.GetValue(
			LauncherSettingsSection,
			LauncherManualCoreProfileKey,
			CoreProfileV24Pure).AsString());
	}

	static void SaveCompatibilitySettings()
	{
		var config = new ConfigFile();
		config.Load(LauncherSettingsPath);
		config.SetValue(LauncherSettingsSection, LauncherAdvancedCompatibilityKey, AdvancedCompatibilityEnabled);
		config.SetValue(LauncherSettingsSection, LauncherManualCoreProfileKey, ManualCoreProfileName);
		config.Save(LauncherSettingsPath);
	}

	static string NormalizeManualCoreProfileName(string profileName)
	{
		if (string.Equals(profileName, CoreProfileV24Pure, System.StringComparison.OrdinalIgnoreCase))
			return CoreProfileV24Pure;
		if (string.Equals(profileName, CoreProfileSnake, System.StringComparison.OrdinalIgnoreCase))
			return CoreProfileSnake;
		if (string.Equals(profileName, CoreProfileEraFl, System.StringComparison.OrdinalIgnoreCase))
			return CoreProfileEraFl;
		// 旧版本曾把“自动识别”写入配置，回退后按安全的 v24 基线处理。
		return CoreProfileV24Pure;
	}

	int GetManualProfileOptionIndex()
	{
		return ManualCoreProfileName switch
		{
			CoreProfileSnake => 1,
			CoreProfileEraFl => 2,
			_ => 0,
		};
	}

	PanelContainer CreatePanel(Color backgroundColor, Color borderColor, bool shadow = false)
	{
		var panel = new PanelContainer();
		panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		panel.AddThemeStyleboxOverride("panel", CreatePanelStyle(backgroundColor, borderColor, shadow));
		return panel;
	}

	VBoxContainer CreatePanelContent(PanelContainer panel, int marginSize)
	{
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", marginSize);
		margin.AddThemeConstantOverride("margin_top", marginSize);
		margin.AddThemeConstantOverride("margin_right", marginSize);
		margin.AddThemeConstantOverride("margin_bottom", marginSize);
		panel.AddChild(margin);

		var content = new VBoxContainer();
		content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		content.SizeFlagsVertical = SizeFlags.ExpandFill;
		margin.AddChild(content);
		return content;
	}

	Label CreateSectionTitle(string text)
	{
		var label = new Label();
		label.Text = text;
		label.AddThemeFontSizeOverride("font_size", 20);
		label.AddThemeColorOverride("font_color", new Color(0.97f, 0.98f, 1.0f));
		return label;
	}

	StyleBoxFlat CreatePanelStyle(Color backgroundColor, Color borderColor, bool shadow = false)
	{
		var style = new StyleBoxFlat();
		style.BgColor = backgroundColor;
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(8);
		if (shadow)
		{
			style.ShadowColor = new Color(0, 0, 0, 0.34f);
			style.ShadowSize = 8;
			style.ShadowOffset = new Vector2(0, 4);
		}
		return style;
	}

	void ApplyPrimaryButtonStyle(Button button)
	{
		if (button == null)
			return;

		button.AddThemeStyleboxOverride("normal", CreatePrimaryButtonStyle(new Color(0.33f, 0.39f, 0.31f), new Color(0.58f, 0.64f, 0.44f), 5));
		button.AddThemeStyleboxOverride("hover", CreatePrimaryButtonStyle(new Color(0.38f, 0.45f, 0.35f), new Color(0.68f, 0.72f, 0.5f), 6));
		button.AddThemeStyleboxOverride("pressed", CreatePrimaryButtonStyle(new Color(0.25f, 0.3f, 0.25f), new Color(0.5f, 0.56f, 0.4f), 2));
		button.AddThemeStyleboxOverride("disabled", CreatePrimaryButtonStyle(new Color(0.16f, 0.18f, 0.18f), new Color(0.22f, 0.24f, 0.24f), 0));
		button.AddThemeColorOverride("font_color", new Color(0.98f, 0.98f, 0.92f));
		button.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.54f, 0.54f));
		button.ButtonDown += () => AnimateButtonScale(button, 0.985f);
		button.ButtonUp += () => AnimateButtonScale(button, 1.0f);
		button.MouseExited += () => AnimateButtonScale(button, 1.0f);
	}

	StyleBoxFlat CreatePrimaryButtonStyle(Color backgroundColor, Color borderColor, int shadowSize)
	{
		var style = new StyleBoxFlat();
		style.BgColor = backgroundColor;
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(8);
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		style.ShadowColor = new Color(0, 0, 0, shadowSize > 0 ? 0.28f : 0);
		style.ShadowSize = shadowSize;
		style.ShadowOffset = new Vector2(0, shadowSize > 0 ? 3 : 0);
		return style;
	}

	void ApplyWideVerticalScrollbar(VScrollBar scrollbar)
	{
		if (scrollbar == null)
			return;

		scrollbar.CustomMinimumSize = new Vector2(LauncherScrollBarWidth, 0);
		scrollbar.AddThemeConstantOverride("scroll_width", LauncherScrollBarWidth);
		scrollbar.AddThemeStyleboxOverride("scroll", CreateScrollTrackStyle());
		scrollbar.AddThemeStyleboxOverride("grabber", CreateScrollGrabberStyle(new Color(0.42f, 0.48f, 0.45f)));
		scrollbar.AddThemeStyleboxOverride("grabber_highlight", CreateScrollGrabberStyle(new Color(0.52f, 0.58f, 0.5f)));
		scrollbar.AddThemeStyleboxOverride("grabber_pressed", CreateScrollGrabberStyle(new Color(0.6f, 0.64f, 0.52f)));
	}

	StyleBoxFlat CreateScrollTrackStyle()
	{
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.065f, 0.072f, 0.072f, 0.9f);
		style.SetCornerRadiusAll(8);
		style.ContentMarginLeft = 4;
		style.ContentMarginRight = 4;
		return style;
	}

	StyleBoxFlat CreateScrollGrabberStyle(Color color)
	{
		var style = new StyleBoxFlat();
		style.BgColor = color;
		style.SetCornerRadiusAll(8);
		style.ContentMarginLeft = 5;
		style.ContentMarginRight = 5;
		style.ContentMarginTop = 4;
		style.ContentMarginBottom = 4;
		return style;
	}

	void CopyFeedbackGroup()
	{
		DisplayServer.ClipboardSet(FeedbackQqGroup);
		if (announcementStatusLabel != null)
			announcementStatusLabel.Text = MultiLanguage.Get("FirstWindow.QQCopied", "QQ群号已复制，可以粘贴分享给需要反馈的人。");
		if (statusLabel != null)
			statusLabel.Text = "";
	}

	void UpdateCategoryHint()
	{
		if (categoryHintLabel == null)
			return;

		categoryHintLabel.Text = MultiLanguage.Get(
			currentCategory == LauncherGameCategory.Snake ? "FirstWindow.SnakeHint" : "FirstWindow.V24PureHint",
			currentCategory == LauncherGameCategory.Snake
				? $"snake: 扫描 {GetSnakeRootHint()} 下的直接游戏目录，并使用 snake 核心。"
				: $"v24: 扫描 {GetNormalRootHint()} 下的直接游戏目录，以及 compat/<profile>/<game>。例如 compat/erafl/eraFL0.48 会显示在本列表，但会自动使用 erafl 模块；不会读取游戏内容识别类型。");
	}

	public override void _ExitTree()
	{
		GetViewport().SizeChanged -= OnViewportSizeChanged;
		if (OS.GetName() == "Android")
			GetTree().OnRequestPermissionsResult -= OnPermissionsResult;
	}

	void OnPermissionsResult(string permission, bool granted)
	{
		androidPermissionResultReceived = true;
		ContinueAfterPermissionRequest();
	}

	void OnPermissionRequestFallbackTimeout()
	{
		if (OS.GetName() == "Android" && androidPermissionCheckPending && !androidPermissionResultReceived)
			ContinueAfterPermissionRequest();
	}

	public override void _Notification(int what)
	{
		if (statusLabel == null || OS.GetName() != "Android" || !androidPermissionCheckPending)
			return;

		if (what == NotificationApplicationResumed || what == NotificationApplicationFocusIn)
			ContinueAfterPermissionRequest();
	}

	void ContinueAfterPermissionRequest()
	{
		statusLabel.Text = "";

		// Check if we can actually access the target directory
		bool canAccess = false;
		using (var dir = DirAccess.Open("/storage/emulated/0"))
		{
			canAccess = dir != null;
		}

		if (!canAccess)
		{
			// Android 11+ 的 MANAGE_EXTERNAL_STORAGE 属于"特殊应用权限"，
			// OS.RequestPermissions() 不会弹出授权对话框，必须由用户到系统设置页
			// 手动开启。开启后返回应用会经 _Notification(ApplicationResumed) 自动
			// 重新扫描，所以这里给出具体操作路径而不是只写"系统设置"。
			statusLabel.Text = "需要\"所有文件访问\"权限：设置 → 应用 → gEmuera → 权限 → 所有文件访问。开启后返回本应用（将自动重新扫描游戏）。\n"
				+ "\"All files access\" required: Settings → Apps → gEmuera → Permissions → All files access, then return (auto re-scan).";
			// Still try to scan — user:// and app-specific dirs don't need this permission
		}
		androidPermissionCheckPending = !canAccess;

		// Try to create the emuera directory
		using (var dir = DirAccess.Open("/storage/emulated/0"))
		{
			if (dir != null && !dir.DirExists("emuera"))
				dir.MakeDir("emuera");
		}

		ScanGames();
	}

	void ScanGames()
	{
		string selectedPath = null;
		if (TryGetSelectedGameEntry(out LauncherGameEntry selectedEntry))
			selectedPath = selectedEntry.GameRoot;
		if (string.IsNullOrEmpty(selectedPath))
			selectedPath = ResolveStartupGamePath();

		gameList.Clear();
		gameEntries.Clear();
		startButton.Disabled = true;

		var roots = GetScanRoots(currentCategory);
		var scannedEntries = new List<LauncherGameEntry>();
		var addedPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
		var scanMessages = new List<string>();
		foreach (var root in roots)
		{
			if (currentCategory == LauncherGameCategory.Snake)
				ScanSnakeRoot(root, scannedEntries, addedPaths, scanMessages);
			else
				ScanV24Root(root, scannedEntries, addedPaths, scanMessages);
		}

		PopulateGameList(scannedEntries, selectedPath);

		if (gameList.ItemCount == 0)
		{
			gameList.AddItem(MultiLanguage.Get("FirstWindow.NoGames", "No era games found"));
			gameList.SetItemDisabled(0, true);
			if (scanMessages.Count > 0)
			{
				string routeErrors = string.Join("\n", scanMessages);
				statusLabel.Text = string.IsNullOrEmpty(statusLabel.Text)
					? routeErrors
					: statusLabel.Text + "\n" + routeErrors;
			}
			if (string.IsNullOrEmpty(statusLabel.Text))
				statusLabel.Text = "请将 era 游戏文件夹放入以下路径:\n" + string.Join("\n", roots);
		}
		else if (scanMessages.Count > 0)
		{
			statusLabel.Text = string.Join("\n", scanMessages);
		}
		else if (!androidPermissionCheckPending)
		{
			statusLabel.Text = "";
			UpdateSelectedGameCompatibilityHint();
		}
	}

	List<string> GetScanRoots(LauncherGameCategory category)
	{
		var roots = new List<string>();
		var baseRoots = GetBaseScanRoots();

		if (category == LauncherGameCategory.Snake)
		{
			foreach (var root in baseRoots)
				AddUniqueRoot(roots, root.TrimEnd('/', '\\') + "/snake");
			return roots;
		}

		foreach (var root in baseRoots)
			AddUniqueRoot(roots, root);
		return roots;
	}

	List<string> GetBaseScanRoots()
	{
		var roots = new List<string>();

		if (OS.GetName() == "Android")
		{
			// 主存储卷必须显式加入：/storage 枚举在某些 ROM 上不可靠，且
			// /storage/self/primary 是指向 emulated/0 的符号链接（见下方跳过）。
			AddUniqueRoot(roots, "/storage/emulated/0/emuera");

			// 枚举所有存储卷（外置 SD 卡等）。部分平板把游戏放在
			// /storage/XXXX-XXXX/emuera 下，只扫主存储会"检测不到游戏"。
			using var storageDir = DirAccess.Open("/storage");
			if (storageDir != null)
			{
				// Android 沙箱下 /storage 根可能无法枚举：Java 侧 dirOpen 返回 0 使
				// list_dir_begin 失败，而 Godot 核心 _get_contents 忽略该错误后直接
				// 调 get_next，打印 "Condition 'id == 0' is true. Returning: ''"。
				// 先用 ListDirBegin 探测可枚举性，失败则跳过卷枚举（主存储已由
				// 上方显式路径覆盖，不受影响）。
				if (storageDir.ListDirBegin() == Error.Ok)
				{
					storageDir.ListDirEnd();
					foreach (string volume in storageDir.GetDirectories())
					{
						if (string.IsNullOrEmpty(volume) || volume == "self")
							continue;
						string volumeEmuera = "/storage/" + volume + "/emuera";
						// 只把确实包含 emuera 容器的卷加入扫描根，避免把无关卷
						// 全扫一遍导致启动器出现大量无效条目。
						if (uEmuera.Utils.DirectoryExists(volumeEmuera))
							AddUniqueRoot(roots, volumeEmuera);
					}
				}
			}

			string appDir = OS.GetUserDataDir();
			if (!string.IsNullOrEmpty(appDir))
				AddUniqueRoot(roots, appDir);
		}
		else
		{
			string exeDir = OS.GetExecutablePath().GetBaseDir();
			if (!string.IsNullOrEmpty(exeDir))
				AddUniqueRoot(roots, exeDir);

			if (OS.HasFeature("editor"))
			{
				string resDir = ProjectSettings.GlobalizePath("res://");
				if (!string.IsNullOrEmpty(resDir))
					AddUniqueRoot(roots, resDir);
			}

			string configuredLibrary = ProjectSettings.GetSetting(
				"launcher/default_game_library",
				"").AsString();
			if (!string.IsNullOrWhiteSpace(configuredLibrary))
				AddUniqueRoot(roots, configuredLibrary);
		}

		string userDir = ProjectSettings.GlobalizePath("user://");
		if (!string.IsNullOrEmpty(userDir))
			AddUniqueRoot(roots, userDir);

		return roots;
	}

	void AddUniqueRoot(List<string> roots, string root)
	{
		if (string.IsNullOrEmpty(root))
			return;

		string normalized = root.TrimEnd('/', '\\');
		foreach (var existing in roots)
		{
			if (PathsEqual(existing, normalized))
				return;
		}
		roots.Add(normalized);
	}

	string GetNormalRootHint()
	{
		if (OS.GetName() == "Android")
			return "/storage/emulated/0/emuera";

		string exeDir = OS.GetExecutablePath().GetBaseDir();
		return string.IsNullOrEmpty(exeDir) ? "emuera" : exeDir.TrimEnd('/', '\\');
	}

	string GetSnakeRootHint()
	{
		if (OS.GetName() == "Android")
			return "/storage/emulated/0/emuera/snake";

		return GetNormalRootHint().TrimEnd('/', '\\') + "/snake";
	}

	void ScanV24Root(
		string root,
		List<LauncherGameEntry> entries,
		HashSet<string> addedPaths,
		List<string> scanMessages)
	{
		if (string.IsNullOrEmpty(root))
			return;

		string normalizedRoot = root.TrimEnd('/', '\\');
		foreach (string directoryName in GetDirectDirectoryNames(normalizedRoot))
		{
			if (entries.Count >= MaxLauncherGameEntries)
			{
				AddScanMessage(scanMessages, $"游戏条目超过 {MaxLauncherGameEntries} 个，已停止继续扫描。");
				break;
			}

			if (string.Equals(directoryName, SnakeDirectoryName, System.StringComparison.OrdinalIgnoreCase)
				|| string.Equals(directoryName, CompatibilityDirectoryName, System.StringComparison.OrdinalIgnoreCase))
				continue;

			string gameRoot = CombineDirectory(normalizedRoot, directoryName);
			if (IsEraGameDirectory(gameRoot))
			{
				AddGameEntry(
					entries,
					addedPaths,
					GetGameDisplayName(normalizedRoot, gameRoot),
					gameRoot,
					CoreProfileV24Pure,
					LauncherGameSource.V24Root,
					scanMessages);
			}
			else
			{
				// 支持 emuera/归档目录/游戏 这类嵌套结构（部分平板用户习惯
				// 用子目录归档游戏包）；只递归一层，配合 IsEraGameDirectory
				// 校验避免把游戏内部目录误报为独立游戏。
				ScanNestedEraGameDirectories(
					gameRoot,
					1,
					CoreProfileV24Pure,
					LauncherGameSource.V24Root,
					entries,
					addedPaths,
					scanMessages);
			}
		}

		ScanCompatibilityDirectory(normalizedRoot, entries, addedPaths, scanMessages);
	}

	void ScanSnakeRoot(
		string root,
		List<LauncherGameEntry> entries,
		HashSet<string> addedPaths,
		List<string> scanMessages)
	{
		if (string.IsNullOrEmpty(root))
			return;

		string normalizedRoot = root.TrimEnd('/', '\\');
		foreach (string directoryName in GetDirectDirectoryNames(normalizedRoot))
		{
			if (entries.Count >= MaxLauncherGameEntries)
			{
				AddScanMessage(scanMessages, $"游戏条目超过 {MaxLauncherGameEntries} 个，已停止继续扫描。");
				break;
			}

			string gameRoot = CombineDirectory(normalizedRoot, directoryName);
			if (IsEraGameDirectory(gameRoot))
			{
				AddGameEntry(
					entries,
					addedPaths,
					GetGameDisplayName(normalizedRoot, gameRoot),
					gameRoot,
					CoreProfileSnake,
					LauncherGameSource.SnakeRoot,
					scanMessages);
			}
			else
			{
				ScanNestedEraGameDirectories(
					gameRoot,
					1,
					CoreProfileSnake,
					LauncherGameSource.SnakeRoot,
					entries,
					addedPaths,
					scanMessages);
			}
		}
	}

	// 在非游戏目录内再找一层游戏目录（总深度不超过 2）。
	void ScanNestedEraGameDirectories(
		string directory,
		int depth,
		string profileId,
		LauncherGameSource source,
		List<LauncherGameEntry> entries,
		HashSet<string> addedPaths,
		List<string> scanMessages)
	{
		if (string.IsNullOrEmpty(directory) || depth > MaxLauncherScanDepth)
			return;

		foreach (string directoryName in GetDirectDirectoryNames(directory))
		{
			if (entries.Count >= MaxLauncherGameEntries)
			{
				AddScanMessage(scanMessages, $"游戏条目超过 {MaxLauncherGameEntries} 个，已停止继续扫描。");
				return;
			}

			string gameRoot = CombineDirectory(directory, directoryName);
			if (!IsEraGameDirectory(gameRoot))
				continue;

			AddGameEntry(
				entries,
				addedPaths,
				GetGameDisplayName(directory, gameRoot),
				gameRoot,
				profileId,
				source,
				scanMessages);
		}
	}

	void ScanCompatibilityDirectory(
		string root,
		List<LauncherGameEntry> entries,
		HashSet<string> addedPaths,
		List<string> scanMessages)
	{
		string compatibilityRoot = CombineDirectory(root, CompatibilityDirectoryName);
		List<string> profileDirectoryNames = GetDirectDirectoryNames(compatibilityRoot);
		if (profileDirectoryNames.Count > MaxCompatibilityProfilesPerRoot)
		{
			AddScanMessage(
				scanMessages,
				$"兼容目录 profile 超过 {MaxCompatibilityProfilesPerRoot} 个，已忽略其余目录。");
		}

		int profileCount = System.Math.Min(profileDirectoryNames.Count, MaxCompatibilityProfilesPerRoot);
		for (int profileIndex = 0; profileIndex < profileCount; profileIndex++)
		{
			string profileDirectoryName = profileDirectoryNames[profileIndex];
			if (!TryResolveCompatibilityRouteProfile(profileDirectoryName, scanMessages, out string profileId))
				continue;

			string profileRoot = CombineDirectory(compatibilityRoot, profileDirectoryName);
			List<string> gameDirectoryNames = GetDirectDirectoryNames(profileRoot);
			if (gameDirectoryNames.Count > MaxGamesPerCompatibilityProfile)
			{
				AddScanMessage(
					scanMessages,
					$"兼容目录 compat/{profileId} 的游戏超过 {MaxGamesPerCompatibilityProfile} 个，已忽略其余目录。");
			}

			int gameCount = System.Math.Min(gameDirectoryNames.Count, MaxGamesPerCompatibilityProfile);
			for (int gameIndex = 0; gameIndex < gameCount; gameIndex++)
			{
				if (entries.Count >= MaxLauncherGameEntries)
				{
					AddScanMessage(scanMessages, $"游戏条目超过 {MaxLauncherGameEntries} 个，已停止继续扫描。");
					return;
				}

				string gameDirectoryName = gameDirectoryNames[gameIndex];
				string gameRoot = CombineDirectory(profileRoot, gameDirectoryName);
				if (!IsEraGameDirectory(gameRoot))
				{
					AddScanMessage(
						scanMessages,
						$"兼容目录 compat/{profileId}/{gameDirectoryName} 不是有效游戏目录，已跳过（不递归扫描）。");
					continue;
				}

				AddGameEntry(
					entries,
					addedPaths,
					GetGameDisplayName(profileRoot, gameRoot),
					gameRoot,
					profileId,
					LauncherGameSource.CompatibilityDirectory,
					scanMessages);
			}
		}
	}

	bool TryResolveCompatibilityRouteProfile(
		string profileDirectoryName,
		List<string> scanMessages,
		out string profileId)
	{
		profileId = null;
		if (!string.Equals(
				profileDirectoryName,
				profileDirectoryName.ToLowerInvariant(),
				System.StringComparison.Ordinal))
		{
			AddScanMessage(scanMessages, $"兼容目录 {profileDirectoryName} 必须使用小写 profile id，已跳过。");
			return false;
		}

		if (string.Equals(profileDirectoryName, CoreProfileV24Pure, System.StringComparison.Ordinal)
			|| string.Equals(profileDirectoryName, CoreProfileSnake, System.StringComparison.Ordinal))
		{
			AddScanMessage(scanMessages, $"兼容目录 {profileDirectoryName} 是保留 launcher lane，已跳过。");
			return false;
		}

		if (!DirectoryRouteProfileCatalog.TryResolve(profileDirectoryName, out CompatibilityProfileDefinition definition))
		{
			AddScanMessage(scanMessages, $"不支持的兼容目录 {profileDirectoryName}，已跳过其中游戏。");
			return false;
		}

		profileId = definition.ProfileId;
		return true;
	}

	static List<string> GetDirectDirectoryNames(string path)
	{
		var names = new List<string>();
		using var dir = DirAccess.Open(path);
		if (dir == null)
			return names;

		dir.IncludeHidden = true;
		foreach (string name in dir.GetDirectories())
		{
			if (string.IsNullOrEmpty(name) || name == "." || name == "..")
				continue;
			names.Add(name);
		}
		names.Sort(System.StringComparer.OrdinalIgnoreCase);
		return names;
	}

	static string CombineDirectory(string root, string child)
	{
		return root.TrimEnd('/', '\\') + "/" + child;
	}

	bool IsEraGameDirectory(string path)
	{
		if (string.IsNullOrEmpty(path))
			return false;
		return HasSubDirectory(path, "erb")
			&& (HasSubDirectory(path, "csv") || HasSubDirectory(path, "dat") || HasSubDirectory(path, "resources"));
	}

	bool HasSubDirectory(string path, string name)
	{
		return uEmuera.Utils.DirectoryExists(path.TrimEnd('/') + "/" + name)
			|| uEmuera.Utils.DirectoryExists(path.TrimEnd('/') + "/" + name.ToUpperInvariant());
	}

	void AddGameEntry(
		List<LauncherGameEntry> entries,
		HashSet<string> addedPaths,
		string displayName,
		string fullPath,
		string profileId,
		LauncherGameSource source,
		List<string> scanMessages)
	{
		if (!addedPaths.Add(fullPath.TrimEnd('/', '\\')))
			return;

		if (entries.Count >= MaxLauncherGameEntries)
		{
			AddScanMessage(scanMessages, $"游戏条目超过 {MaxLauncherGameEntries} 个，已停止继续扫描。");
			return;
		}

		entries.Add(new LauncherGameEntry(
			displayName,
			fullPath.TrimEnd('/', '\\'),
			profileId,
			source));
	}

	string GetGameDisplayName(string root, string fullPath)
	{
		string normalizedRoot = root.TrimEnd('/', '\\');
		if (fullPath.StartsWith(normalizedRoot + "/", System.StringComparison.OrdinalIgnoreCase))
			return fullPath.Substring(normalizedRoot.Length + 1);
		return Path.GetFileName(fullPath.TrimEnd('/', '\\'));
	}

	void PopulateGameList(List<LauncherGameEntry> scannedEntries, string selectedPath)
	{
		scannedEntries.Sort((left, right) =>
		{
			int displayNameComparison = System.StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
			if (displayNameComparison != 0)
				return displayNameComparison;
			int profileComparison = System.StringComparer.Ordinal.Compare(left.ProfileId, right.ProfileId);
			if (profileComparison != 0)
				return profileComparison;
			return System.StringComparer.OrdinalIgnoreCase.Compare(left.GameRoot, right.GameRoot);
		});

		var duplicateDisplayNames = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
		foreach (LauncherGameEntry entry in scannedEntries)
		{
			duplicateDisplayNames.TryGetValue(entry.DisplayName, out int count);
			duplicateDisplayNames[entry.DisplayName] = count + 1;
		}

		foreach (LauncherGameEntry entry in scannedEntries)
		{
			string label = duplicateDisplayNames[entry.DisplayName] > 1
				? $"{entry.DisplayName} ({GetEntryRouteDescription(entry)})"
				: entry.DisplayName;
			gameList.AddItem(label);
			gameEntries.Add(entry);
			if (PathsEqual(entry.GameRoot, selectedPath))
			{
				gameList.Select(gameList.ItemCount - 1);
				startButton.Disabled = false;
			}
		}
	}

	static string GetEntryRouteDescription(LauncherGameEntry entry)
	{
		return entry.Source switch
		{
			LauncherGameSource.V24Root => "v24",
			LauncherGameSource.SnakeRoot => "snake",
			_ => CompatibilityDirectoryName + "/" + entry.ProfileId,
		};
	}

	bool TryGetSelectedGameEntry(out LauncherGameEntry entry)
	{
		var selectedItems = gameList.GetSelectedItems();
		if (selectedItems.Length > 0)
			return TryGetGameEntry(selectedItems[0], out entry);

		entry = null;
		return false;
	}

	bool TryGetGameEntry(long index, out LauncherGameEntry entry)
	{
		if (index >= 0 && index < gameEntries.Count)
		{
			entry = gameEntries[(int)index];
			return true;
		}

		entry = null;
		return false;
	}

	void AddScanMessage(List<string> scanMessages, string message)
	{
		if (scanMessages.Count >= MaxScanMessages || scanMessages.Contains(message))
			return;
		scanMessages.Add(message);
	}

	void OnGameSelected(long index)
	{
		if (!TryGetGameEntry(index, out LauncherGameEntry entry))
			return;

		startButton.Disabled = false;
		UpdateSelectedGameCompatibilityHint(entry);
	}

	void OnGameActivated(long index)
	{
		if (!TryGetGameEntry(index, out LauncherGameEntry entry))
			return;
		LaunchGameEntry(entry);
	}

	void OnStartPressed()
	{
		if (!TryGetSelectedGameEntry(out LauncherGameEntry entry))
			return;
		LaunchGameEntry(entry);
	}

	void LaunchGameEntry(LauncherGameEntry entry)
	{
		SetSelectedGamePath(entry.GameRoot, GetSelectedCoreProfileName(entry));
		GetTree().ChangeSceneToFile("res://main.tscn");
	}

	public static string ResolveStartupGamePath()
	{
		if (IsUsableEraGameDirectory(SelectedGamePath))
			return SelectedGamePath;

		string saved = LoadLastGamePath();
		if (IsUsableEraGameDirectory(saved))
		{
			SelectedGamePath = saved;
			SelectedCoreProfileName = LoadLastCoreProfileName();
			return saved;
		}

		return null;
	}

	string GetSelectedCoreProfileName(LauncherGameEntry entry)
	{
		if (AdvancedCompatibilityEnabled)
			return NormalizeCoreProfileName(ManualCoreProfileName);
		return entry.ProfileId;
	}

	void UpdateSelectedGameCompatibilityHint()
	{
		if (TryGetSelectedGameEntry(out LauncherGameEntry entry))
			UpdateSelectedGameCompatibilityHint(entry);
	}

	void UpdateSelectedGameCompatibilityHint(LauncherGameEntry entry)
	{
		if (statusLabel == null)
			return;

		string effectiveProfile = GetSelectedCoreProfileName(entry);
		string routeDescription = GetEntryRouteDescription(entry);
		statusLabel.Text = AdvancedCompatibilityEnabled
			? $"高级兼容模式：本次启动使用 {effectiveProfile}（目录路由为 {entry.ProfileId}，来源 {routeDescription}）。"
			: $"目录路由：{routeDescription} → {effectiveProfile}";
	}

	static void SetSelectedGamePath(string path, string coreProfileName = CoreProfileV24Pure)
	{
		if (string.IsNullOrEmpty(path))
			return;
		if (!TryNormalizeCoreProfileName(coreProfileName, out string normalizedProfileName))
			throw new System.InvalidOperationException($"Unsupported compatibility profile: {coreProfileName}");

		SelectedGamePath = path.TrimEnd('/', '\\');
		SelectedCoreProfileName = normalizedProfileName;
		SaveLastGamePath(SelectedGamePath, SelectedCoreProfileName);
	}

	static string NormalizeCoreProfileName(string coreProfileName)
	{
		return TryNormalizeCoreProfileName(coreProfileName, out string normalizedProfileName)
			? normalizedProfileName
			: CoreProfileV24Pure;
	}

	static bool TryNormalizeCoreProfileName(string coreProfileName, out string normalizedProfileName)
	{
		normalizedProfileName = null;
		// launcher.cfg 与 M0 runner 是旧入口，保留三个已存在 profile 的大小写兼容；
		// compat 目录本身仍在扫描时按小写、精确 allowlist 校验。
		if (string.Equals(coreProfileName, CoreProfileV24Pure, System.StringComparison.OrdinalIgnoreCase))
		{
			normalizedProfileName = CoreProfileV24Pure;
			return true;
		}
		if (string.Equals(coreProfileName, CoreProfileSnake, System.StringComparison.OrdinalIgnoreCase))
		{
			normalizedProfileName = CoreProfileSnake;
			return true;
		}
		if (string.Equals(coreProfileName, CoreProfileEraFl, System.StringComparison.OrdinalIgnoreCase))
		{
			normalizedProfileName = CoreProfileEraFl;
			return true;
		}

		if (!DirectoryRouteProfileCatalog.TryResolve(coreProfileName, out CompatibilityProfileDefinition definition))
			return false;

		normalizedProfileName = definition.ProfileId;
		return true;
	}

	static CompatibilityProfileCatalog CreateDirectoryRouteProfileCatalog()
	{
		CompatibilityProfileCatalog catalog = BuiltInDialectCatalog.CreateLegacyProfileCatalog();
		catalog.Freeze();
		return catalog;
	}

	static string LoadLastGamePath()
	{
		var config = new ConfigFile();
		if (config.Load(LauncherSettingsPath) != Error.Ok)
			return null;

		return config.GetValue(LauncherSettingsSection, LauncherLastGamePathKey, "").As<string>();
	}

	static string LoadLastCoreProfileName()
	{
		var config = new ConfigFile();
		if (config.Load(LauncherSettingsPath) != Error.Ok)
			return CoreProfileV24Pure;

		return NormalizeCoreProfileName(config.GetValue(LauncherSettingsSection, LauncherLastCoreProfileKey, CoreProfileV24Pure).As<string>());
	}

	static void SaveLastGamePath(string path, string coreProfileName)
	{
		if (string.IsNullOrEmpty(path))
			return;
		if (!TryNormalizeCoreProfileName(coreProfileName, out string normalizedProfileName))
			return;

		var config = new ConfigFile();
		config.Load(LauncherSettingsPath);
		config.SetValue(LauncherSettingsSection, LauncherLastGamePathKey, path);
		config.SetValue(LauncherSettingsSection, LauncherLastCoreProfileKey, normalizedProfileName);
		config.Save(LauncherSettingsPath);
	}

	static string FindFirstEraGameDirectory(string root, int maxDepth)
	{
		if (string.IsNullOrEmpty(root))
			return null;

		root = root.TrimEnd('/', '\\');
		return FindFirstEraGameDirectoryRecursive(root, 0, maxDepth);
	}

	static string FindFirstEraGameDirectoryRecursive(string path, int depth, int maxDepth)
	{
		if (string.IsNullOrEmpty(path) || depth > maxDepth)
			return null;

		if (IsUsableEraGameDirectory(path))
			return path.TrimEnd('/', '\\');

		using var dir = DirAccess.Open(path);
		if (dir == null)
			return null;

		dir.IncludeHidden = true;
		foreach (string entry in dir.GetDirectories())
		{
			if (string.IsNullOrEmpty(entry) || entry == "." || entry == "..")
				continue;

			string found = FindFirstEraGameDirectoryRecursive(path.TrimEnd('/', '\\') + "/" + entry, depth + 1, maxDepth);
			if (!string.IsNullOrEmpty(found))
				return found;
		}

		return null;
	}

	static bool IsUsableEraGameDirectory(string path)
	{
		if (string.IsNullOrEmpty(path))
			return false;

		return HasEraSubDirectory(path, "erb")
			&& (HasEraSubDirectory(path, "csv") || HasEraSubDirectory(path, "dat") || HasEraSubDirectory(path, "resources"));
	}

	static bool HasEraSubDirectory(string path, string name)
	{
		return uEmuera.Utils.DirectoryExists(path.TrimEnd('/', '\\') + "/" + name)
			|| uEmuera.Utils.DirectoryExists(path.TrimEnd('/', '\\') + "/" + name.ToUpperInvariant());
	}

	static bool PathsEqual(string left, string right)
	{
		if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
			return false;

		return string.Equals(left.TrimEnd('/', '\\'), right.TrimEnd('/', '\\'), System.StringComparison.OrdinalIgnoreCase);
	}
}

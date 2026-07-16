using Godot;
using System;
using MinorShift._Library;
using MinorShift.Emuera;
using MinorShift.Emuera.GameView;
using System.Collections.Concurrent;
using System.Threading;
using MinorShift.Emuera.Content;
using gEmuera.Diagnostics;
using GEmuera.Core.Session;
using gEmuera.GodotHost;
using System.Threading.Tasks;

public partial class EmueraMain : Node
{
	[Export] public bool debug = false;
	[Export] public bool use_coroutine = false;
	[Export] public bool enable_sprite_debug_viewer = true;

	// GPU work queue for cross-thread ColorMatrix rendering from background thread
	public class GpuWorkItem
	{
		public int Id;
		public Godot.Image SrcImage;
		public Godot.Rect2I SrcRegion;
		public float[][] ColorMatrix;
		public Godot.Image ResultImage;
		public ManualResetEventSlim Completed = new ManualResetEventSlim(false);
	}

	public class TextRenderItem
	{
		public int Id;
		public string Text;
		public string FontName;
		public int FontSize;
		public int FontStyle;
		public uEmuera.Drawing.Color Color;
		public int Width;
		public int Height;
		public Godot.Image ResultImage;
		public ManualResetEventSlim Completed = new ManualResetEventSlim(false);
	}

	static ConcurrentQueue<GpuWorkItem> gpuQueue = new ConcurrentQueue<GpuWorkItem>();
	static ConcurrentQueue<TextRenderItem> textRenderQueue = new ConcurrentQueue<TextRenderItem>();
	static EmueraMain currentInstance;
	static int gpuWorkIdCounter = 0;
	static int textRenderIdCounter = 0;
	static readonly object configMapCacheLock = new object();
	static System.Collections.Generic.Dictionary<string, string> cachedShiftJisToUtf8Map;
	static System.Collections.Generic.Dictionary<string, string> cachedUtf8ZhCnToUtf8Map;
	LegacySessionFacade legacySessionFacade;
	LegacySessionBackend legacySessionBackend;
	LegacySessionLaunchRegistry m0RunnerSessionLaunchRegistry;

	/// <summary>
	/// True once _Process has been called at least once, indicating the main loop is running
	/// and the SubViewport render pipeline is ready to process GPU work.
	/// </summary>
	public static bool GpuReady { get; private set; } = false;

	/// <summary>
	/// Completes and drops render work submitted by the stopped legacy session.
	/// Work items block a worker for a bounded wait while the Godot main loop
	/// renders them; leaving them in these process-static queues would let a
	/// stale image or text result be consumed by the next candidate.
	/// </summary>
	internal static void ResetCanarySessionState()
	{
		while (gpuQueue.TryDequeue(out var gpuItem))
		{
			gpuItem.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
			gpuItem.Completed.Set();
		}
		while (textRenderQueue.TryDequeue(out var textItem))
		{
			textItem.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
			textItem.Completed.Set();
		}
		currentInstance?.ResetPendingRenderState();
		GpuReady = false;
	}

	void ResetPendingRenderState()
	{
		if (pendingGpuItem != null)
		{
			pendingGpuItem.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
			pendingGpuItem.Completed.Set();
			pendingGpuItem = null;
		}
		gpuWaitingForRender = false;
		gpuRenderFrameCount = 0;
		if (gpuViewport != null)
			gpuViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

		if (pendingTextRenderItem != null)
		{
			pendingTextRenderItem.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
			pendingTextRenderItem.Completed.Set();
			pendingTextRenderItem = null;
		}
		textWaitingForRender = false;
		textRenderFrameCount = 0;
		if (textViewport != null)
			textViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
	}

	/// Submit ColorMatrix work from any thread. Returns the GpuWorkItem for direct wait.
	public static GpuWorkItem GpuSubmitColorMatrix(Godot.Image src, Godot.Rect2I region, float[][] cm)
	{
		var item = new GpuWorkItem
		{
			Id = Interlocked.Increment(ref gpuWorkIdCounter),
			SrcImage = src,
			SrcRegion = region,
			ColorMatrix = cm
		};
		gpuQueue.Enqueue(item);
		return item;
	}

	public static TextRenderItem SubmitTextRender(string text, string fontName, int fontSize, int fontStyle, uEmuera.Drawing.Color color, int width, int height)
	{
		if (GenericUtils.IsOnMainThread())
			return null;
		var item = new TextRenderItem
		{
			Id = Interlocked.Increment(ref textRenderIdCounter),
			Text = text ?? "",
			FontName = fontName,
			FontSize = System.Math.Max(1, fontSize),
			FontStyle = fontStyle,
			Color = color,
			Width = System.Math.Max(1, width),
			Height = System.Math.Max(1, height)
		};
		textRenderQueue.Enqueue(item);
		return item;
	}

	// SubViewport-based GPU rendering for ColorMatrix
	SubViewport gpuViewport;
	TextureRect gpuTextureRect;
	ShaderMaterial gpuShaderMaterial;
	GpuWorkItem pendingGpuItem;
	int gpuRenderFrameCount = 0;
	bool gpuWaitingForRender = false;
	SubViewport textViewport;
	Label textRenderLabel;
	FontFile textRenderFont;
	TextRenderItem pendingTextRenderItem;
	int textRenderFrameCount = 0;
	bool textWaitingForRender = false;
	bool startupStarted = false;
	Control startupOverlay;
	Label startupStatusLabel;
	// Root-level Android lifecycle state. The main node owns frame-rate throttling
	// while EmueraContent owns reversible audio pause state for script channels.
	bool applicationPauseActive = false;
	int maxFpsBeforeApplicationPause = -1;

	void SetupGpuRenderer()
	{
		if (!ShouldUseGpuRenderer() || gpuViewport != null)
			return;

		gpuViewport = new SubViewport();
		gpuViewport.TransparentBg = true;
		gpuViewport.Size = new Vector2I(16, 16);
		gpuViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		gpuViewport.Name = "GpuRenderViewport";

		gpuTextureRect = new TextureRect();
		gpuTextureRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		gpuTextureRect.Name = "GpuTextureRect";

		gpuShaderMaterial = ColorMatrixGPU.CreateCompositMaterial();
		gpuTextureRect.Material = gpuShaderMaterial;

		gpuViewport.AddChild(gpuTextureRect);
		AddChild(gpuViewport);
	}

	static bool ShouldUseGpuRenderer()
	{
		return !OS.HasFeature("mobile");
	}

	void SetupTextRenderer()
	{
		if (textViewport != null)
			return;

		textRenderFont = ResourceLoader.Load<FontFile>("res://Fonts/MS Gothic.ttf");
		textViewport = new SubViewport();
		textViewport.TransparentBg = true;
		textViewport.Size = new Vector2I(16, 16);
		textViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		textViewport.Name = "TextRenderViewport";

		textRenderLabel = new Label();
		textRenderLabel.Name = "TextRenderLabel";
		textRenderLabel.Position = Vector2.Zero;
		textRenderLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		textRenderLabel.VerticalAlignment = VerticalAlignment.Top;
		textRenderLabel.HorizontalAlignment = HorizontalAlignment.Left;
		textRenderLabel.AutowrapMode = TextServer.AutowrapMode.Off;
		textRenderLabel.ClipText = true;
		if (textRenderFont != null)
			textRenderLabel.AddThemeFontOverride("font", textRenderFont);

		textViewport.AddChild(textRenderLabel);
		AddChild(textViewport);
	}

	void ProcessTextRenderQueue()
	{
		if (textViewport == null)
			SetupTextRenderer();
		if (textViewport == null)
			return;

		if (textWaitingForRender)
		{
			textRenderFrameCount++;
			if (textRenderFrameCount >= 2)
			{
				var vpTex = textViewport.GetTexture();
				var resultImg = vpTex?.GetImage();
				if (resultImg != null && resultImg.GetWidth() > 0 && resultImg.GetHeight() > 0)
				{
					if (resultImg.GetFormat() != Godot.Image.Format.Rgba8)
						resultImg.Convert(Godot.Image.Format.Rgba8);
					pendingTextRenderItem.ResultImage = resultImg;
				}
				else
				{
					pendingTextRenderItem.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
				}
				pendingTextRenderItem.Completed.Set();
				pendingTextRenderItem = null;
				textWaitingForRender = false;
				textViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
			}
		}

		if (!textWaitingForRender && textRenderQueue.TryDequeue(out var item))
		{
			textViewport.Size = new Vector2I(item.Width, item.Height);
			textRenderLabel.Text = item.Text ?? "";
			textRenderLabel.Size = new Vector2(item.Width, item.Height);
			textRenderLabel.CustomMinimumSize = textRenderLabel.Size;
			textRenderLabel.AddThemeFontSizeOverride("font_size", item.FontSize);
			textRenderLabel.AddThemeColorOverride("font_color", new Color(item.Color.r, item.Color.g, item.Color.b, item.Color.a));
			textRenderLabel.Position = Vector2.Zero;

			textViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
			textRenderFrameCount = 0;
			textWaitingForRender = true;
			pendingTextRenderItem = item;
		}
	}

	/// Process pending GPU work. Called from _Process on the main thread.
	/// Frame-counted cycle: setup render → wait 2 frames → retrieve result.
	/// Falls back to CPU processing if GPU render produces no output.
	void ProcessGpuQueue()
	{
		if (!ShouldUseGpuRenderer())
		{
			while (gpuQueue.TryDequeue(out var queuedItem))
			{
				queuedItem.ResultImage = MinorShift.Emuera.Content.GraphicsImage.ApplyColorMatrixGPU(
					queuedItem.SrcImage, queuedItem.SrcRegion, queuedItem.ColorMatrix);
				queuedItem.Completed.Set();
			}
			return;
		}

		if (gpuViewport == null)
			SetupGpuRenderer();
		if (gpuViewport == null)
			return;

		// Phase 1: Wait for render to complete (2 frames after setup)
		if (gpuWaitingForRender)
		{
			gpuRenderFrameCount++;
			if (gpuRenderFrameCount >= 2)
			{
				var vpTex = gpuViewport.GetTexture();
				if (vpTex != null)
				{
					var resultImg = vpTex.GetImage();
					if (resultImg != null && resultImg.GetWidth() > 0 && resultImg.GetHeight() > 0)
					{
						if (resultImg.GetFormat() != Godot.Image.Format.Rgba8)
							resultImg.Convert(Godot.Image.Format.Rgba8);
						pendingGpuItem.ResultImage = resultImg;
					}
					else
					{
						pendingGpuItem.ResultImage = MinorShift.Emuera.Content.GraphicsImage.ApplyColorMatrixGPU(
							pendingGpuItem.SrcImage, pendingGpuItem.SrcRegion, pendingGpuItem.ColorMatrix);
					}
				}
				else
				{
					pendingGpuItem.ResultImage = MinorShift.Emuera.Content.GraphicsImage.ApplyColorMatrixGPU(
						pendingGpuItem.SrcImage, pendingGpuItem.SrcRegion, pendingGpuItem.ColorMatrix);
				}
				pendingGpuItem.Completed.Set();
				pendingGpuItem = null;
				gpuWaitingForRender = false;
				gpuViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
			}
		}

		// Phase 2: Set up next render if idle
		if (!gpuWaitingForRender && gpuQueue.TryDequeue(out var item))
		{
			var srcW = item.SrcRegion.Size.X;
			var srcH = item.SrcRegion.Size.Y;
			if (srcW > 0 && srcH > 0)
			{
				var subImg = item.SrcImage.GetRegion(item.SrcRegion);
				if (subImg != null)
				{
					var imgTex = ImageTexture.CreateFromImage(subImg);
					gpuTextureRect.Texture = imgTex;
					gpuTextureRect.Size = new Vector2(srcW, srcH);
					gpuTextureRect.Position = Vector2.Zero;
					gpuViewport.Size = item.SrcRegion.Size;

					ColorMatrixGPU.SetMatrixUniforms(gpuShaderMaterial, item.ColorMatrix);

					gpuViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
					gpuRenderFrameCount = 0;
					gpuWaitingForRender = true;
					pendingGpuItem = item;
				}
				else
				{
					// GetRegion failed, use CPU fallback
					item.ResultImage = MinorShift.Emuera.Content.GraphicsImage.ApplyColorMatrixGPU(
						item.SrcImage, item.SrcRegion, item.ColorMatrix);
					item.Completed.Set();
				}
			}
			else
			{
				item.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
				item.Completed.Set();
			}
		}
	}

	public override void _Ready()
	{
		currentInstance = this;
		FrameRateHelper.Apply();
		ResolutionHelper.Apply();
		GenericUtils.SetMainThread();
		GenericUtils.InitializeLogging();
		RuntimeDiagnosticsPanel.AttachFloatingTo(this);
		uEmuera.Logger.isEnabled = GenericUtils.IsLogEnabled;
		uEmuera.Logger.sink = GenericUtils.LogFromBridge;
		uEmuera.Logger.info = content => GenericUtils.Info(content);
		uEmuera.Logger.warn = content => GenericUtils.Warn(content);
		uEmuera.Logger.error = content => GenericUtils.Error(content);

		CreateStartupOverlay();
		CallDeferred(nameof(StartGameDeferred));
	}

	public override void _Notification(int what)
	{
		// Godot delivers these notifications for APK background/foreground
		// transitions. Handle them centrally so the emulator core does not need to
		// know about platform lifecycle details.
		if (what == NotificationApplicationPaused)
		{
			SetApplicationPaused(true);
			GenericUtils.NotifyLifecycleState("android_pause");
		}
		else if (what == NotificationApplicationResumed)
		{
			SetApplicationPaused(false);
			GenericUtils.NotifyLifecycleState("android_resume");
		}
	}

	void SetApplicationPaused(bool paused)
	{
		if (applicationPauseActive == paused)
			return;
		applicationPauseActive = paused;
		if (paused)
		{
			// Keep the main loop alive at a low cadence instead of stopping it.
			// This avoids a burst of queued work on resume while reducing battery
			// use when Android backgrounds the APK.
			maxFpsBeforeApplicationPause = Engine.MaxFps;
			Engine.MaxFps = 5;
			EmueraContent.instance?.SetApplicationPaused(true);
			return;
		}

		// Restore the user's configured frame-rate policy after the temporary
        // lifecycle cap; FrameRateHelper re-applies config in case it changed
        // while the app was backgrounded.
        Engine.MaxFps = maxFpsBeforeApplicationPause > 0
            ? maxFpsBeforeApplicationPause
            : FrameRateHelper.CurrentFrameRate;
        FrameRateHelper.ApplyConfigFps();
        EmueraContent.instance?.SetApplicationPaused(false);
    }

    async void StartGameDeferred()
    {
        if (startupStarted)
            return;
        startupStarted = true;

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree())
            return;

        UpdateStartupStatus("正在准备游戏目录...");

        // Setup path resolution
        string eraPath = FirstWindow.ResolveStartupGamePath();
        if (string.IsNullOrEmpty(eraPath) || !uEmuera.Utils.DirectoryExists(eraPath))
        {
            eraPath = ProjectSettings.GlobalizePath("res://eraAkumaMaid0.305-CH-正式版");
        }
        if (!string.IsNullOrEmpty(eraPath) && uEmuera.Utils.DirectoryExists(eraPath))
        {
            Sys.ExeDir = uEmuera.Utils.NormalizePath(eraPath + "/");
        }
        else
        {
            Sys.ExeDir = uEmuera.Utils.NormalizePath(OS.GetExecutablePath().GetBaseDir() + "/");
        }
        GenericUtils.NotifyGamePathSelected(Sys.ExeDir, FirstWindow.SelectedCoreProfileName);

        // Load SHIFT-JIS / UTF-8 config maps
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree())
            return;

        UpdateStartupStatus("Loading config...");
        LoadConfigMaps();

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree())
            return;

        UpdateStartupStatus("Creating interface...");

        // Sprite debug viewer — press F3 to toggle
        if (enable_sprite_debug_viewer && OS.GetName() != "Android")
        {
            var debugViewer = new SpriteDebugViewer();
            debugViewer.Name = "SpriteDebugViewer";
            AddChild(debugViewer);
        }

        // Create content renderer
        var content = new EmueraContent();
        content.Name = "EmueraContent";
        AddChild(content);

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInsideTree())
            return;

        UpdateStartupStatus("Starting game...");
        if (!await StartLegacySessionAsync())
        {
            UpdateStartupStatus("Unable to start game.");
            return;
        }
        working = true;

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        HideStartupOverlay();
    }

    public override void _Process(double delta)
    {
        if (ShouldUseGpuRenderer())
            GpuReady = true;
        GenericUtils.FlushLogs();
        GenericUtils.FlushUI();
        ProcessTextRenderQueue();
        if (GenericUtils.IsPerformanceSamplingEnabled)
            GenericUtils.SamplePerformanceFrame(delta, gpuQueue.Count + textRenderQueue.Count);

        if (!working)
            return;

        if (clearRequested)
        {
            clearRequested = false;
            GenericUtils.ClearText();
        }

        if (restartRequested)
        {
            restartRequested = false;
            GetTree().ReloadCurrentScene();
            return;
        }

        if (GlobalStatic.MainWindow != null)
            GlobalStatic.MainWindow.Update();

        ProcessGpuQueue();

        SpriteManager.UpdateCleanup();
        SpriteManager.UpdateOtherThreads();
        MinorShift._Library.WinInput.UpdateKeyState();

        var console = GlobalStatic.Console;
        var content = EmueraContent.instance;
        if (console != null && content != null)
        {
            bool needsInput = console.IsWaitingInputSomething;
            if (!needsInput && content.IsInputVisible())
                content.ShowInput(false);
        }
    }

	public override void _ExitTree()
	{
        GenericUtils.NotifyApplicationShutdown();
        StopLegacySession();
        working = false;
		uEmuera.Utils.ResourceClear();
		if (currentInstance == this)
			currentInstance = null;
	}

	public async void Run()
	{
		if (await StartLegacySessionAsync())
			working = true;
	}

	/// <summary>
	/// M0 runner-only same-process restart seam. This remains internal to the
	/// Godot host assembly: normal UI startup never calls it, and legacy Parser/
	/// VM semantics still own the actual session behavior.
	/// </summary>
	/// <summary>
	/// Configures the immutable, runner-only host route allowlist before this
	/// node enters the tree. Normal launcher startup never supplies this map;
	/// it therefore cannot expose a live game/profile switch UI.
	/// </summary>
	internal void ConfigureM0RunnerSessionLaunchRegistry(LegacySessionLaunchRegistry launchRegistry)
	{
		ArgumentNullException.ThrowIfNull(launchRegistry);
		if (startupStarted || legacySessionFacade is not null || legacySessionBackend is not null)
			throw new InvalidOperationException("Runner session launch routes must be configured before startup.");
		m0RunnerSessionLaunchRegistry = launchRegistry;
	}

	internal Task<LegacySessionSwitchResult> RestartLegacySessionForM0RunnerAsync()
	{
		var selection = legacySessionFacade?.Current?.Selection
			?? new SessionSelection(
				BuildLegacyGameId(Sys.ExeDir),
				FirstWindow.SelectedCoreProfileName);
		return SwitchLegacySessionForM0RunnerAsync(selection);
	}

	/// <summary>
	/// M0 runner-only cross-configuration seam. The caller can select only a
	/// pre-registered host binding; Core receives the resulting opaque game id
	/// and profile, never a filesystem path.
	/// </summary>
	internal Task<LegacySessionSwitchResult> SwitchLegacySessionForM0RunnerAsync(
		LegacySessionLaunchConfiguration launch)
	{
		ArgumentNullException.ThrowIfNull(launch);
		return SwitchLegacySessionForM0RunnerAsync(launch.CreateSelection());
	}

	async Task<LegacySessionSwitchResult> SwitchLegacySessionForM0RunnerAsync(
		SessionSelection selection)
	{
		var facade = legacySessionFacade
			?? throw new InvalidOperationException("M1 session-isolation facade is not active.");
		// EmueraThread.Running() mirrors Console.IsInProcess, which is false while
		// the legacy VM is deliberately blocked at INPUT/WAIT. The facade owns the
		// lifecycle state needed for a transactional restart.
		if (!facade.IsBackendRunning)
			throw new InvalidOperationException("Legacy session is not running and cannot be restarted.");

		GetLegacySessionLaunchRegistry().Resolve(selection);
		bool wasWorking = working;
		working = false;
		try
		{
			var result = await facade.SwitchAsync(selection, null);
			if (result.IsCommitted)
			{
				GenericUtils.Info(
					$"M1_SESSION_ISOLATION_INPROCESS_SWITCH generation={result.Session.Stamp.Generation.Value} " +
					$"profile={selection.ProfileId} plan={result.Session.Session?.Compatibility.CanonicalHash ?? "unknown"}");
			}
			return result;
		}
		finally
		{
			working = wasWorking && facade.IsBackendRunning;
		}
	}

	async Task<bool> StartLegacySessionAsync()
    {
        // This is deliberately evaluated only when starting a session. A
        // diagnostic-config hot reload must never swap an already running VM
        // between the M0 baseline path and the M1 canary path.
        if (legacySessionBackend?.IsRunning == true)
            return true;

        bool useSessionIsolation = GenericUtils.GetRuntimeDiagnosticsConfig()?.MigrationSessionIsolationEnabled == true;
        if (!useSessionIsolation)
        {
            var backend = legacySessionBackend ??= new LegacySessionBackend(debug, use_coroutine);
            try
            {
                await backend.StartLegacyBaselineAsync();
                return true;
            }
            catch (Exception error)
            {
                GenericUtils.Error($"LEGACY_BASELINE_START_EXCEPTION {error}");
                return false;
            }
        }

        try
        {
            var backend = legacySessionBackend ??= new LegacySessionBackend(
                debug,
                use_coroutine,
                GetLegacySessionLaunchRegistry());
            legacySessionFacade ??= LegacySessionFacade.CreateLegacyBaseline(
                backend);
            var selection = new SessionSelection(
                BuildLegacyGameId(Sys.ExeDir),
                FirstWindow.SelectedCoreProfileName);
			GetLegacySessionLaunchRegistry().Resolve(selection);
            var result = await legacySessionFacade.SwitchAsync(selection, null);
            if (result.IsCommitted)
            {
                GenericUtils.Info(
                    $"M1_SESSION_ISOLATION_CANARY profile={selection.ProfileId} plan={result.Session.Session?.Compatibility.CanonicalHash ?? "unknown"}");
                return true;
            }

            var error = result.BackendError ?? result.Session.Error;
            GenericUtils.Error(
                $"LEGACY_SESSION_START_FAILED status={result.Session.Status} generation={result.Session.Stamp.Generation.Value} " +
                $"error={error?.Message ?? "none"}");
            return false;
        }
        catch (Exception error)
        {
            GenericUtils.Error($"LEGACY_SESSION_START_EXCEPTION {error}");
            return false;
        }
    }

    void StopLegacySession()
    {
        var facade = legacySessionFacade;
        legacySessionFacade = null;
        if (facade is not null)
        {
            try
            {
                // The current legacy bridge completes synchronously after stopping
                // EmueraThread. Keeping the wait here preserves the former
                // _ExitTree ordering: thread end -> legacy state reset -> resources.
                facade.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                GenericUtils.Error($"LEGACY_SESSION_STOP_FAILED {error}");
            }
            return;
        }

        var backend = legacySessionBackend;
        if (backend is null || !backend.IsRunning)
            return;

        try
        {
            backend.StopLegacyBaselineAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            GenericUtils.Error($"LEGACY_BASELINE_STOP_FAILED {error}");
        }
    }

    static string BuildLegacyGameId(string gameDirectory)
    {
        return LegacySessionLaunchConfiguration.CreateGameId(gameDirectory);
    }

	LegacySessionLaunchRegistry GetLegacySessionLaunchRegistry()
	{
		return m0RunnerSessionLaunchRegistry ?? new LegacySessionLaunchRegistry(
			new[]
			{
				new LegacySessionLaunchConfiguration(
					Sys.ExeDir,
					FirstWindow.SelectedCoreProfileName),
			});
	}

    public void Clear()
    {
        if (working)
        {
            // Request clear on next process
            clearRequested = true;
        }
    }

    public void Restart()
    {
        if (working)
        {
            restartRequested = true;
        }
    }

    bool working = false;
    bool clearRequested = false;
    bool restartRequested = false;

    void CreateStartupOverlay()
    {
        startupOverlay = new Control();
        startupOverlay.Name = "StartupOverlay";
        startupOverlay.AnchorLeft = 0;
        startupOverlay.AnchorTop = 0;
        startupOverlay.AnchorRight = 1;
        startupOverlay.AnchorBottom = 1;
        startupOverlay.MouseFilter = Control.MouseFilterEnum.Stop;

        var bg = new ColorRect();
        bg.AnchorLeft = 0;
        bg.AnchorTop = 0;
        bg.AnchorRight = 1;
        bg.AnchorBottom = 1;
        bg.Color = Colors.Black;
        startupOverlay.AddChild(bg);

        startupStatusLabel = new Label();
        startupStatusLabel.AnchorLeft = 0;
        startupStatusLabel.AnchorTop = 0;
        startupStatusLabel.AnchorRight = 1;
        startupStatusLabel.AnchorBottom = 1;
        startupStatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        startupStatusLabel.VerticalAlignment = VerticalAlignment.Center;
        startupStatusLabel.Text = "Loading game...";
        startupStatusLabel.AddThemeFontSizeOverride("font_size", 20);
        startupStatusLabel.AddThemeColorOverride("font_color", Colors.White);
        startupOverlay.AddChild(startupStatusLabel);

        AddChild(startupOverlay);
    }

    void UpdateStartupStatus(string status)
    {
        if (startupStatusLabel != null)
            startupStatusLabel.Text = status;
    }

    void HideStartupOverlay()
    {
        if (startupOverlay == null)
            return;

        startupOverlay.QueueFree();
        startupOverlay = null;
        startupStatusLabel = null;
    }

    void LoadConfigMaps()
    {
        lock (configMapCacheLock)
        {
            if (cachedShiftJisToUtf8Map != null && cachedUtf8ZhCnToUtf8Map != null)
            {
                uEmuera.Utils.SetSHIFTJIS_to_UTF8Dict(cachedShiftJisToUtf8Map);
                uEmuera.Utils.SetUTF8ZHCN_to_UTF8Dict(cachedUtf8ZhCnToUtf8Map);
                return;
            }
        }

		char[] split = new char[] { '\r', '\n' };
        var shiftjisPath = "res://Text/emuera_config_shiftjis.bytes";
        var utf8Path = "res://Text/emuera_config_utf8.txt";
        var utf8CnPath = "res://Text/emuera_config_utf8_zhcn.txt";

        if (!Godot.FileAccess.FileExists(shiftjisPath) ||
            !Godot.FileAccess.FileExists(utf8Path) ||
            !Godot.FileAccess.FileExists(utf8CnPath))
            return;

        var shiftjisBytes = Godot.FileAccess.GetFileAsBytes(shiftjisPath);
        var utf8Text = Godot.FileAccess.GetFileAsString(utf8Path);
        var utf8CnText = Godot.FileAccess.GetFileAsString(utf8CnPath);

        var jis_md5_strs = GenericUtils.CalcMd5List(shiftjisBytes);

        var utf8_strs = utf8Text.Split(split, System.StringSplitOptions.RemoveEmptyEntries);
        var utf8_str_list = new System.Collections.Generic.List<string>();
        foreach (var str in utf8_strs)
        {
            if (string.IsNullOrWhiteSpace(str))
                continue;
            utf8_str_list.Add(str);
        }

        var utf8cn_strs = utf8CnText.Split(split, System.StringSplitOptions.RemoveEmptyEntries);
        var utf8cn_str_list = new System.Collections.Generic.List<string>();
        foreach (var str in utf8cn_strs)
        {
            if (string.IsNullOrWhiteSpace(str))
                continue;
            utf8cn_str_list.Add(str);
        }

        if (jis_md5_strs.Count == 0 || utf8_str_list.Count == 0)
            return;

        var jis_map = new System.Collections.Generic.Dictionary<string, string>();
        int jisCount = System.Math.Min(jis_md5_strs.Count, utf8_str_list.Count);
        for (int i = 0; i < jisCount; ++i)
        {
            jis_map[jis_md5_strs[i]] = utf8_str_list[i];
        }
        var utf8cn_map = new System.Collections.Generic.Dictionary<string, string>();
        int utf8CnCount = System.Math.Min(utf8cn_str_list.Count, utf8_str_list.Count);
        for (int i = 0; i < utf8CnCount; ++i)
        {
            utf8cn_map[utf8cn_str_list[i]] = utf8_str_list[i];
        }
        lock (configMapCacheLock)
        {
            // res://Text 配置映射在进程内不变化，缓存后重启游戏不再重复读盘和构建字典。
            cachedShiftJisToUtf8Map ??= jis_map;
            cachedUtf8ZhCnToUtf8Map ??= utf8cn_map;
            uEmuera.Utils.SetSHIFTJIS_to_UTF8Dict(cachedShiftJisToUtf8Map);
            uEmuera.Utils.SetUTF8ZHCN_to_UTF8Dict(cachedUtf8ZhCnToUtf8Map);
        }
    }
}

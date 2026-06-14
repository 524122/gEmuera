using Godot;
using MinorShift.Emuera;
using System.Threading;
using gEmuera.Diagnostics;

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

    /// <summary>
    /// True once _Process has been called at least once, indicating the main loop is running
    /// and the SubViewport render pipeline is ready to process GPU work.
    /// </summary>
    public static bool GpuReady => EmueraGpuRenderComponent.GpuReady;

    /// Submit ColorMatrix work from any thread. Returns the GpuWorkItem for direct wait.
    public static GpuWorkItem GpuSubmitColorMatrix(Godot.Image src, Godot.Rect2I region, float[][] cm)
    {
        return EmueraGpuRenderComponent.Submit(src, region, cm);
    }

    public static TextRenderItem SubmitTextRender(string text, string fontName, int fontSize, int fontStyle, uEmuera.Drawing.Color color, int width, int height)
    {
        return EmueraTextRenderComponent.Submit(text, fontName, fontSize, fontStyle, color, width, height);
    }

    bool startupStarted = false;
    EmueraGpuRenderComponent gpuRenderComponent;
    EmueraTextRenderComponent textRenderComponent;
    EmueraStartupComponent startupComponent;
    EmueraLifecycleComponent lifecycleComponent;
    EmueraStartupOverlayView startupOverlay;

    public override void _Ready()
    {
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

        InstallHostComponents();
        CallDeferred(nameof(StartGameDeferred));
    }

    public override void _Notification(int what)
    {
        lifecycleComponent?.HandleNotification(what);
    }

    async void StartGameDeferred()
    {
        if (startupStarted)
            return;
        startupStarted = true;

        if (startupComponent == null || !await startupComponent.PrepareAsync())
            return;

        startupOverlay?.SetStatus("Creating interface...");

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

        startupOverlay?.SetStatus("Starting game...");
        // Start the engine
        EmueraThread.instance.Start(debug, use_coroutine);
        working = true;

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        HideStartupOverlay();
    }

    public override void _Process(double delta)
    {
        gpuRenderComponent?.MarkFrameReady();
        GenericUtils.FlushLogs();
        GenericUtils.FlushUI();
        textRenderComponent?.ProcessQueue();
        if (GenericUtils.IsPerformanceSamplingEnabled)
        {
            GenericUtils.SamplePerformanceFrame(
                delta,
                EmueraGpuRenderComponent.QueuedWorkCount + EmueraTextRenderComponent.QueuedWorkCount);
        }

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

        gpuRenderComponent?.ProcessQueue();

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
        EmueraThread.instance.End();
        working = false;
        GlobalStatic.Reset();
        uEmuera.Utils.ResourceClear();
    }

    public void Run()
    {
        EmueraThread.instance.Start(debug, use_coroutine);
        working = true;
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

    void InstallHostComponents()
    {
        lifecycleComponent = new EmueraLifecycleComponent();
        lifecycleComponent.Name = "LifecycleComponent";
        AddChild(lifecycleComponent);

        gpuRenderComponent = new EmueraGpuRenderComponent();
        gpuRenderComponent.Name = "GpuRenderComponent";
        AddChild(gpuRenderComponent);

        textRenderComponent = new EmueraTextRenderComponent();
        textRenderComponent.Name = "TextRenderComponent";
        AddChild(textRenderComponent);

        startupComponent = new EmueraStartupComponent();
        startupComponent.Name = "StartupComponent";
        startupComponent.StatusChanged += OnStartupStatusChanged;
        AddChild(startupComponent);

        startupOverlay = new EmueraStartupOverlayView();
        startupOverlay.Build();
        AddChild(startupOverlay);
    }

    void OnStartupStatusChanged(string status)
    {
        startupOverlay?.SetStatus(status);
    }

    void HideStartupOverlay()
    {
        if (startupOverlay == null)
            return;

        startupOverlay.QueueFree();
        startupOverlay = null;
    }
}

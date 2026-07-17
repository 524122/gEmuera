using System;
using System.Threading;
using Godot;
using MinorShift.Emuera.GameProc;
using gEmuera.GodotHost;

public class EmueraThread
{
    const int StopJoinTimeoutMilliseconds = 2000;

    public static EmueraThread instance { get { return instance_; } }
    static EmueraThread instance_ = new EmueraThread();

    EmueraThread()
    { }

    public void Start(bool debug, bool use_coroutine)
    {
        lock (lifecycleGate)
        {
            // A thread reference may remain after a prior End timeout. It is
            // safe to clean up only once IsAlive proves that worker exited.
            if (thread != null && thread.IsAlive)
            {
                throw new InvalidOperationException(
                    "Cannot start a legacy session while the previous worker is still alive.");
            }

            ReleaseStoppedWorkerResourcesLocked();
            debugmode = debug;
            running = true;
            inputEvent = new ManualResetEventSlim(false);
            var worker = new Thread(Work);
            thread = worker;
            try
            {
                worker.Start();
            }
            catch
            {
                thread = null;
                running = false;
                inputEvent.Dispose();
                inputEvent = null;
                throw;
            }
        }
    }

    public void End()
    {
        lock (lifecycleGate)
        {
            if(!running && thread == null)
                return;

            running = false;
            // Wake up the input wait loop before waiting. If it does not stop,
            // leave both the thread and its event intact for a later cleanup.
            inputEvent?.Set();
            if(thread != null)
            {
                LegacyThreadQuiescence.WaitForStopOrThrow(
                    thread,
                    TimeSpan.FromMilliseconds(StopJoinTimeoutMilliseconds),
                    "ending the legacy session");
            }

            ReleaseStoppedWorkerResourcesLocked();
        }
    }

    /// <summary>
    /// Lifecycle state for host start/stop decisions. Unlike Running(), this
    /// remains true while the legacy VM is blocked at INPUT/WAIT.
    /// </summary>
    public bool IsSessionActive
    {
        get
        {
            lock (lifecycleGate)
                return thread != null && thread.IsAlive;
        }
    }

    public bool Running()
    {
        var console = MinorShift.Emuera.GlobalStatic.Console;
        if(console != null && console.IsInProcess)
            return true;
        return false;
    }

    public void Input(string c, bool from_button, bool skip = false, int mouseButton = 0,
        bool requestDynamicMapFollowBottom = false, int dynamicMapScrollInteractionSerial = int.MinValue)
    {
        var console = MinorShift.Emuera.GlobalStatic.Console;
        if(console == null)
            return;
        if(!from_button && console.IsWaitingInputSomething)
        {
            if (gEmuera.M0.LegacyTrace.IsEnabled)
            {
                gEmuera.M0.LegacyTrace.TryRecordInput("submission_rejected", gEmuera.M0.LegacyTraceThreadOwner.GodotMain,
                    gEmuera.M0.LegacyTraceOrderingPoint.InputSubmission, c, from_button, skip, mouseButton, false,
                    console.InputType.ToString(), console.NewButtonGeneration);
            }
            if (GenericUtils.IsScrollTraceActive)
            {
                GenericUtils.ScrollTrace("input",
                    () => $"submit_ignored fromButton={from_button} skip={skip} mouse={mouseButton} value={GenericUtils.ClipTrace(c, 64)} {FormatCurrentCoreLineForTrace()}");
            }
            if (GenericUtils.IsSaveLogOperationCaptureEnabled)
            {
                string ignoredLine = FormatCurrentCoreLineForTrace();
                GenericUtils.CaptureSaveLogOperation("keyboard_ignored", c, ignoredLine, ignoredLine, FormatWaitState(console), false);
            }
            return;
        }
        long submissionSequence = 0;
        bool accepted = false;
        lock (inputGate)
        {
            // 输入文本、鼠标键、跳过标记和显示来源序号必须作为一个整体交给工作线程。
            // 单槽尚未被取走时拒绝后续提交，避免连续点击把旧输入替换成新输入，
            // 也保证序号与脚本实际消费的输入严格一一对应。
            if (pendingInput == null)
            {
                submissionSequence = ++inputSubmissionSequence;
                pendingInput = new PendingInput(c, skip, mouseButton, from_button, submissionSequence,
                    requestDynamicMapFollowBottom, dynamicMapScrollInteractionSerial);
                accepted = true;
            }
        }
        if (!accepted)
        {
            if (gEmuera.M0.LegacyTrace.IsEnabled)
            {
                gEmuera.M0.LegacyTrace.TryRecordInput("submission_rejected", gEmuera.M0.LegacyTraceThreadOwner.GodotMain,
                    gEmuera.M0.LegacyTraceOrderingPoint.InputSubmission, c, from_button, skip, mouseButton, false,
                    console.InputType.ToString(), console.NewButtonGeneration);
            }
            if (GenericUtils.IsScrollTraceActive)
            {
                GenericUtils.ScrollTrace("input",
                    () => $"submit_ignored_pending fromButton={from_button} skip={skip} mouse={mouseButton} value={GenericUtils.ClipTrace(c, 64)} {FormatCurrentCoreLineForTrace()}");
            }
            return;
        }
        if (GenericUtils.IsScrollTraceActive)
        {
            GenericUtils.ScrollTrace("input",
                () => $"submit fromButton={from_button} skip={skip} mouse={mouseButton} value={GenericUtils.ClipTrace(c, 64)} waiting={console.IsWaitingInput} enter={console.IsWaitingEnterKey} any={console.IsWaitAnyKey} {FormatCurrentCoreLineForTrace()}");
        }
        if (GenericUtils.IsInputReplayCaptureEnabled)
        {
            var replayPosition = GetReplayPointerPosition();
            GenericUtils.CaptureInputReplay(from_button ? "button" : "keyboard", c,
                replayPosition, replayPosition, FormatWaitState(console), false);
        }
        if (GenericUtils.IsInputTraceEnabled("submit"))
            GenericUtils.InputTrace("INPUT.SUBMIT", () => "input submit", () => $"fromButton={from_button} skip={skip} mouse={mouseButton}");
        if (GenericUtils.IsScrollTraceActive)
            GenericUtils.StartScrollTraceCoreWindow(() => $"input fromButton={from_button} mouse={mouseButton} value={GenericUtils.ClipTrace(c, 64)}");
        if (gEmuera.M0.LegacyTrace.IsEnabled)
        {
            gEmuera.M0.LegacyTrace.TryRecordInput("submitted", gEmuera.M0.LegacyTraceThreadOwner.GodotMain,
                gEmuera.M0.LegacyTraceOrderingPoint.InputSubmission, c, from_button, skip, mouseButton, true,
                console.InputType.ToString(), console.NewButtonGeneration);
        }
        SignalInputEvent();
    }

    public bool IsSkipFlag { get { return skipflag; } }

    void Work()
    {
        MinorShift.Emuera.Program.debugMode = debugmode;
        MinorShift.Emuera.Program.Main(new string[0] { });

        uEmuera.Utils.ResourceClear();
        GC.Collect();

        ClearPendingInput();
        var console = MinorShift.Emuera.GlobalStatic.Console;
        var random = new System.Random();
        while(running)
        {
            skipflag = false;
            inputEvent.Reset();
            PendingInput nextInput = null;

            while((nextInput = TakePendingInput()) == null)
            {
                // Block efficiently until Input() is called or a short timeout expires
                inputEvent.Wait(100);
                if(!running)
                    return;
                uEmuera.Forms.Timer.Update();
            }

            if(console.IsWaitingInput)
            {
                skipflag = nextInput.Skip;
                string originalInput = nextInput.Value;
                int originalMouseButton = nextInput.MouseButton;
                bool originalFromButton = nextInput.FromButton;
                long originalInputSubmissionSequence = nextInput.SubmissionSequence;
                if (nextInput.RequestDynamicMapFollowBottom)
                {
                    GenericUtils.RequestDynamicMapUserNavigationFollowBottom(
                        originalInputSubmissionSequence,
                        nextInput.DynamicMapScrollInteractionSerial);
                }
                string codeBefore = null;
                if (gEmuera.M0.LegacyTrace.IsEnabled)
                {
                    gEmuera.M0.LegacyTrace.TryRecordInput("consumption_started", gEmuera.M0.LegacyTraceThreadOwner.LegacyVm,
                        gEmuera.M0.LegacyTraceOrderingPoint.VmConsumption, originalInput, originalFromButton, skipflag,
                        originalMouseButton, true, console.InputType.ToString(), console.NewButtonGeneration);
                }
                if (GenericUtils.IsScrollTraceActive)
                {
                    GenericUtils.ScrollTrace("input", () =>
                    {
                        codeBefore ??= FormatCurrentCoreLineForTrace();
                        return $"consume skip={skipflag} mouse={originalMouseButton} value={GenericUtils.ClipTrace(originalInput, 64)} enter={console.IsWaitingEnterKey} any={console.IsWaitAnyKey} {codeBefore}";
                    });
                }
                if (GenericUtils.IsInputReplayCaptureEnabled)
                {
                    var replayPosition = GetReplayPointerPosition();
                    GenericUtils.CaptureInputReplay(originalMouseButton != 0 ? "button_consume" : "keyboard_consume", originalInput,
                        replayPosition, replayPosition, FormatWaitState(console), true);
                }
                if (GenericUtils.IsInputTraceEnabled("consume"))
                    GenericUtils.InputTrace("INPUT.CONSUME", () => "input consume", () => $"skip={skipflag} mouse={originalMouseButton}");
                bool consumed = false;
                console.SetCurrentInputSubmissionSequence(originalInputSubmissionSequence);
                try
                {
                    if(originalMouseButton != 0)
                        MinorShift.Emuera.GlobalStatic.Process?.InputInteger(1, originalMouseButton);
                    // 右/中键点击空区域时 input=""，IntValue 状态下 PressEnterKey 无法解析空字符串会直接
                    // return false，导致等待不推进、脚本永远读不到 RESULT:1。
                    // eraFL(USERCOM_INPUT.ERB) 判定"未点击任何按钮"的条件是 RESULT:0 == -1，
                    // 不是 0——必须补 "-1" 而不是 "0"，否则 RESULT:0==-1 的判断永远不成立。
                    string submitInput = console.IsWaitingEnterKey ? "" : originalInput;
                    if (MinorShift.Emuera.Program.IsEraFlProfile)
                    {
                        submitInput = GEmuera.Core.Compatibility.EraFlCompatibilityModule
                            .NormalizePointerIntegerSubmission(
                                submitInput,
                                originalMouseButton,
                                console.InputType == MinorShift.Emuera.GameProc.InputType.IntValue);
                    }
                    console.PressEnterKey(skipflag, submitInput, originalMouseButton != 0);
                    consumed = true;
                }
                finally
                {
                    console.SetCurrentInputSubmissionSequence(0);
                    if (gEmuera.M0.LegacyTrace.IsEnabled)
                    {
                        gEmuera.M0.LegacyTrace.TryRecordInput("consumption_finished", gEmuera.M0.LegacyTraceThreadOwner.LegacyVm,
                            gEmuera.M0.LegacyTraceOrderingPoint.VmConsumption, originalInput, originalFromButton, skipflag,
                            originalMouseButton, consumed, console.InputType.ToString(), console.NewButtonGeneration);
                    }
                    if (GenericUtils.IsSaveLogOperationCaptureEnabled)
                    {
                        codeBefore ??= FormatCurrentCoreLineForTrace();
                        GenericUtils.CaptureSaveLogOperation(
                            originalMouseButton != 0 ? "button" : "keyboard",
                            originalInput,
                            codeBefore,
                            FormatCurrentCoreLineForTrace(),
                            FormatWaitState(console),
                            consumed);
                    }
                    CompletePendingInput(nextInput);
                }
            }
            else
            {
                CompletePendingInput(nextInput);
            }
        }
    }

    readonly object lifecycleGate = new object();
    readonly object inputGate = new object();
    Thread thread = null;
    ManualResetEventSlim inputEvent;
    bool debugmode;
    volatile bool running;
    volatile bool skipflag;
    long inputSubmissionSequence;
    PendingInput pendingInput;

    sealed class PendingInput
    {
        public readonly string Value;
        public readonly bool Skip;
        public readonly int MouseButton;
        public readonly bool FromButton;
        public readonly long SubmissionSequence;
        public readonly bool RequestDynamicMapFollowBottom;
        public readonly int DynamicMapScrollInteractionSerial;

        public PendingInput(string value, bool skip, int mouseButton, bool fromButton, long submissionSequence,
            bool requestDynamicMapFollowBottom, int dynamicMapScrollInteractionSerial)
        {
            Value = value;
            Skip = skip;
            MouseButton = mouseButton;
            FromButton = fromButton;
            SubmissionSequence = submissionSequence;
            RequestDynamicMapFollowBottom = requestDynamicMapFollowBottom;
            DynamicMapScrollInteractionSerial = dynamicMapScrollInteractionSerial;
        }
    }

    PendingInput TakePendingInput()
    {
        lock (inputGate)
            return pendingInput;
    }

    void CompletePendingInput(PendingInput completedInput)
    {
        lock (inputGate)
        {
            // 处理期间保留占用信封，避免双击在下一次 INPUT 中被当作新输入消费。
            if (ReferenceEquals(pendingInput, completedInput))
                pendingInput = null;
        }
    }

    void ClearPendingInput()
    {
        lock (inputGate)
            pendingInput = null;
    }

    void ReleaseStoppedWorkerResourcesLocked()
    {
        if (thread != null && thread.IsAlive)
        {
            throw new InvalidOperationException(
                "Cannot release legacy thread resources while the worker is still alive.");
        }

        thread = null;
        inputEvent?.Dispose();
        inputEvent = null;
        ClearPendingInput();
        skipflag = false;
    }

    void SignalInputEvent()
    {
        ManualResetEventSlim eventToSignal;
        lock (lifecycleGate)
            eventToSignal = inputEvent;

        try
        {
            eventToSignal?.Set();
        }
        catch (ObjectDisposedException)
        {
            // End may have completed after Input captured the old event. The
            // input belongs to a stopped session and must not revive it.
        }
    }

    static string FormatCurrentCoreLineForTrace()
    {
        var line = MinorShift.Emuera.GlobalStatic.Process?.getCurrentLine;
        if (line == null)
            return "line=<null>";
        string position = line.Position == null ? "<unknown>" : $"{line.Position.Filename}:{line.Position.LineNo}";
        string label = line.ParentLabelLine == null ? "" : $"@{line.ParentLabelLine.LabelName}";
        string op = line is InstructionLine instruction ? instruction.Function.Name : line.GetType().Name;
        string raw;
        try
        {
            raw = GenericUtils.ClipTrace(line.ToString(), 160);
        }
        catch (Exception ex)
        {
            raw = "<raw_error:" + ex.GetType().Name + ">";
        }
        return $"line={position} label={label} op={op} raw={raw}";
    }

    static Vector2 GetReplayPointerPosition()
    {
        var p = GenericUtils.GetPointerPosition();
        return new Vector2(p.X, p.Y);
    }

    static string FormatWaitState(MinorShift.Emuera.GameView.EmueraConsole console)
    {
        if (console == null)
            return "none";
        return "input=" + console.IsWaitingInput
            + ",enter=" + console.IsWaitingEnterKey
            + ",any=" + console.IsWaitAnyKey
            + ",something=" + console.IsWaitingInputSomething;
    }
}

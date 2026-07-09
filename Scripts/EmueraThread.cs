using System;
using System.Threading;
using Godot;
using MinorShift.Emuera.GameProc;

public class EmueraThread
{
    public static EmueraThread instance { get { return instance_; } }
    static EmueraThread instance_ = new EmueraThread();

    EmueraThread()
    { }

    public void Start(bool debug, bool use_coroutine)
    {
        debugmode = debug;
        running = true;
        if(inputEvent == null)
            inputEvent = new ManualResetEventSlim(false);
        if(thread != null)
        {
            thread.Join(1000);
            thread = null;
        }
        thread = new Thread(Work);
        thread.Start();
    }

    public void End()
    {
        if(!running && thread == null)
            return;
        running = false;
        // Wake up the input wait loop
        inputEvent?.Set();
        if(thread != null)
        {
            thread.Join(2000);
            thread = null;
        }
        inputEvent?.Dispose();
        inputEvent = null;
    }

    public bool Running()
    {
        var console = MinorShift.Emuera.GlobalStatic.Console;
        if(console != null && console.IsInProcess)
            return true;
        return false;
    }

    public void Input(string c, bool from_button, bool skip = false, int mouseButton = 0)
    {
        var console = MinorShift.Emuera.GlobalStatic.Console;
        if(console == null)
            return;
        if(!from_button && console.IsWaitingInputSomething)
        {
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
        input = c;
        skipflag = skip;
        inputMouseButton = mouseButton;
        inputEvent?.Set();
    }

    public bool IsSkipFlag { get { return skipflag; } }

    void Work()
    {
        MinorShift.Emuera.Program.debugMode = debugmode;
        MinorShift.Emuera.Program.Main(new string[0] { });

        uEmuera.Utils.ResourceClear();
        GC.Collect();

        input = null;
        var console = MinorShift.Emuera.GlobalStatic.Console;
        var random = new System.Random();
        while(running)
        {
            skipflag = false;
            inputEvent.Reset();

            while(input == null)
            {
                // Block efficiently until Input() is called or a short timeout expires
                inputEvent.Wait(100);
                if(!running)
                    return;
                uEmuera.Forms.Timer.Update();
            }

            if(console.IsWaitingInput)
            {
                string originalInput = input;
                int originalMouseButton = inputMouseButton;
                string codeBefore = null;
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
                try
                {
                    if(console.IsWaitingEnterKey)
                        input = "";
                    if(originalMouseButton != 0)
                        MinorShift.Emuera.GlobalStatic.Process?.InputInteger(1, originalMouseButton);
                    // 右/中键点击空区域时 input=""，IntValue 状态下 PressEnterKey 无法解析空字符串会直接
                    // return false，导致等待不推进、脚本永远读不到 RESULT:1。
                    // eraFL(USERCOM_INPUT.ERB) 判定"未点击任何按钮"的条件是 RESULT:0 == -1，
                    // 不是 0——必须补 "-1" 而不是 "0"，否则 RESULT:0==-1 的判断永远不成立。
                    string submitInput = input;
                    if (originalMouseButton != 0x01 && originalMouseButton != 0
                        && string.IsNullOrEmpty(submitInput)
                        && console.InputType == MinorShift.Emuera.GameProc.InputType.IntValue)
                    {
                        submitInput = "-1";
                    }
                    console.PressEnterKey(skipflag, submitInput, originalMouseButton != 0);
                    consumed = true;
                }
                finally
                {
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
                }
            }
            input = null;
            inputMouseButton = 0;
        }
    }

    Thread thread = null;
    ManualResetEventSlim inputEvent = new ManualResetEventSlim(false);
    bool debugmode;
    volatile bool running;
    volatile string input;
    volatile bool skipflag;
    volatile int inputMouseButton;

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

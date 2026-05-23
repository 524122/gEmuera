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
            GenericUtils.ScrollTrace("input", $"submit_ignored fromButton={from_button} skip={skip} mouse={mouseButton} value={GenericUtils.ClipTrace(c, 64)} {FormatCurrentCoreLineForTrace()}");
            return;
        }
        GenericUtils.ScrollTrace("input", $"submit fromButton={from_button} skip={skip} mouse={mouseButton} value={GenericUtils.ClipTrace(c, 64)} waiting={console.IsWaitingInput} enter={console.IsWaitingEnterKey} any={console.IsWaitAnyKey} {FormatCurrentCoreLineForTrace()}");
        GenericUtils.StartScrollTraceCoreWindow($"input fromButton={from_button} mouse={mouseButton} value={GenericUtils.ClipTrace(c, 64)}");
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
                GenericUtils.ScrollTrace("input", $"consume skip={skipflag} mouse={inputMouseButton} value={GenericUtils.ClipTrace(input, 64)} enter={console.IsWaitingEnterKey} any={console.IsWaitAnyKey} {FormatCurrentCoreLineForTrace()}");
                if(console.IsWaitingEnterKey)
                    input = "";
                if(inputMouseButton != 0)
                    MinorShift.Emuera.GlobalStatic.Process?.InputInteger(1, inputMouseButton);
                console.PressEnterKey(skipflag, input, inputMouseButton != 0);
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
        return $"line={position} label={label} op={op}";
    }
}

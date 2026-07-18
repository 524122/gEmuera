using System;
using uEmuera.Forms;
using uEmuera.Drawing;
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.GameView;
using MinorShift._Library;
using System.Threading;

namespace uEmuera.Window
{
    public class DebugDialog : IDisposable
    {
        public void Dispose()
        { }

        internal void SetParent(EmueraConsole emueraConsole, Process emuera)
        {
            //throw new NotImplementedException();
        }

        internal void Show()
        {
            //throw new NotImplementedException();
        }

        internal void Focus()
        {
            //throw new NotImplementedException();
        }

        public bool Created { get { return true; } }
    }

    public class MainWindow : IDisposable
    {
        public static string uEmueraVer = "";

        public MainWindow()
        {}

        public void Dispose()
        { }

        public void clear_richText()
        {
            //uEmuera.Logger.Info("MainWindow.clear_richText");
            //throw new NotImplementedException();
        }
        public void Focus()
        {
            //uEmuera.Logger.Info("MainWindow.Focus");
            //throw new NotImplementedException();
        }

        public int Refresh()
        {
            //uEmuera.Logger.Info("MainWindow.Refresh");
            dirty_ = true;
            return Interlocked.Increment(ref refreshRequestGeneration);
        }

        public void Close()
        {
            uEmuera.Logger.Info("MainWindow.Close");
            //throw new NotImplementedException();
        }
        public void update_lastinput()
        {
            uEmuera.Logger.Info("MainWindow.update_lastinput");
            //throw new NotImplementedException();
        }

        internal void Reboot()
        {
            uEmuera.Logger.Info("MainWindow.Reboot");
            //throw new NotImplementedException();
        }

        internal void ShowConfigDialog()
        {
            uEmuera.Logger.Info("MainWindow.ShowConfigDialog");
            //throw new NotImplementedException();
        }

        public void Init()
        {
            if(created_)
                return;
            created_ = true;
            console_ = new EmueraConsole(this);
            console_.Initialize();
        }
        public void Update()
        {
            //uEmuera.Logger.Info("MainWindow.Update");
            if(console_ == null)
                return;
            if(GenericUtils.HasPendingDisplayWork)
                return;

            WinmmTimer.FrameStart();

            if(console_.IsInitializing)
            {
                ShowProcess();
                if(!dirty_)
                    return;
            }
            else if(console_.IsInProcess)
            {
                CheckProcess();
                if(wait_process && !EmueraThread.instance.IsSkipFlag)
                    return;
                if(!dirty_)
                    return;
            }
            else if(!dirty_)
            {
                return;
            }

            dirty_ = false;

            GenericUtils.SetBackgroundColor(console_.bgColor);

            int prev = GenericUtils.GetTextMaxLineNo();
            int min_lineno = GenericUtils.GetTextMinLineNo();
            var displayLines = console_.GetDisplayLinesSnapshotForuEmuera(
                min_lineno, out int console_count, out _);
            int snapshotCount = displayLines.Length;
            if(console_count == 0)
            {
                if(console_.IsInProcess)
                {
                    dirty_ = true;
                    Volatile.Write(ref processedRefreshGeneration, Volatile.Read(ref refreshRequestGeneration));
                    return;
                }
                //清空
                GenericUtils.ClearText();
                Volatile.Write(ref processedRefreshGeneration, Volatile.Read(ref refreshRequestGeneration));
                return;
            }

            bool need_update_flag = false;
            int removeBottomCount = 0;
            System.Collections.Generic.List<(ConsoleDisplayLine Line, bool Update)> linesToAdd = null;
            System.Collections.Generic.List<ConsoleDisplayLine> linesToRefreshData = null;
            int newMaxLineNo = GetSnapshotMaxLineNo(displayLines);
            bool fullReset = prev >= 0 && min_lineno >= 0 && newMaxLineNo >= 0 && newMaxLineNo < min_lineno;

            if(prev >= 0 && newMaxLineNo >= 0)
            {
                if(fullReset)
                {
                    removeBottomCount = prev - min_lineno + 1;
                    need_update_flag = true;
                }
                else if(prev > newMaxLineNo)
                {
                    removeBottomCount = prev - newMaxLineNo;
                    need_update_flag = true;
                }
            }

            for(int i = 0; i < snapshotCount; i++)
            {
                var line = displayLines[i];
                if(line == null)
                    continue;

                bool isKnownRenderedLine = !fullReset && prev >= 0 && min_lineno >= 0
                    && line.LineNo >= min_lineno && line.LineNo <= prev;
                if(!fullReset && prev >= 0 && min_lineno >= 0 && line.LineNo < min_lineno)
                    continue;

                bool isUpdate = isKnownRenderedLine;
                if(isKnownRenderedLine)
                {
                    var existing = GenericUtils.GetText(line.LineNo);
                    if(ShouldReuseRenderedLine(existing, line, out bool refreshDataOnly))
                    {
                        if(refreshDataOnly)
                        {
                            if(linesToRefreshData == null)
                                linesToRefreshData = new System.Collections.Generic.List<ConsoleDisplayLine>();
                            linesToRefreshData.Add(line);
                        }
                        continue;
                    }
                    need_update_flag = true;
                }

                if(linesToAdd == null)
                    linesToAdd = new System.Collections.Generic.List<(ConsoleDisplayLine Line, bool Update)>();
                linesToAdd.Add((line, isUpdate));
            }

            EmueraDisplayScrollMode scrollMode = DecideScrollModeForDisplayDelta(prev, removeBottomCount, need_update_flag,
                linesToAdd, linesToRefreshData);

            GenericUtils.ApplyTextChanges(removeBottomCount, linesToAdd, need_update_flag, console_.LastButtonGeneration, scrollMode, linesToRefreshData);

            GenericUtils.ShowIsInProcess(false);
            GenericUtils.RefreshCBG(console_);
            if (console_.NeedSetTimer())
                EmueraThread.instance.WakeForTimerSchedule();
            last_process_tic = 0;
            Volatile.Write(ref processedRefreshGeneration, Volatile.Read(ref refreshRequestGeneration));
        }

        static EmueraDisplayScrollMode DecideScrollModeForDisplayDelta(int previousMaxLineNo, int removeBottomCount, bool update,
            System.Collections.Generic.List<(ConsoleDisplayLine Line, bool Update)> linesToAdd,
            System.Collections.Generic.List<ConsoleDisplayLine> dataOnlyLines)
        {
            // 状态面板等页面通常通过删除底部旧行再重画当前屏幕来刷新。
            // 这不是“追加新文本”，因此不能触发 ScrollContainer 自动滚到底；否则 Android 会在重绘时把视口拖走。
            // 只有确实新增了显示行时才追底，避免把内容类型识别重新耦合回滚动策略。
            if ((linesToAdd == null || linesToAdd.Count == 0)
                && (dataOnlyLines == null || dataOnlyLines.Count == 0))
                return EmueraDisplayScrollMode.PreserveViewport;

            bool hasAppendAfterPreviousMax = false;
            int linesToAddCount = linesToAdd?.Count ?? 0;
            for (int i = 0; i < linesToAddCount; i++)
            {
                var item = linesToAdd[i];
                if (item.Line == null)
                    continue;
                if (!item.Update && item.Line.LineNo > previousMaxLineNo)
                {
                    hasAppendAfterPreviousMax = true;
                    break;
                }
            }

            // 普通会话/泡茶等输出可能在追加新文本的同时刷新旧行元数据。
            // 只要确实出现了新行，就按普通 Emuera 输出追到底部。
            if (hasAppendAfterPreviousMax)
                return EmueraDisplayScrollMode.FollowBottom;

            if (removeBottomCount > 0 || update)
                return EmueraDisplayScrollMode.PreserveViewport;

            return EmueraDisplayScrollMode.PreserveViewport;
        }

        static int GetSnapshotMaxLineNo(ConsoleDisplayLine[] lines)
        {
            if(lines == null || lines.Length == 0)
                return -1;
            for(int i = lines.Length - 1; i >= 0; i--)
            {
                if(lines[i] != null)
                    return lines[i].LineNo;
            }
            return -1;
        }

        static bool ShouldReuseRenderedLine(ConsoleDisplayLine current, ConsoleDisplayLine next, out bool refreshDataOnly)
        {
            refreshDataOnly = false;
            if(ReferenceEquals(current, next))
                return true;
            if(current == null || next == null)
                return false;
            if(!DisplayLineVisualEquals(current, next))
                return false;
            refreshDataOnly = HasCommandButton(current) || HasCommandButton(next);
            return true;
        }

        static bool HasCommandButton(ConsoleDisplayLine line)
        {
            if(line?.Buttons == null)
                return false;
            for(int i = 0; i < line.Buttons.Length; i++)
            {
                var button = line.Buttons[i];
                if(button == null)
                    continue;
                if(button.IsButton)
                    return true;
                if(button.StrArray == null)
                    continue;
                for(int j = 0; j < button.StrArray.Length; j++)
                {
                    if(button.StrArray[j] is ConsoleDivPart div && div.Children != null)
                    {
                        for(int k = 0; k < div.Children.Length; k++)
                        {
                            if(HasCommandButton(div.Children[k]))
                                return true;
                        }
                    }
                }
            }
            return false;
        }

        static bool DisplayLineVisualEquals(ConsoleDisplayLine current, ConsoleDisplayLine next)
        {
            if(current.LineNo != next.LineNo
                || current.IsLogicalLine != next.IsLogicalLine
                || current.IsTemporary != next.IsTemporary
                || current.IsLineEnd != next.IsLineEnd
                || current.Align != next.Align
                || current.TextBackgroundColor != next.TextBackgroundColor)
                return false;

            var currentButtons = current.Buttons;
            var nextButtons = next.Buttons;
            if(currentButtons == null || nextButtons == null)
                return currentButtons == nextButtons;
            if(currentButtons.Length != nextButtons.Length)
                return false;

            for(int i = 0; i < currentButtons.Length; i++)
            {
                if(!DisplayButtonVisualEquals(currentButtons[i], nextButtons[i]))
                    return false;
            }
            return true;
        }

        static bool DisplayButtonVisualEquals(ConsoleButtonString current, ConsoleButtonString next)
        {
            if(current == null || next == null)
                return current == next;
            if(current.IsButton != next.IsButton
                || current.IsInteger != next.IsInteger
                || current.Input != next.Input
                || !string.Equals(current.Inputs ?? "", next.Inputs ?? "", StringComparison.Ordinal)
                || current.PointX != next.PointX
                || current.PointXisLocked != next.PointXisLocked
                || current.RelativePointX != next.RelativePointX
                || current.Width != next.Width
                || current.XsubPixel != next.XsubPixel
                || !string.Equals(current.Title ?? "", next.Title ?? "", StringComparison.Ordinal))
                return false;

            var currentParts = current.StrArray;
            var nextParts = next.StrArray;
            if(currentParts == null || nextParts == null)
                return currentParts == nextParts;
            if(currentParts.Length != nextParts.Length)
                return false;

            for(int i = 0; i < currentParts.Length; i++)
            {
                if(!DisplayPartVisualEquals(currentParts[i], nextParts[i]))
                    return false;
            }
            return true;
        }

        static bool DisplayPartVisualEquals(AConsoleDisplayPart current, AConsoleDisplayPart next)
        {
            if(current == null || next == null)
                return current == next;
            if(current.GetType() != next.GetType()
                || current.Error != next.Error
                || current.PointX != next.PointX
                || current.XsubPixel != next.XsubPixel
                || current.Width != next.Width
                || current.WidthF != next.WidthF
                || current.Top != next.Top
                || current.Bottom != next.Bottom
                || !string.Equals(current.Str ?? "", next.Str ?? "", StringComparison.Ordinal)
                || !string.Equals(current.AltText ?? "", next.AltText ?? "", StringComparison.Ordinal))
                return false;

            if(current is ConsoleStyledString currentText && next is ConsoleStyledString nextText)
                return currentText.StringStyle == nextText.StringStyle;

            if(current is ConsoleImagePart currentImage && next is ConsoleImagePart nextImage)
            {
                return string.Equals(currentImage.ResourceName ?? "", nextImage.ResourceName ?? "", StringComparison.Ordinal)
                    && string.Equals(currentImage.ButtonResourceName ?? "", nextImage.ButtonResourceName ?? "", StringComparison.Ordinal)
                    && RectangleEquals(currentImage.dest_rect, nextImage.dest_rect)
                    && currentImage.Display == nextImage.Display
                    && currentImage.PositionX == nextImage.PositionX
                    && currentImage.PositionY == nextImage.PositionY
                    && currentImage.FlipX == nextImage.FlipX
                    && currentImage.FlipY == nextImage.FlipY
                    && string.Equals(currentImage.ColorMatrixVariableName ?? "", nextImage.ColorMatrixVariableName ?? "", StringComparison.Ordinal);
            }

            if(current is ConsoleDivPart currentDiv && next is ConsoleDivPart nextDiv)
            {
                if(currentDiv.X != nextDiv.X
                    || currentDiv.Y != nextDiv.Y
                    || currentDiv.DivWidth != nextDiv.DivWidth
                    || currentDiv.DivHeight != nextDiv.DivHeight
                    || currentDiv.Depth != nextDiv.Depth
                    || currentDiv.BackgroundColor != nextDiv.BackgroundColor
                    || currentDiv.IsRelative != nextDiv.IsRelative
                    || currentDiv.Display != nextDiv.Display)
                    return false;
                var currentChildren = currentDiv.Children;
                var nextChildren = nextDiv.Children;
                if(currentChildren == null || nextChildren == null)
                    return currentChildren == nextChildren;
                if(currentChildren.Length != nextChildren.Length)
                    return false;
                for(int i = 0; i < currentChildren.Length; i++)
                {
                    if(!DisplayLineVisualEquals(currentChildren[i], nextChildren[i]))
                        return false;
                }
            }

            // shape 的颜色字段在兼容层里是 protected；保守起见不复用 shape 行，避免颜色变化被误判为不变。
            if(current is ConsoleShapePart)
                return false;

            return true;
        }

        static bool RectangleEquals(Rectangle current, Rectangle next)
        {
            return current.X == next.X
                && current.Y == next.Y
                && current.Width == next.Width
                && current.Height == next.Height;
        }

        private EmueraConsole console_ = null;
        private volatile bool dirty_ = false;
        private int refreshRequestGeneration = 0;
        private int processedRefreshGeneration = 0;
        public int RefreshRequestGeneration
        {
            get { return Volatile.Read(ref refreshRequestGeneration); }
        }

        public void WaitForRefreshProcessed(int generation, int timeoutMs)
        {
            if (GenericUtils.IsOnMainThread())
                return;
            long deadline = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Math.Max(0, timeoutMs);
            while (Volatile.Read(ref processedRefreshGeneration) < generation
                && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < deadline)
            {
                Thread.Sleep(1);
            }
        }

        public string InternalEmueraVer { get { return uEmueraVer; } }
        public string EmueraVerText { get { return uEmueraVer; } }

        public bool Created { get { return created_; } }
        bool created_ = false;

        public ScrollBar ScrollBar = new ScrollBar();
        public PictureBox MainPicBox = new PictureBox();
        public string Text { get; set; }
        public ToolTip ToolTip = new ToolTip();
        public TextBox TextBox = new TextBox();

        public void SetTextBoxPos(int x, int y, int width)
        {
            TextBox.X = x;
            TextBox.Y = y;
            TextBox.Width = Math.Max(0, width);
            TextBox.UseCustomPosition = true;
        }

        public void ResetTextBoxPos()
        {
            TextBox.X = 0;
            TextBox.Y = 0;
            TextBox.Width = 0;
            TextBox.UseCustomPosition = false;
        }

        void ShowProcess()
        {
            GenericUtils.ShowIsInProcess(true);
        }
        void CheckProcess()
        {
            var now = MinorShift._Library.WinmmTimer.TickCount;
            if(last_process_tic == 0)
                last_process_tic = now;
            else if(now - last_process_tic > 1500u)
            {
                GenericUtils.ShowIsInProcess(true);
            }
        }
        uint last_process_tic = 0;
        bool wait_process = false;
    }
}

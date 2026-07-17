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

            var displayLines = console_.GetDisplayLinesSnapshotForuEmuera();
            var console_count = displayLines.Length;
            if(console_count == 0)
            {
                dynamicMapViewActive = false;
                dynamicMapViewBaseLineNo = -1;
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
            int prev = GenericUtils.GetTextMaxLineNo();
            int min_lineno = GenericUtils.GetTextMinLineNo();
            int displayStartIndex = 0;
            if(TryFindDynamicMapWindowStart(displayLines, out int dynamicMapStartIndex))
            {
                int mapBaseLineNo = displayLines[dynamicMapStartIndex].LineNo;
                // 这里只保留动态地图上下文标记，不再裁剪 Godot 显示视图。
                // 历史内容继续参与行级 diff，进入地图后仍可向上查看前文；
                // 动态地图内完全停用“把视口拉回选项区”的补偿，避免查看历史时被刷新拉回底部。
                dynamicMapViewActive = true;
                dynamicMapViewBaseLineNo = mapBaseLineNo;
            }
            else if(dynamicMapViewActive)
            {
                dynamicMapViewActive = false;
                dynamicMapViewBaseLineNo = -1;
            }
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

            for(int i = displayStartIndex; i < console_count; i++)
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

            bool hasDynamicMapFunctionDelta = GenericUtils.ContainsDynamicMapFunctionScope(linesToAdd);
            EmueraDisplayScrollMode scrollMode = DecideScrollModeForDisplayDelta(prev, removeBottomCount, need_update_flag,
                linesToAdd, dynamicMapViewActive, hasDynamicMapFunctionDelta);
            bool scrollToBottom = scrollMode == EmueraDisplayScrollMode.FollowBottom;

            if (GenericUtils.IsDynamicMapLineSnapshotTraceEnabled)
            {
                bool hasBitmapContext = GenericUtils.ContainsDynamicMapBitmapContext(linesToAdd)
                    || GenericUtils.ContainsDynamicMapBitmapContextTail(displayLines);
                if (GenericUtils.ShouldTraceDynamicMap(hasBitmapContext))
                {
                    GenericUtils.DynamicMapTrace("DYNAMIC_MAP.BRIDGE.SUBMIT",
                        () => "dynamic map bridge display diff",
                        () => "console_count=" + console_count
                            + " remove_bottom=" + removeBottomCount
                            + " add=" + (linesToAdd?.Count ?? 0)
                            + " data_only=" + (linesToRefreshData?.Count ?? 0)
                            + " view_start=" + displayStartIndex
                            + " map_view=" + dynamicMapViewActive
                            + " map_base=" + dynamicMapViewBaseLineNo
                            + " map_delta=" + hasDynamicMapFunctionDelta
                            + " update=" + need_update_flag
                            + " scroll_mode=" + scrollMode
                            + " auto_scroll=" + scrollToBottom
                            + " prev_max=" + prev
                            + " last_button_generation=" + console_.LastButtonGeneration
                            + " has_bitmap_context=" + hasBitmapContext
                            + " tail=" + GenericUtils.BuildDynamicMapLineTailSummary(displayLines)
                            + " incoming=" + GenericUtils.BuildDynamicMapDeltaLineSummary(linesToAdd));
                }
            }

            GenericUtils.ApplyTextChanges(removeBottomCount, linesToAdd, need_update_flag, console_.LastButtonGeneration, scrollMode, linesToRefreshData);

            GenericUtils.ShowIsInProcess(false);
            GenericUtils.RefreshCBG(console_);
            console_.NeedSetTimer();
            last_process_tic = 0;
            Volatile.Write(ref processedRefreshGeneration, Volatile.Read(ref refreshRequestGeneration));
        }

        static EmueraDisplayScrollMode DecideScrollModeForDisplayDelta(int previousMaxLineNo, int removeBottomCount, bool update,
            System.Collections.Generic.List<(ConsoleDisplayLine Line, bool Update)> linesToAdd, bool dynamicMapViewActive,
            bool hasDynamicMapFunctionDelta)
        {
            // 动态地图、状态面板一类内容通常通过删除底部旧行再重画当前屏幕来刷新。
            // 这不是“追加新文本”，因此不能触发 ScrollContainer 自动滚到底；否则 Android 会在重绘时把视口拖走。
            // 但第一次进入地图或普通新文本追加仍应允许追到底部，否则会出现需要手动滑到底的问题。
            if (linesToAdd == null || linesToAdd.Count == 0)
                return EmueraDisplayScrollMode.PreserveViewport;

            bool hasAppendAfterPreviousMax = false;
            for (int i = 0; i < linesToAdd.Count; i++)
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

            if ((dynamicMapViewActive || hasDynamicMapFunctionDelta) && (removeBottomCount > 0 || update))
                return EmueraDisplayScrollMode.PreserveViewport;

            // 普通会话/泡茶等输出可能在追加新文本的同时刷新旧行元数据。
            // 只要不是动态地图重绘，并且确实出现了新行，就按普通 Emuera 输出追到底部。
            if (hasAppendAfterPreviousMax)
                return EmueraDisplayScrollMode.FollowBottom;

            if (removeBottomCount > 0 || update)
                return EmueraDisplayScrollMode.PreserveViewport;

            return EmueraDisplayScrollMode.PreserveViewport;
        }

        static bool TryFindDynamicMapWindowStart(ConsoleDisplayLine[] lines, out int startIndex)
        {
            startIndex = -1;
            if(lines == null || lines.Length == 0)
                return false;

            int lastBitmapIndex = -1;
            int searchStart = Math.Max(0, lines.Length - DynamicMapTailSearchLineCount);
            for(int i = lines.Length - 1; i >= searchStart; i--)
            {
                if(GenericUtils.LineHasDynamicMapBitmapContext(lines[i]))
                {
                    lastBitmapIndex = i;
                    break;
                }
            }
            if(lastBitmapIndex < 0)
                return false;

            int firstBitmapIndex = lastBitmapIndex;
            while(firstBitmapIndex > 0 && GenericUtils.LineHasDynamicMapBitmapContext(lines[firstBitmapIndex - 1]))
                firstBitmapIndex--;

            int bitmapLineCount = lastBitmapIndex - firstBitmapIndex + 1;
            if(bitmapLineCount < 6)
                return false;

            if(!LooksLikeMapWindow(lines, firstBitmapIndex, lastBitmapIndex, bitmapLineCount))
                return false;

            startIndex = firstBitmapIndex;
            return true;
        }

        static bool LooksLikeMapWindow(ConsoleDisplayLine[] lines, int firstBitmapIndex, int lastBitmapIndex, int bitmapLineCount)
        {
            int commandCount = 0;
            for(int i = firstBitmapIndex; i <= lastBitmapIndex; i++)
                commandCount += CountCommandButtons(lines[i], 0);

            if(bitmapLineCount >= 12 && commandCount >= 2)
                return true;

            int start = Math.Max(0, firstBitmapIndex - 3);
            int end = Math.Min(lines.Length - 1, lastBitmapIndex + 8);
            for(int i = start; i <= end; i++)
            {
                string text = lines[i]?.ToString() ?? "";
                if(text.IndexOf("地图", StringComparison.Ordinal) >= 0
                    || text.IndexOf("地圖", StringComparison.Ordinal) >= 0
                    || text.IndexOf("地図", StringComparison.Ordinal) >= 0
                    || text.IndexOf("所在地", StringComparison.Ordinal) >= 0
                    || text.IndexOf("当前位置", StringComparison.Ordinal) >= 0
                    || text.IndexOf("現在地", StringComparison.Ordinal) >= 0
                    || text.IndexOf("察觉", StringComparison.Ordinal) >= 0
                    || text.IndexOf("察覺", StringComparison.Ordinal) >= 0
                    || text.IndexOf("察知", StringComparison.Ordinal) >= 0)
                    return commandCount > 0 || bitmapLineCount >= 8;
            }
            return false;
        }

        static int CountCommandButtons(ConsoleDisplayLine line, int depth)
        {
            if(line?.Buttons == null || depth > 4)
                return 0;
            int count = 0;
            for(int i = 0; i < line.Buttons.Length; i++)
            {
                var button = line.Buttons[i];
                if(button == null)
                    continue;
                if(button.IsButton)
                    count++;
                if(button.StrArray == null)
                    continue;
                for(int j = 0; j < button.StrArray.Length; j++)
                {
                    if(button.StrArray[j] is ConsoleDivPart div && div.Children != null)
                    {
                        for(int k = 0; k < div.Children.Length; k++)
                            count += CountCommandButtons(div.Children[k], depth + 1);
                    }
                }
            }
            return count;
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
                || current.BitmapCacheEnabled != next.BitmapCacheEnabled
                || current.DynamicMapFunctionScoped != next.DynamicMapFunctionScoped
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
        private bool dynamicMapViewActive = false;
        private int dynamicMapViewBaseLineNo = -1;
        private const int DynamicMapTailSearchLineCount = 32;

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

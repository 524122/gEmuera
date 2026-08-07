using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Godot;
using MinorShift.Emuera;
using MinorShift.Emuera.GameView;
using uEmuera.Window;
// 基类从 PanelContainer(Control) 改为 Window(Viewport) 后，Control 嵌套枚举不再隐式可见。
using SizeFlags = Godot.Control.SizeFlags;
using MouseFilterEnum = Godot.Control.MouseFilterEnum;
using FocusModeEnum = Godot.Control.FocusModeEnum;

namespace gEmuera.GodotHost
{
	/// <summary>
	/// worker 线程（DebugDialog 定时快照）推送给主线程面板的只读快照。
	/// 面板一律通过本 DTO 展示引擎态，禁止主线程直接读取 EmueraConsole 的
	/// DebugConsoleLog / GetDebugTraceLog / 变量求值。
	/// </summary>
	public sealed class EmueraDebugSnapshot
	{
		public readonly string ConsoleLog;
		public readonly string TraceLog;
		/// <summary>当前 watch 表达式列表（worker 侧权威回显，用于面板首次同步）。</summary>
		public readonly string[] WatchExpressions;
		/// <summary>与 WatchExpressions 平行的求值结果。</summary>
		public readonly string[] WatchValues;

		public EmueraDebugSnapshot(
			string consoleLog,
			string traceLog,
			string[] watchExpressions,
			string[] watchValues)
		{
			ConsoleLog = consoleLog ?? "";
			TraceLog = traceLog ?? "";
			WatchExpressions = watchExpressions ?? Array.Empty<string>();
			WatchValues = watchValues ?? Array.Empty<string>();
		}
	}

	/// <summary>
	/// Emuera DEBUG 模式调试窗口（Godot 原生 Window，代码构建，不依赖 .tscn）。
	/// 引擎运行在 worker 线程，本窗口运行在 Godot 主线程：
	/// - 打开/关闭/聚焦由 worker 的 DebugDialog 通过静态入口调度（uiActions 队列，
	///   由 _Process 在主线程消费，不使用 CallDeferred，避免 C# 方法表注册问题）；
	/// - 展示数据（日志/调用栈/watch 求值）由 worker 定时快照推送到 snapshotQueue。
	/// 三个 Tab：变量监视 / 调用栈 / 调试控制台，深色主题走 GEmueraTheme token。
	///
	/// 为什么用 Window 而不是画布内嵌 PanelContainer：古早版本是覆盖在游戏画布上的
	/// 面板，会挡住游戏内容（DEBUG 模式看不到画面）。Window 节点在桌面端是独立 OS
	/// 窗口，在 Android 是带标题栏、可拖动/缩放的浮动子窗口，不再遮挡游戏主画面。
	/// </summary>
	public partial class EmueraDebugDialogPanel : Window
	{
		const int MinWindowWidth = 320;
		const int MinWindowHeight = 240;

		static EmueraDebugDialogPanel currentInstance; // 跨线程读写统一走 Volatile.Read/Write
		static readonly ConcurrentQueue<EmueraDebugSnapshot> snapshotQueue =
			new ConcurrentQueue<EmueraDebugSnapshot>();

		readonly ConcurrentQueue<Action> uiActions = new ConcurrentQueue<Action>();

		// 绑定（仅主线程在 DoShow/DoHide 内读写）
		EmueraConsole boundConsole;
		DebugDialog debugLink;

		// UI 组件
		TabContainer tabs;
		VBoxContainer watchRowsRoot;
		LineEdit addExpressionInput;
		LineEdit commandInput;
		ScrollContainer logScroll;
		RichTextLabel consoleLogText;
		ScrollContainer traceScroll;
		RichTextLabel traceText;

		readonly List<WatchRow> watchRows = new List<WatchRow>();
		string[] localExpressions = Array.Empty<string>();

		sealed class WatchRow
		{
			public LineEdit Expression;
			public Label Value;
		}

		#region 静态入口（worker 线程可调用）

		/// <summary>EmueraMain._Ready 挂载窗口宿主（主线程）。</summary>
		public static void AttachTo(Node parent)
		{
			if (parent == null)
				return;
			var panel = new EmueraDebugDialogPanel();
			panel.Name = "EmueraDebugDialogPanel";
			panel.Visible = false;
			// Window 子节点：桌面端按项目设置可成独立 OS 窗口，Android 为嵌入浮动子窗口。
			parent.AddChild(panel);
			panel.TreeExiting += () =>
			{
				if (System.Threading.Volatile.Read(ref currentInstance) == panel)
					System.Threading.Volatile.Write(ref currentInstance, null);
			};
			System.Threading.Volatile.Write(ref currentInstance, panel);
		}

		// 参数含 internal 类型（EmueraConsole / DebugDialog），保持 internal 可见性。
		internal static void ShowPanel(EmueraConsole console, DebugDialog link)
		{
			var panel = System.Threading.Volatile.Read(ref currentInstance);
			if (panel == null || !GodotObject.IsInstanceValid(panel))
				return;
			panel.uiActions.Enqueue(() => panel.DoShow(console, link));
		}

		internal static void FocusPanel()
		{
			var panel = System.Threading.Volatile.Read(ref currentInstance);
			if (panel == null || !GodotObject.IsInstanceValid(panel))
				return;
			panel.uiActions.Enqueue(() => panel.DoFocus());
		}

		internal static void HidePanel()
		{
			var panel = System.Threading.Volatile.Read(ref currentInstance);
			if (panel == null || !GodotObject.IsInstanceValid(panel))
				return;
			panel.uiActions.Enqueue(() => panel.DoHide());
		}

		internal static void PushSnapshot(EmueraDebugSnapshot snapshot)
		{
			if (snapshot == null)
				return;
			snapshotQueue.Enqueue(snapshot);
		}

		#endregion

		#region Godot 生命周期（主线程）

		public override void _Ready()
		{
			// Window 配置：标题栏。桌面端按 embed_subwindows 设置决定
			// 是否原生 OS 窗口；Android 恒为嵌入浮动子窗口（Window 默认可缩放）。
			// 不用 WrapControls：窗口尺寸由 ApplyWindowGeometry 显式控制，内容靠根节点 FullRect 填充。
			Title = "调试窗口 (Debug)";
			MinSize = new Vector2I(MinWindowWidth, MinWindowHeight);
			CloseRequested += OnWindowCloseRequested;
			BuildPanel();
		}

		public override void _Process(double delta)
		{
			while (uiActions.TryDequeue(out var action))
			{
				try
				{
					action();
				}
				catch (Exception ex)
				{
					GenericUtils.Error(
						$"DEBUG_DIALOG_UI_ACTION_FAILED {ex.GetType().Name}: {ex.Message}");
				}
			}

			if (!Visible)
				return;
			while (snapshotQueue.TryDequeue(out var snap))
			{
				try
				{
					ApplySnapshot(snap);
				}
				catch (Exception ex)
				{
					GenericUtils.Error(
						$"DEBUG_DIALOG_SNAPSHOT_FAILED {ex.GetType().Name}: {ex.Message}");
				}
			}
		}

		#endregion

		#region 显示/隐藏/聚焦

		void DoShow(EmueraConsole console, DebugDialog link)
		{
			boundConsole = console;
			debugLink = link;
			ApplyWindowGeometry();
			while (snapshotQueue.TryDequeue(out _)) { } // 丢弃旧会话残留快照
			Visible = true;
		}

		void DoFocus()
		{
			if (!Visible)
				Visible = true;
			// Window 不是 Control：聚焦输入框而非窗口自身。
			if (addExpressionInput != null)
			{
				addExpressionInput.GrabFocus();
				addExpressionInput.CaretColumn = addExpressionInput.Text.Length;
			}
		}

		void DoHide()
		{
			Visible = false;
			boundConsole = null;
			debugLink = null;
			while (snapshotQueue.TryDequeue(out _)) { }
		}

		void ApplyWindowGeometry()
		{
			int width = Math.Max(MinWindowWidth, Config.DebugWindowWidth);
			int height = Math.Max(MinWindowHeight, Config.DebugWindowHeight);
			MinSize = new Vector2I(MinWindowWidth, MinWindowHeight);
			Size = new Vector2I(width, height);
			// 嵌入 Window 的 Position 相对父窗口内容区，单位是物理像素。
			// 必须用主窗口实际物理尺寸（GetTree().Root.Size），不能用 ProjectSettings 的
			// 逻辑分辨率——否则在拉伸/高分屏上居中坐标严重偏上、标题栏贴顶无法拖动。
			var parentSize = GetParentWindowPixelSize();
			const int topMargin = 12; // 标题栏离开顶部，保证可抓取拖动
			if (Config.DebugSetWindowPos)
				Position = new Vector2I((int)Config.DebugWindowPosX, (int)Config.DebugWindowPosY);
			else
				Position = new Vector2I((parentSize.X - width) / 2, (parentSize.Y - height) / 2);
			Position = new Vector2I(
				Mathf.Clamp(Position.X, 0, Math.Max(0, parentSize.X - width)),
				Mathf.Clamp(Position.Y, topMargin, Math.Max(topMargin, parentSize.Y - height)));
		}

		/// <summary>父窗口（主窗口）内容区的物理像素尺寸；嵌入 Window 定位/钳制的基准。</summary>
		Vector2I GetParentWindowPixelSize()
		{
			var root = GetTree()?.Root;
			if (root != null && GodotObject.IsInstanceValid(root))
				return root.Size;
			// 兜底：退化为 ProjectSettings 逻辑分辨率（尽量接近）。
			return new Vector2I(
				(int)ProjectSettings.GetSetting("display/window/size/viewport_width", 1280),
				(int)ProjectSettings.GetSetting("display/window/size/viewport_height", 720));
		}

		#endregion

		#region 构建

		void BuildPanel()
		{
			// Window 不是 Control：主题与 panel 样式落在根 PanelContainer 上。
			// FullRect 锚点让根面板填满整个 Window（否则内容只占左上角）。
			var rootPanel = new PanelContainer();
			rootPanel.Theme = GEmueraTheme.LoadTheme();
			rootPanel.AddThemeStyleboxOverride(
				"panel",
				GEmueraTheme.SurfaceStyle(
					GEmueraTheme.SurfaceRaised, GEmueraTheme.Border, GEmueraTheme.CardRadius,
					1, 14, new Vector2(0, 6)));
			rootPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			rootPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			rootPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
			rootPanel.MouseFilter = MouseFilterEnum.Stop;
			AddChild(rootPanel);

			var margin = new MarginContainer();
			margin.MouseFilter = MouseFilterEnum.Pass;
			margin.AddThemeConstantOverride("margin_left", 12);
			margin.AddThemeConstantOverride("margin_right", 12);
			margin.AddThemeConstantOverride("margin_top", 10);
			margin.AddThemeConstantOverride("margin_bottom", 10);
			rootPanel.AddChild(margin);

			var root = new VBoxContainer();
			root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			root.SizeFlagsVertical = SizeFlags.ExpandFill;
			root.AddThemeConstantOverride("separation", 8);
			root.MouseFilter = MouseFilterEnum.Pass;
			margin.AddChild(root);

			root.AddChild(CreateHeader());

			tabs = CreateTabs();
			root.AddChild(tabs);

			// Tab 1：变量监视
			var watchTab = new VBoxContainer();
			watchTab.Name = "变量监视";
			watchTab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			watchTab.SizeFlagsVertical = SizeFlags.ExpandFill;
			watchTab.AddThemeConstantOverride("separation", 6);
			watchTab.MouseFilter = MouseFilterEnum.Stop;
			tabs.AddChild(watchTab);

			var watchScroll = CreateScroll();
			watchScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
			watchTab.AddChild(watchScroll);

			watchRowsRoot = new VBoxContainer();
			watchRowsRoot.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			watchRowsRoot.AddThemeConstantOverride("separation", 4);
			watchRowsRoot.MouseFilter = MouseFilterEnum.Pass;
			watchScroll.AddChild(watchRowsRoot);

			var addRow = new HBoxContainer();
			addRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			addRow.AddThemeConstantOverride("separation", 6);
			watchTab.AddChild(addRow);

			addExpressionInput = new LineEdit { PlaceholderText = "添加监视表达式（如 DAY、CFLAG:100），回车或点添加" };
			addExpressionInput.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			StyleLineEdit(addExpressionInput);
			addExpressionInput.TextSubmitted += _ => AddWatchExpression();
			addRow.AddChild(addExpressionInput);

			var addButton = new Button { Text = "添加" };
			GEmueraTheme.ApplyButton(addButton, GEmueraTheme.Surface, GEmueraTheme.Accent);
			addButton.Pressed += AddWatchExpression;
			addRow.AddChild(addButton);

			// Tab 2：调用栈
			var traceTab = new VBoxContainer();
			traceTab.Name = "调用栈";
			traceTab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			traceTab.SizeFlagsVertical = SizeFlags.ExpandFill;
			traceTab.MouseFilter = MouseFilterEnum.Stop;
			tabs.AddChild(traceTab);

			traceScroll = CreateScroll();
			traceScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
			traceTab.AddChild(traceScroll);

			traceText = CreateReadOnlyText();
			traceScroll.AddChild(traceText);

			// Tab 3：调试控制台
			var consoleTab = new VBoxContainer();
			consoleTab.Name = "调试控制台";
			consoleTab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			consoleTab.SizeFlagsVertical = SizeFlags.ExpandFill;
			consoleTab.AddThemeConstantOverride("separation", 6);
			consoleTab.MouseFilter = MouseFilterEnum.Stop;
			tabs.AddChild(consoleTab);

			logScroll = CreateScroll();
			logScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
			consoleTab.AddChild(logScroll);

			consoleLogText = CreateReadOnlyText();
			logScroll.AddChild(consoleLogText);

			commandInput = new LineEdit { PlaceholderText = "调试命令（如 SET DAY:0、PRINT 文本），回车执行" };
			StyleLineEdit(commandInput);
			commandInput.TextSubmitted += text =>
			{
				if (string.IsNullOrEmpty(text))
					return;
				var link = debugLink;
				if (link != null)
					link.EnqueueCommand(text);
				commandInput.Text = "";
			};
			consoleTab.AddChild(commandInput);
		}

		Control CreateHeader()
		{
			var header = new HBoxContainer();
			header.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			header.AddThemeConstantOverride("separation", 8);

			var title = new Label { Text = "调试窗口 (Debug)" };
			title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			title.MouseFilter = MouseFilterEnum.Ignore;
			title.AddThemeFontSizeOverride("font_size", 16);
			title.AddThemeColorOverride("font_color", GEmueraTheme.TextPrimary);
			header.AddChild(title);

			// 关闭走 Window 标题栏原生 ✕（CloseRequested 信号），这里不再放第二个 ✕。
			return header;
		}

		static TabContainer CreateTabs()
		{
			var tabs = new TabContainer();
			tabs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			tabs.SizeFlagsVertical = SizeFlags.ExpandFill;
			tabs.MouseFilter = MouseFilterEnum.Stop;
			tabs.AddThemeColorOverride("font_selected_color", GEmueraTheme.TextPrimary);
			tabs.AddThemeColorOverride("font_unselected_color", GEmueraTheme.TextSecondary);
			tabs.AddThemeColorOverride("font_hover_color", GEmueraTheme.TextPrimary);
			tabs.AddThemeStyleboxOverride(
				"tab_selected",
				GEmueraTheme.ButtonBox(GEmueraTheme.SurfaceRaised, GEmueraTheme.Accent, GEmueraTheme.SmallRadius));
			tabs.AddThemeStyleboxOverride(
				"tab_unselected",
				GEmueraTheme.ButtonBox(GEmueraTheme.Surface, GEmueraTheme.Border, GEmueraTheme.SmallRadius));
			tabs.AddThemeStyleboxOverride(
				"tab_hover",
				GEmueraTheme.ButtonBox(GEmueraTheme.SurfaceRaised, GEmueraTheme.BorderStrong, GEmueraTheme.SmallRadius));
			tabs.AddThemeStyleboxOverride(
				"panel",
				GEmueraTheme.SurfaceStyle(GEmueraTheme.Surface, GEmueraTheme.Border, GEmueraTheme.SmallRadius));
			return tabs;
		}

		static ScrollContainer CreateScroll()
		{
			var scroll = new ScrollContainer();
			scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
			scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
			scroll.MouseFilter = MouseFilterEnum.Stop;
			GEmueraTheme.ApplyWideScrollbar(scroll.GetVScrollBar(), 14);
			return scroll;
		}

		static RichTextLabel CreateReadOnlyText()
		{
			var text = new RichTextLabel();
			text.BbcodeEnabled = false;
			text.ScrollActive = false;
			// FitContent=true 让高度随内容（ScrollContainer 垂直滚动）；但宽度也会收缩到
			// 内容宽，导致文本靠左留白。必须 ExpandFill 水平撑满 ScrollContainer 宽度。
			text.FitContent = true;
			text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			text.SelectionEnabled = true;
			text.MouseFilter = MouseFilterEnum.Stop;
			text.AddThemeColorOverride("default_color", GEmueraTheme.TextPrimary);
			text.AddThemeColorOverride("font_selected_color", GEmueraTheme.TextPrimary);
			text.AddThemeColorOverride("selection_color", GEmueraTheme.WithAlpha(GEmueraTheme.Accent, 0.4f));
			return text;
		}

		static void StyleLineEdit(LineEdit lineEdit)
		{
			lineEdit.AddThemeStyleboxOverride(
				"normal",
				GEmueraTheme.SurfaceStyle(
					GEmueraTheme.Background, GEmueraTheme.Border, GEmueraTheme.SmallRadius, 1,
					0, null, 6, 6, 4, 4));
			lineEdit.AddThemeStyleboxOverride(
				"focus",
				GEmueraTheme.ButtonBox(GEmueraTheme.Background, GEmueraTheme.Accent, GEmueraTheme.SmallRadius, 2));
			lineEdit.AddThemeColorOverride("font_color", GEmueraTheme.TextPrimary);
			lineEdit.AddThemeColorOverride("font_placeholder_color", GEmueraTheme.TextDim);
			lineEdit.AddThemeColorOverride("caret_color", GEmueraTheme.Accent);
			lineEdit.AddThemeColorOverride("selection_color", GEmueraTheme.WithAlpha(GEmueraTheme.Accent, 0.4f));
		}

		#endregion

		#region 快照应用（主线程）

		void ApplySnapshot(EmueraDebugSnapshot snap)
		{
			if (consoleLogText.Text != snap.ConsoleLog)
			{
				bool stickToBottom =
					logScroll.GetVScrollBar().MaxValue - logScroll.ScrollVertical < 24;
				consoleLogText.Text = snap.ConsoleLog;
				if (stickToBottom)
					logScroll.ScrollVertical = (int)logScroll.GetVScrollBar().MaxValue;
			}
			if (traceText.Text != snap.TraceLog)
			{
				bool stickToBottom =
					traceScroll.GetVScrollBar().MaxValue - traceScroll.ScrollVertical < 24;
				traceText.Text = snap.TraceLog;
				if (stickToBottom)
					traceScroll.ScrollVertical = (int)traceScroll.GetVScrollBar().MaxValue;
			}
			SyncWatchRows(snap);
		}

		void SyncWatchRows(EmueraDebugSnapshot snap)
		{
			if (!ArraysEqual(localExpressions, snap.WatchExpressions))
			{
				localExpressions = snap.WatchExpressions;
				RebuildWatchRows(localExpressions, snap.WatchValues);
				return;
			}
			int count = Math.Min(watchRows.Count, snap.WatchValues.Length);
			for (int i = 0; i < count; i++)
			{
				string value = snap.WatchValues[i];
				if (watchRows[i].Value.Text != value)
					watchRows[i].Value.Text = value;
			}
		}

		static bool ArraysEqual(string[] a, string[] b)
		{
			if (ReferenceEquals(a, b))
				return true;
			if (a == null || b == null)
				return false;
			if (a.Length != b.Length)
				return false;
			for (int i = 0; i < a.Length; i++)
			{
				if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
					return false;
			}
			return true;
		}

		void RebuildWatchRows(string[] expressions, string[] values)
		{
			foreach (Node child in watchRowsRoot.GetChildren())
				child.QueueFree();
			watchRows.Clear();
			for (int i = 0; i < expressions.Length; i++)
				AddWatchRow(expressions[i], i < values.Length ? values[i] : "");
		}

		void AddWatchRow(string expression, string value)
		{
			var row = new HBoxContainer();
			row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			row.AddThemeConstantOverride("separation", 6);
			row.MouseFilter = MouseFilterEnum.Pass;

			var lineEdit = new LineEdit { Text = expression, PlaceholderText = "表达式" };
			lineEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			StyleLineEdit(lineEdit);
			lineEdit.TextSubmitted += _ => PushWatchList();
			lineEdit.FocusExited += () => PushWatchList();
			row.AddChild(lineEdit);

			var valueLabel = new Label { Text = value, CustomMinimumSize = new Vector2(160, 0) };
			valueLabel.MouseFilter = MouseFilterEnum.Ignore;
			valueLabel.AddThemeColorOverride("font_color", GEmueraTheme.TextSecondary);
			valueLabel.ClipText = true;
			row.AddChild(valueLabel);

			int index = watchRows.Count;
			var removeButton = new Button { Text = "✕", CustomMinimumSize = new Vector2(28, 0) };
			GEmueraTheme.ApplyButton(removeButton, GEmueraTheme.Surface, GEmueraTheme.Danger);
			removeButton.Pressed += () => RemoveWatchRowAt(index);
			row.AddChild(removeButton);

			watchRowsRoot.AddChild(row);
			watchRows.Add(new WatchRow { Expression = lineEdit, Value = valueLabel });
		}

		List<string> CollectExpressions()
		{
			var list = new List<string>();
			foreach (var row in watchRows)
			{
				string text = row.Expression.Text?.Trim();
				if (!string.IsNullOrEmpty(text))
					list.Add(text);
			}
			return list;
		}

		void PushWatchList()
		{
			var link = debugLink;
			if (link == null)
				return;
			var list = CollectExpressions();
			localExpressions = list.ToArray();
			link.EnqueueWatchList(list.ToArray());
		}

		void CommitExpressions(List<string> list)
		{
			localExpressions = list.ToArray();
			RebuildWatchRows(localExpressions, new string[localExpressions.Length]);
			PushWatchList();
		}

		void AddWatchExpression()
		{
			string text = addExpressionInput.Text?.Trim();
			if (string.IsNullOrEmpty(text))
				return;
			addExpressionInput.Text = "";
			var list = CollectExpressions();
			list.Add(text);
			CommitExpressions(list);
		}

		void RemoveWatchRowAt(int index)
		{
			if (index < 0 || index >= watchRows.Count)
				return;
			var list = CollectExpressions();
			if (index < list.Count)
				list.RemoveAt(index);
			CommitExpressions(list);
		}

		void RequestClose()
		{
			// 面板关闭等价于 DebugDialog.Dispose（worker 侧保存 console.log + watchlist.csv）。
			// Dispose 只能由 worker 线程执行（它操作 uEmuera.Forms.Timer 与引擎缓冲），
			// 因此主线程只投递关闭请求，由 worker 定时器在空闲边界消费。
			var link = debugLink;
			if (link != null)
				link.EnqueueCloseRequest();
			else
				DoHide();
		}

		// Window 标题栏 ✕：与 header 内 ✕ 按钮同一关闭路径。
		void OnWindowCloseRequested()
		{
			RequestClose();
		}

		#endregion
	}
}

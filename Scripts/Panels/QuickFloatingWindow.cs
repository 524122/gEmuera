using Godot;

// 快捷按钮面板的悬浮宿主（Godot Window 组件）。
// 悬浮样式沿用 quick 面板自身的透明样式：无边框透明窗口承载 QuickButtons（CanvasLayer），
// 没有原生标题栏；拖动面板（内容无滚动余量时）移动窗口，内容有滚动余量时拖动仍为滚动。
// 面板显隐通过 PadShown/PadHidden 信号同步窗口显隐；关闭/隐藏复用系统菜单 quick 按钮。
// 桌面端由 EmueraContent 按配置挂载；Android 不挂载本类（嵌入 Window 在
// gl_compatibility 下内容不渲染，见 EmueraContent 门控），保持画布内嵌。
//
// 职责（组合优于继承）：只负责窗口几何、生命周期与位置拖动，不触碰按钮/滚动/
// 拖拽等业务逻辑——那些仍由 QuickButtons 自行处理。
public partial class QuickFloatingWindow : Window
{
	const int PanelMargin = 20;
	const int ScreenMargin = 32;
	const int MinWindowWidth = 80;
	const int MinWindowHeight = 60;

	QuickButtons quick;
	bool positionInitialized;

	/// <summary>把 QuickButtons 内容挂进本窗口；只允许在 _Ready 前调用一次。</summary>
	public void AttachQuick(QuickButtons content)
	{
		quick = content;
		// 悬浮宿主注入：锚点贴左贴顶（窗口内容区内）、面板拖动 → 移动窗口。
		content.FloatingHosted = true;
		content.WindowDragEnabled = true;
		content.WindowDragRequested += OnPanelWindowDrag;
		AddChild(content);
	}

	/// <summary>
	/// 迁移/销毁前显式解除与面板的关联：退订全部事件并复位宿主注入状态。
	/// 不依赖 QueueFree 的 _ExitTree 时序（同帧迁移时旧窗口尚未退订，避免双订阅）。
	/// </summary>
	public void DetachQuick()
	{
		if (quick == null)
			return;
		quick.PadShown -= OnPadShown;
		quick.PadHidden -= OnPadHidden;
		quick.WindowDragRequested -= OnPanelWindowDrag;
		quick.FloatingHosted = false;
		quick.WindowDragEnabled = false;
		if (quick.GetParent() == this)
			RemoveChild(quick);
		quick = null;
	}

	public override void _Ready()
	{
		// 无边框透明窗口：沿用 quick 面板的透明样式，不显示原生标题栏。
		Borderless = true;
		Transparent = true;
		TransparentBg = true;
		Visible = false;
		// _Process 只服务窗口尺寸同步；窗口隐藏时关闭，避免每帧一次
		// native→managed 调用（OnPadShown 会按需重开）。
		SetProcess(false);
		// 内容 1:1（逻辑像素 = 物理像素）：按钮大小就是配置值，不再按主窗口
		// stretch 缩放比放大，用户调节宽度/字号所见即所得。
		MinSize = new Vector2I(MinWindowWidth, MinWindowHeight);
		if (quick != null)
		{
			quick.PadShown += OnPadShown;
			quick.PadHidden += OnPadHidden;
		}
	}

	public override void _ExitTree()
	{
		// 兜底退订（DetachQuick 之外的销毁路径，如场景卸载）。
		if (quick != null)
		{
			quick.PadShown -= OnPadShown;
			quick.PadHidden -= OnPadHidden;
			quick.WindowDragRequested -= OnPanelWindowDrag;
		}
	}

	void OnPadShown()
	{
		SyncWindowSize();
		// 首次显示定位到主窗口右下角；之后保持用户拖动的位置。
		if (!positionInitialized)
		{
			var root = GetTree()?.Root;
			if (root != null && GodotObject.IsInstanceValid(root))
			{
				Position = new Vector2I(
					Mathf.Max(root.Position.X, root.Position.X + root.Size.X - Size.X - ScreenMargin),
					Mathf.Max(root.Position.Y, root.Position.Y + root.Size.Y - Size.Y - ScreenMargin));
			}
			positionInitialized = true;
		}
		SetProcess(true);
		Show();
	}

	void OnPadHidden()
	{
		SetProcess(false);
		Hide();
	}

	public override void _Process(double delta)
	{
		if (!Visible || quick == null)
			return;
		SyncWindowSize();
	}

	// 面板拖动 → 窗口移动（位移直接相加；1:1 内容下拖动量即物理位移）。
	void OnPanelWindowDrag(Vector2 delta)
	{
		Position += new Vector2I(Mathf.RoundToInt(delta.X), Mathf.RoundToInt(delta.Y));
		ClampToRoot();
	}

	// 悬浮窗保持在主窗口工作区内（独立 OS 窗口 Position 为屏幕坐标）。
	void ClampToRoot()
	{
		var root = GetTree()?.Root;
		if (root == null || !GodotObject.IsInstanceValid(root))
			return;
		int minX = root.Position.X;
		int minY = root.Position.Y;
		var pos = Position;
		pos.X = Mathf.Clamp(pos.X, minX, Mathf.Max(minX, minX + root.Size.X - Size.X));
		pos.Y = Mathf.Clamp(pos.Y, minY, Mathf.Max(minY, minY + root.Size.Y - Size.Y));
		Position = pos;
	}

	// 窗口内容区 = 面板尺寸 + 四周留白（无标题栏，1:1 无缩放）。
	void SyncWindowSize()
	{
		if (quick == null)
			return;
		var panelSize = quick.GetPanelSize();
		if (panelSize.X <= 0 || panelSize.Y <= 0)
			return;
		var target = new Vector2I(
			Mathf.RoundToInt(panelSize.X + PanelMargin * 2),
			Mathf.RoundToInt(panelSize.Y + PanelMargin * 2));
		if (Size != target)
			Size = target;
	}
}

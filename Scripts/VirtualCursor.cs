using Godot;

/// <summary>
/// 虚拟光标：Android 触摸板模式的可见、可拖动移动光标。
/// 展开后单指拖动=移动光标（相对位移），短按=左键，长按=右键，常驻"M"按钮=中键。
/// CanvasLayer(Layer=92)として EmueraContent の子に追加。
/// 光标本体 MouseFilter=Ignore，不参与任何点击判定，只做视觉展示。
/// 所有命中测试、hover 同步、点击提交均调用 EmueraContent 公开的转发方法。
/// </summary>
public partial class VirtualCursor : CanvasLayer
{
	const int VK_LEFT   = 0x01;
	const int VK_RIGHT  = 0x02;
	const int VK_MIDDLE = 0x04;

	const float CursorMoveSensitivity = 1.0f;  // 触摸位移 → 光标位移灵敏度系数
	const float LongPressDuration = 0.45f;     // 长按阈值（秒）
	const float DragThreshold = 10.0f;         // 总位移超过此值视为"移动"，不触发点击
	const int MiddleButtonSize = 48;           // 常驻中键按钮尺寸
	const int TopMargin = 68;                  // 中键按钮顶部边距（避开菜单栏）
	const int RightMargin = 8;

	Control layerRoot;
	TextureRect cursorVisual;
	Button middleButton;

	bool enabled = false;
	Vector2 cursorGlobalPosition = Vector2.Zero; // 光标当前坐标（屏幕全局坐标，不跟随内容滚动/缩放）

	// 手势状态
	int trackedTouchIndex = -1;
	Vector2 gestureStartGlobal = Vector2.Zero;
	Vector2 gestureLastGlobal = Vector2.Zero;
	float gestureElapsed = 0f;
	bool gestureMoved = false;
	bool gestureLongPressTriggered = false;

	Control lastHoverButton = null;

	public override void _Ready()
	{
		Layer = 92;

		layerRoot = new Control();
		layerRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layerRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(layerRoot);

		BuildCursorVisual();
		BuildMiddleButton();

		layerRoot.Visible = false;
	}

	void BuildCursorVisual()
	{
		cursorVisual = new TextureRect();
		cursorVisual.Texture = ResourceLoader.Load<Texture2D>("res://Icons/cursor.svg");
		cursorVisual.CustomMinimumSize = new Vector2(32, 32);
		cursorVisual.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		cursorVisual.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		cursorVisual.MouseFilter = Control.MouseFilterEnum.Ignore;
		cursorVisual.Visible = false;
		layerRoot.AddChild(cursorVisual);
	}

	void BuildMiddleButton()
	{
		// 半透明背景容器
		var container = new PanelContainer();
		container.CustomMinimumSize = new Vector2(MiddleButtonSize + 8, MiddleButtonSize + 8);
		container.AnchorLeft = 1f;
		container.AnchorRight = 1f;
		container.AnchorTop = 0f;
		container.AnchorBottom = 0f;
		container.GrowHorizontal = Control.GrowDirection.Begin;
		container.GrowVertical = Control.GrowDirection.End;
		container.OffsetLeft = -(MiddleButtonSize + 8 + RightMargin);
		container.OffsetTop = TopMargin;
		container.OffsetRight = -RightMargin;
		container.OffsetBottom = TopMargin + MiddleButtonSize + 8;
		container.MouseFilter = Control.MouseFilterEnum.Ignore;

		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.95f);
		panelStyle.BorderColor = new Color(0.4f, 0.4f, 0.5f, 1.0f);
		panelStyle.BorderWidthTop = panelStyle.BorderWidthBottom = 2;
		panelStyle.BorderWidthLeft = panelStyle.BorderWidthRight = 2;
		panelStyle.CornerRadiusTopLeft = panelStyle.CornerRadiusTopRight = 6;
		panelStyle.CornerRadiusBottomLeft = panelStyle.CornerRadiusBottomRight = 6;
		panelStyle.ContentMarginLeft = panelStyle.ContentMarginRight = 4;
		panelStyle.ContentMarginTop = panelStyle.ContentMarginBottom = 4;
		container.AddThemeStyleboxOverride("panel", panelStyle);

		middleButton = new Button();
		middleButton.Text = "M";
		middleButton.CustomMinimumSize = new Vector2(MiddleButtonSize, MiddleButtonSize);
		middleButton.MouseFilter = Control.MouseFilterEnum.Stop;
		EmueraContent.StyleButton(middleButton);
		middleButton.Pressed += OnMiddleButtonPressed;

		container.AddChild(middleButton);
		layerRoot.AddChild(container);
	}

	void OnMiddleButtonPressed()
	{
		var content = EmueraContent.instance;
		if (content == null)
			return;
		// 光标存储的是屏幕全局坐标，直接用于中键提交
		content.VirtualCursorCommitClick(cursorGlobalPosition, VK_MIDDLE);
	}

	public void Enable()
	{
		enabled = true;
		layerRoot.Visible = true;
		ResetGestureState();
		// 初始位置：屏幕中心（不依赖游戏内容加载状态）
		var viewport = GetViewport();
		if (viewport != null)
		{
			var viewportRect = viewport.GetVisibleRect();
			cursorGlobalPosition = viewportRect.Position + viewportRect.Size * 0.5f;
		}
		UpdateCursorVisualPosition();
	}

	public void Disable()
	{
		enabled = false;
		layerRoot.Visible = false;
		ResetGestureState();
		ClearHover();
	}

	public bool IsEnabled => enabled;

	// EmueraContent.HandleContentPointerInput 调用：虚拟光标模式开启时，
	// 单指按下/拖动/释放统一交给这里处理（移动光标 + 手势判定）。
	public bool HandleGesture(InputEvent @event, bool acceptEvent)
	{
		if (!enabled)
			return false;

		// 桌面测试：支持鼠标左键拖动（模拟触摸）
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Pressed)
			{
				if (trackedTouchIndex < 0)
				{
					trackedTouchIndex = 0; // 鼠标用固定 index
					gestureStartGlobal = mb.GlobalPosition;
					gestureLastGlobal = mb.GlobalPosition;
					gestureElapsed = 0f;
					gestureMoved = false;
					gestureLongPressTriggered = false;
					if (acceptEvent)
						AcceptEvent(@event);
					return true;
				}
			}
			else if (trackedTouchIndex == 0)
			{
				// 释放：判断是否触发点击
				if (!gestureMoved && !gestureLongPressTriggered)
				{
					// 短按 = 左键
					CommitClick(VK_LEFT);
				}
				ResetGestureState();
				if (acceptEvent)
					AcceptEvent(@event);
				return true;
			}
			return false;
		}

		if (@event is InputEventMouseMotion mm && trackedTouchIndex == 0)
		{
			var delta = mm.GlobalPosition - gestureLastGlobal;
			gestureLastGlobal = mm.GlobalPosition;

			// 移动光标
			MoveCursorBy(delta * CursorMoveSensitivity);

			// 判断是否已"移动过"
			var totalDelta = mm.GlobalPosition - gestureStartGlobal;
			if (totalDelta.Length() >= DragThreshold)
				gestureMoved = true;

			if (acceptEvent)
				AcceptEvent(@event);
			return true;
		}

		// Android 触摸：只处理单指（双指已经被 HandleContentTouchGesture 拦截了）
		if (@event is InputEventScreenTouch touch)
		{
			if (touch.Pressed)
			{
				if (trackedTouchIndex < 0)
				{
					trackedTouchIndex = touch.Index;
					gestureStartGlobal = touch.Position;
					gestureLastGlobal = touch.Position;
					gestureElapsed = 0f;
					gestureMoved = false;
					gestureLongPressTriggered = false;
					if (acceptEvent)
						AcceptEvent(@event);
					return true;
				}
			}
			else if (touch.Index == trackedTouchIndex)
			{
				// 释放：判断是否触发点击
				if (!gestureMoved && !gestureLongPressTriggered)
				{
					// 短按 = 左键
					CommitClick(VK_LEFT);
				}
				ResetGestureState();
				if (acceptEvent)
					AcceptEvent(@event);
				return true;
			}
			return false;
		}

		if (@event is InputEventScreenDrag drag && drag.Index == trackedTouchIndex)
		{
			var delta = drag.Position - gestureLastGlobal;
			gestureLastGlobal = drag.Position;

			// 移动光标
			MoveCursorBy(delta * CursorMoveSensitivity);

			// 判断是否已"移动过"
			var totalDelta = drag.Position - gestureStartGlobal;
			if (totalDelta.Length() >= DragThreshold)
				gestureMoved = true;

			if (acceptEvent)
				AcceptEvent(@event);
			return true;
		}

		return false;
	}

	void MoveCursorBy(Vector2 delta)
	{
		// 直接操作屏幕全局坐标
		cursorGlobalPosition += delta;

		// clamp 到屏幕可视区域边界
		var viewport = GetViewport();
		if (viewport != null)
		{
			var viewportRect = viewport.GetVisibleRect();
			cursorGlobalPosition.X = Mathf.Clamp(cursorGlobalPosition.X, viewportRect.Position.X, viewportRect.Position.X + viewportRect.Size.X);
			cursorGlobalPosition.Y = Mathf.Clamp(cursorGlobalPosition.Y, viewportRect.Position.Y, viewportRect.Position.Y + viewportRect.Size.Y);
		}

		// 更新 hover 高亮（双通道同步）
		var content = EmueraContent.instance;
		if (content != null)
			content.VirtualCursorUpdateHover(cursorGlobalPosition);

		// 更新光标可视化位置
		UpdateCursorVisualPosition();
	}

	void UpdateCursorVisualPosition()
	{
		if (cursorVisual == null)
			return;

		cursorVisual.GlobalPosition = cursorGlobalPosition;
		cursorVisual.Visible = true;
	}

	void CommitClick(int mouseVk)
	{
		var content = EmueraContent.instance;
		if (content == null)
			return;

		// 光标存储的是屏幕全局坐标，直接用于点击提交
		content.VirtualCursorCommitClick(cursorGlobalPosition, mouseVk);
	}

	void ResetGestureState()
	{
		trackedTouchIndex = -1;
		gestureElapsed = 0f;
		gestureMoved = false;
		gestureLongPressTriggered = false;
	}

	void ClearHover()
	{
		GenericUtils.ClearPointingButton();
		lastHoverButton = null;
	}

	void AcceptEvent(InputEvent @event)
	{
		var vp = GetViewport();
		if (vp != null && GodotObject.IsInstanceValid(vp))
			vp.SetInputAsHandled();
	}

	public override void _Process(double delta)
	{
		if (!enabled || trackedTouchIndex < 0 || gestureLongPressTriggered || gestureMoved)
			return;

		gestureElapsed += (float)delta;
		if (gestureElapsed >= LongPressDuration)
		{
			gestureLongPressTriggered = true;
			// 长按 = 右键，立即触发（不等松手）
			CommitClick(VK_RIGHT);
		}
	}
}

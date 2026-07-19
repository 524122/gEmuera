using Godot;

/// <summary>
/// Android 端虚拟光标：单指拖动移动光标，短按提交左键，长按未移动提交右键，
/// 长按后继续拖动则进入真实滑动模式，复用 EmueraContent 的滚动链路。
/// </summary>
public partial class VirtualCursor : CanvasLayer
{
	const int VK_LEFT = 0x01;
	const int VK_RIGHT = 0x02;
	const int VK_MIDDLE = 0x04;

	const float CursorMoveSensitivity = 1.0f;
	const float LongPressDuration = 0.45f;
	const float DragThreshold = 10.0f;
	const float MinDragDeltaSquared = 0.0001f;
	const int MiddleButtonSize = 48;
	const int TopMargin = 68;
	const int RightMargin = 8;

	Control layerRoot;
	TextureRect cursorVisual;
	Button middleButton;

	bool enabled = false;
	bool hasCursorPosition = false;
	Vector2 cursorGlobalPosition = Vector2.Zero;

	int trackedTouchIndex = -1;
	Vector2 gestureStartGlobal = Vector2.Zero;
	Vector2 gestureLastGlobal = Vector2.Zero;
	float gestureElapsed = 0f;
	bool gestureMoved = false;
	bool gestureLongPressTriggered = false;
	bool gestureHadDragSample = false;
	bool gestureLongPressSlideActive = false;

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
		EmueraContent.instance?.VirtualCursorCommitClick(cursorGlobalPosition, VK_MIDDLE);
	}

	public void Enable()
	{
		enabled = true;
		layerRoot.Visible = true;
		ResetGestureState();

		if (!hasCursorPosition)
		{
			var bounds = GetCursorMovementBounds();
			cursorGlobalPosition = bounds.Position + bounds.Size * 0.5f;
			hasCursorPosition = true;
		}
		ClampCursorToMovementBounds();
		UpdateCursorVisualPosition();
		EmueraContent.instance?.VirtualCursorSynchronizePosition(cursorGlobalPosition);
	}

	public void Disable()
	{
		enabled = false;
		layerRoot.Visible = false;
		ResetGestureState();
		ClearHover();
	}

	public bool IsEnabled => enabled;

	public bool HandleGesture(InputEvent @event, bool acceptEvent)
	{
		if (!enabled)
			return false;

		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Pressed)
			{
				if (trackedTouchIndex < 0)
				{
					BeginGesture(0, mb.GlobalPosition);
					if (acceptEvent)
						AcceptEvent(@event);
					return true;
				}
			}
			else if (trackedTouchIndex == 0)
			{
				ReleaseGesture();
				if (acceptEvent)
					AcceptEvent(@event);
				return true;
			}
			return false;
		}

		if (@event is InputEventMouseMotion mm && trackedTouchIndex == 0)
			return HandleDrag(mm.GlobalPosition, mm.Relative, acceptEvent, @event);

		if (@event is InputEventScreenTouch touch)
		{
			if (touch.Pressed)
			{
				if (trackedTouchIndex < 0)
				{
					BeginGesture(touch.Index, touch.Position);
					if (acceptEvent)
						AcceptEvent(@event);
					return true;
				}
			}
			else if (touch.Index == trackedTouchIndex)
			{
				ReleaseGesture();
				if (acceptEvent)
					AcceptEvent(@event);
				return true;
			}
			return false;
		}

		if (@event is InputEventScreenDrag drag && drag.Index == trackedTouchIndex)
			return HandleDrag(drag.Position, drag.Relative, acceptEvent, @event);

		return false;
	}

	void BeginGesture(int touchIndex, Vector2 globalPosition)
	{
		trackedTouchIndex = touchIndex;
		gestureStartGlobal = globalPosition;
		gestureLastGlobal = globalPosition;
		gestureElapsed = 0f;
		gestureMoved = false;
		gestureLongPressTriggered = false;
		gestureHadDragSample = false;
		gestureLongPressSlideActive = false;
	}

	void ReleaseGesture()
	{
		if (gestureLongPressSlideActive)
			EndLongPressSlide(true);
		else if (!gestureMoved && gestureLongPressTriggered)
			CommitClick(VK_RIGHT);
		else if (!gestureMoved)
			CommitClick(VK_LEFT);

		ResetGestureState();
	}

	bool HandleDrag(Vector2 eventPosition, Vector2 eventRelative, bool acceptEvent, InputEvent @event)
	{
		var delta = ResolveDragDelta(eventPosition, eventRelative);

		if (gestureLongPressTriggered)
		{
			HandleLongPressSlideDelta(delta);
			if (acceptEvent)
				AcceptEvent(@event);
			return true;
		}

		MoveCursorBy(delta * CursorMoveSensitivity);

		var totalDelta = eventPosition - gestureStartGlobal;
		if (totalDelta.Length() >= DragThreshold)
			gestureMoved = true;

		if (acceptEvent)
			AcceptEvent(@event);
		return true;
	}

	void HandleLongPressSlideDelta(Vector2 delta)
	{
		if (delta.LengthSquared() <= MinDragDeltaSquared)
			return;

		if (!gestureLongPressSlideActive)
			BeginLongPressSlide();
		EmueraContent.instance?.VirtualCursorSlideBy(delta);
		gestureMoved = true;
	}

	void BeginLongPressSlide()
	{
		if (gestureLongPressSlideActive)
			return;

		gestureLongPressSlideActive = true;
		// 长按后继续移动时，不提交右键，改为复用主控制台的真实滚动链路。
		EmueraContent.instance?.VirtualCursorBeginSlide(cursorGlobalPosition);
	}

	void EndLongPressSlide(bool startInertia)
	{
		if (!gestureLongPressSlideActive)
			return;

		gestureLongPressSlideActive = false;
		EmueraContent.instance?.VirtualCursorEndSlide(startInertia);
	}

	Vector2 ResolveDragDelta(Vector2 eventPosition, Vector2 eventRelative)
	{
		bool firstDragSample = !gestureHadDragSample;
		gestureHadDragSample = true;
		var fallbackDelta = eventPosition - gestureLastGlobal;
		gestureLastGlobal = eventPosition;

		// Godot 拖动事件已经提供 relative。虚拟光标是触摸板模式，优先使用
		// relative，避免 Android 首帧 position 基准不一致导致光标起步横跳。
		if (eventRelative.LengthSquared() > MinDragDeltaSquared)
			return eventRelative;
		if (firstDragSample && fallbackDelta.LengthSquared() >= DragThreshold * DragThreshold)
			return Vector2.Zero;
		return fallbackDelta;
	}

	void MoveCursorBy(Vector2 delta)
	{
		cursorGlobalPosition += delta;
		ClampCursorToMovementBounds();

		EmueraContent.instance?.VirtualCursorSynchronizePosition(cursorGlobalPosition);
		UpdateCursorVisualPosition();
	}

	Rect2 GetCursorMovementBounds()
	{
		var content = EmueraContent.instance;
		if (content != null)
		{
			var contentRect = content.VirtualCursorGetContentViewportRect();
			if (contentRect.Size.X > 1.0f && contentRect.Size.Y > 1.0f)
				return contentRect;
		}

		var viewport = GetViewport();
		if (viewport != null)
			return viewport.GetVisibleRect();
		return new Rect2(Vector2.Zero, new Vector2(1, 1));
	}

	void ClampCursorToMovementBounds()
	{
		var bounds = GetCursorMovementBounds();
		cursorGlobalPosition.X = Mathf.Clamp(cursorGlobalPosition.X, bounds.Position.X, bounds.Position.X + bounds.Size.X);
		cursorGlobalPosition.Y = Mathf.Clamp(cursorGlobalPosition.Y, bounds.Position.Y, bounds.Position.Y + bounds.Size.Y);
	}

	void UpdateCursorVisualPosition()
	{
		if (cursorVisual == null)
			return;

		cursorVisual.GlobalPosition = cursorGlobalPosition;
		cursorVisual.Visible = true;
	}

	// 旋转、分屏或系统栏变化时保留已有位置，只将其收回新的可视内容区域。
	public void RefreshViewportBounds()
	{
		if (!enabled)
			return;

		ClampCursorToMovementBounds();
		UpdateCursorVisualPosition();
		EmueraContent.instance?.VirtualCursorSynchronizePosition(cursorGlobalPosition);
	}

	void CommitClick(int mouseVk)
	{
		EmueraContent.instance?.VirtualCursorCommitClick(cursorGlobalPosition, mouseVk);
	}

	void ResetGestureState()
	{
		EndLongPressSlide(false);
		trackedTouchIndex = -1;
		gestureElapsed = 0f;
		gestureMoved = false;
		gestureLongPressTriggered = false;
		gestureHadDragSample = false;
		gestureLongPressSlideActive = false;
	}

	void ClearHover()
	{
		GenericUtils.ClearPointingButton();
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
			// 长按先进入待定状态：松手不移动=右键；继续移动=真实滑动。
			gestureLongPressTriggered = true;
		}
	}
}

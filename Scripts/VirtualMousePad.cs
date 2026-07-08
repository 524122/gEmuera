using Godot;

/// <summary>
/// Android 向け虚拟鼠标键选择面板。
/// 左/中/右键の切替と单次(tap)/锁定(long-press)モードをサポートする。
/// CanvasLayer(Layer=91)として EmueraContent の子に追加。
/// root の MouseFilter は Ignore なので Emuera ボタン判定には影響しない。
/// </summary>
public partial class VirtualMousePad : CanvasLayer
{
    // VK コード定数
    const int VK_LEFT   = 0x01;
    const int VK_RIGHT  = 0x02;
    const int VK_MIDDLE = 0x04;

    const int BtnSize     = 52;   // タップしやすいサイズ
    const int PadWidth    = 200;
    const int PadHeight   = 120;
    const float LongPressDuration = 0.45f; // 秒

    Control layerRoot;
    Button  toggleButton;
    Control pad;
    Label   lockIndicator;

    bool padOpen = false;
    // 長押し検出用
    Button pendingLongPressBtn = null;
    int    pendingLongPressVk  = VK_LEFT;
    float  longPressElapsed    = 0f;
    bool   longPressTriggered  = false;

    public override void _Ready()
    {
        Layer = 91;

        layerRoot = new Control();
        layerRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layerRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(layerRoot);

        BuildToggleButton();
        BuildPad();
        RefreshStatusLabel();
    }

    void BuildToggleButton()
    {
        toggleButton = new Button();
        toggleButton.Text = "L";
        toggleButton.CustomMinimumSize = new Vector2(BtnSize, BtnSize);
        toggleButton.AnchorLeft  = 0f;
        toggleButton.AnchorTop   = 1f;
        toggleButton.AnchorRight = 0f;
        toggleButton.AnchorBottom= 1f;
        toggleButton.GrowHorizontal = Control.GrowDirection.End;
        toggleButton.GrowVertical   = Control.GrowDirection.Begin;
        // 左下: 右键面板 QuickButtons が右下に占有しているため左下に配置
        toggleButton.OffsetLeft   = 8;
        toggleButton.OffsetTop    = -(BtnSize + 8);
        toggleButton.OffsetRight  = BtnSize + 8;
        toggleButton.OffsetBottom = -8;
        toggleButton.MouseFilter  = Control.MouseFilterEnum.Stop;
        StyleToggleButton(toggleButton, false);
        toggleButton.Pressed += OnTogglePressed;
        layerRoot.AddChild(toggleButton);
    }

    void BuildPad()
    {
        pad = new Control();
        pad.CustomMinimumSize = new Vector2(PadWidth, PadHeight);
        pad.AnchorLeft   = 0f;
        pad.AnchorTop    = 1f;
        pad.AnchorRight  = 0f;
        pad.AnchorBottom = 1f;
        pad.GrowHorizontal = Control.GrowDirection.End;
        pad.GrowVertical   = Control.GrowDirection.Begin;
        pad.OffsetLeft   = 8;
        pad.OffsetTop    = -(BtnSize + 8 + PadHeight + 6);
        pad.OffsetRight  = PadWidth + 8;
        pad.OffsetBottom = -(BtnSize + 8 + 6);
        pad.MouseFilter  = Control.MouseFilterEnum.Stop;
        pad.Visible      = false;

        // 半透明背景
        var bg = new PanelContainer();
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.MouseFilter = Control.MouseFilterEnum.Ignore;
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.05f, 0.05f, 0.07f, 0.92f);
        style.CornerRadiusTopLeft = style.CornerRadiusTopRight = 6;
        style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 6;
        style.ContentMarginLeft = style.ContentMarginRight = 8;
        style.ContentMarginTop  = style.ContentMarginBottom = 8;
        bg.AddThemeStyleboxOverride("panel", style);
        pad.AddChild(bg);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        vbox.AddThemeConstantOverride("separation", 6);
        bg.AddChild(vbox);

        // 行1: 左/中/右
        var row1 = new HBoxContainer();
        row1.AddThemeConstantOverride("separation", 6);
        row1.MouseFilter = Control.MouseFilterEnum.Ignore;
        vbox.AddChild(row1);

        row1.AddChild(MakeMouseBtn("L", VK_LEFT));
        row1.AddChild(MakeMouseBtn("M", VK_MIDDLE));
        row1.AddChild(MakeMouseBtn("R", VK_RIGHT));

        // 行2: キャンセル（左键に戻す）
        var row2 = new HBoxContainer();
        row2.AddThemeConstantOverride("separation", 6);
        row2.MouseFilter = Control.MouseFilterEnum.Ignore;
        vbox.AddChild(row2);

        var cancelBtn = new Button();
        cancelBtn.Text = "×";
        cancelBtn.CustomMinimumSize = new Vector2(54, 36);
        cancelBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cancelBtn.MouseFilter = Control.MouseFilterEnum.Stop;
        EmueraContent.StyleButton(cancelBtn);
        cancelBtn.Pressed += OnCancelPressed;
        row2.AddChild(cancelBtn);

        // ロック状態インジケータ
        lockIndicator = new Label();
        lockIndicator.Text = "";
        lockIndicator.HorizontalAlignment = HorizontalAlignment.Right;
        lockIndicator.CustomMinimumSize = new Vector2(0, 20);
        lockIndicator.AddThemeColorOverride("font_color", new Color(1, 0.85f, 0.3f));
        vbox.AddChild(lockIndicator);

        layerRoot.AddChild(pad);
    }

    Button MakeMouseBtn(string label, int vk)
    {
        var btn = new Button();
        btn.Text = label;
        btn.CustomMinimumSize = new Vector2(54, 38);
        btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        btn.MouseFilter = Control.MouseFilterEnum.Stop;
        EmueraContent.StyleButton(btn);

        // press: 長押し計測開始
        btn.ButtonDown += () =>
        {
            pendingLongPressBtn = btn;
            pendingLongPressVk  = vk;
            longPressElapsed    = 0f;
            longPressTriggered  = false;
        };
        // release: 長押し未発火なら単次モード
        btn.ButtonUp += () =>
        {
            if (pendingLongPressBtn == btn && !longPressTriggered)
                CommitMouseButton(vk, singleShot: true);
            pendingLongPressBtn = null;
        };

        return btn;
    }

    void CommitMouseButton(int vk, bool singleShot)
    {
        EmueraContent.instance?.SetPendingMouseButton(vk, singleShot);
        // ロック中はパネルを開けたまま
        if (singleShot)
            ClosePad();
        else
            RefreshStatusLabel();
    }

    void OnTogglePressed()
    {
        if (padOpen)
            ClosePad();
        else
            OpenPad();
    }

    void OnCancelPressed()
    {
        EmueraContent.instance?.SetPendingMouseButton(VK_LEFT, false);
        ClosePad();
    }

    void OpenPad()
    {
        padOpen = true;
        if (pad != null) pad.Visible = true;
    }

    void ClosePad()
    {
        padOpen = false;
        if (pad != null) pad.Visible = false;
    }

    public void RefreshStatusLabel()
    {
        int vk = EmueraContent.instance?.PendingMouseVk ?? VK_LEFT;
        bool singleShot = EmueraContent.instance?.PendingMouseSingleShot ?? false;
        string keyLabel = vk == VK_RIGHT ? "R" : vk == VK_MIDDLE ? "M" : "L";
        bool locked = !singleShot && vk != VK_LEFT;

        if (toggleButton != null)
        {
            toggleButton.Text = keyLabel;
            StyleToggleButton(toggleButton, locked);
        }
        if (lockIndicator != null)
            lockIndicator.Text = locked ? $"[{keyLabel}] 锁定" : "";
    }

    static void StyleToggleButton(Button btn, bool active)
    {
        var normal = new StyleBoxFlat();
        normal.BgColor = active
            ? new Color(0.8f, 0.3f, 0.1f, 0.9f)
            : new Color(0.08f, 0.08f, 0.12f, 0.85f);
        normal.CornerRadiusTopLeft = normal.CornerRadiusTopRight = 8;
        normal.CornerRadiusBottomLeft = normal.CornerRadiusBottomRight = 8;
        normal.ContentMarginLeft = normal.ContentMarginRight = 6;
        normal.ContentMarginTop  = normal.ContentMarginBottom = 6;
        normal.BorderColor = active ? new Color(1f, 0.6f, 0.2f) : new Color(0.35f, 0.35f, 0.45f);
        normal.BorderWidthTop = normal.BorderWidthBottom = 1;
        normal.BorderWidthLeft = normal.BorderWidthRight = 1;
        btn.AddThemeStyleboxOverride("normal", normal);
        btn.AddThemeStyleboxOverride("pressed", normal);
        btn.AddThemeStyleboxOverride("hover", normal);
        btn.AddThemeFontSizeOverride("font_size", 16);
    }

    public override void _Process(double delta)
    {
        if (pendingLongPressBtn == null || longPressTriggered)
            return;
        longPressElapsed += (float)delta;
        if (longPressElapsed >= LongPressDuration)
        {
            longPressTriggered = true;
            CommitMouseButton(pendingLongPressVk, singleShot: false);
        }
    }
}

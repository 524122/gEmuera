using Godot;

/// <summary>
/// 启动遮罩视图。它只负责显示当前启动状态，不解析路径、不操作核心状态。
/// </summary>
public sealed partial class EmueraStartupOverlayView : Control
{
    Label statusLabel;

    public void Build()
    {
        Name = "StartupOverlay";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var bg = new ColorRect();
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        bg.Color = Colors.Black;
        AddChild(bg);

        statusLabel = new Label();
        statusLabel.SetAnchorsPreset(LayoutPreset.FullRect);
        statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        statusLabel.VerticalAlignment = VerticalAlignment.Center;
        statusLabel.Text = "Loading game...";
        statusLabel.AddThemeFontSizeOverride("font_size", 20);
        statusLabel.AddThemeColorOverride("font_color", Colors.White);
        AddChild(statusLabel);
    }

    public void SetStatus(string status)
    {
        if (statusLabel != null)
            statusLabel.Text = status;
    }
}

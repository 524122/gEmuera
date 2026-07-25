using Godot;
using System.Collections.Concurrent;
using System.Threading;

/// <summary>
/// Godot 宿主文字渲染组件，负责把后台线程的 GDrawString 请求转成主线程 SubViewport 渲染。
/// 组件只拥有离屏渲染节点和队列处理，调用方仍通过 EmueraMain 静态门面保持兼容。
/// </summary>
public sealed partial class EmueraTextRenderComponent : Node
{
	static readonly ConcurrentQueue<EmueraMain.TextRenderItem> renderQueue = new ConcurrentQueue<EmueraMain.TextRenderItem>();
	static EmueraTextRenderComponent currentInstance;
	static int renderIdCounter = 0;

	SubViewport textViewport;
	Label textRenderLabel;
	FontFile textRenderFont;
	EmueraMain.TextRenderItem pendingTextRenderItem;
	int textRenderFrameCount = 0;
	bool textWaitingForRender = false;

	public static int QueuedWorkCount => renderQueue.Count;

	/// <summary>
	/// Completes and drops text render requests belonging to the previous
	/// session so a later view cannot observe stale text output.
	/// </summary>
	internal static void ResetCanarySessionState()
	{
		while (renderQueue.TryDequeue(out var item))
		{
			item.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
			item.Completed.Set();
		}
		currentInstance?.ResetPendingRenderState();
	}

	void ResetPendingRenderState()
	{
		if (pendingTextRenderItem != null)
		{
			pendingTextRenderItem.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
			pendingTextRenderItem.Completed.Set();
			pendingTextRenderItem = null;
		}
		textWaitingForRender = false;
		textRenderFrameCount = 0;
		if (textViewport != null)
			textViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
	}

	public static EmueraMain.TextRenderItem Submit(string text, string fontName, int fontSize, int fontStyle, uEmuera.Drawing.Color color, int width, int height)
	{
		if (GenericUtils.IsOnMainThread())
			return null;

		var item = new EmueraMain.TextRenderItem
		{
			Id = Interlocked.Increment(ref renderIdCounter),
			Text = text ?? "",
			FontName = fontName,
			FontSize = System.Math.Max(1, fontSize),
			FontStyle = fontStyle,
			Color = color,
			Width = System.Math.Max(1, width),
			Height = System.Math.Max(1, height)
		};
		renderQueue.Enqueue(item);
		return item;
	}

	public override void _Ready()
	{
		currentInstance = this;
	}

	public override void _Process(double delta)
	{
		// Do not create a SubViewport or load the fallback font until legacy
		// GDRAWSTRING actually submits work. This keeps the Compatibility
		// startup path free of an otherwise unused offscreen render target.
		if (!textWaitingForRender && renderQueue.IsEmpty)
			return;
		ProcessQueue();
	}

	public void ProcessQueue()
	{
		if (textViewport == null)
			SetupTextRenderer();
		if (textViewport == null)
			return;

		if (textWaitingForRender)
		{
			textRenderFrameCount++;
			if (textRenderFrameCount >= 2)
				CompletePendingRender();
		}

		if (!textWaitingForRender && renderQueue.TryDequeue(out var item))
			StartRender(item);
	}

	public override void _ExitTree()
	{
		// 无法在退出阶段可靠继续绘制文字，返回空图可以释放后台等待并保持调用方 fallback 语义。
		if (pendingTextRenderItem != null)
		{
			CompleteWithEmptyImage(pendingTextRenderItem);
			pendingTextRenderItem = null;
		}

		while (renderQueue.TryDequeue(out var queuedItem))
			CompleteWithEmptyImage(queuedItem);
		if (currentInstance == this)
			currentInstance = null;
	}

	void SetupTextRenderer()
	{
		if (textViewport != null)
			return;

		textRenderFont = ResourceLoader.Load<FontFile>("res://Fonts/MS Gothic.ttf");
		textViewport = new SubViewport();
		textViewport.TransparentBg = true;
		textViewport.Size = new Vector2I(16, 16);
		textViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		textViewport.Name = "TextRenderViewport";

		textRenderLabel = new Label();
		textRenderLabel.Name = "TextRenderLabel";
		textRenderLabel.Position = Vector2.Zero;
		textRenderLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		textRenderLabel.VerticalAlignment = VerticalAlignment.Top;
		textRenderLabel.HorizontalAlignment = HorizontalAlignment.Left;
		textRenderLabel.AutowrapMode = TextServer.AutowrapMode.Off;
		textRenderLabel.ClipText = true;
		if (textRenderFont != null)
			textRenderLabel.AddThemeFontOverride("font", textRenderFont);

		textViewport.AddChild(textRenderLabel);
		AddChild(textViewport);
	}

	void CompletePendingRender()
	{
		if (pendingTextRenderItem == null)
		{
			textWaitingForRender = false;
			if (textViewport != null)
				textViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
			return;
		}

		var vpTex = textViewport.GetTexture();
		var resultImg = vpTex?.GetImage();
		if (resultImg != null && resultImg.GetWidth() > 0 && resultImg.GetHeight() > 0)
		{
			if (resultImg.GetFormat() != Godot.Image.Format.Rgba8)
				resultImg.Convert(Godot.Image.Format.Rgba8);
			pendingTextRenderItem.ResultImage = resultImg;
		}
		else
		{
			pendingTextRenderItem.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
		}

		pendingTextRenderItem.Completed.Set();
		pendingTextRenderItem = null;
		textWaitingForRender = false;
		textViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
	}

	void StartRender(EmueraMain.TextRenderItem item)
	{
		textViewport.Size = new Vector2I(item.Width, item.Height);
		textRenderLabel.Text = item.Text ?? "";
		textRenderLabel.Size = new Vector2(item.Width, item.Height);
		textRenderLabel.CustomMinimumSize = textRenderLabel.Size;
		textRenderLabel.AddThemeFontSizeOverride("font_size", item.FontSize);
		textRenderLabel.AddThemeColorOverride("font_color", new Color(item.Color.r, item.Color.g, item.Color.b, item.Color.a));
		textRenderLabel.Position = Vector2.Zero;

		textViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		textRenderFrameCount = 0;
		textWaitingForRender = true;
		pendingTextRenderItem = item;
	}

	static void CompleteWithEmptyImage(EmueraMain.TextRenderItem item)
	{
		item.ResultImage = Godot.Image.CreateEmpty(1, 1, false, Godot.Image.Format.Rgba8);
		item.Completed.Set();
	}
}

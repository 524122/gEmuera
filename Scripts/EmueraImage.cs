using Godot;

// Lightweight CanvasItem used for CBG/image presentation. It owns draw parameters
// only; texture lifetime remains in SpriteManager/EmueraContent so cache eviction
// and UI ownership stay centralized.
public partial class EmueraImage : Control
{
	private Texture2D _sourceTexture;
	private Rect2 _sourceRegion;
	private Vector2 _drawOffset;
	private Vector2 _drawSize;
	private bool _flipX;
	private bool _flipY;
	// Last applied ColorMatrix key. Avoiding redundant Material assignment reduces
	// CanvasItem invalidation when animated CBG layers refresh with the same matrix.
	private ulong _colorMatrixKey;
	private bool _hasColorMatrixKey;

	public Texture2D SourceTexture
	{
		get => _sourceTexture;
		set
		{
			_sourceTexture = value;
			QueueRedraw();
		}
	}

	public Rect2 SourceRegion
	{
		get => _sourceRegion;
		set
		{
			_sourceRegion = value;
			QueueRedraw();
		}
	}

	public Vector2 DrawOffset
	{
		get => _drawOffset;
		set
		{
			_drawOffset = value;
			QueueRedraw();
		}
	}

	public Vector2 DrawSize
	{
		get => _drawSize;
		set
		{
			_drawSize = value;
			QueueRedraw();
		}
	}

	public bool FlipX
	{
		get => _flipX;
		set
		{
			_flipX = value;
			QueueRedraw();
		}
	}

	public bool FlipY
	{
		get => _flipY;
		set
		{
			_flipY = value;
			QueueRedraw();
		}
	}

	/// <summary>
	/// Set the ColorMatrix shader material for display-time color transformation.
	/// Pass null to remove the shader. The material is applied as CanvasItem.Material.
	/// </summary>
	public void SetColorMatrix(float[][] cm)
	{
		if (cm == null)
		{
			Material = null;
			_hasColorMatrixKey = false;
			return;
		}
		ulong matrixKey = ColorMatrixGPU.GetMatrixKey(cm);
		if (_hasColorMatrixKey && _colorMatrixKey == matrixKey && Material is ShaderMaterial sm && sm.Shader == ColorMatrixGPU.Shader)
			return;

		// Shared materials are treated as read-only. A new key selects a different
		// cached ShaderMaterial instead of mutating uniforms that may be used by
		// other visible EmueraImage nodes.
		Material = ColorMatrixGPU.GetSharedMaterial(cm, matrixKey);
		_colorMatrixKey = matrixKey;
		_hasColorMatrixKey = true;
	}

	public EmueraImage()
	{
		TextureFilter = TextureFilterEnum.Nearest;
	}

	public override void _Ready()
	{
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (SourceTexture == null) return;

		var drawSize = DrawSize.X > 0 && DrawSize.Y > 0 ? DrawSize : Size;
		var destRect = new Rect2(DrawOffset.X, DrawOffset.Y, drawSize.X, drawSize.Y);

		bool flip = FlipX || FlipY;
		if (flip)
		{
			var center = destRect.Position + destRect.Size / 2;
			DrawSetTransform(center, 0, new Vector2(FlipX ? -1 : 1, FlipY ? -1 : 1));
			destRect.Position = -destRect.Size / 2;
		}

		if (SourceRegion.Size.X > 0 && SourceRegion.Size.Y > 0)
		{
			DrawTextureRectRegion(SourceTexture, destRect, SourceRegion);
		}
		else
		{
			DrawTextureRect(SourceTexture, destRect, false);
		}

		if (flip)
			DrawSetTransform(Vector2.Zero, 0, Vector2.One);
	}
}

namespace GEmuera.Core.Resources;

public readonly record struct PixelHandle(long Value)
{
    public bool IsValid => Value > 0;
}

public readonly record struct PixelRevision(long Value)
{
    public static PixelRevision Initial => new(1);
    public bool IsValid => Value > 0;
}

/// <summary>
/// An owned CPU surface. The byte memory is copied at construction and is not
/// exposed as writable memory; PixelStore is the only mutation authority.
/// </summary>
public sealed class PixelSurface
{
    private const int MaxDimension = 8192;
    private const long MaxPixels = 67_108_864;
    private readonly byte[] _bytes;

    public PixelSurface(
        int width,
        int height,
        PixelFormat format,
        int stride,
        ReadOnlySpan<byte> bytes,
        SourceToken sourceToken = default)
    {
        ValidateGeometry(width, height, format, stride, bytes.Length);
        _bytes = bytes.ToArray();
        Width = width;
        Height = height;
        Format = format;
        Stride = stride;
        SourceToken = sourceToken;
    }

    public int Width { get; }
    public int Height { get; }
    public PixelFormat Format { get; }
    public int Stride { get; }
    public SourceToken SourceToken { get; }
    public int ByteLength => _bytes.Length;
    public ReadOnlyMemory<byte> Bytes => _bytes;

    public static PixelSurface Empty(
        int width,
        int height,
        PixelFormat format = PixelFormat.Rgba8888StraightAlpha,
        int? stride = null,
        SourceToken sourceToken = default)
    {
        var minimumStride = checked(width * format.BytesPerPixel());
        var actualStride = stride ?? minimumStride;
        var length = checked(actualStride * height);
        return new PixelSurface(width, height, format, actualStride, new byte[length], sourceToken);
    }

    public PixelColor GetPixel(int x, int y)
    {
        ValidateCoordinate(x, y);
        var index = checked(y * Stride + x * Format.BytesPerPixel());
        return new PixelColor(_bytes[index], _bytes[index + 1], _bytes[index + 2], _bytes[index + 3]);
    }

    internal byte[] CopyBytes()
    {
        return (byte[])_bytes.Clone();
    }

    internal void ValidateCoordinate(int x, int y)
    {
        if ((uint)x >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x), x, "Pixel coordinate is outside the surface.");
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y), y, "Pixel coordinate is outside the surface.");
    }

    internal PixelSurfaceSnapshot Snapshot(PixelRevision revision, PixelRect dirtyRect)
    {
        return new PixelSurfaceSnapshot(
            Width,
            Height,
            Format,
            Stride,
            _bytes,
            revision,
            dirtyRect,
            SourceToken);
    }

    internal static void ValidateGeometry(
        int width,
        int height,
        PixelFormat format,
        int stride,
        int byteLength)
    {
        if (width <= 0 || width > MaxDimension)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Pixel width must be between 1 and 8192.");
        if (height <= 0 || height > MaxDimension)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Pixel height must be between 1 and 8192.");
        if ((long)width * height > MaxPixels)
            throw new ArgumentException("Pixel surface exceeds the CPU pixel budget.", nameof(height));

        var bytesPerPixel = format.BytesPerPixel();
        var minimumStride = checked(width * bytesPerPixel);
        if (stride < minimumStride)
            throw new ArgumentOutOfRangeException(nameof(stride), stride, "Stride is smaller than one complete row.");
        var expectedLength = checked(stride * height);
        if (byteLength != expectedLength)
            throw new ArgumentException("Owned bytes must exactly match stride multiplied by height.", nameof(byteLength));
    }
}

/// <summary>
/// Immutable, revision-stamped CPU truth. Every returned byte buffer is a
/// private copy, so a caller cannot mutate a published revision in place.
/// </summary>
public sealed class PixelSurfaceSnapshot
{
    private readonly byte[] _bytes;

    internal PixelSurfaceSnapshot(
        int width,
        int height,
        PixelFormat format,
        int stride,
        ReadOnlySpan<byte> bytes,
        PixelRevision revision,
        PixelRect dirtyRect,
        SourceToken sourceToken)
    {
        PixelSurface.ValidateGeometry(width, height, format, stride, bytes.Length);
        if (!revision.IsValid)
            throw new ArgumentOutOfRangeException(nameof(revision));
        if (dirtyRect.X < 0 || dirtyRect.Y < 0 || (long)dirtyRect.X + dirtyRect.Width > width || (long)dirtyRect.Y + dirtyRect.Height > height)
            throw new ArgumentOutOfRangeException(nameof(dirtyRect), "Dirty rectangle is outside the surface.");

        _bytes = bytes.ToArray();
        Width = width;
        Height = height;
        Format = format;
        Stride = stride;
        Revision = revision;
        DirtyRect = dirtyRect;
        SourceToken = sourceToken;
    }

    public int Width { get; }
    public int Height { get; }
    public PixelFormat Format { get; }
    public int Stride { get; }
    public PixelRevision Revision { get; }
    public PixelRect DirtyRect { get; }
    public SourceToken SourceToken { get; }
    public int ByteLength => _bytes.Length;
    public ReadOnlyMemory<byte> Bytes => _bytes;

    public PixelColor GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x), x, "Pixel coordinate is outside the surface.");
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y), y, "Pixel coordinate is outside the surface.");
        var index = checked(y * Stride + x * Format.BytesPerPixel());
        return new PixelColor(_bytes[index], _bytes[index + 1], _bytes[index + 2], _bytes[index + 3]);
    }

    internal byte[] CopyBytes()
    {
        return (byte[])_bytes.Clone();
    }
}

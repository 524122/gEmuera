namespace GEmuera.Core.Resources;

/// <summary>
/// Pixel formats understood by the CPU resource contract. The alpha mode is
/// explicit so it cannot be inferred from a platform texture default.
/// </summary>
public enum PixelFormat
{
    Rgba8888StraightAlpha = 1,
    Rgba8888PremultipliedAlpha = 2,
}

public static class PixelFormatInfo
{
    public static int BytesPerPixel(this PixelFormat format)
    {
        return format switch
        {
            PixelFormat.Rgba8888StraightAlpha or PixelFormat.Rgba8888PremultipliedAlpha => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported pixel format."),
        };
    }

    public static bool UsesPremultipliedAlpha(this PixelFormat format)
    {
        return format switch
        {
            PixelFormat.Rgba8888StraightAlpha => false,
            PixelFormat.Rgba8888PremultipliedAlpha => true,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported pixel format."),
        };
    }
}

/// <summary>
/// A validated 8-bit RGBA value. Integer construction is provided for parser
/// and script boundaries where invalid alpha values must be rejected.
/// </summary>
public readonly record struct PixelColor
{
    public PixelColor(byte red, byte green, byte blue, byte alpha)
    {
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
    }

    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }
    public byte Alpha { get; }

    public static PixelColor FromRgba(int red, int green, int blue, int alpha)
    {
        ValidateChannel(red, nameof(red));
        ValidateChannel(green, nameof(green));
        ValidateChannel(blue, nameof(blue));
        ValidateChannel(alpha, nameof(alpha));
        return new PixelColor((byte)red, (byte)green, (byte)blue, (byte)alpha);
    }

    public static PixelColor FromArgb(int alpha, int red, int green, int blue)
    {
        return FromRgba(red, green, blue, alpha);
    }

    private static void ValidateChannel(int value, string name)
    {
        if ((uint)value > byte.MaxValue)
            throw new ArgumentOutOfRangeException(name, value, "RGBA channels must be between 0 and 255.");
    }
}

public readonly record struct PixelOffset(int X, int Y);

/// <summary>
/// A non-negative rectangle. Empty rectangles are represented by zero width
/// or height and are useful for descriptors whose source dimensions are not
/// known until decode.
/// </summary>
public readonly record struct PixelRect
{
    public PixelRect(int x, int y, int width, int height)
    {
        if (width < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public bool IsEmpty => Width == 0 || Height == 0;

    public bool Contains(int x, int y)
    {
        return !IsEmpty && x >= X && y >= Y && (long)x < (long)X + Width && (long)y < (long)Y + Height;
    }

    public PixelRect Intersect(PixelRect other)
    {
        var left = Math.Max(X, other.X);
        var top = Math.Max(Y, other.Y);
        var right = Math.Min((long)X + Width, (long)other.X + other.Width);
        var bottom = Math.Min((long)Y + Height, (long)other.Y + other.Height);
        if (right <= left || bottom <= top)
            return new PixelRect(left, top, 0, 0);
        return new PixelRect(left, top, checked((int)(right - left)), checked((int)(bottom - top)));
    }

    public PixelRect Union(PixelRect other)
    {
        if (IsEmpty)
            return other;
        if (other.IsEmpty)
            return this;

        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        var right = Math.Max((long)X + Width, (long)other.X + other.Width);
        var bottom = Math.Max((long)Y + Height, (long)other.Y + other.Height);
        return new PixelRect(
            left,
            top,
            checked((int)(right - left)),
            checked((int)(bottom - top)));
    }
}

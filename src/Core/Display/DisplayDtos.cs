using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GEmuera.Core.Display;

/// <summary>
/// A source-space rectangle. Values are copied as observed; this type does not
/// normalize negative coordinates, round values, or perform layout.
/// </summary>
public readonly record struct DisplayRect(int X, int Y, int Width, int Height);

public readonly record struct DisplayPoint(int X, int Y);

public readonly record struct DisplayEffectSequence
{
    public DisplayEffectSequence(long start, long end)
    {
        if (start < 0 || end < 0)
            throw new ArgumentOutOfRangeException(nameof(start), "Effect sequence values cannot be negative.");
        if ((start == 0) != (end == 0) || (start > 0 && end < start))
            throw new ArgumentException("Effect sequence must be empty (0,0) or an ordered positive range.");
        Start = start;
        End = end;
    }

    public long Start { get; }
    public long End { get; }

    public static DisplayEffectSequence Empty => new(0, 0);

    public bool IsEmpty => Start == 0 && End == 0;
}

public enum DisplayPartKind
{
    Text,
    Image,
    Shape,
    Div,
    Opaque,
}

public enum DisplayPlacement
{
    Relative,
    Absolute,
    AbsoluteLeftTop,
    AbsoluteLeftBottom,
}

public enum DisplayLineAlignment
{
    Left,
    Center,
    Right,
}

public enum DisplayScrollIntent
{
    None,
    FollowBottom,
    PreserveViewport,
    KeepChoicesVisible,
}

public enum DisplayBarrierKind
{
    Commit,
    Flush,
    Redraw,
    Scroll,
    Wait,
    Input,
}

/// <summary>
/// Origin information is deliberately textual and data-only. It is not a
/// file handle, Godot object, or mutable legacy object reference.
/// </summary>
public sealed class DisplayProvenance
{
    public DisplayProvenance(
        string source,
        string? detail = null,
        int? sourceLine = null,
        int? sourceColumn = null,
        IEnumerable<KeyValuePair<string, string>>? metadata = null)
    {
        Source = Required(source, nameof(source));
        Detail = detail ?? string.Empty;
        if (sourceLine is < 0 || sourceColumn is < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceLine), "Source positions cannot be negative.");
        SourceLine = sourceLine;
        SourceColumn = sourceColumn;
        Metadata = CopyMetadata(metadata, nameof(metadata));
    }

    public string Source { get; }
    public string Detail { get; }
    public int? SourceLine { get; }
    public int? SourceColumn { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public static DisplayProvenance Empty { get; } = new("unknown");

    internal DisplayProvenance DeepCopy() =>
        new(Source, Detail, SourceLine, SourceColumn, Metadata);

    private static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Provenance source must not be empty.", parameterName);
        return value.Trim();
    }

    internal static IReadOnlyDictionary<string, string> CopyMetadata(
        IEnumerable<KeyValuePair<string, string>>? values,
        string parameterName)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (values is not null)
        {
            foreach (var pair in values)
            {
                if (pair.Key is null)
                    throw new ArgumentException("Metadata keys cannot be null.", parameterName);
                if (!copy.TryAdd(pair.Key, pair.Value ?? string.Empty))
                    throw new ArgumentException($"Duplicate metadata key: {pair.Key}", parameterName);
            }
        }
        return new ReadOnlyDictionary<string, string>(copy);
    }
}

/// <summary>
/// Scroll is an intent from the script/display producer. It contains no
/// measured viewport or font information and therefore cannot perform layout.
/// </summary>
public sealed class DisplayScroll
{
    public DisplayScroll(
        DisplayScrollIntent intent,
        string? anchorLineId = null,
        double intraLineOffset = 0,
        string? provenance = null)
    {
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent));
        Intent = intent;
        AnchorLineId = anchorLineId ?? string.Empty;
        if (double.IsNaN(intraLineOffset) || double.IsInfinity(intraLineOffset))
            throw new ArgumentOutOfRangeException(nameof(intraLineOffset));
        IntraLineOffset = intraLineOffset;
        Provenance = provenance ?? string.Empty;
    }

    public DisplayScrollIntent Intent { get; }
    public string AnchorLineId { get; }
    public double IntraLineOffset { get; }
    public string Provenance { get; }

    public static DisplayScroll None { get; } = new(DisplayScrollIntent.None);

    internal DisplayScroll DeepCopy() =>
        new(Intent, AnchorLineId, IntraLineOffset, Provenance);
}

/// <summary>
/// Style is copied as declared by the producer. No theme, font, color-space,
/// or image fallback is resolved here.
/// </summary>
public sealed class DisplayStyle
{
    public DisplayStyle(
        int? foregroundColor = null,
        int? backgroundColor = null,
        int? borderColor = null,
        IEnumerable<int>? margin = null,
        IEnumerable<int>? padding = null,
        IEnumerable<int>? border = null,
        IEnumerable<int>? radius = null,
        IEnumerable<float[]>? colorMatrix = null,
        string? styleToken = null,
        IEnumerable<KeyValuePair<string, string>>? metadata = null,
        string? fontName = null,
        int fontStyle = 0,
        int? buttonColor = null,
        bool colorChanged = false)
    {
        ForegroundColor = foregroundColor;
        BackgroundColor = backgroundColor;
        BorderColor = borderColor;
        Margin = CopyInts(margin, nameof(margin));
        Padding = CopyInts(padding, nameof(padding));
        Border = CopyInts(border, nameof(border));
        Radius = CopyInts(radius, nameof(radius));
        ColorMatrix = CopyMatrix(colorMatrix, nameof(colorMatrix));
        StyleToken = styleToken ?? string.Empty;
        Metadata = DisplayProvenance.CopyMetadata(metadata, nameof(metadata));
        FontName = fontName ?? string.Empty;
        FontStyle = fontStyle;
        ButtonColor = buttonColor;
        ColorChanged = colorChanged;
    }

    public int? ForegroundColor { get; }
    public int? BackgroundColor { get; }
    public int? BorderColor { get; }
    public IReadOnlyList<int> Margin { get; }
    public IReadOnlyList<int> Padding { get; }
    public IReadOnlyList<int> Border { get; }
    public IReadOnlyList<int> Radius { get; }
    public IReadOnlyList<IReadOnlyList<float>> ColorMatrix { get; }
    public string StyleToken { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public string FontName { get; }
    public int FontStyle { get; }
    public int? ButtonColor { get; }
    public bool ColorChanged { get; }

    public static DisplayStyle Empty { get; } = new();

    internal DisplayStyle DeepCopy() => new(
        ForegroundColor,
        BackgroundColor,
        BorderColor,
        Margin,
        Padding,
        Border,
        Radius,
        ColorMatrix.Select(row => row.ToArray()),
        StyleToken,
        Metadata,
        FontName,
        FontStyle,
        ButtonColor,
        ColorChanged);

    private static IReadOnlyList<int> CopyInts(IEnumerable<int>? values, string parameterName)
    {
        var copy = values?.ToArray() ?? Array.Empty<int>();
        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<IReadOnlyList<float>> CopyMatrix(
        IEnumerable<float[]>? values,
        string parameterName)
    {
        var rows = new List<IReadOnlyList<float>>();
        if (values is not null)
        {
            foreach (var row in values)
            {
                if (row is null)
                    throw new ArgumentException("Color matrix rows cannot be null.", parameterName);
                rows.Add(Array.AsReadOnly(row.ToArray()));
            }
        }
        return new ReadOnlyCollection<IReadOnlyList<float>>(rows);
    }
}

public sealed class DisplayInteraction
{
    public DisplayInteraction(
        bool isInteractive,
        string? value = null,
        string? input = null,
        string? inputType = null,
        bool isInteger = false,
        long generation = 0,
        bool pointXIsLocked = false,
        int? lockedX = null,
        string? title = null,
        IEnumerable<KeyValuePair<string, string>>? hitMetadata = null)
    {
        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (pointXIsLocked && lockedX is null)
            throw new ArgumentException("A locked X interaction must preserve its locked coordinate.", nameof(lockedX));
        IsInteractive = isInteractive;
        Value = value ?? string.Empty;
        Input = input ?? string.Empty;
        InputType = inputType ?? string.Empty;
        IsInteger = isInteger;
        Generation = generation;
        PointXIsLocked = pointXIsLocked;
        LockedX = lockedX;
        Title = title ?? string.Empty;
        HitMetadata = DisplayProvenance.CopyMetadata(hitMetadata, nameof(hitMetadata));
    }

    public bool IsInteractive { get; }
    public string Value { get; }
    public string Input { get; }
    public string InputType { get; }
    public bool IsInteger { get; }
    public long Generation { get; }
    public bool PointXIsLocked { get; }
    public int? LockedX { get; }
    public string Title { get; }
    public IReadOnlyDictionary<string, string> HitMetadata { get; }

    internal DisplayInteraction DeepCopy() => new(
        IsInteractive,
        Value,
        Input,
        InputType,
        IsInteger,
        Generation,
        PointXIsLocked,
        LockedX,
        Title,
        HitMetadata);
}

public abstract class DisplayPart
{
    protected DisplayPart(
        DisplayPartKind kind,
        DisplayRect bounds,
        DisplayStyle? style,
        DisplayInteraction? interaction,
        string? text,
        string? altText,
        int pointX,
        float xSubPixel,
        float widthF,
        int width,
        int top,
        int bottom,
        bool error,
        IEnumerable<KeyValuePair<string, string>>? metadata)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (float.IsNaN(xSubPixel) || float.IsInfinity(xSubPixel) || float.IsNaN(widthF) || float.IsInfinity(widthF))
            throw new ArgumentOutOfRangeException(nameof(xSubPixel));
        Kind = kind;
        Bounds = bounds;
        Style = (style ?? DisplayStyle.Empty).DeepCopy();
        Interaction = interaction?.DeepCopy();
        Text = text ?? string.Empty;
        AltText = altText ?? string.Empty;
        PointX = pointX;
        XSubPixel = xSubPixel;
        WidthF = widthF;
        Width = width;
        Top = top;
        Bottom = bottom;
        Error = error;
        Metadata = DisplayProvenance.CopyMetadata(metadata, nameof(metadata));
    }

    public DisplayPartKind Kind { get; }
    public DisplayRect Bounds { get; }
    public DisplayStyle Style { get; }
    public DisplayInteraction? Interaction { get; }
    public string Text { get; }
    public string AltText { get; }
    public int PointX { get; }
    public float XSubPixel { get; }
    public float WidthF { get; }
    public int Width { get; }
    public int Top { get; }
    public int Bottom { get; }
    public bool Error { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public abstract DisplayPart DeepCopy();

    protected IEnumerable<KeyValuePair<string, string>> CopyMetadata() => Metadata;
}

public sealed class DisplayTextPart : DisplayPart
{
    public DisplayTextPart(
        string text,
        DisplayRect bounds = default,
        DisplayStyle? style = null,
        DisplayInteraction? interaction = null,
        string? altText = null,
        int pointX = 0,
        float xSubPixel = 0,
        float widthF = 0,
        int width = 0,
        int top = 0,
        int bottom = 0,
        bool error = false,
        IEnumerable<KeyValuePair<string, string>>? metadata = null)
        : base(DisplayPartKind.Text, bounds, style, interaction, text, altText, pointX, xSubPixel, widthF, width, top, bottom, error, metadata)
    {
    }

    public override DisplayTextPart DeepCopy() => new(
        Text, Bounds, Style, Interaction, AltText, PointX, XSubPixel, WidthF, Width, Top, Bottom, Error, CopyMetadata());
}

public sealed class DisplayImagePart : DisplayPart
{
    public DisplayImagePart(
        string? src,
        string? srcb,
        DisplayRect bounds = default,
        DisplayPlacement placement = DisplayPlacement.Relative,
        int positionX = 0,
        int positionY = 0,
        string? colorMatrixVariable = null,
        IEnumerable<float[]>? colorMatrix = null,
        bool flipX = false,
        bool flipY = false,
        DisplayStyle? style = null,
        DisplayInteraction? interaction = null,
        string? altText = null,
        int pointX = 0,
        float xSubPixel = 0,
        float widthF = 0,
        int width = 0,
        int top = 0,
        int bottom = 0,
        bool error = false,
        IEnumerable<KeyValuePair<string, string>>? metadata = null)
        : base(DisplayPartKind.Image, bounds, style, interaction, string.Empty, altText, pointX, xSubPixel, widthF, width, top, bottom, error, metadata)
    {
        if (!Enum.IsDefined(placement))
            throw new ArgumentOutOfRangeException(nameof(placement));
        Src = src ?? string.Empty;
        Srcb = srcb ?? string.Empty;
        Placement = placement;
        PositionX = positionX;
        PositionY = positionY;
        ColorMatrixVariable = colorMatrixVariable ?? string.Empty;
        ColorMatrix = CopyMatrix(colorMatrix);
        FlipX = flipX;
        FlipY = flipY;
    }

    public string Src { get; }
    public string Srcb { get; }
    public DisplayPlacement Placement { get; }
    public int PositionX { get; }
    public int PositionY { get; }
    public string ColorMatrixVariable { get; }
    public IReadOnlyList<IReadOnlyList<float>> ColorMatrix { get; }
    public bool FlipX { get; }
    public bool FlipY { get; }

    public override DisplayImagePart DeepCopy() => new(
        Src, Srcb, Bounds, Placement, PositionX, PositionY, ColorMatrixVariable,
        ColorMatrix.Select(row => row.ToArray()), FlipX, FlipY, Style, Interaction, AltText,
        PointX, XSubPixel, WidthF, Width, Top, Bottom, Error, CopyMetadata());

    private static IReadOnlyList<IReadOnlyList<float>> CopyMatrix(IEnumerable<float[]>? values)
    {
        var rows = new List<IReadOnlyList<float>>();
        if (values is not null)
        {
            foreach (var row in values)
            {
                if (row is null)
                    throw new ArgumentException("Color matrix rows cannot be null.", nameof(values));
                rows.Add(Array.AsReadOnly(row.ToArray()));
            }
        }
        return new ReadOnlyCollection<IReadOnlyList<float>>(rows);
    }
}

public sealed class DisplayShapePart : DisplayPart
{
    public DisplayShapePart(
        DisplayRect bounds,
        int? color = null,
        DisplayStyle? style = null,
        DisplayInteraction? interaction = null,
        string? altText = null,
        int pointX = 0,
        float xSubPixel = 0,
        float widthF = 0,
        int width = 0,
        int top = 0,
        int bottom = 0,
        bool error = false,
        IEnumerable<KeyValuePair<string, string>>? metadata = null)
        : base(DisplayPartKind.Shape, bounds, style, interaction, string.Empty, altText, pointX, xSubPixel, widthF, width, top, bottom, error, metadata)
    {
        Color = color;
    }

    public int? Color { get; }

    public override DisplayShapePart DeepCopy() => new(
        Bounds, Color, Style, Interaction, AltText, PointX, XSubPixel, WidthF, Width, Top, Bottom, Error, CopyMetadata());
}

public sealed class DisplayOpaquePart : DisplayPart
{
    public DisplayOpaquePart(
        string legacyType,
        DisplayRect bounds = default,
        DisplayStyle? style = null,
        DisplayInteraction? interaction = null,
        string? text = null,
        string? altText = null,
        int pointX = 0,
        float xSubPixel = 0,
        float widthF = 0,
        int width = 0,
        int top = 0,
        int bottom = 0,
        bool error = false,
        IEnumerable<KeyValuePair<string, string>>? metadata = null)
        : base(DisplayPartKind.Opaque, bounds, style, interaction, text, altText, pointX, xSubPixel, widthF, width, top, bottom, error, metadata)
    {
        if (string.IsNullOrWhiteSpace(legacyType))
            throw new ArgumentException("Opaque part type must not be empty.", nameof(legacyType));
        LegacyType = legacyType.Trim();
    }

    public string LegacyType { get; }

    public override DisplayOpaquePart DeepCopy() => new(
        LegacyType, Bounds, Style, Interaction, Text, AltText, PointX, XSubPixel, WidthF, Width, Top, Bottom, Error, CopyMetadata());
}

public sealed class DisplayDiv : DisplayPart
{
    public DisplayDiv(
        DisplayRect bounds,
        int depth = 0,
        bool isRelative = true,
        DisplayPlacement placement = DisplayPlacement.Relative,
        DisplayStyle? style = null,
        DisplayInteraction? interaction = null,
        IEnumerable<DisplayLine>? children = null,
        string? altText = null,
        int pointX = 0,
        float xSubPixel = 0,
        float widthF = 0,
        int width = 0,
        int top = 0,
        int bottom = 0,
        bool error = false,
        IEnumerable<KeyValuePair<string, string>>? metadata = null)
        : base(DisplayPartKind.Div, bounds, style, interaction, string.Empty, altText, pointX, xSubPixel, widthF, width, top, bottom, error, metadata)
    {
        if (!Enum.IsDefined(placement))
            throw new ArgumentOutOfRangeException(nameof(placement));
        Depth = depth;
        IsRelative = isRelative;
        Placement = placement;
        Children = CopyLines(children, nameof(children));
    }

    public int Depth { get; }
    public bool IsRelative { get; }
    public DisplayPlacement Placement { get; }
    public IReadOnlyList<DisplayLine> Children { get; }

    public override DisplayDiv DeepCopy() => new(
        Bounds, Depth, IsRelative, Placement, Style, Interaction,
        Children.Select(child => child.DeepCopy()), AltText, PointX, XSubPixel, WidthF, Width, Top, Bottom, Error, CopyMetadata());

    private static IReadOnlyList<DisplayLine> CopyLines(IEnumerable<DisplayLine>? values, string parameterName)
    {
        var copy = values?.Select(value => value?.DeepCopy() ?? throw new ArgumentException("Children cannot contain null.", parameterName)).ToArray()
            ?? Array.Empty<DisplayLine>();
        return Array.AsReadOnly(copy);
    }
}

public sealed class DisplayLine
{
    public DisplayLine(
        string? lineId = null,
        int lineNumber = -1,
        bool isLogicalLine = true,
        bool isTemporary = false,
        bool isLineEnd = true,
        DisplayLineAlignment alignment = DisplayLineAlignment.Left,
        DisplayStyle? style = null,
        IEnumerable<DisplayPart>? parts = null,
        IEnumerable<DisplayInteraction>? interactions = null,
        IEnumerable<KeyValuePair<string, string>>? metadata = null,
        bool bitmapCacheEnabled = false,
        bool dynamicMapFunctionScoped = false,
        int? textBackgroundColor = null)
    {
        if (!Enum.IsDefined(alignment))
            throw new ArgumentOutOfRangeException(nameof(alignment));
        LineId = lineId ?? string.Empty;
        LineNumber = lineNumber;
        IsLogicalLine = isLogicalLine;
        IsTemporary = isTemporary;
        IsLineEnd = isLineEnd;
        Alignment = alignment;
        Style = (style ?? DisplayStyle.Empty).DeepCopy();
        Parts = CopyParts(parts, nameof(parts));
        Interactions = CopyInteractions(interactions, nameof(interactions));
        Metadata = DisplayProvenance.CopyMetadata(metadata, nameof(metadata));
        BitmapCacheEnabled = bitmapCacheEnabled;
        DynamicMapFunctionScoped = dynamicMapFunctionScoped;
        TextBackgroundColor = textBackgroundColor;
    }

    public string LineId { get; }
    public int LineNumber { get; }
    public bool IsLogicalLine { get; }
    public bool IsTemporary { get; }
    public bool IsLineEnd { get; }
    public DisplayLineAlignment Alignment { get; }
    public DisplayStyle Style { get; }
    public IReadOnlyList<DisplayPart> Parts { get; }
    public IReadOnlyList<DisplayInteraction> Interactions { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public bool BitmapCacheEnabled { get; }
    public bool DynamicMapFunctionScoped { get; }
    public int? TextBackgroundColor { get; }

    public DisplayLine DeepCopy() => new(
        LineId, LineNumber, IsLogicalLine, IsTemporary, IsLineEnd, Alignment, Style,
        Parts.Select(part => part.DeepCopy()), Interactions.Select(interaction => interaction.DeepCopy()), Metadata,
        BitmapCacheEnabled, DynamicMapFunctionScoped, TextBackgroundColor);

    private static IReadOnlyList<DisplayPart> CopyParts(IEnumerable<DisplayPart>? values, string parameterName)
    {
        var copy = values?.Select(value => value?.DeepCopy() ?? throw new ArgumentException("Parts cannot contain null.", parameterName)).ToArray()
            ?? Array.Empty<DisplayPart>();
        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<DisplayInteraction> CopyInteractions(IEnumerable<DisplayInteraction>? values, string parameterName)
    {
        var copy = values?.Select(value => value?.DeepCopy() ?? throw new ArgumentException("Interactions cannot contain null.", parameterName)).ToArray()
            ?? Array.Empty<DisplayInteraction>();
        return Array.AsReadOnly(copy);
    }
}

/// <summary>
/// A data-only update of an existing interaction. Its contract intentionally
/// has no visual parts and can therefore never rebuild an unchanged visual tree.
/// </summary>
public sealed class DisplayDataOnlyPatch
{
    public DisplayDataOnlyPatch(
        string lineId,
        string interactionKey,
        string? value = null,
        string? input = null,
        string? inputType = null,
        long generation = 0,
        string? title = null,
        IEnumerable<KeyValuePair<string, string>>? hitMetadata = null,
        long effectSequence = 0,
        DisplayProvenance? provenance = null)
    {
        if (string.IsNullOrWhiteSpace(lineId))
            throw new ArgumentException("Data-only patches require a line id.", nameof(lineId));
        if (string.IsNullOrWhiteSpace(interactionKey))
            throw new ArgumentException("Data-only patches require an interaction key.", nameof(interactionKey));
        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (effectSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(effectSequence));
        LineId = lineId;
        InteractionKey = interactionKey;
        Value = value ?? string.Empty;
        Input = input ?? string.Empty;
        InputType = inputType ?? string.Empty;
        Generation = generation;
        Title = title ?? string.Empty;
        HitMetadata = DisplayProvenance.CopyMetadata(hitMetadata, nameof(hitMetadata));
        EffectSequence = effectSequence;
        Provenance = (provenance ?? DisplayProvenance.Empty).DeepCopy();
    }

    public string LineId { get; }
    public string InteractionKey { get; }
    public string Value { get; }
    public string Input { get; }
    public string InputType { get; }
    public long Generation { get; }
    public string Title { get; }
    public IReadOnlyDictionary<string, string> HitMetadata { get; }
    public long EffectSequence { get; }
    public DisplayProvenance Provenance { get; }
    public bool RebuildsVisualTree => false;

    public DisplayDataOnlyPatch DeepCopy() => new(
        LineId, InteractionKey, Value, Input, InputType, Generation, Title, HitMetadata, EffectSequence, Provenance);
}

public abstract class DisplayTransactionItem
{
    public abstract DisplayTransactionItem DeepCopy();
}

public sealed class DisplayLineItem : DisplayTransactionItem
{
    public DisplayLineItem(DisplayLine line)
    {
        Line = line?.DeepCopy() ?? throw new ArgumentNullException(nameof(line));
    }

    public DisplayLine Line { get; }

    public override DisplayLineItem DeepCopy() => new(Line);
}

public sealed class DisplayDataOnlyItem : DisplayTransactionItem
{
    public DisplayDataOnlyItem(DisplayDataOnlyPatch patch)
    {
        Patch = patch?.DeepCopy() ?? throw new ArgumentNullException(nameof(patch));
    }

    public DisplayDataOnlyPatch Patch { get; }

    public override DisplayDataOnlyItem DeepCopy() => new(Patch);
}

public sealed class DisplayOrderBarrier
{
    public DisplayOrderBarrier(DisplayBarrierKind kind, long effectSequence = 0, string? reason = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (effectSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(effectSequence));
        Kind = kind;
        EffectSequence = effectSequence;
        Reason = reason ?? string.Empty;
    }

    public DisplayBarrierKind Kind { get; }
    public long EffectSequence { get; }
    public string Reason { get; }

    public DisplayOrderBarrier DeepCopy() => new(Kind, EffectSequence, Reason);
}

public sealed class DisplayBarrierItem : DisplayTransactionItem
{
    public DisplayBarrierItem(DisplayOrderBarrier barrier)
    {
        Barrier = barrier?.DeepCopy() ?? throw new ArgumentNullException(nameof(barrier));
    }

    public DisplayOrderBarrier Barrier { get; }

    public override DisplayBarrierItem DeepCopy() => new(Barrier);
}

/// <summary>
/// Immutable M2.0 display batch. Items retain their original order; consumers
/// must not move a barrier, drop a commit, or merge across a wait/input item.
/// </summary>
public sealed class DisplayTransaction
{
    public DisplayTransaction(
        long generation,
        string transactionId,
        DisplayEffectSequence effectSequence,
        IEnumerable<DisplayTransactionItem>? items,
        DisplayScroll? scroll = null,
        bool atomicVisibility = false,
        DisplayProvenance? provenance = null)
    {
        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (string.IsNullOrWhiteSpace(transactionId))
            throw new ArgumentException("Transaction id must not be empty.", nameof(transactionId));
        Generation = generation;
        TransactionId = transactionId;
        EffectSequence = effectSequence;
        Items = CopyItems(items, nameof(items));
        Scroll = (scroll ?? DisplayScroll.None).DeepCopy();
        AtomicVisibility = atomicVisibility;
        Provenance = (provenance ?? DisplayProvenance.Empty).DeepCopy();
        Lines = Array.AsReadOnly(Items.OfType<DisplayLineItem>().Select(item => item.Line).ToArray());
        DataOnlyPatches = Array.AsReadOnly(Items.OfType<DisplayDataOnlyItem>().Select(item => item.Patch).ToArray());
        CanonicalHash = DisplayCanonicalHash.Compute(this);
    }

    public DisplayTransaction(
        long generation,
        string transactionId,
        DisplayEffectSequence effectSequence,
        IEnumerable<DisplayLine>? lines,
        DisplayScroll? scroll = null,
        bool atomicVisibility = false,
        DisplayProvenance? provenance = null,
        IEnumerable<DisplayDataOnlyPatch>? dataOnlyPatches = null,
        IEnumerable<DisplayOrderBarrier>? barriers = null)
        : this(
            generation,
            transactionId,
            effectSequence,
            BuildItems(lines, dataOnlyPatches, barriers),
            scroll,
            atomicVisibility,
            provenance)
    {
    }

    public long Generation { get; }
    public string TransactionId { get; }
    public DisplayEffectSequence EffectSequence { get; }
    public long EffectSequenceStart => EffectSequence.Start;
    public long EffectSequenceEnd => EffectSequence.End;
    public IReadOnlyList<DisplayTransactionItem> Items { get; }
    public IReadOnlyList<DisplayLine> Lines { get; }
    public IReadOnlyList<DisplayDataOnlyPatch> DataOnlyPatches { get; }
    public DisplayScroll Scroll { get; }
    public bool AtomicVisibility { get; }
    public DisplayProvenance Provenance { get; }
    public string CanonicalHash { get; }

    public DisplayTransaction DeepCopy() => new(
        Generation,
        TransactionId,
        EffectSequence,
        Items.Select(item => item.DeepCopy()),
        Scroll,
        AtomicVisibility,
        Provenance);

    private static IReadOnlyList<DisplayTransactionItem> CopyItems(
        IEnumerable<DisplayTransactionItem>? values,
        string parameterName)
    {
        var copy = values?.Select(value => value?.DeepCopy() ?? throw new ArgumentException("Transaction items cannot contain null.", parameterName)).ToArray()
            ?? Array.Empty<DisplayTransactionItem>();
        return Array.AsReadOnly(copy);
    }

    private static IEnumerable<DisplayTransactionItem> BuildItems(
        IEnumerable<DisplayLine>? lines,
        IEnumerable<DisplayDataOnlyPatch>? patches,
        IEnumerable<DisplayOrderBarrier>? barriers)
    {
        if (lines is not null)
            foreach (var line in lines)
                yield return new DisplayLineItem(line ?? throw new ArgumentException("Lines cannot contain null.", nameof(lines)));
        if (patches is not null)
            foreach (var patch in patches)
                yield return new DisplayDataOnlyItem(patch ?? throw new ArgumentException("Patches cannot contain null.", nameof(patches)));
        if (barriers is not null)
            foreach (var barrier in barriers)
                yield return new DisplayBarrierItem(barrier ?? throw new ArgumentException("Barriers cannot contain null.", nameof(barriers)));
    }
}

/// <summary>
/// DTO-side tee boundary. The legacy renderer remains the owner of its own
/// mutable objects; this class only publishes a second, detached snapshot.
/// </summary>
public interface IDisplayTeeObserver
{
    void Observe(DisplayTransaction transaction);
}

public sealed class DisplayTee
{
    public DisplayTransaction Capture(DisplayTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction.DeepCopy();
    }

    public void Observe(DisplayTransaction transaction, IDisplayTeeObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        observer.Observe(Capture(transaction));
    }
}

internal static class DisplayCanonicalHash
{
    public static string Compute(DisplayTransaction transaction)
    {
        var builder = new StringBuilder();
        Write(builder, transaction);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void Write(StringBuilder builder, DisplayTransaction transaction)
    {
        builder.Append("generation="); AppendInvariant(builder, transaction.Generation); builder.Append('\n')
            .Append("transaction=").Append(transaction.TransactionId).Append('\n')
            .Append("effect="); AppendInvariant(builder, transaction.EffectSequence.Start); builder.Append('-'); AppendInvariant(builder, transaction.EffectSequence.End); builder.Append('\n')
            .Append("atomic=").Append(transaction.AtomicVisibility).Append('\n')
            .Append("scroll=").Append(transaction.Scroll.Intent).Append('|').Append(transaction.Scroll.AnchorLineId).Append('|'); AppendInvariant(builder, transaction.Scroll.IntraLineOffset); builder.Append('|').Append(transaction.Scroll.Provenance).Append('\n');
        WriteProvenance(builder, transaction.Provenance, "transaction-provenance");
        foreach (var item in transaction.Items)
        {
            switch (item)
            {
                case DisplayLineItem line:
                    builder.Append("item=line\n");
                    WriteLine(builder, line.Line);
                    break;
                case DisplayDataOnlyItem patch:
                    builder.Append("item=data-only\n");
                    WritePatch(builder, patch.Patch);
                    break;
                case DisplayBarrierItem barrier:
                    builder.Append("item=barrier|").Append(barrier.Barrier.Kind).Append('|'); AppendInvariant(builder, barrier.Barrier.EffectSequence); builder.Append('|').Append(barrier.Barrier.Reason).Append('\n');
                    break;
                default:
                    throw new InvalidOperationException($"Unknown display transaction item: {item.GetType().FullName}");
            }
        }
    }

    private static void WriteLine(StringBuilder builder, DisplayLine line)
    {
        builder.Append("line=").Append(line.LineId).Append('|'); AppendInvariant(builder, line.LineNumber); builder.Append('|')
            .Append(line.IsLogicalLine).Append('|').Append(line.IsTemporary).Append('|').Append(line.IsLineEnd).Append('|').Append(line.Alignment)
            .Append('|').Append(line.BitmapCacheEnabled).Append('|').Append(line.DynamicMapFunctionScoped).Append('|'); AppendInvariant(builder, line.TextBackgroundColor); builder.Append('\n');
        WriteStyle(builder, line.Style);
        WriteMetadata(builder, line.Metadata);
        foreach (var interaction in line.Interactions)
            WriteInteraction(builder, interaction);
        foreach (var part in line.Parts)
            WritePart(builder, part);
    }

    private static void WritePart(StringBuilder builder, DisplayPart part)
    {
        builder.Append("part=");
        builder.Append(part.Kind).Append('|');
        AppendInvariant(builder, part.Bounds.X); builder.Append('|');
        AppendInvariant(builder, part.Bounds.Y); builder.Append('|');
        AppendInvariant(builder, part.Bounds.Width); builder.Append('|');
        AppendInvariant(builder, part.Bounds.Height).Append('|');
        builder.Append(part.Text).Append('|').Append(part.AltText).Append('|');
        AppendInvariant(builder, part.PointX).Append('|');
        AppendInvariant(builder, part.XSubPixel).Append('|');
        AppendInvariant(builder, part.WidthF).Append('|');
        AppendInvariant(builder, part.Width).Append('|');
        AppendInvariant(builder, part.Top).Append('|');
        AppendInvariant(builder, part.Bottom).Append('|');
        builder.Append(part.Error).Append('\n');
        WriteStyle(builder, part.Style);
        if (part.Interaction is not null)
            WriteInteraction(builder, part.Interaction);
        WriteMetadata(builder, part.Metadata);
        switch (part)
        {
            case DisplayImagePart image:
                builder.Append("image=").Append(image.Src).Append('|').Append(image.Srcb).Append('|').Append(image.Placement).Append('|')
                    .Append('|'); AppendInvariant(builder, image.PositionX); builder.Append('|'); AppendInvariant(builder, image.PositionY); builder.Append('|').Append(image.ColorMatrixVariable).Append('|')
                    .Append(image.FlipX).Append('|').Append(image.FlipY).Append('\n');
                WriteMatrix(builder, image.ColorMatrix);
                break;
            case DisplayShapePart shape:
                builder.Append("shape="); AppendInvariant(builder, shape.Color); builder.Append('\n');
                break;
            case DisplayDiv div:
                builder.Append("div="); AppendInvariant(builder, div.Depth); builder.Append('|').Append(div.IsRelative).Append('|').Append(div.Placement).Append('\n');
                foreach (var child in div.Children)
                    WriteLine(builder, child);
                break;
            case DisplayOpaquePart opaque:
                builder.Append("opaque=").Append(opaque.LegacyType).Append('\n');
                break;
        }
    }

    private static void WritePatch(StringBuilder builder, DisplayDataOnlyPatch patch)
    {
        builder.Append("patch=").Append(patch.LineId).Append('|').Append(patch.InteractionKey).Append('|').Append(patch.Value).Append('|')
            .Append(patch.Input).Append('|'); AppendInvariant(builder, patch.Generation); builder.Append('|').Append(patch.Title)
            .Append('|'); AppendInvariant(builder, patch.EffectSequence); builder.Append('|').Append(patch.RebuildsVisualTree).Append('\n');
        WriteMetadata(builder, patch.HitMetadata);
        WriteProvenance(builder, patch.Provenance, "patch-provenance");
    }

    private static void WriteStyle(StringBuilder builder, DisplayStyle style)
    {
        builder.Append("style="); AppendInvariant(builder, style.ForegroundColor); builder.Append('|'); AppendInvariant(builder, style.BackgroundColor); builder.Append('|'); AppendInvariant(builder, style.BorderColor); builder.Append('|').Append(style.StyleToken)
            .Append('|').Append(style.FontName).Append('|'); AppendInvariant(builder, style.FontStyle); builder.Append('|'); AppendInvariant(builder, style.ButtonColor); builder.Append('|').Append(style.ColorChanged).Append('\n');
        WriteInts(builder, style.Margin, "margin");
        WriteInts(builder, style.Padding, "padding");
        WriteInts(builder, style.Border, "border");
        WriteInts(builder, style.Radius, "radius");
        WriteMatrix(builder, style.ColorMatrix);
        WriteMetadata(builder, style.Metadata);
    }

    private static void WriteInteraction(StringBuilder builder, DisplayInteraction interaction)
    {
        builder.Append("interaction=").Append(interaction.IsInteractive).Append('|').Append(interaction.Value).Append('|').Append(interaction.Input);
        builder.Append('|').Append(interaction.InputType).Append('|').Append(interaction.IsInteger).Append('|');
        AppendInvariant(builder, interaction.Generation);
        builder.Append('|').Append(interaction.PointXIsLocked).Append('|');
        AppendInvariant(builder, interaction.LockedX);
        builder.Append('|').Append(interaction.Title).Append('\n');
        WriteMetadata(builder, interaction.HitMetadata);
    }

    private static void WriteProvenance(StringBuilder builder, DisplayProvenance provenance, string label)
    {
        builder.Append(label).Append('=').Append(provenance.Source).Append('|').Append(provenance.Detail).Append('|')
            ; AppendInvariant(builder, provenance.SourceLine); builder.Append('|'); AppendInvariant(builder, provenance.SourceColumn); builder.Append('\n');
        WriteMetadata(builder, provenance.Metadata);
    }

    private static void WriteInts(StringBuilder builder, IReadOnlyList<int> values, string label)
    {
        builder.Append(label).Append('=');
        foreach (var value in values)
        {
            AppendInvariant(builder, value);
            builder.Append(',');
        }
        builder.Append('\n');
    }

    private static void WriteMatrix(StringBuilder builder, IReadOnlyList<IReadOnlyList<float>> matrix)
    {
        builder.Append("matrix=");
        foreach (var row in matrix)
        {
            builder.Append('[');
            foreach (var value in row)
            {
                AppendInvariant(builder, value);
                builder.Append(',');
            }
            builder.Append(']');
        }
        builder.Append('\n');
    }

    private static void WriteMetadata(StringBuilder builder, IReadOnlyDictionary<string, string> metadata)
    {
        foreach (var pair in metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            builder.Append("metadata=").Append(pair.Key).Append('|').Append(pair.Value).Append('\n');
    }

    private static StringBuilder AppendInvariant(StringBuilder builder, object? value)
    {
        if (value is IFormattable formattable)
            builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
        else
            builder.Append(value);
        return builder;
    }
}

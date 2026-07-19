using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using GEmuera.Core.Resources;

namespace GEmuera.Core.Parsing;

public readonly record struct ContentToken
{
    public ContentToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Content token must not be empty.", nameof(value));
        Value = value.Trim();
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct SourceSpan
{
    public SourceSpan(ContentToken source, int line, int column, int length)
    {
        if (line <= 0) throw new ArgumentOutOfRangeException(nameof(line));
        if (column <= 0) throw new ArgumentOutOfRangeException(nameof(column));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Source = source;
        Line = line;
        Column = column;
        Length = length;
    }

    public ContentToken Source { get; }
    public int Line { get; }
    public int Column { get; }
    public int Length { get; }
}

public enum CoreDiagnosticSeverity { Info, Warning, Error }

public sealed record CoreDiagnostic(
    string Code,
    CoreDiagnosticSeverity Severity,
    string Message,
    SourceSpan Span)
{
    public string Code { get; } = Required(Code, nameof(Code));
    public string Message { get; } = Required(Message, nameof(Message));

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value must not be empty.", name) : value.Trim();
}

public enum ErbLineKind { Empty, Comment, Label, Instruction }

public sealed record ErbInstruction(string Name, IReadOnlyList<string> Arguments, SourceSpan Span)
{
    public string Name { get; } = RequiredName(Name);
    public IReadOnlyList<string> Arguments { get; } = CopyArguments(Arguments);

    private static string RequiredName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Regex.IsMatch(value.Trim(), "^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Instruction names must be uppercase identifiers.", nameof(value));
        return value.Trim().ToUpperInvariant();
    }

    private static IReadOnlyList<string> CopyArguments(IReadOnlyList<string>? values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Any(value => value is null)) throw new ArgumentException("Arguments cannot contain null.", nameof(values));
        return new ReadOnlyCollection<string>(values.Select(value => value.Trim()).ToArray());
    }
}

public sealed record ErbLogicalLine(
    int LineNumber,
    string Text,
    ErbLineKind Kind,
    SourceSpan Span,
    ErbInstruction? Instruction = null,
    string? Label = null)
{
    public string Text { get; } = Text ?? throw new ArgumentNullException(nameof(Text));
}

public sealed class ErbParseResult
{
    internal ErbParseResult(IReadOnlyList<ErbLogicalLine> lines, IReadOnlyList<CoreDiagnostic> diagnostics)
    {
        Lines = new ReadOnlyCollection<ErbLogicalLine>(lines.ToList());
        Diagnostics = new ReadOnlyCollection<CoreDiagnostic>(diagnostics.ToList());
    }

    public IReadOnlyList<ErbLogicalLine> Lines { get; }
    public IReadOnlyList<CoreDiagnostic> Diagnostics { get; }
    public bool IsValid => Diagnostics.All(diagnostic => diagnostic.Severity != CoreDiagnosticSeverity.Error);
}

/// <summary>
/// A deterministic ERB line boundary. It intentionally does not build display
/// nodes or perform game-specific instruction semantics.
/// </summary>
public sealed class ErbParser
{
    private static readonly Regex LabelPattern = new("^LABEL\\s+([A-Z][A-Z0-9_]*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ErbParseResult Parse(string source, ContentToken sourceToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var lines = new List<ErbLogicalLine>();
        var diagnostics = new List<CoreDiagnostic>();
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var rawLines = normalized.Split('\n');
        for (var index = 0; index < rawLines.Length; index++)
        {
            var lineNumber = index + 1;
            var raw = rawLines[index];
            var text = raw.Trim();
            var span = new SourceSpan(sourceToken, lineNumber, raw.Length == 0 ? 1 : raw.IndexOf(text, StringComparison.Ordinal) + 1, text.Length);
            if (text.Length == 0)
            {
                lines.Add(new ErbLogicalLine(lineNumber, raw, ErbLineKind.Empty, span));
                continue;
            }
            if (text.StartsWith(";", StringComparison.Ordinal) || text.StartsWith("//", StringComparison.Ordinal))
            {
                lines.Add(new ErbLogicalLine(lineNumber, raw, ErbLineKind.Comment, span));
                continue;
            }
            var label = LabelPattern.Match(text);
            if (label.Success)
            {
                lines.Add(new ErbLogicalLine(lineNumber, raw, ErbLineKind.Label, span, Label: label.Groups[1].Value));
                continue;
            }
            var nameEnd = 0;
            while (nameEnd < text.Length && !char.IsWhiteSpace(text[nameEnd])) nameEnd++;
            var name = text[..nameEnd].ToUpperInvariant();
            if (!Regex.IsMatch(name, "^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant))
            {
                diagnostics.Add(new CoreDiagnostic("erb.invalid-instruction", CoreDiagnosticSeverity.Error, "Invalid instruction name.", span));
                continue;
            }
            var arguments = TokenizeArguments(text[nameEnd..], out var unterminatedQuote);
            if (unterminatedQuote)
            {
                diagnostics.Add(new CoreDiagnostic("erb.unterminated-string", CoreDiagnosticSeverity.Error, "Instruction argument contains an unterminated quoted string.", span));
                continue;
            }
            var instruction = new ErbInstruction(name, arguments, span);
            lines.Add(new ErbLogicalLine(lineNumber, raw, ErbLineKind.Instruction, span, instruction));
        }
        return new ErbParseResult(lines, diagnostics);
    }

    private static IReadOnlyList<string> TokenizeArguments(string text, out bool unterminatedQuote)
    {
        var values = new List<string>();
        var builder = new System.Text.StringBuilder();
        var quoted = false;
        var escaped = false;
        var hasValue = false;
        foreach (var character in text)
        {
            if (escaped)
            {
                builder.Append(character);
                escaped = false;
                hasValue = true;
                continue;
            }
            if (character == '\\' && quoted)
            {
                escaped = true;
                continue;
            }
            if (character == '"')
            {
                quoted = !quoted;
                hasValue = true;
                continue;
            }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (hasValue)
                {
                    values.Add(builder.ToString());
                    builder.Clear();
                    hasValue = false;
                }
                continue;
            }
            builder.Append(character);
            hasValue = true;
        }
        unterminatedQuote = quoted || escaped;
        if (hasValue && !unterminatedQuote) values.Add(builder.ToString());
        return values;
    }
}

public abstract record ExpressionNode(SourceSpan Span);
public sealed record LiteralExpression(SourceSpan Span, CoreLiteralValue Value) : ExpressionNode(Span);
public sealed record IdentifierExpression(SourceSpan Span, string Name) : ExpressionNode(Span)
{
    public string Name { get; } = string.IsNullOrWhiteSpace(Name) ? throw new ArgumentException("Identifier is required.", nameof(Name)) : Name.Trim();
}
public sealed record BinaryExpression(SourceSpan Span, ExpressionNode Left, string Operator, ExpressionNode Right) : ExpressionNode(Span)
{
    public ExpressionNode Left { get; } = Left ?? throw new ArgumentNullException(nameof(Left));
    public string Operator { get; } = string.IsNullOrWhiteSpace(Operator) ? throw new ArgumentException("Operator is required.", nameof(Operator)) : Operator.Trim();
    public ExpressionNode Right { get; } = Right ?? throw new ArgumentNullException(nameof(Right));
}

public readonly record struct CoreLiteralValue
{
    private CoreLiteralValue(object value, CoreLiteralKind kind) { Value = value; Kind = kind; }
    public object Value { get; }
    public CoreLiteralKind Kind { get; }
    public static CoreLiteralValue Integer(long value) => new(value, CoreLiteralKind.Integer);
    public static CoreLiteralValue Float(double value) => double.IsFinite(value) ? new(value, CoreLiteralKind.Float) : throw new ArgumentOutOfRangeException(nameof(value));
    public static CoreLiteralValue String(string value) => new(value ?? throw new ArgumentNullException(nameof(value)), CoreLiteralKind.String);
}
public enum CoreLiteralKind { Integer, Float, String }

using System.Text.RegularExpressions;

namespace GEmuera.Core.Resources;

/// <summary>
/// Opaque identity for a source that has already been validated by a platform
/// adapter. It is deliberately not a filesystem path and is safe to carry into
/// Core snapshots.
/// </summary>
public readonly record struct SourceToken
{
    public SourceToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Source token must not be empty.", nameof(value));
        var normalized = value.Trim();
        if (normalized.Length > 256 || !Regex.IsMatch(normalized, "^[A-Za-z0-9][A-Za-z0-9._:/-]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Source token contains unsupported characters.", nameof(value));
        Value = normalized;
    }

    public string? Value { get; }
    public bool IsValid => !string.IsNullOrEmpty(Value);

    public override string ToString() => Value ?? string.Empty;
}

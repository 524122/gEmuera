using System.Text;
using GEmuera.Core.State;

namespace GEmuera.Core.Save;

public readonly record struct SaveProfileId
{
	public SaveProfileId(string value)
	{
		if (string.IsNullOrWhiteSpace(value) || !System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), "^[a-z][a-z0-9.\\-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
			throw new ArgumentException("Invalid save profile id.", nameof(value));
		Value = value.Trim();
	}
	public string Value { get; }
	public override string ToString() => Value;
}

public sealed class SaveSnapshot
{
	private readonly IReadOnlyDictionary<VariableKey, CoreValue> _values;
	public SaveSnapshot(SaveProfileId profile, Session.SessionGeneration generation, long sequence, IEnumerable<KeyValuePair<VariableKey, CoreValue>> values)
	{
		ArgumentNullException.ThrowIfNull(values); if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
		Profile = profile; Generation = generation; Sequence = sequence;
		_values = new System.Collections.ObjectModel.ReadOnlyDictionary<VariableKey, CoreValue>(values.ToDictionary(pair => pair.Key, pair => pair.Value));
	}
	public SaveProfileId Profile { get; }
	public Session.SessionGeneration Generation { get; }
	public long Sequence { get; }
	public IReadOnlyDictionary<VariableKey, CoreValue> Values => _values;
	public static SaveSnapshot From(VariableStore store, SaveProfileId profile) { ArgumentNullException.ThrowIfNull(store); var snapshot = store.Capture(); return new SaveSnapshot(profile, snapshot.Generation, snapshot.Sequence, snapshot.Values); }
}

public interface ISaveCodec
{
	SaveProfileId Profile { get; }
	byte[] Encode(SaveSnapshot snapshot);
	SaveSnapshot Decode(ReadOnlySpan<byte> data, SaveProfileId expectedProfile);
}

/// <summary>Small deterministic binary codec used as the pure candidate path.</summary>
public sealed class DeterministicSaveCodec : ISaveCodec
{
	private static readonly byte[] Magic = Encoding.ASCII.GetBytes("GEMUERA-SAVE-1");
	public DeterministicSaveCodec(SaveProfileId profile, int maxBytes = 16 * 1024 * 1024) { Profile = profile; if (maxBytes <= Magic.Length) throw new ArgumentOutOfRangeException(nameof(maxBytes)); MaxBytes = maxBytes; }
	public SaveProfileId Profile { get; }
	public int MaxBytes { get; }
	public byte[] Encode(SaveSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot); EnsureProfile(snapshot.Profile);
		using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
		writer.Write(Magic); writer.Write(Profile.Value); writer.Write(snapshot.Generation.Value); writer.Write(snapshot.Sequence);
		var entries = snapshot.Values.OrderBy(pair => pair.Key.Scope).ThenBy(pair => pair.Key.Name, StringComparer.Ordinal).ThenBy(pair => pair.Key.Index).ToArray(); writer.Write(entries.Length);
		foreach (var pair in entries) { writer.Write((byte)pair.Key.Scope); writer.Write(pair.Key.Name); writer.Write(pair.Key.Index); writer.Write((byte)pair.Value.Kind); switch (pair.Value.Kind) { case CoreValueKind.Integer: writer.Write(pair.Value.Integer); break; case CoreValueKind.Float: writer.Write(pair.Value.Float); break; case CoreValueKind.String: writer.Write(pair.Value.Text ?? string.Empty); break; case CoreValueKind.Reference: writer.Write(pair.Value.Reference); break; default: throw new InvalidDataException("Unknown value kind."); } }
		writer.Flush(); if (stream.Length > MaxBytes) throw new InvalidOperationException("Save payload exceeds the configured limit."); return stream.ToArray();
	}
	public SaveSnapshot Decode(ReadOnlySpan<byte> data, SaveProfileId expectedProfile)
	{
		if (data.Length == 0 || data.Length > MaxBytes) throw new InvalidDataException("Save payload size is outside the configured limit.");
		using var stream = new MemoryStream(data.ToArray(), false); using var reader = new BinaryReader(stream, Encoding.UTF8, true);
		if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("Save magic is invalid.");
		var actual = new SaveProfileId(reader.ReadString()); EnsureProfile(actual); if (actual != expectedProfile) throw new InvalidDataException("Save profile does not match the requested codec profile.");
		var generation = new Session.SessionGeneration(reader.ReadInt64()); var sequence = reader.ReadInt64(); var count = reader.ReadInt32(); if (count < 0 || count > 1_000_000) throw new InvalidDataException("Save entry count is invalid.");
		var values = new Dictionary<VariableKey, CoreValue>(); for (var i = 0; i < count; i++) { var key = new VariableKey((VariableScope)reader.ReadByte(), reader.ReadString(), reader.ReadInt32()); var kind = (CoreValueKind)reader.ReadByte(); var value = kind switch { CoreValueKind.Integer => CoreValue.FromInteger(reader.ReadInt64()), CoreValueKind.Float => CoreValue.FromFloat(reader.ReadDouble()), CoreValueKind.String => CoreValue.FromString(reader.ReadString()), CoreValueKind.Reference => CoreValue.FromReference(reader.ReadInt64()), _ => throw new InvalidDataException("Save value kind is invalid.") }; if (!values.TryAdd(key, value)) throw new InvalidDataException("Save contains duplicate variable keys."); }
		if (stream.Position != stream.Length) throw new InvalidDataException("Save contains trailing bytes."); return new SaveSnapshot(actual, generation, sequence, values);
	}
	private void EnsureProfile(SaveProfileId profile) { if (profile != Profile) throw new InvalidOperationException($"Save profile '{profile}' does not match '{Profile}'."); }
}

using System.Text.Json.Serialization;

namespace MinorShift.Emuera
{
	internal sealed class JSONConfigData
	{
		[JsonPropertyName("UseButtonFocusBackgroundColor")]
		public bool UseButtonFocusBackgroundColor { get; set; }

		[JsonPropertyName("UseNewRandom")]
		public bool UseNewRandom { get; set; }

		[JsonPropertyName("UseScopedVariableInstruction")]
		public bool UseScopedVariableInstruction { get; set; } = true;

		[JsonPropertyName("RenderingBackend")]
		public RenderingBackend RenderingBackend { get; set; } = RenderingBackend.Auto;
	}
}

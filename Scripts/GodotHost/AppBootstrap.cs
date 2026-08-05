using Godot;
using System;

/// <summary>
/// Explicit application bootstrap boundary. The first prototype only exposes
/// feature flags and lifecycle signals; session-owned services are created by
/// the scene host after a game selection is available.
/// </summary>
public partial class AppBootstrap : Node
{
	[Signal]
	public delegate void BootstrapReadyEventHandler();

	[Signal]
	public delegate void BootstrapFaultEventHandler(string message);

	public bool IsReady { get; private set; }
	public bool PrototypeRuntimeEnabled { get; private set; }
	public bool TypedPortsEnabled { get; private set; }
	public bool PixelStoreEnabled { get; private set; }

	public override void _Ready()
	{
		CallDeferred(nameof(Initialize));
	}

	private void Initialize()
	{
		try
		{
			PrototypeRuntimeEnabled = ReadBoolSetting("application/prototype_runtime", true);
			TypedPortsEnabled = ReadBoolSetting("application/typed_ports", true);
			PixelStoreEnabled = ReadBoolSetting("application/pixel_store", true);
			IsReady = true;
			EmitSignal(SignalName.BootstrapReady);
		}
		catch (Exception error)
		{
			IsReady = false;
			EmitSignal(SignalName.BootstrapFault, error.Message);
		}
	}

	private static bool ReadBoolSetting(string path, bool fallback)
	{
		if (!ProjectSettings.HasSetting(path))
			return fallback;
		return ProjectSettings.GetSetting(path, fallback).AsBool();
	}
}

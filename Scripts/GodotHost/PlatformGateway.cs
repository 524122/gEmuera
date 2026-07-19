using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Application-scoped platform capability surface. It owns lifecycle and
/// platform tokens only; game/session state stays in the Core session.
/// </summary>
public partial class PlatformGateway : Node
{
    [Signal]
    public delegate void ApplicationPausedEventHandler();

    [Signal]
    public delegate void ApplicationResumedEventHandler();

    [Signal]
    public delegate void PlatformReadyEventHandler(string platformId);

    private readonly Dictionary<string, bool> capabilities = new(StringComparer.Ordinal);

    public string PlatformId { get; private set; } = "unknown";
    public bool IsInitialized { get; private set; }

    public override void _Ready()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (IsInitialized)
            return;

        PlatformId = OS.GetName().ToLowerInvariant();
        capabilities["platform.mobile"] = OS.HasFeature("mobile");
        capabilities["platform.android"] = OS.GetName() == "Android";
        capabilities["platform.desktop"] = OS.HasFeature("pc") || OS.GetName() == "Windows" || OS.GetName() == "Linux" || OS.GetName() == "macOS";
        capabilities["platform.file_picker"] = true;
        capabilities["platform.saf"] = OS.GetName() == "Android";
        capabilities["platform.sqlite"] = true;
        IsInitialized = true;
        EmitSignal(SignalName.PlatformReady, PlatformId);
    }

    public bool HasCapability(string capabilityId)
    {
        return !string.IsNullOrWhiteSpace(capabilityId) && capabilities.TryGetValue(capabilityId.Trim(), out var enabled) && enabled;
    }

    public Godot.Collections.Dictionary GetCapabilitySnapshot()
    {
        var snapshot = new Godot.Collections.Dictionary();
        foreach (var pair in capabilities)
            snapshot[pair.Key] = pair.Value;
        snapshot["platform.id"] = PlatformId;
        return snapshot;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationPaused)
            EmitSignal(SignalName.ApplicationPaused);
        else if (what == NotificationApplicationResumed)
            EmitSignal(SignalName.ApplicationResumed);
    }
}

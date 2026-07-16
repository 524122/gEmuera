namespace GEmuera.Core.Ports;

public static class M5PortManifest
{
    public static readonly PortTypeId Input = new("IInputPort");
    public static readonly PortTypeId PointerPolicy = new("IPointerInputSubmissionPolicy");
    public static readonly PortTypeId Storage = new("IStoragePort");
    public static readonly PortTypeId SafCanary = new("ISafCanaryPort");
    public static readonly PortTypeId Database = new("IDatabasePort");
    public static readonly PortTypeId Audio = new("IAudioPort");
    public static readonly PortTypeId Lifecycle = new("ILifecyclePort");

    public static readonly CapabilityId InputCapability = new("platform.input.v1");
    public static readonly CapabilityId NoFocusCapability = new("input.nofocus.v1");
    public static readonly CapabilityId PointerCapability = new("platform.pointer.v1");
    public static readonly CapabilityId StorageCapability = new("platform.storage.v1");
    public static readonly CapabilityId SafCapability = new("platform.saf.v1");
    public static readonly CapabilityId DatabaseCapability = new("platform.database.v1");
    public static readonly CapabilityId AudioCapability = new("platform.audio.v1");
    public static readonly CapabilityId LifecycleCapability = new("platform.lifecycle.v1");

    public static PortManifest CreateDefault()
    {
        return new PortManifest(new[]
        {
            Entry(Input, InputCapability, PortCompletionMode.WaitPort, 64 * 1024, 64 * 1024, "legacy.input", true),
            Entry(PointerPolicy, PointerCapability, PortCompletionMode.CoreImmediate, 8 * 1024, 8 * 1024, "legacy.pointer", true),
            Entry(Storage, StorageCapability, PortCompletionMode.WaitPort, 4 * 1024 * 1024, 4 * 1024 * 1024, "legacy.storage", true),
            Entry(SafCanary, SafCapability, PortCompletionMode.WaitPort, 4 * 1024 * 1024, 64 * 1024, "legacy.storage", true),
            Entry(Database, DatabaseCapability, PortCompletionMode.WaitPort, 256 * 1024, 4 * 1024 * 1024, "legacy.database", true),
            Entry(Audio, AudioCapability, PortCompletionMode.WaitPort, 64 * 1024, 64 * 1024, "legacy.audio", false),
            Entry(Lifecycle, LifecycleCapability, PortCompletionMode.CoreImmediate, 16 * 1024, 16 * 1024, "legacy.lifecycle", true),
        });
    }

    private static PortManifestEntry Entry(
        PortTypeId portType,
        CapabilityId capability,
        PortCompletionMode completionMode,
        long maxRequest,
        long maxCompletion,
        string fallbackId,
        bool affectsScriptOrder)
    {
        return new PortManifestEntry(
            portType,
            capability,
            "1.0.0",
            new PortOwner(PortOwnerKind.Vm, "vm"),
            completionMode,
            new PortPayloadLimits(maxRequest, maxCompletion),
            new PortCancellationContract(PortCancellationMode.CancelAndDrain, true, TimeSpan.FromSeconds(2)),
            new PortTimeout(TimeSpan.FromSeconds(30), PortTimeoutMode.UseFallback),
            new[]
            {
                PortErrorCode.InvalidRequest,
                PortErrorCode.Unsupported,
                PortErrorCode.Cancelled,
                PortErrorCode.TimedOut,
                PortErrorCode.StaleCompletion,
                PortErrorCode.IoFailure,
                PortErrorCode.OwnerUnavailable,
            },
            new FallbackAdapterDescriptor(FallbackAdapterKind.Legacy, fallbackId, "M5 adapter is canary-scoped until its platform evidence is complete.", affectsScriptOrder),
            affectsScriptOrder);
    }
}

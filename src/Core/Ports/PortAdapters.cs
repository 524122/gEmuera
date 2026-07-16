using GEmuera.Core.Session;

namespace GEmuera.Core.Ports;

public interface IPortAdapter<TArguments, TPayload>
{
    ValueTask<PortCompletion<TPayload>> ExecuteAsync(
        PortRequest<TArguments> request,
        CancellationToken cancellationToken = default);
}

public sealed record PortSessionContext
{
    public PortSessionContext(SessionGeneration generation, PortManifest manifest)
    {
        Generation = generation;
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    public SessionGeneration Generation { get; }
    public PortManifest Manifest { get; }
}

public interface IPortSessionAdapterFactory
{
    IPortSessionAdapters Create(PortSessionContext context);
}

public interface IPortSessionAdapters : IAsyncDisposable
{
    PortSessionContext Context { get; }
}

public sealed class PortSessionScope : IAsyncDisposable
{
    private readonly CompletionDispatcher _dispatcher;
    private readonly IPortSessionAdapters _adapters;
    private int _disposed;

    public PortSessionScope(CompletionDispatcher dispatcher, IPortSessionAdapters adapters)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _adapters = adapters ?? throw new ArgumentNullException(nameof(adapters));
    }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _dispatcher.Close();
        await _adapters.DisposeAsync().ConfigureAwait(false);
    }
}

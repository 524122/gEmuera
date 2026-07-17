using System.Collections.ObjectModel;
using System.Threading;

namespace GEmuera.Core.Resources;

/// <summary>
/// VM-owner-thread authority for mutable CPU surfaces. Readers may take
/// immutable snapshots from any thread; only the captured owner can publish.
/// </summary>
public sealed class PixelStore : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<PixelHandle, SurfaceEntry> _surfaces = new();
    private readonly int _ownerThreadId;
    private long _nextHandle;
    private int _disposed;

    public PixelStore(int? ownerThreadId = null)
    {
        _ownerThreadId = ownerThreadId ?? Environment.CurrentManagedThreadId;
    }

    public int OwnerThreadId => _ownerThreadId;

    public PixelHandle Create(PixelSurface surface)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(surface);
        EnsureNotDisposed();

        var handle = new PixelHandle(Interlocked.Increment(ref _nextHandle));
        var initial = surface.Snapshot(PixelRevision.Initial, new PixelRect(0, 0, surface.Width, surface.Height));
        lock (_gate)
        {
            _surfaces.Add(handle, new SurfaceEntry(initial));
        }
        return handle;
    }

    public PixelHandle CreateEmpty(
        int width,
        int height,
        PixelFormat format = PixelFormat.Rgba8888StraightAlpha,
        int? stride = null,
        SourceToken sourceToken = default)
    {
        return Create(PixelSurface.Empty(width, height, format, stride, sourceToken));
    }

    public PixelSurfaceSnapshot Read(PixelHandle handle)
    {
        EnsureNotDisposed();
        var entry = GetEntry(handle);
        return Volatile.Read(ref entry.Published);
    }

    public bool TryRead(PixelHandle handle, out PixelSurfaceSnapshot? snapshot)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            snapshot = null;
            return false;
        }

        lock (_gate)
        {
            if (!_surfaces.TryGetValue(handle, out var entry))
            {
                snapshot = null;
                return false;
            }
            snapshot = Volatile.Read(ref entry.Published);
            return true;
        }
    }

    public PixelColor GetPixel(PixelHandle handle, int x, int y)
    {
        return Read(handle).GetPixel(x, y);
    }

    public PixelRevision SetPixel(PixelHandle handle, int x, int y, PixelColor color)
    {
        EnsureOwnerThread();
        EnsureNotDisposed();
        var entry = GetEntry(handle);
        lock (_gate)
        {
            var current = Volatile.Read(ref entry.Published);
            if ((uint)x >= (uint)current.Width)
                throw new ArgumentOutOfRangeException(nameof(x), x, "Pixel coordinate is outside the surface.");
            if ((uint)y >= (uint)current.Height)
                throw new ArgumentOutOfRangeException(nameof(y), y, "Pixel coordinate is outside the surface.");

            var bytes = current.CopyBytes();
            WriteColor(bytes, current, x, y, color);
            return Publish(entry, current, bytes, new PixelRect(x, y, 1, 1));
        }
    }

    public PixelRevision FillRect(PixelHandle handle, PixelRect rectangle, PixelColor color)
    {
        EnsureOwnerThread();
        EnsureNotDisposed();
        var entry = GetEntry(handle);
        lock (_gate)
        {
            var current = Volatile.Read(ref entry.Published);
            ValidateRect(current, rectangle);
            if (rectangle.IsEmpty)
                return current.Revision;

            var bytes = current.CopyBytes();
            for (var y = rectangle.Y; y < rectangle.Y + rectangle.Height; y++)
            {
                for (var x = rectangle.X; x < rectangle.X + rectangle.Width; x++)
                    WriteColor(bytes, current, x, y, color);
            }
            return Publish(entry, current, bytes, rectangle);
        }
    }

    public PixelRevision WritePixels(PixelHandle handle, PixelRect rectangle, ReadOnlySpan<PixelColor> pixels)
    {
        EnsureOwnerThread();
        EnsureNotDisposed();
        var entry = GetEntry(handle);
        lock (_gate)
        {
            var current = Volatile.Read(ref entry.Published);
            ValidateRect(current, rectangle);
            var required = checked(rectangle.Width * rectangle.Height);
            if (pixels.Length != required)
                throw new ArgumentException("Pixel input length must match the rectangle area.", nameof(pixels));
            if (rectangle.IsEmpty)
                return current.Revision;

            var bytes = current.CopyBytes();
            var sourceIndex = 0;
            for (var y = rectangle.Y; y < rectangle.Y + rectangle.Height; y++)
            {
                for (var x = rectangle.X; x < rectangle.X + rectangle.Width; x++)
                    WriteColor(bytes, current, x, y, pixels[sourceIndex++]);
            }
            return Publish(entry, current, bytes, rectangle);
        }
    }

    public bool Dispose(PixelHandle handle)
    {
        EnsureOwnerThread();
        EnsureNotDisposed();
        lock (_gate)
        {
            return _surfaces.Remove(handle);
        }
    }

    public PixelStoreSnapshot Capture()
    {
        EnsureNotDisposed();
        lock (_gate)
        {
            var copy = new Dictionary<PixelHandle, PixelSurfaceSnapshot>(_surfaces.Count);
            foreach (var pair in _surfaces)
                copy.Add(pair.Key, Volatile.Read(ref pair.Value.Published));
            return new PixelStoreSnapshot(copy);
        }
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        lock (_gate)
            _surfaces.Clear();
    }

    private PixelRevision Publish(
        SurfaceEntry entry,
        PixelSurfaceSnapshot current,
        byte[] bytes,
        PixelRect dirtyRect)
    {
        var revision = new PixelRevision(checked(current.Revision.Value + 1));
        var published = new PixelSurfaceSnapshot(
            current.Width,
            current.Height,
            current.Format,
            current.Stride,
            bytes,
            revision,
            dirtyRect,
            current.SourceToken);
        Interlocked.Exchange(ref entry.Published, published);
        return revision;
    }

    private SurfaceEntry GetEntry(PixelHandle handle)
    {
        if (!handle.IsValid)
            throw new ArgumentOutOfRangeException(nameof(handle), handle, "Pixel handle is invalid.");
        lock (_gate)
        {
            if (_surfaces.TryGetValue(handle, out var entry))
                return entry;
        }
        throw new KeyNotFoundException($"Pixel handle {handle.Value} is not registered.");
    }

    private static void ValidateRect(PixelSurfaceSnapshot surface, PixelRect rectangle)
    {
        if (rectangle.X < 0 || rectangle.Y < 0 || (long)rectangle.X + rectangle.Width > surface.Width || (long)rectangle.Y + rectangle.Height > surface.Height)
            throw new ArgumentOutOfRangeException(nameof(rectangle), "Rectangle is outside the surface.");
    }

    private static void WriteColor(byte[] bytes, PixelSurfaceSnapshot surface, int x, int y, PixelColor color)
    {
        var index = checked(y * surface.Stride + x * surface.Format.BytesPerPixel());
        bytes[index] = color.Red;
        bytes[index + 1] = color.Green;
        bytes[index + 2] = color.Blue;
        bytes[index + 3] = color.Alpha;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("PixelStore mutation must run on its VM owner thread.");
    }

    private void EnsureNotDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(PixelStore));
    }

    private sealed class SurfaceEntry
    {
        public SurfaceEntry(PixelSurfaceSnapshot published)
        {
            Published = published;
        }

        public PixelSurfaceSnapshot Published;
    }
}

public sealed class PixelStoreSnapshot
{
    private readonly IReadOnlyDictionary<PixelHandle, PixelSurfaceSnapshot> _surfaces;

    internal PixelStoreSnapshot(IReadOnlyDictionary<PixelHandle, PixelSurfaceSnapshot> surfaces)
    {
        _surfaces = new ReadOnlyDictionary<PixelHandle, PixelSurfaceSnapshot>(surfaces.ToDictionary());
    }

    public IReadOnlyDictionary<PixelHandle, PixelSurfaceSnapshot> Surfaces => _surfaces;

    public bool TryGet(PixelHandle handle, out PixelSurfaceSnapshot? snapshot)
    {
        return _surfaces.TryGetValue(handle, out snapshot);
    }
}

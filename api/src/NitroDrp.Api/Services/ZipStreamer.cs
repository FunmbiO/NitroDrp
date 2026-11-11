using System.IO.Compression;

namespace NitroDrp.Api.Services;

public sealed record ZipSource(string Name, Func<CancellationToken, Task<Stream>> Open);

/// <summary>
/// Streams a store-only (no compression) zip to a forward-only stream such as an HTTP response.
/// Memory stays flat however big the files are: bytes flow source -> entry -> output in small chunks.
/// Photos and video are already compressed, so storing them costs nothing and saves CPU.
/// </summary>
public static class ZipStreamer
{
    public static async Task WriteAsync(Stream output, IEnumerable<ZipSource> sources, CancellationToken ct)
    {
        var tolerant = new SyncTolerantStream(output);
        await using (var zip = await ZipArchive.CreateAsync(tolerant, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, ct))
        {
            foreach (var source in sources)
            {
                var entry = zip.CreateEntry(source.Name, CompressionLevel.NoCompression);
                await using (var dest = await entry.OpenAsync(ct))
                await using (var src = await source.Open(ct))
                    await src.CopyToAsync(dest, 256 * 1024, ct);
            }
        }
        await tolerant.FlushAsync(ct); // central directory
    }

    /// <summary>
    /// ZipArchive still writes its small headers and data descriptors synchronously, which Kestrel forbids
    /// on the response body (and blocking threads would be wrong anyway). Those few bytes are buffered and
    /// flushed asynchronously ahead of the next async write, so byte order is preserved and file data,
    /// which arrives via WriteAsync, never sits in memory.
    /// </summary>
    private sealed class SyncTolerantStream(Stream inner) : Stream
    {
        private readonly MemoryStream _pending = new();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => _pending.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => _pending.Write(buffer);
        public override void Flush() { }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            await DrainAsync(ct);
            await inner.WriteAsync(buffer, ct);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override async Task FlushAsync(CancellationToken ct)
        {
            await DrainAsync(ct);
            await inner.FlushAsync(ct);
        }

        private async Task DrainAsync(CancellationToken ct)
        {
            if (_pending.Length == 0) return;
            var bytes = _pending.ToArray();
            _pending.SetLength(0);
            await inner.WriteAsync(bytes, ct);
        }
    }

    /// <summary>"a.zip", "a.zip" -> "a.zip", "a (2).zip". Zip entries must have unique names.</summary>
    public static IReadOnlyList<string> UniqueNames(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var name in names)
        {
            var candidate = name;
            var ext = Path.GetExtension(name);
            var stem = name[..^ext.Length];
            for (var i = 2; !seen.Add(candidate); i++) candidate = $"{stem} ({i}){ext}";
            result.Add(candidate);
        }
        return result;
    }
}

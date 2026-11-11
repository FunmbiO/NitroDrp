using System.IO.Compression;
using NitroDrp.Api.Services;

namespace NitroDrp.Tests;

public class ZipStreamerTests
{
    /// <summary>Like Kestrel's response body: forward-only and async-only.</summary>
    private sealed class AsyncOnlyStream : Stream
    {
        public MemoryStream Inner { get; } = new();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new InvalidOperationException("Synchronous IO is not allowed");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken ct = default) => Inner.WriteAsync(b, ct);
    }

    private static ZipSource Src(string name, byte[] data) => new(name, _ => Task.FromResult<Stream>(new MemoryStream(data)));

    [Fact]
    public async Task Writes_a_valid_zip_to_an_async_only_forward_only_stream()
    {
        var big = new byte[3_000_000];
        new Random(1).NextBytes(big);
        var output = new AsyncOnlyStream();

        await ZipStreamer.WriteAsync(output, [Src("a.txt", "hello"u8.ToArray()), Src("Réception.mp4", big)], default);

        using var zip = new ZipArchive(new MemoryStream(output.Inner.ToArray()), ZipArchiveMode.Read);
        Assert.Equal(["a.txt", "Réception.mp4"], zip.Entries.Select(e => e.FullName));
        using var s = zip.GetEntry("Réception.mp4")!.Open();
        var read = new MemoryStream();
        s.CopyTo(read);
        Assert.Equal(big, read.ToArray());
        Assert.Equal(zip.GetEntry("a.txt")!.Length, zip.GetEntry("a.txt")!.CompressedLength); // stored, not deflated
    }

    [Fact]
    public async Task An_empty_selection_still_produces_a_readable_zip()
    {
        var output = new AsyncOnlyStream();
        await ZipStreamer.WriteAsync(output, [], default);
        using var zip = new ZipArchive(new MemoryStream(output.Inner.ToArray()), ZipArchiveMode.Read);
        Assert.Empty(zip.Entries);
    }

    [Fact]
    public void Duplicate_names_get_numbered_suffixes_ignoring_case()
    {
        Assert.Equal(["a.zip", "A (2).zip", "a (3).zip", "b"], ZipStreamer.UniqueNames(["a.zip", "A.zip", "a.zip", "b"]));
    }
}

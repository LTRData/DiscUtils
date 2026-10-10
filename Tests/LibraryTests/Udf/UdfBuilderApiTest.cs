// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using DiscUtils.Udf;
using Xunit;

namespace LibraryTests.Udf;

public class UdfBuilderApiTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpanAndArrayReadsPreserveExtentBoundaries(bool useSpan)
    {
        using var source = new MemoryStream();
        source.Write(new byte[] { 1, 2, 3 }, 0, 3);
        var builder = new UdfBuilder();
        builder.AddFile("file", 3, () => source);
        using var image = builder.Build();
        long start = PayloadStart(image, 3);
        image.Position = start;
        Assert.Equal(1, image.ReadByte());
        source.Position = 3;
        source.Write(new byte[] { 4, 5, 6 }, 0, 3);
        image.Position = start;
        var bytes = Enumerable.Repeat((byte)255, 10).ToArray();
        int read = useSpan ? image.Read(bytes.AsSpan(1, 8)) : image.Read(bytes, 1, 8);
        Assert.Equal(8, read);
        Assert.Equal(new byte[] { 255, 1, 2, 3, 0, 0, 0, 0, 0, 255 }, bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AsyncReadsUseSourceAsyncIoAndPreserveExtentBoundaries(bool useMemory)
    {
        int opens = 0;
        using var source = new AsyncOnlyStream(new byte[] { 1, 2, 3 });
        var builder = new UdfBuilder();
        builder.AddFile("file", 3, () => { opens++; return source; });
        using var image = await builder.BuildAsync(CancellationToken.None);
        Assert.Equal(0, opens);
        long start = PayloadStart(image, 3);
        image.Position = start;
        var bytes = Enumerable.Repeat((byte)255, 10).ToArray();
        using var cancellation = new CancellationTokenSource();
        Assert.Equal(1, await image.ReadAsync(bytes, 0, 1, cancellation.Token));
        source.Append(new byte[] { 4, 5, 6 });
        image.Position = start;
        bytes[0] = 255;
        int read = useMemory
            ? await image.ReadAsync(bytes.AsMemory(1, 8), cancellation.Token)
            : await image.ReadAsync(bytes, 1, 8, cancellation.Token);
        Assert.Equal(8, read);
        Assert.Equal(new byte[] { 255, 1, 2, 3, 0, 0, 0, 0, 0, 255 }, bytes);
        Assert.Equal(1, opens);
        Assert.Equal(2, source.AsyncReads);
        Assert.Equal(cancellation.Token, source.LastToken);
        Assert.True(source.Disposed); // Leaving the payload closes its read state.
    }

    [Fact]
    public async Task AsyncMaterializationUsesSourceAsyncIo()
    {
        using var source = new AsyncOnlyStream(new byte[] { 1, 2, 3 });
        var builder = new UdfBuilder();
        builder.AddFile("file", 3, () => source);
        using var output = new MemoryStream();
        await builder.BuildAsync(output, CancellationToken.None);
        Assert.Equal(1, source.AsyncReads);
        Assert.True(source.Disposed);
        output.Position = 0;
        using var reader = new UdfReader(output);
        using var file = reader.OpenFile("file", FileMode.Open);
        var bytes = new byte[3];
        Assert.Equal(3, file.Read(bytes, 0, 3));
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
    }

    [Fact]
    public async Task AsyncBuildHonorsCancellationBeforeConstructingMetadata()
    {
        var builder = new UdfBuilder();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => builder.BuildAsync(cancellation.Token));
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => builder.BuildAsync(output, cancellation.Token));
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task AsyncPayloadReadHonorsCancellation()
    {
        using var source = new AsyncOnlyStream(new byte[] { 1, 2, 3 });
        var builder = new UdfBuilder();
        builder.AddFile("file", 3, () => source);
        using var image = builder.Build();
        image.Position = PayloadStart(image, 3);
        long position = image.Position;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => image.ReadAsync(new byte[3], 0, 3, cancellation.Token));
        Assert.Equal(position, image.Position);
        Assert.Equal(0, source.AsyncReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedTruncationIsReportedAcrossReadApis(bool useAsync)
    {
        using var source = new MemoryStream();
        source.Write(new byte[] { 1, 2, 3 }, 0, 3);
        var builder = new UdfBuilder();
        builder.AddFile("file", 3, () => source);
        using var image = builder.Build();
        image.Position = PayloadStart(image, 3);
        Assert.Equal(1, image.ReadByte());
        source.SetLength(1);
        if (useAsync)
        {
            await Assert.ThrowsAsync<EndOfStreamException>(
                () => image.ReadAsync(new byte[1], 0, 1));
        }
        else
        {
            Assert.Throws<EndOfStreamException>(() => image.Read(new byte[1].AsSpan()));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidSourceIsDisposedEvenWhenLengthAccessThrows(bool throwOnLength)
    {
        using var source = new InvalidSourceStream(throwOnLength);
        var builder = new UdfBuilder();
        builder.AddFile("file", 3, () => source);
        using var image = builder.Build();
        image.Position = PayloadStart(image, 3);
        Assert.Throws<IOException>(() => image.ReadByte());
        Assert.True(source.Disposed);
    }

    [Fact]
    public void BuildsHaveIndependentPositionsAndOwnedSources()
    {
        var sources = new System.Collections.Generic.List<AsyncOnlyStream>();
        var builder = new UdfBuilder();
        builder.AddFile("file", 3, () =>
        {
            var source = new AsyncOnlyStream(new byte[] { 1, 2, 3 }, allowSync: true);
            sources.Add(source);
            return source;
        });
        using var first = builder.Build();
        using var second = builder.Build();
        first.Position = PayloadStart(first, 3);
        second.Position = PayloadStart(second, 3) + 1;
        Assert.Equal(1, first.ReadByte());
        Assert.Equal(2, second.ReadByte());
        Assert.Equal(2, first.ReadByte());
        Assert.Equal(2, sources.Count);
        first.Dispose();
        Assert.True(sources[0].Disposed);
        Assert.False(sources[1].Disposed);
        Assert.Equal(3, second.ReadByte());
    }

    [Fact]
    public void SourcePathChangesAreRejectedBeforePayloadReads()
    {
        string path = Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            var builder = new UdfBuilder();
            builder.AddFile("file", path);
            using var image = builder.Build();
            System.IO.File.WriteAllBytes(path, new byte[] { 4, 5, 6 });
            System.IO.File.SetLastWriteTimeUtc(path, System.IO.File.GetLastWriteTimeUtc(path).AddMinutes(1));
            image.Position = PayloadStart(image, 3);
            Assert.Throws<IOException>(() => image.ReadByte());
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private static long PayloadStart(Stream image, long length)
        => ((SparseStream)image).Extents.Single(extent => extent.Length == length).Start;

    private sealed class InvalidSourceStream : MemoryStream
    {
        private readonly bool _throwOnLength;
        public InvalidSourceStream(bool throwOnLength) : base(new byte[2])
            => _throwOnLength = throwOnLength;

        public bool Disposed { get; private set; }
        public override long Length => _throwOnLength ? throw new IOException("Length unavailable.") : base.Length;
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class AsyncOnlyStream : ReadOnlyCompatibilityStream
    {
        private readonly MemoryStream _source;
        private readonly bool _allowSync;
        public AsyncOnlyStream(byte[] content, bool allowSync = false)
        {
            _source = new MemoryStream();
            _source.Write(content, 0, content.Length);
            _source.Position = 0;
            _allowSync = allowSync;
        }

        public void Append(byte[] content)
        {
            long position = _source.Position;
            _source.Position = _source.Length;
            _source.Write(content, 0, content.Length);
            _source.Position = position;
        }

        public int AsyncReads { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override long Length => _source.Length;
        public override long Position { get => _source.Position; set => _source.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => _source.Seek(offset, origin);
        public override int Read(byte[] buffer, int offset, int count)
            => _allowSync ? _source.Read(buffer, offset, count) : throw new InvalidOperationException("Synchronous payload read.");
        public override int Read(Span<byte> buffer)
            => _allowSync ? _source.Read(buffer) : throw new InvalidOperationException("Synchronous payload read.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            AsyncReads++;
            LastToken = cancellationToken;
            return _source.ReadAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
                _source.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

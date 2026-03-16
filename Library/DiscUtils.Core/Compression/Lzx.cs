using System;
using System.Buffers;

namespace DiscUtils.Compression;

public sealed class Lzx : IBlockDecompressor, IDisposable
{
    private readonly int _windowBits;
    private readonly int _windowSize;
    private readonly int _mainTreeSymbols;
    private bool _disposed;

    private byte[] _window;
    private byte[] _mainLengths;
    private byte[] _lengthLengths;
    private byte[] _alignedLengths;
    private byte[] _preTreeLengths;

    private ushort[] _mainTable;
    private ushort[] _lengthTable;
    private ushort[] _alignedTable;
    private ushort[] _preTreeTable;

    public Lzx(int windowBits)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowBits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(windowBits, 25);
#else
        if (windowBits <= 0 || windowBits > 25)
        {
            throw new ArgumentOutOfRangeException(nameof(windowBits));
        }
#endif

        _windowBits = windowBits;
        _windowSize = 1 << windowBits;
        _mainTreeSymbols = 256 + 16 * windowBits;

        _window = ArrayPool<byte>.Shared.Rent(_windowSize);
        _mainLengths = ArrayPool<byte>.Shared.Rent(_mainTreeSymbols);
        _lengthLengths = ArrayPool<byte>.Shared.Rent(249);
        _alignedLengths = ArrayPool<byte>.Shared.Rent(8);
        _preTreeLengths = ArrayPool<byte>.Shared.Rent(20);

        // Oversized but simple and legacy-like: one 16-bit lookup table per live tree.
        _mainTable = ArrayPool<ushort>.Shared.Rent(1 << 16);
        _lengthTable = ArrayPool<ushort>.Shared.Rent(1 << 16);
        _alignedTable = ArrayPool<ushort>.Shared.Rent(1 << 16);
        _preTreeTable = ArrayPool<ushort>.Shared.Rent(1 << 16);
    }

    public int WindowBits => _windowBits;

    public int E8FixupMaxSize { get; set; } = 12000000;

    int IBlockDecompressor.BlockSize { get; set; }

    public static bool TryDecompress(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        int windowBits,
        out int bytesConsumed,
        out int bytesWritten)
    {
        using var lzx = new Lzx(windowBits);

        return lzx.TryDecompress(
            source,
            destination,
            out bytesConsumed,
            out bytesWritten);
    }

    public bool TryDecompress(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        out int bytesConsumed,
        out int bytesWritten)
    {
        var workspace = GetWorkspace();
        var decoder = new LzxDecoder(WindowBits, E8FixupMaxSize, workspace);
        return decoder.TryDecompress(source, destination, out bytesConsumed, out bytesWritten);
    }

    bool IBlockDecompressor.TryDecompress(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        out int bytesWritten)
    => TryDecompress(source, destination, out _, out bytesWritten);

    private LzxWorkspace GetWorkspace()
    {
#if NET7_0_OR_GREATER
        ObjectDisposedException.ThrowIf(_disposed, this);
#else
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Lzx));
        }
#endif

        return new LzxWorkspace(
            _window.AsSpan(0, _windowSize),
            _mainLengths.AsSpan(0, _mainTreeSymbols),
            _lengthLengths.AsSpan(0, 249),
            _alignedLengths.AsSpan(0, 8),
            _preTreeLengths.AsSpan(0, 20),
            _mainTable.AsSpan(),
            _lengthTable.AsSpan(),
            _alignedTable.AsSpan(),
            _preTreeTable.AsSpan());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_window is not null)
        {
            ArrayPool<byte>.Shared.Return(_window);
            _window = null!;
        }

        if (_mainLengths is not null)
        {
            ArrayPool<byte>.Shared.Return(_mainLengths);
            _mainLengths = null!;
        }

        if (_lengthLengths is not null)
        {
            ArrayPool<byte>.Shared.Return(_lengthLengths);
            _lengthLengths = null!;
        }

        if (_alignedLengths is not null)
        {
            ArrayPool<byte>.Shared.Return(_alignedLengths);
            _alignedLengths = null!;
        }

        if (_preTreeLengths is not null)
        {
            ArrayPool<byte>.Shared.Return(_preTreeLengths);
            _preTreeLengths = null!;
        }

        if (_mainTable is not null)
        {
            ArrayPool<ushort>.Shared.Return(_mainTable);
            _mainTable = null!;
        }

        if (_lengthTable is not null)
        {
            ArrayPool<ushort>.Shared.Return(_lengthTable);
            _lengthTable = null!;
        }

        if (_alignedTable is not null)
        {
            ArrayPool<ushort>.Shared.Return(_alignedTable);
            _alignedTable = null!;
        }

        if (_preTreeTable is not null)
        {
            ArrayPool<ushort>.Shared.Return(_preTreeTable);
            _preTreeTable = null!;
        }
    }
}

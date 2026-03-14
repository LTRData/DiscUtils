using System;

namespace DiscUtils.Compression;

internal ref struct LzxWorkspace
{
    public Span<byte> Window;
    public Span<byte> MainLengths;
    public Span<byte> LengthLengths;
    public Span<byte> AlignedLengths;
    public Span<byte> PreTreeLengths;

    public Span<ushort> SortedSymbols;
    public Span<ushort> LengthCounts;
    public Span<int> FirstCode;
    public Span<int> FirstSymbol;
    public Span<int> NextSymbol;
    public Span<ushort> FastSymbols;
    public Span<byte> FastLengths;

    public LzxWorkspace(
        Span<byte> window,
        Span<byte> mainLengths,
        Span<byte> lengthLengths,
        Span<byte> alignedLengths,
        Span<byte> preTreeLengths,
        Span<ushort> sortedSymbols,
        Span<ushort> lengthCounts,
        Span<int> firstCode,
        Span<int> firstSymbol,
        Span<int> nextSymbol,
        Span<ushort> fastSymbols,
        Span<byte> fastLengths)
    {
        Window = window;
        MainLengths = mainLengths;
        LengthLengths = lengthLengths;
        AlignedLengths = alignedLengths;
        PreTreeLengths = preTreeLengths;
        SortedSymbols = sortedSymbols;
        LengthCounts = lengthCounts;
        FirstCode = firstCode;
        FirstSymbol = firstSymbol;
        NextSymbol = nextSymbol;
        FastSymbols = fastSymbols;
        FastLengths = fastLengths;
    }
}

using System;

namespace DiscUtils.Compression;

internal ref struct LzxWorkspace
{
    public Span<byte> Window;

    public Span<byte> MainLengths;
    public Span<byte> LengthLengths;
    public Span<byte> AlignedLengths;
    public Span<byte> PreTreeLengths;

    public Span<ushort> MainTable;
    public Span<ushort> LengthTable;
    public Span<ushort> AlignedTable;
    public Span<ushort> PreTreeTable;

    public LzxWorkspace(
        Span<byte> window,
        Span<byte> mainLengths,
        Span<byte> lengthLengths,
        Span<byte> alignedLengths,
        Span<byte> preTreeLengths,
        Span<ushort> mainTable,
        Span<ushort> lengthTable,
        Span<ushort> alignedTable,
        Span<ushort> preTreeTable)
    {
        Window = window;
        MainLengths = mainLengths;
        LengthLengths = lengthLengths;
        AlignedLengths = alignedLengths;
        PreTreeLengths = preTreeLengths;
        MainTable = mainTable;
        LengthTable = lengthTable;
        AlignedTable = alignedTable;
        PreTreeTable = preTreeTable;
    }
}

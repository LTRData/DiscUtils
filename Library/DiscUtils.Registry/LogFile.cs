using DiscUtils.Streams;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace DiscUtils.Registry;

internal class LogFile : IEnumerable<LogEntry>
{
    private const int LOG_ENTRY = 1162638920;

    public HiveHeader HiveHeader { get; } = new();

    public bool HeaderValid { get; }

    private readonly byte[] buffer;

    public LogFile(Stream stream)
    {
        stream.Position = 0;
        buffer = stream.ReadExactly((int)stream.Length);
        var headerResult = HiveHeader.ReadFrom(buffer, throwOnInvalidData: false);
        if (headerResult > 0)
        {
            HeaderValid = true;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<LogEntry> GetEnumerator()
    {
        var last_sequence = 0;

        for (var offset = 0x200; offset < buffer.Length - HiveHeader.HeaderSize;)
        {
            var signature = EndianUtilities.ToInt32LittleEndian(buffer, offset);
            if (signature != LOG_ENTRY)
            {
                yield break;
            }

            var size = EndianUtilities.ToInt32LittleEndian(buffer, offset + 4);
                        
            // Check for corrupt log records
            if (size <= 0 || offset + size > buffer.Length)
            {
                break;
            }

            var stored_hash1 = EndianUtilities.ToInt64LittleEndian(buffer, offset + 24);
            var stored_hash2 = EndianUtilities.ToInt64LittleEndian(buffer, offset + 32);
            var calc_hash1 = CalculateLogEntryHash(buffer.AsSpan(offset + 40, size - 40));
            var calc_hash2 = CalculateLogEntryHash(buffer.AsSpan(offset, 32));
            var sequence_number = EndianUtilities.ToInt32LittleEndian(buffer, offset + 12);

            if (stored_hash1 != calc_hash1 || stored_hash2 != calc_hash2 ||
                (last_sequence != 0 && sequence_number != last_sequence + 1))
            {
                Trace.WriteLine($"Hash or sequence mismatch for log entry at {offset}, sequence {sequence_number}");
                yield break;
            }

            last_sequence = sequence_number;

            if (sequence_number >= HiveHeader.Sequence1)
            {
                var dirty_page_count = EndianUtilities.ToInt32LittleEndian(buffer, offset + 20);

                for (int i = 0, dataoffset = offset + 40 + dirty_page_count * 8;
                    i < dirty_page_count;
                    i++)
                {
                    var pageoffset = EndianUtilities.ToInt32LittleEndian(buffer, offset + 40 + i * 8);
                    var pagesize = EndianUtilities.ToInt32LittleEndian(buffer, offset + 40 + i * 8 + 4);
                    yield return new(sequence_number, pageoffset, pagesize, dataoffset);
                    dataoffset += pagesize;
                }
            }

            offset += size;
        }
    }

    private static long CalculateLogEntryHash(ReadOnlySpan<byte> buffer) =>
        Marvin.ComputeHash(buffer, 0x82EF4D887A4E55C5);

    public (int SequenceNumber, int MaxPosition) UpdateHive(Stream hive)
    {
        var sequenceNumber = 0;
        var maxPosition = 0;

        foreach (var entry in this)
        {
            hive.Position = 0x1000 + entry.PageOffset;
            hive.Write(buffer, entry.BufferOffset, entry.PageSize);
            sequenceNumber = entry.SequenceNumber;

            if (hive.Position - RegistryHive.BinStart > maxPosition)
            {
                maxPosition = (int)(hive.Position - RegistryHive.BinStart);
            }
        }

        return (sequenceNumber, maxPosition);
    }
}


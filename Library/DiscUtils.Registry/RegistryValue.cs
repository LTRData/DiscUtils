//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.Buffers;
using System.Text;
using DiscUtils.Streams;

namespace DiscUtils.Registry;

/// <summary>
/// A registry value.
/// </summary>
public sealed class RegistryValue
{
    private readonly ValueCell _cell;
    private readonly RegistryHive _hive;

    internal RegistryValue(RegistryHive hive, ValueCell cell)
    {
        _hive = hive;
        _cell = cell;
    }

    /// <summary>
    /// Gets the type of the value.
    /// </summary>
    public RegistryValueType DataType
    {
        get { return _cell.DataType; }
    }

    /// <summary>
    /// Gets the name of the value, or empty string if unnamed.
    /// </summary>
    public string Name
    {
        get { return _cell.Name ?? string.Empty; }
    }

    /// <summary>
    /// Gets the value data mapped to a .net object.
    /// </summary>
    /// <remarks>The mapping from registry type of .NET type is as follows:
    /// <list type="table">
    ///   <listheader>
    ///     <term>Value Type</term>
    ///     <term>.NET type</term>
    ///   </listheader>
    ///   <item>
    ///     <description>String</description>
    ///     <description>string</description>
    ///   </item>
    ///   <item>
    ///     <description>ExpandString</description>
    ///     <description>string</description>
    ///   </item>
    ///   <item>
    ///     <description>Link</description>
    ///     <description>string</description>
    ///   </item>
    ///   <item>
    ///     <description>DWord</description>
    ///     <description>uint</description>
    ///   </item>
    ///   <item>
    ///     <description>DWordBigEndian</description>
    ///     <description>uint</description>
    ///   </item>
    ///   <item>
    ///     <description>MultiString</description>
    ///     <description>string[]</description>
    ///   </item>
    ///   <item>
    ///     <description>QWord</description>
    ///     <description>ulong</description>
    ///   </item>
    /// </list>
    /// </remarks>
    public object Value
    {
        get
        {
            var buffer = ArrayPool<byte>.Shared.Rent(_cell.DataLength & int.MaxValue);
            try
            {
                return ConvertToObject(GetRawData(buffer), DataType);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    /// Gets raw binary value without interpretation as any particular data type.
    /// </summary>
    public byte[] RawValue
    {
        get
        {
            var buffer = ArrayPool<byte>.Shared.Rent(_cell.DataLength & int.MaxValue);
            try
            {
                return GetRawData(buffer).ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    /// The raw value data as a byte array.
    /// </summary>
    /// <returns>The value as a raw byte array.</returns>
    internal Span<byte> GetRawData(Span<byte> maxBytes)
    {
        if (_cell.DataLength < 0)
        {
            // ToDo: Are we really supposed to return a zeroed buffer except
            // for first four bytes set to length of data?

            var len = _cell.DataLength & 0x7FFFFFFF;
            Span<byte> buffer = stackalloc byte[4];
            EndianUtilities.WriteBytesLittleEndian(_cell.DataIndex, buffer);

            var result = maxBytes.Slice(0, len);
            buffer.Slice(0, Math.Min(len, buffer.Length)).CopyTo(result);
            return result;
        }

        return _hive.RawCellData(_cell.DataIndex, maxBytes.Slice(0, _cell.DataLength));
    }

    /// <summary>
    /// Sets the value as raw bytes, with no validation that enough data is specified for the given value type.
    /// </summary>
    /// <param name="data">The data to store.</param>
    /// <param name="valueType">The type of the data.</param>
    public void SetRawData(ReadOnlySpan<byte> data, RegistryValueType valueType)
    {
        // If we can place the data in the DataIndex field, do that to save space / allocation
        if ((valueType == RegistryValueType.Dword || valueType == RegistryValueType.DwordBigEndian) && data.Length <= 4)
        {
            if (_cell.DataLength >= 0)
            {
                _hive.FreeCell(_cell.DataIndex);
            }

            _cell.DataLength = (int)((uint)data.Length | 0x80000000);
            _cell.DataIndex = EndianUtilities.ToInt32LittleEndian(data);
            _cell.DataType = valueType;
        }
        else
        {
            if (_cell.DataIndex == -1 || _cell.DataLength < 0)
            {
                _cell.DataIndex = _hive.AllocateRawCell(data.Length);
            }

            if (!_hive.WriteRawCellData(_cell.DataIndex, data))
            {
                var newDataIndex = _hive.AllocateRawCell(data.Length);
                _hive.WriteRawCellData(newDataIndex, data);
                _hive.FreeCell(_cell.DataIndex);
                _cell.DataIndex = newDataIndex;
            }

            _cell.DataLength = data.Length;
            _cell.DataType = valueType;
        }

        _hive.UpdateCell(_cell, false);
    }

    /// <summary>
    /// Sets the value stored.
    /// </summary>
    /// <param name="value">The value to store.</param>
    /// <param name="valueType">The registry type of the data.</param>
    public void SetValue(object value, RegistryValueType valueType)
    {
        var data = ConvertToData(value, valueType);
        SetRawData(data.Span, valueType);
    }

    /// <summary>
    /// Gets a string representation of the registry value.
    /// </summary>
    /// <returns>The registry value as a string.</returns>
    public override string ToString() => $"{Name}:{DataType}:{DataAsString()}";

    private static object ConvertToObject(ReadOnlySpan<byte> data, RegistryValueType type)
    {
        switch (type)
        {
            case RegistryValueType.String:
            case RegistryValueType.ExpandString:
            case RegistryValueType.Link:
                return EndianUtilities.LittleEndianUnicodeBytesToString(data).Trim('\0');

            case RegistryValueType.Dword:
                return EndianUtilities.ToInt32LittleEndian(data);

            case RegistryValueType.DwordBigEndian:
                return EndianUtilities.ToInt32BigEndian(data);

            case RegistryValueType.MultiString:
                var multiString = EndianUtilities.LittleEndianUnicodeBytesToString(data).TrimEnd('\0');
                return multiString.Split('\0');

            case RegistryValueType.Qword:
                return string.Empty + EndianUtilities.ToUInt64LittleEndian(data);

            default:
                return data.ToArray();
        }
    }

    private static ReadOnlyMemory<byte> ConvertToData(object value, RegistryValueType valueType)
    {
        byte[] data;

        switch (valueType)
        {
            case RegistryValueType.String:
            case RegistryValueType.ExpandString:
                var strValue = value.ToString();
                data = new byte[strValue.Length * 2 + 2];
                Encoding.Unicode.GetBytes(strValue, 0, strValue.Length, data, 0);
                break;

            case RegistryValueType.Dword:
                data = new byte[sizeof(int)];
                EndianUtilities.WriteBytesLittleEndian((int)value, data, 0);
                break;

            case RegistryValueType.Qword:
                data = new byte[sizeof(long)];
                EndianUtilities.WriteBytesLittleEndian((int)value, data, 0);
                break;

            case RegistryValueType.DwordBigEndian:
                data = new byte[sizeof(int)];
                EndianUtilities.WriteBytesBigEndian((int)value, data, 0);
                break;

            case RegistryValueType.MultiString:
                var multiStrValue = $"{string.Join("\0", (string[])value)}\0";
                data = new byte[multiStrValue.Length * 2 + 2];
                Encoding.Unicode.GetBytes(multiStrValue, 0, multiStrValue.Length, data, 0);
                break;

            default:
                if (value is byte[] array)
                {
                    return array;
                }
                else if (value is ReadOnlyMemory<byte> romem)
                {
                    return romem;
                }
                else if (value is Memory<byte> mem)
                {
                    return mem;
                }
                else
                {
                    throw new ArgumentException("Invalid data type for value data parameter", nameof(value));
                }
        }

        return data;
    }

    private string DataAsString()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_cell.DataLength & int.MaxValue);
        try
        {
            var data = GetRawData(buffer);

            switch (DataType)
            {
                case RegistryValueType.String:
                case RegistryValueType.ExpandString:
                case RegistryValueType.Link:
                case RegistryValueType.Dword:
                case RegistryValueType.DwordBigEndian:
                case RegistryValueType.Qword:
                    return ConvertToObject(data, DataType).ToString();

                case RegistryValueType.MultiString:
                    return string.Join(",", (string[])ConvertToObject(data, DataType));

                default:
                    var result = string.Empty;
                    for (var i = 0; i < Math.Min(data.Length, 8); ++i)
                    {
                        result += $"{(int)data[i]:X2} ";
                    }

                    return result + $" ({data.Length} bytes)";
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
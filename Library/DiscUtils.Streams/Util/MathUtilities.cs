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
using System.Numerics;
using System.Runtime.CompilerServices;

namespace DiscUtils.Streams;

public static class MathUtilities
{
    /// <summary>
    /// Round up a value to a multiple of a unit size.
    /// </summary>
    /// <param name="value">The value to round up.</param>
    /// <param name="unit">The unit (the returned value will be a multiple of this number).</param>
    /// <returns>The rounded-up value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long RoundUp(long value, long unit)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unit);
#else
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (unit <= 0) throw new ArgumentOutOfRangeException(nameof(unit));
#endif

        if (IsPowerOfTwo((ulong)unit))
        {
            var mask = unit - 1;

            if ((value & mask) == 0)
            {
                return value;
            }

            return checked((value | mask) + 1);
        }

        var remainder = value % unit;

        return remainder == 0
            ? value
            : checked(value + unit - remainder);
    }

    /// <summary>
    /// Round up a value to a multiple of a unit size.
    /// </summary>
    /// <param name="value">The value to round up.</param>
    /// <param name="unit">The unit (the returned value will be a multiple of this number).</param>
    /// <returns>The rounded-up value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RoundUp(int value, int unit)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unit);
#else
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (unit <= 0) throw new ArgumentOutOfRangeException(nameof(unit));
#endif

        if (IsPowerOfTwo((uint)unit))
        {
            var mask = unit - 1;

            if ((value & mask) == 0)
            {
                return value;
            }

            return checked((value | mask) + 1);
        }

        var remainder = value % unit;

        return remainder == 0
            ? value
            : checked(value + unit - remainder);
    }

    /// <summary>
    /// Round down a value to a multiple of a unit size.
    /// </summary>
    /// <param name="value">The value to round down.</param>
    /// <param name="unit">The unit (the returned value will be a multiple of this number).</param>
    /// <returns>The rounded-down value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long RoundDown(long value, long unit)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unit);
#else
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (unit <= 0) throw new ArgumentOutOfRangeException(nameof(unit));
#endif

        if (IsPowerOfTwo((ulong)unit))
        {
            return value & ~(unit - 1);
        }

        return value - value % unit;
    }

    /// <summary>
    /// Round down a value to a multiple of a unit size.
    /// </summary>
    /// <param name="value">The value to round down.</param>
    /// <param name="unit">The unit (the returned value will be a multiple of this number).</param>
    /// <returns>The rounded-down value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int RoundDown(int value, int unit)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unit);
#else
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (unit <= 0) throw new ArgumentOutOfRangeException(nameof(unit));
#endif

        if (IsPowerOfTwo((uint)unit))
        {
            return value & ~(unit - 1);
        }

        return value - value % unit;
    }

    /// <summary>
    /// Calculates the CEIL function.
    /// </summary>
    /// <param name="numerator">The value to divide.</param>
    /// <param name="denominator">The value to divide by.</param>
    /// <returns>The value of CEIL(numerator/denominator).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Ceil(int numerator, int denominator)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
#else
        if (numerator < 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
#endif

        if (IsPowerOfTwo(denominator))
        {
            var mask = denominator - 1;
            var shift = Log2(denominator);

            return (numerator >> shift)
                 + ((numerator & mask) != 0 ? 1 : 0);
        }

        var quotient = Math.DivRem(numerator, denominator, out var remainder);
        return quotient + (remainder != 0 ? 1 : 0);
    }

    /// <summary>
    /// Calculates the CEIL function.
    /// </summary>
    /// <param name="numerator">The value to divide.</param>
    /// <param name="denominator">The value to divide by.</param>
    /// <returns>The value of CEIL(numerator/denominator).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Ceil(uint numerator, uint denominator)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
#else
        if (numerator < 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
#endif

        if (IsPowerOfTwo(denominator))
        {
            var mask = denominator - 1;
            var shift = Log2(denominator);

            return (numerator >> shift)
                 + ((numerator & mask) != 0 ? 1u : 0u);
        }

        var quotient = Math.DivRem(numerator, denominator, out var remainder);
        return (uint)(quotient + (remainder != 0 ? 1 : 0));
    }

    /// <summary>
    /// Calculates the CEIL function.
    /// </summary>
    /// <param name="numerator">The value to divide.</param>
    /// <param name="denominator">The value to divide by.</param>
    /// <returns>The value of CEIL(numerator/denominator).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Ceil(long numerator, long denominator)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegative(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
#else
        if (numerator < 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
#endif

        if (IsPowerOfTwo((ulong)denominator))
        {
            var mask = denominator - 1;
            var shift = Log2((ulong)denominator);

            return (numerator >> shift)
                 + ((numerator & mask) != 0 ? 1 : 0);
        }

        var quotient = Math.DivRem(numerator, denominator, out var remainder);
        return quotient + (remainder != 0 ? 1 : 0);
    }

#if NET7_0_OR_GREATER
    public static int Log2(uint val)
    {
        ArgumentOutOfRangeException.ThrowIfZero(val);

        if (val <= 0 || !BitOperations.IsPow2(val))
        {
            throw new ArgumentOutOfRangeException(nameof(val), "Value must be a positive power of two.");
        }

        return BitOperations.Log2(val);
    }

    public static int Log2(int val)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(val);

        if (val == 0 || !BitOperations.IsPow2(val))
        {
            throw new ArgumentOutOfRangeException(nameof(val), "Value must be a positive power of two.");
        }

        return BitOperations.Log2(unchecked((uint)val));
    }

    public static int Log2(ulong val)
    {
        ArgumentOutOfRangeException.ThrowIfZero(val);

        if (val <= 0 || !BitOperations.IsPow2(val))
        {
            throw new ArgumentOutOfRangeException(nameof(val), "Value must be a positive power of two.");
        }

        return BitOperations.Log2(val);
    }

    public static int Log2(long val)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(val);

        if (val == 0 || !BitOperations.IsPow2(val))
        {
            throw new ArgumentOutOfRangeException(nameof(val), "Value must be a positive power of two.");
        }

        return BitOperations.Log2(unchecked((uint)val));
    }
#else
    public static int Log2(uint val)
    {
        if (val == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(val), "Cannot calculate log of Zero");
        }

        var result = 0;
        while ((val & 1) != 1)
        {
            val >>= 1;
            ++result;
        }

        if (val == 1)
        {
            return result;
        }

        throw new ArgumentOutOfRangeException(nameof(val), "Input is not a power of Two");
    }

    public static int Log2(int val)
    {
        if (val <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(val), "Cannot calculate log of Zero");
        }

        var result = 0;
        while ((val & 1) != 1)
        {
            val >>= 1;
            ++result;
        }

        if (val == 1)
        {
            return result;
        }

        throw new ArgumentOutOfRangeException(nameof(val), "Input is not a power of Two");
    }

    public static int Log2(ulong val)
    {
        if (val == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(val), "Cannot calculate log of Zero");
        }

        var result = 0;
        while ((val & 1) != 1)
        {
            val >>= 1;
            ++result;
        }

        if (val == 1)
        {
            return result;
        }

        throw new ArgumentOutOfRangeException(nameof(val), "Input is not a power of Two");
    }

    public static int Log2(long val)
    {
        if (val <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(val), "Cannot calculate log of Zero");
        }

        var result = 0;
        while ((val & 1) != 1)
        {
            val >>= 1;
            ++result;
        }

        if (val == 1)
        {
            return result;
        }

        throw new ArgumentOutOfRangeException(nameof(val), "Input is not a power of Two");
    }
#endif

#if NET6_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOfTwo(int bpbBytesPerSec)
        => BitOperations.IsPow2(bpbBytesPerSec);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOfTwo(uint bpbBytesPerSec)
        => BitOperations.IsPow2(bpbBytesPerSec);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOfTwo(long bpbBytesPerSec)
        => BitOperations.IsPow2(bpbBytesPerSec);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOfTwo(ulong bpbBytesPerSec)
        => BitOperations.IsPow2(bpbBytesPerSec);
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOfTwo(int bpbBytesPerSec) =>
        bpbBytesPerSec > 0 && (bpbBytesPerSec & bpbBytesPerSec - 1) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOfTwo(uint bpbBytesPerSec) =>
        bpbBytesPerSec > 0 && (bpbBytesPerSec & bpbBytesPerSec - 1) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOfTwo(long bpbBytesPerSec) =>
        bpbBytesPerSec > 0 && (bpbBytesPerSec & bpbBytesPerSec - 1) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPowerOfTwo(ulong bpbBytesPerSec) =>
        (bpbBytesPerSec & bpbBytesPerSec - 1) == 0;
#endif
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LibraryTests.ReFs;

/// <summary>A small JSON reader for the manifests (objects, arrays, strings, numbers, booleans, null); no JSON library on every test target. Integers are returned as
/// <see cref="long"/> (FILETIME values exceed the precision of a double), other numbers as <see cref="double"/>.</summary>
internal static class MiniJson // Remove when net46 is no longer a target.
{
    public static object? Parse(string text)
    {
        var i = 0;
        var value = Value(text, ref i);
        return value;
    }

    private static void Skip(string s, ref int i)
    {
        while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == '﻿'))
        {
            i++;
        }
    }

    private static object? Value(string s, ref int i)
    {
        Skip(s, ref i);
        switch (s[i])
        {
            case '{':
                var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
                i++;
                Skip(s, ref i);
                if (s[i] == '}')
                {
                    i++;
                    return obj;
                }

                while (true)
                {
                    Skip(s, ref i);
                    var key = String(s, ref i);
                    Skip(s, ref i);
                    i++; // ':'
                    obj[key] = Value(s, ref i);
                    Skip(s, ref i);
                    if (s[i++] == '}')
                    {
                        return obj;
                    }
                }

            case '[':
                var list = new List<object?>();
                i++;
                Skip(s, ref i);
                if (s[i] == ']')
                {
                    i++;
                    return list;
                }

                while (true)
                {
                    list.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (s[i++] == ']')
                    {
                        return list;
                    }
                }

            case '"':
                return String(s, ref i);
            case 't':
                i += 4;
                return true;
            case 'f':
                i += 5;
                return false;
            case 'n':
                i += 4;
                return null;
            default:
                var start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
                {
                    i++;
                }

                var number = s.Substring(start, i - start);

                // Boxed separately: a conditional expression would convert the long to double.
                if (long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                {
                    return integer;
                }

                return double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    private static string String(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++; // opening quote
        while (s[i] != '"')
        {
            if (s[i] == '\\')
            {
                i++;
                switch (s[i])
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(s[i]); break;
                }
            }
            else
            {
                sb.Append(s[i]);
            }

            i++;
        }

        i++;
        return sb.ToString();
    }
}
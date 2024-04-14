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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace DiscUtils.Ntfs;

public class CookedDataRuns
{
    private int _firstDirty = int.MaxValue;
    private int _lastDirty;
    private readonly List<CookedDataRun> _runs;

    internal CookedDataRuns()
    {
        _runs = new List<CookedDataRun>();
    }

    internal CookedDataRuns(IEnumerable<DataRun> rawRuns, NonResidentAttributeRecord attributeExtent)
    {
        _runs = new List<CookedDataRun>();
        Append(rawRuns, attributeExtent);
    }

    public ReadOnlyCollection<CookedDataRun> AsReadOnly() => _runs.AsReadOnly();

    public int Count => _runs.Count;

    public CookedDataRun this[int index]
    {
        get { return _runs[index]; }
    }

    public CookedDataRun Last
    {
        get
        {
            if (_runs.Count == 0)
            {
                return null;
            }

            return _runs[_runs.Count - 1];
        }
    }

    public long NextVirtualCluster
    {
        get
        {
            if (_runs.Count == 0)
            {
                return 0;
            }

            var lastRun = _runs.Count - 1;
            return _runs[lastRun].StartVcn + _runs[lastRun].Length;
        }
    }

    public int FindDataRun(long vcn, int startIdx)
    {
        var numRuns = _runs.Count;
        if (numRuns > 0)
        {
            var run = _runs[numRuns - 1];
            if (vcn >= run.StartVcn)
            {
                if (run.StartVcn + run.Length > vcn)
                {
                    return numRuns - 1;
                }

                throw new IOException("Looking for VCN outside of data runs");
            }

            for (var i = startIdx; i < numRuns; ++i)
            {
                run = _runs[i];
                if (run.StartVcn + run.Length > vcn)
                {
                    return i;
                }
            }
        }

        throw new IOException("Looking for VCN outside of data runs");
    }

    internal void Append(DataRun rawRun, NonResidentAttributeRecord attributeExtent)
    {
        var last = Last;
        _runs.Add(new CookedDataRun(rawRun, NextVirtualCluster, last == null ? 0 : last.StartLcn, attributeExtent));
    }

    internal void Append(IEnumerable<DataRun> rawRuns, NonResidentAttributeRecord attributeExtent)
    {
        var vcn = NextVirtualCluster;
        long lcn = 0;
        foreach (var run in rawRuns)
        {
            _runs.Add(new CookedDataRun(run, vcn, lcn, attributeExtent));
            vcn += run.RunLength;
            lcn += run.RunOffset;
        }
    }

    internal void MakeSparse(int index)
    {
        if (index < _firstDirty)
        {
            _firstDirty = index;
        }

        if (index > _lastDirty)
        {
            _lastDirty = index;
        }

        var prevLcn = index == 0 ? 0 : _runs[index - 1].StartLcn;
        var run = _runs[index];

        if (run.IsSparse)
        {
            throw new ArgumentException("Run is already sparse", nameof(index));
        }

        _runs[index] = new CookedDataRun(new DataRun(0, run.Length, true), run.StartVcn, prevLcn,
            run.AttributeExtent);
        run.AttributeExtent.ReplaceRun(run.DataRun, _runs[index].DataRun);

        for (var i = index + 1; i < _runs.Count; ++i)
        {
            if (!_runs[i].IsSparse)
            {
                _runs[i].DataRun.RunOffset += run.StartLcn - prevLcn;
                break;
            }
        }
    }

    internal void MakeNonSparse(int index, IEnumerable<DataRun> rawRuns)
    {
        if (index < _firstDirty)
        {
            _firstDirty = index;
        }

        if (index > _lastDirty)
        {
            _lastDirty = index;
        }

        var prevLcn = index == 0 ? 0 : _runs[index - 1].StartLcn;
        var run = _runs[index];

        if (!run.IsSparse)
        {
            throw new ArgumentException("Run is already non-sparse", nameof(index));
        }

        _runs.RemoveAt(index);
        var insertIdx = run.AttributeExtent.RemoveRun(run.DataRun);

        CookedDataRun lastNewRun = null;
        var lcn = prevLcn;
        var vcn = run.StartVcn;
        foreach (var rawRun in rawRuns)
        {
            var newRun = new CookedDataRun(rawRun, vcn, lcn, run.AttributeExtent);

            _runs.Insert(index, newRun);
            run.AttributeExtent.InsertRun(insertIdx, rawRun);

            vcn += rawRun.RunLength;
            lcn += rawRun.RunOffset;

            lastNewRun = newRun;
            insertIdx++;

            index++;
        }

        for (var i = index; i < _runs.Count; ++i)
        {
            if (_runs[i].IsSparse)
            {
                _runs[i].StartLcn = lastNewRun.StartLcn;
            }
            else
            {
                _runs[i].DataRun.RunOffset = _runs[i].StartLcn - lastNewRun.StartLcn;
                break;
            }
        }
    }

    internal void SplitRun(int runIdx, long vcn)
    {
        if (runIdx < _firstDirty)
        {
            _firstDirty = runIdx;
        }

        if (runIdx > _lastDirty)
        {
            _lastDirty = runIdx;
        }

        var run = _runs[runIdx];

        if (run.StartVcn >= vcn || run.StartVcn + run.Length <= vcn)
        {
            throw new ArgumentException("Attempt to split run outside of it's range", nameof(vcn));
        }

        var distance = vcn - run.StartVcn;
        var offset = run.IsSparse ? 0 : distance;
        var newRun = new CookedDataRun(new DataRun(offset, run.Length - distance, run.IsSparse), vcn,
            run.StartLcn, run.AttributeExtent);

        run.Length = distance;

        _runs.Insert(runIdx + 1, newRun);
        run.AttributeExtent.InsertRun(run.DataRun, newRun.DataRun);

        for (var i = runIdx + 2; i < _runs.Count; ++i)
        {
            if (_runs[i].IsSparse)
            {
                _runs[i].StartLcn += offset;
            }
            else
            {
                _runs[i].DataRun.RunOffset -= offset;
                break;
            }
        }
    }

    /// <summary>
    /// Truncates the set of data runs.
    /// </summary>
    /// <param name="index">The first run to be truncated.</param>
    internal void TruncateAt(int index)
    {
        while (index < _runs.Count)
        {
            _runs[index].AttributeExtent.RemoveRun(_runs[index].DataRun);
            _runs.RemoveAt(index);
        }
    }

    internal void CollapseRuns()
    {
        var i = _firstDirty > 1 ? _firstDirty - 1 : 0;
        while (i < _runs.Count - 1 && i <= _lastDirty + 1)
        {
            if (_runs[i].IsSparse && _runs[i + 1].IsSparse)
            {
                _runs[i].Length += _runs[i + 1].Length;
                _runs[i + 1].AttributeExtent.RemoveRun(_runs[i + 1].DataRun);
                _runs.RemoveAt(i + 1);
            }
            else if (!_runs[i].IsSparse && !_runs[i].IsSparse &&
                     _runs[i].StartLcn + _runs[i].Length == _runs[i + 1].StartLcn)
            {
                _runs[i].Length += _runs[i + 1].Length;
                _runs[i + 1].AttributeExtent.RemoveRun(_runs[i + 1].DataRun);
                _runs.RemoveAt(i + 1);

                for (var j = i + 1; j < _runs.Count; ++j)
                {
                    if (_runs[j].IsSparse)
                    {
                        _runs[j].StartLcn = _runs[i].StartLcn;
                    }
                    else
                    {
                        _runs[j].DataRun.RunOffset = _runs[j].StartLcn - _runs[i].StartLcn;
                        break;
                    }
                }
            }
            else
            {
                ++i;
            }
        }

        _firstDirty = int.MaxValue;
        _lastDirty = 0;
    }
}
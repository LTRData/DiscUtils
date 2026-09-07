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

using System.Collections.Generic;

namespace DiscUtils.Internal;

/// <summary>Extension point for mapping disks and physical volumes to logical volumes.</summary>
/// <remarks>
/// Implementations can return custom mappings using the public <see cref="LogicalVolumeInfo"/> constructor.
/// Register an instance with <see cref="VolumeManager.RegisterLogicalVolumeFactory(LogicalVolumeFactory)"/>
/// or apply <see cref="LogicalVolumeFactoryAttribute"/> for generated or reflection-based discovery.
/// </remarks>
public abstract class LogicalVolumeFactory
{
    /// <summary>Indicates whether this factory maps the supplied physical volume.</summary>
    public abstract bool HandlesPhysicalVolume(PhysicalVolumeInfo volume);

    /// <summary>Adds logical volumes to the shared result, keyed by their stable identities.</summary>
    public abstract void MapDisks(IEnumerable<VirtualDisk> disks, Dictionary<string, LogicalVolumeInfo> result);
}

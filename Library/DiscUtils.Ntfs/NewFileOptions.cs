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

using DiscUtils.Core.WindowsSecurity.AccessControl;

namespace DiscUtils.Ntfs;

/// <summary>
/// Options controlling how new NTFS files are created.
/// </summary>
public readonly struct NewFileOptions
{
    /// <summary>
    /// Initializes a new instance of the NewFileOptions class.
    /// </summary>
    public NewFileOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the NewFileOptions class.
    /// The default (<c>null</c>) value of a parameter indicates the file system default behaviour applies
    /// </summary>
    /// <param name="compressed">Sets whether the new file should be compressed.</param>
    /// <param name="createShortNames">Sets whether a short name should be created for the file.</param>
    /// <param name="securityDescriptor">Sets the security descriptor that to set for the new file.</param>
    public NewFileOptions(bool? compressed, bool? createShortNames, RawSecurityDescriptor securityDescriptor)
    {
        Compressed = compressed;
        CreateShortNames = createShortNames;
        SecurityDescriptor = securityDescriptor;
    }

    /// <summary>
    /// Gets whether the new file should be compressed.
    /// </summary>
    /// <remarks>The default (<c>null</c>) value indicates the file system default behaviour applies.</remarks>
    public bool? Compressed { get; }

    /// <summary>
    /// Gets whether a short name should be created for the file.
    /// </summary>
    /// <remarks>The default (<c>null</c>) value indicates the file system default behaviour applies.</remarks>
    public bool? CreateShortNames { get; }

    /// <summary>
    /// Gets the security descriptor that to set for the new file.
    /// </summary>
    /// <remarks>The default (<c>null</c>) value indicates the security descriptor is inherited.</remarks>
    public RawSecurityDescriptor SecurityDescriptor { get; }
}
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
using System.IO;

namespace DiscUtils;

public interface IFileSystemWithEnumerationOptions : IFileSystem
{
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    /// <summary>
    /// Gets the names of files and subdirectories in a specified directory matching a specified
    /// search pattern.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <param name="searchPattern">The search string to match against.</param>
    /// <param name="options">An object that describes the search and enumeration configuration to use.</param>
    /// <returns>Array of directories matching the search pattern.</returns>
    IEnumerable<string> GetDirectories(string path, string searchPattern, EnumerationOptions options);

    /// <summary>
    /// Gets the names of files in a specified directory matching a specified
    /// search pattern, using a value to determine whether to search subdirectories.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <param name="searchPattern">The search string to match against.</param>
    /// <param name="options">An object that describes the search and enumeration configuration to use.</param>
    /// <returns>Array of files matching the search pattern.</returns>
    IEnumerable<string> GetFiles(string path, string searchPattern, EnumerationOptions options);

    /// <summary>
    /// Gets the names of files and subdirectories in a specified directory matching a specified
    /// search pattern.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <param name="searchPattern">The search string to match against.</param>
    /// <param name="options">An object that describes the search and enumeration configuration to use.</param>
    /// <returns>Array of files and subdirectories matching the search pattern.</returns>
    IEnumerable<string> GetFileSystemEntries(string path, string searchPattern, EnumerationOptions options);
#endif
}

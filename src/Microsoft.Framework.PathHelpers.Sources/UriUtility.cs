// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;

namespace Microsoft.Framework.PathHelpers
{
    internal static class UriUtility
    {
        /// <summary>
        /// Converts a uri to a path. Only used for local paths.
        /// </summary>
        internal static string GetPath(Uri uri)
        {
            return GetPath(uri, Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Converts a uri to a path with given path separator. Only used for local paths.
        /// </summary>
        internal static string GetPath(Uri uri, char separator)
        {
            string path = uri.OriginalString;
            if (path.StartsWith("/", StringComparison.Ordinal))
            {
                path = path.Substring(1);
            }

            // Bug 483: We need the unescaped uri string to ensure that all characters are valid for a path.
            // Change the direction of the slashes to match the given separator.
            return Uri.UnescapeDataString(path.Replace('/', separator));
        }
    }
}

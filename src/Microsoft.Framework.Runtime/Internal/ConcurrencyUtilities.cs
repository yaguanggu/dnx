// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Framework.Runtime.Internal
{
    public static class ConcurrencyUtilities
    {
        internal static string FilePathToLockName(string filePath)
        {
            // If we use a file path directly as the name of a semaphore,
            // the ctor of semaphore looks for the file and throws an IOException
            // when the file doesn't exist. So we need a conversion from a file path
            // to a unique lock name.
            return filePath.Replace(Path.DirectorySeparatorChar, '_');
        }

        public static void ExecuteWithFileLocked(string filePath, Action<bool> action)
        {
            ExecuteWithFileLocked(filePath, createdNew =>
            {
                action(createdNew);
                return Task.FromResult(1);
            })
            .GetAwaiter().GetResult();
        }

        public async static Task<T> ExecuteWithFileLocked<T>(string filePath, Func<bool, Task<T>> action)
        {
            for (var i = 0; i < 3; ++i)
            {
                var createdNew = false;
                var fileLock = new Semaphore(initialCount: 0, maximumCount: 1, name: FilePathToLockName(filePath),
                    createdNew: out createdNew);
                try
                {
                    // If this lock is already acquired by another process, wait until we can acquire it
                    if (!createdNew)
                    {
                        // Timeout and retry after 5 seconds
                        if (fileLock.WaitOne(5000) == false)
                        {
                            continue;
                        }
                    }

                    return await action(createdNew);
                }
                finally
                {
                    fileLock.Release();
                }
            }

            throw new TaskCanceledException($"Failed to acquire Semaphore to lock file: {filePath}");
        }
    }
}
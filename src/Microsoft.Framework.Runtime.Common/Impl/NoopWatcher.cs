using System;
using Microsoft.Framework.Runtime.Compilation;

namespace Microsoft.Framework.Runtime
{
    internal sealed class NoopWatcher : IFileWatcher, IFileMonitor
    {
        public static readonly NoopWatcher Instance = new NoopWatcher();

        private NoopWatcher()
        {
        }

        public bool WatchFile(string path)
        {
            return true;
        }

        // Suppressing warning CS0067: The event 'Microsoft.Framework.Runtime.FileSystem.NoopWatcher.OnChanged' is never used
#pragma warning disable 0067

        public event Action<string> OnChanged;

#pragma warning restore 0067

        public void WatchDirectory(string path, string extension)
        {
        }

        public void Dispose()
        {
        }

        public void WatchProject(string path)
        {
        }
    }
}
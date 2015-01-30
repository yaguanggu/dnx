using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using NuGet;

namespace Microsoft.Framework.Runtime.DependencyManagement
{
    public class LockFilePackageFile : IPackageFile
    {
        public bool IsAssemblyReference { get; set; }

        public string EffectivePath
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public string Path { get; set; }

        public IEnumerable<FrameworkName> SupportedFrameworks
        {
            get; set;
        }

        public FrameworkName TargetFramework
        {
            get; set;
        }

        public Stream GetStream()
        {
            throw new NotImplementedException();
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NuGet;

namespace Microsoft.Framework.Runtime.Internal
{
    public class AssemblyUtils
    {
        internal static SemanticVersion GetAssemblyVersion(string path)
        {
#if DNX451
            return new SemanticVersion(AssemblyName.GetAssemblyName(path).Version);
#else
            return new SemanticVersion(System.Runtime.Loader.AssemblyLoadContext.GetAssemblyName(path).Version);
#endif
        }
    }
}

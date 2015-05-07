using System;
using System.Reflection;

namespace Microsoft.Framework.Runtime.Internal
{
    internal class AssemblyUtils
    {
        internal static Version GetAssemblyVersion(string path)
        {
#if DNX451
            return AssemblyName.GetAssemblyName(path).Version;
#else
            return System.Runtime.Loader.AssemblyLoadContext.GetAssemblyName(path).Version;
#endif
        }
    }
}

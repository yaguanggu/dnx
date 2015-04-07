using System;
using Microsoft.Framework.Runtime.Caching;

namespace Microsoft.Framework.Runtime.Compilation
{
    public interface IProjectCompilerFactory
    {
        IProjectCompiler CreateCompiler(
            ICache cache,
            ICacheContextAccessor cacheContextAccessor,
            INamedCacheDependencyProvider namedCacheDependencyProvider,
            IFileWatcher fileWatcher,
            IApplicationEnvironment applicationEnvironment,
            IAssemblyLoadContextFactory loadContextFactory);
    }
}
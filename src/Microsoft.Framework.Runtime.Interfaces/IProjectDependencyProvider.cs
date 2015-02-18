using System;
using System.Collections.Generic;

namespace Microsoft.Framework.Runtime
{
    /// <summary>
    /// Used by compilers to the dependencies required for compilation as well
    /// as development dependencies for other things.
    /// </summary>
    public interface IProjectDependencyProvider
    {
        ILibraryExport GetDependencyExport();

        IEnumerable<ILibraryInformation> GetDevelopmentDependencies();
    }
}
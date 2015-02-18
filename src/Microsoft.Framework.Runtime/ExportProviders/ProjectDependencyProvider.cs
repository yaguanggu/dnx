using System;
using System.Collections.Generic;

namespace Microsoft.Framework.Runtime
{
    public class ProjectDependencyProvider : IProjectDependencyProvider
    {
        private readonly ICache _cache;
        private readonly ILibraryExportProvider _exportProvider;
        private readonly ILibraryManager _libraryManager;
        private readonly ILibraryKey _target;

        public ProjectDependencyProvider(ICache cache,
                                         ILibraryManager libraryManager,
                                         ILibraryExportProvider exportProvider,
                                         ILibraryKey target)
        {
            _cache = cache;
            _libraryManager = libraryManager;
            _exportProvider = exportProvider;
            _target = target;
        }

        public ILibraryExport GetDependencyExport(ILibraryKey libraryKey)
        {
            return ProjectExportProviderHelper.GetExportsRecursive(
                        _cache,
                        _libraryManager,
                        _exportProvider,
                        _target,
                        dependenciesOnly: true);
        }

        public IEnumerable<ILibraryInformation> GetDevelopmentDependencies()
        {
        }
    }
}
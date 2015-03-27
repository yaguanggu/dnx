using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Framework.Logging;
using Microsoft.Framework.Runtime.Dependencies;
using Microsoft.Framework.Runtime.Internal;
using Microsoft.Framework.Runtime.ProjectModel;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.ProjectModel;

namespace Microsoft.Framework.Runtime.Compilation
{
    public class LibraryExporter
    {
        public static readonly string LibraryExportLibraryPropertyName = "Microsoft.Framework.Runtime.Compilation.LibraryExport";
        public static readonly string ProjectLibraryPropertyName = "Microsoft.Framework.Runtime.Compilation.Project";

        private static readonly ILogger Log = RuntimeLogging.Logger<LibraryExporter>();

        private readonly NuGetFramework _targetFramework;
        private readonly PackagePathResolver _packagePathResolver;
        private readonly IServiceProvider _serviceProvider;
        private readonly IAssemblyLoadContextAccessor _loadContextAccessor;

        private readonly Dictionary<TypeInformation, IProjectCompiler> _compilers = new Dictionary<TypeInformation, IProjectCompiler>();

        public LibraryExporter(
            IServiceProvider serviceProvider,
            IAssemblyLoadContextAccessor loadContextAccessor,
            NuGetFramework targetFramework,
            PackagePathResolver packagePathResolver)
        {
            _serviceProvider = serviceProvider;
            _loadContextAccessor = loadContextAccessor;
            _targetFramework = targetFramework;
            _packagePathResolver = packagePathResolver;
        }

        /// <summary>
        /// Creates a <see cref="ILibraryExport"/> containing the references necessary
        /// to use the provided <see cref="Library"/> during compilation.
        /// </summary>
        /// <param name="library">The <see cref="Library"/> to export</param>
        /// <returns>A <see cref="ILibraryExport"/> containing the references exported by this library</returns>
        public ILibraryExport ExportLibrary(Library library, DependencyManager dependencies)
        {
            // TODO: Caching by framework/etc.
            switch (library.Identity.Type)
            {
                case LibraryTypes.Package:
                    return ExportPackageLibrary(library);

                case LibraryTypes.Project:
                    return ExportProjectLibrary(library, dependencies);

                default:
                    return ExportOtherLibrary(library);
            }
        }

        private ILibraryExport ExportOtherLibrary(Library library)
        {
            // Try to create an export for a library of other or unknown type
            // based on well-known properties.

            // Reference Assemblies just put the full path in a property for us.
            var path = library.GetItem<string>(KnownLibraryProperties.AssemblyPath);
            if (!string.IsNullOrEmpty(path))
            {
                return new LibraryExport(
                    new MetadataFileReference(
                        Path.GetFileNameWithoutExtension(path),
                        path));
            }

            Log.LogWarning($"Unable to export {library.Identity}. {library.Identity.Type} libraries are not supported.");
            return LibraryExport.Empty;
        }

        private ILibraryExport ExportPackageLibrary(Library library)
        {
            // Get the lock file group and library
            var group = library.GetRequiredItem<LockFileFrameworkGroup>(KnownLibraryProperties.LockFileFrameworkGroup);
            var lockFileLibrary = library.GetRequiredItem<LockFileLibrary>(KnownLibraryProperties.LockFileLibrary);

            // Resolve the package root
            var packageRoot = _packagePathResolver.ResolvePackagePath(
                lockFileLibrary.Sha,
                lockFileLibrary.Name,
                lockFileLibrary.Version);

            // Grab the compile time assemblies and their full paths
            var metadataReferences = new List<IMetadataReference>();
            foreach (var compileTimeAssembly in group.CompileTimeAssemblies)
            {
                var reference = new MetadataFileReference(
                    Path.GetFileNameWithoutExtension(compileTimeAssembly),
                    Path.Combine(packageRoot, compileTimeAssembly));

                metadataReferences.Add(reference);
            }

            return new LibraryExport(metadataReferences);
        }

        private ILibraryExport ExportProjectLibrary(Library library, DependencyManager dependencies)
        {
            // Get the project
            var project = GetProject(library);
            Log.LogInformation($"Exporting {library.Identity.Name}");

            // TODO: BEFORE PUSH: Add compiled project reference support

            // Figure out the compiler
            var compilerType = project.CompilerServices?.ProjectCompiler ?? Project.DefaultRuntimeCompiler;

            // Create the compiler
            var compiler = _compilers.GetOrAdd(compilerType, typeInfo =>
                CompilerServices.CreateService<IProjectCompiler>(
                    _serviceProvider,
                    _loadContextAccessor.Default,
                    compilerType));

            Log.LogDebug($"  Using compiler {compilerType.TypeName}");

            // TODO: BEFORE PUSH: DEFINITELY CHANGE THIS BEFORE PUSHING!!
            var imports = dependencies.EnumerateAllDependencies(library)
                .Select(lib => Tuple.Create(lib, ExportLibrary(lib, dependencies)));
            LogImports(imports);
            var import = new LibraryExport(
                imports.SelectMany(l => l.Item2.MetadataReferences).ToList(),
                imports.SelectMany(l => l.Item2.SourceReferences).ToList());

            // Compile the project
            var projectReference = compiler.CompileProject(
                project,
                new LibraryKey()
                {
                    Name = library.Identity.Name,
                    Configuration = "Debug",
                    Aspect = null,
                    TargetFramework = new FrameworkName(_targetFramework.DotNetFrameworkName)
                },
                () => import,
                () => new List<ResourceDescriptor>());

            return new LibraryExport(projectReference);
        }

        private Project GetProject(Library library)
        {
            // First, try to load the project straight out of the library (if it provided one)
            var project = library.GetItem<Project>(ProjectLibraryPropertyName);
            if(project == null)
            {
                // Get the package spec (this is required)
                var packageSpec = library.GetItem<PackageSpec>(KnownLibraryProperties.PackageSpec);
                // Parse the project
                project = Project.FromPackageSpec(packageSpec);
                // Stash it on the library for later use
                library[ProjectLibraryPropertyName] = project;
            }
            return project;
        }

        private void LogImports(IEnumerable<Tuple<Library, ILibraryExport>> imports)
        {
            if (Log.IsEnabled(LogLevel.Debug))
            {
                foreach (var import in imports)
                {
                    Log.LogDebug($"    Importing {import.Item1.Identity}");
                    foreach (var reference in Enumerable.Concat<object>(import.Item2.MetadataReferences, import.Item2.SourceReferences).Where(o => o != null))
                    {
                        Log.LogDebug($"      {reference}");
                    }
                }
            }
        }

        private class LibraryKey : ILibraryKey
        {
            public string Aspect { get; set; }
            public string Configuration { get; set; }
            public string Name { get; set; }
            public FrameworkName TargetFramework { get; set; }
        }
    }
}
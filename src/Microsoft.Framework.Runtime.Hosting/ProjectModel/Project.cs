// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Framework.Runtime.Compilation;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using NuGet.Versioning;

namespace Microsoft.Framework.Runtime.ProjectModel
{
    public class Project : ICompilationProject
    {
        public static readonly TypeInformation DefaultRuntimeCompiler = new TypeInformation("Microsoft.Framework.Runtime.Roslyn", "Microsoft.Framework.Runtime.Roslyn.RoslynProjectCompiler");
        private static readonly CompilerOptions _emptyOptions = new CompilerOptions();

        private CompilerOptions _projectCompilationOptions;
        private readonly Dictionary<string, CompilerOptions> _configurationCompilerOptions = new Dictionary<string, CompilerOptions>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<NuGetFramework, CompilerOptions> _frameworkCompilerOptions = new Dictionary<NuGetFramework, CompilerOptions>();
        private Project(PackageSpec packageSpec)
        {
            ProjectDirectory = Path.GetDirectoryName(packageSpec.FilePath);
            Metadata = packageSpec;
            Files = new ProjectFilesCollection(packageSpec.Properties, packageSpec.BaseDirectory, packageSpec.FilePath);

            // Load additional metadata from the project json
            EntryPoint = Metadata.Properties.GetValue<string>("entryPoint");

            var commands = Metadata.Properties["commands"] as JObject;
            if (commands != null)
            {
                foreach (var command in commands)
                {
                    Commands[command.Key] = command.Value.Value<string>();
                }
            }

            // Load Assembly File Version
            var fileVersion = Environment.GetEnvironmentVariable("DNX_ASSEMBLY_FILE_VERSION");
            if (string.IsNullOrWhiteSpace(fileVersion))
            {
                AssemblyFileVersion = Version.Version;
            }
            else
            {
                try
                {
                    var simpleVersion = Version.Version;
                    AssemblyFileVersion = new Version(simpleVersion.Major,
                        simpleVersion.Minor,
                        simpleVersion.Build,
                        int.Parse(fileVersion));
                }
                catch (FormatException ex)
                {
                    throw new FormatException("The assembly file version is invalid: " + fileVersion, ex);
                }
            }

            LoadCompilationData();
        }

        public string ProjectDirectory { get; private set; }
        public string Name { get { return Metadata.Name; } }
        public string ProjectFilePath { get { return Metadata.FilePath; } }
        public NuGetVersion Version { get { return Metadata.Version; } }
        public Version AssemblyFileVersion { get; private set; }
        public IProjectFilesCollection Files { get; private set; }
        public PackageSpec Metadata { get; private set; }
        public string EntryPoint { get; private set; }
        public bool EmbedInteropTypes { get; private set; }
        public IDictionary<string, string> Commands { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public CompilerServices CompilerServices { get; private set; }

        // Temporary while old and new runtime are separate
        string ICompilationProject.Version { get { return Version?.ToString(); } }
        string ICompilationProject.AssemblyFileVersion { get { return AssemblyFileVersion?.ToString(); } }

        public static Project FromPackageSpec(PackageSpec packageSpec)
        {
            return new Project(packageSpec);
        }

        private CompilerServices LoadCompilerServices(JObject compiler)
        {
            var language = compiler.GetValue<string>("name") ?? "C#";
            var compilerAssembly = compiler.GetValue<string>("compilerAssembly");
            var compilerType = compiler.GetValue<string>("compilerType");

            return new CompilerServices(
                language,
                new TypeInformation(compilerAssembly, compilerType));
        }

        public ICompilerOptions GetCompilerOptions(FrameworkName targetFramework, string configuration)
        {
            // Combine the necessary options
            var framework = NuGetFramework.ParseFrameworkName(targetFramework.FullName, DefaultFrameworkNameProvider.Instance);
            return CompilerOptions.Combine(_projectCompilationOptions,
                GetCompilerOptions(configuration, _configurationCompilerOptions),
                GetCompilerOptions(framework, _frameworkCompilerOptions));
        }

        private CompilerOptions GetCompilerOptions<T>(T key, Dictionary<T, CompilerOptions> dictionary) where T: class
        {
            CompilerOptions options;
            if(key != null && dictionary.TryGetValue(key, out options))
            {
                return options;
            }
            return null;
        }

        private CompilerOptions ParseCompilerOptions(JToken topLevelOrConfiguration)
        {
            var rawOptions = topLevelOrConfiguration["compilationOptions"];

            if (rawOptions == null)
            {
                return null;
            }

            return new CompilerOptions()
            {
                Defines = rawOptions.ValueAsArray<string>("define"),
                LanguageVersion = rawOptions.GetValue<string>("languageVersion"),
                AllowUnsafe = rawOptions.GetValue<bool?>("allowUnsafe"),
                Platform = rawOptions.GetValue<string>("platform"),
                WarningsAsErrors = rawOptions.GetValue<bool?>("warningsAsErrors"),
                Optimize = rawOptions.GetValue<bool?>("optimize"),
            };
        }

        private void LoadCompilationData()
        {
            // Load Compilation data
            var compiler = Metadata.Properties["compiler"] as JObject;
            if (compiler != null)
            {
                CompilerServices = LoadCompilerServices(compiler);
            }
            EmbedInteropTypes = Metadata.Properties.GetValue<bool>("embedInteropTypes");

            // Load global options
            _projectCompilationOptions = ParseCompilerOptions(Metadata.Properties) ?? _emptyOptions;

            // Add default configurations
            _configurationCompilerOptions["Debug"] = new CompilerOptions
            {
                Defines = new[] { "DEBUG", "TRACE" },
                Optimize = false
            };

            _configurationCompilerOptions["Release"] = new CompilerOptions
            {
                Defines = new[] { "RELEASE", "TRACE" },
                Optimize = true
            };

            // Load project configurations
            var configurations = Metadata.Properties["configurations"] as JObject;
            if (configurations != null)
            {
                foreach (var configuration in configurations)
                {
                    var compilerOptions = ParseCompilerOptions(configuration.Value);

                    // Only use this as a configuration if it's not a target framework
                    _configurationCompilerOptions[configuration.Key] = compilerOptions;
                }
            }

            // Load framework compilation options
            foreach(var framework in Metadata.TargetFrameworks)
            {
                var compilerOptions = ParseCompilerOptions(framework.Properties);
                _frameworkCompilerOptions[framework.FrameworkName] = compilerOptions;
            }
        }
    }
}
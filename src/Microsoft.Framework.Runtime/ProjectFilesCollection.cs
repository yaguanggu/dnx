// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.Framework.Runtime.Json;

namespace Microsoft.Framework.Runtime
{
    public class ProjectFilesCollection : IProjectFilesCollection
    {
        public static readonly string[] DefaultCompileBuiltInPatterns = new[] { @"**/*.cs" };
        public static readonly string[] DefaultPublishExcludePatterns = new[] { @"obj/**/*.*", @"bin/**/*.*", @"**/.*/**" };
        public static readonly string[] DefaultPreprocessPatterns = new[] { @"compiler/preprocess/**/*.cs" };
        public static readonly string[] DefaultSharedPatterns = new[] { @"compiler/shared/**/*.cs" };
        public static readonly string[] DefaultResourcesBuiltInPatterns = new[] { @"compiler/resources/**/*", "**/*.resx" };
        public static readonly string[] DefaultContentsBuiltInPatterns = new[] { @"**/*" };

        public static readonly string[] DefaultBuiltInExcludePatterns = new[] { "bin/**", "obj/**", "**/*.xproj" };

        private PatternGroup _sharedPatternsGroup;
        private PatternGroup _resourcePatternsGroup;
        private PatternGroup _preprocessPatternsGroup;
        private PatternGroup _compilePatternsGroup;
        private PatternGroup _contentPatternsGroup;
        private IDictionary<string, string> _namedResources;

        private readonly string _projectDirectory;
        private readonly string _projectFilePath;

        private IEnumerable<string> _publishExcludePatterns;
        private JsonObject _rawProject;
        private bool _initialized;

        internal ProjectFilesCollection(JsonObject rawProject,
                                        string projectDirectory,
                                        string projectFilePath,
                                        ICollection<ICompilationMessage> warnings = null)
        {
            _projectDirectory = projectDirectory;
            _projectFilePath = projectFilePath;
            _rawProject = rawProject;

            // If the caller is asking for warnings then process up front, otherwise delay it until first use
            if (warnings != null)
            {
                EnsureInitializePatternGroups(warnings);
            }
        }

        private void EnsureInitializePatternGroups(ICollection<ICompilationMessage> warnings = null)
        {
            if (_initialized)
            {
                return;
            }

            var excludeBuiltIns = PatternsCollectionHelper.GetPatternsCollection(_rawProject, _projectDirectory, _projectFilePath, "excludeBuiltIn", DefaultBuiltInExcludePatterns);
            var excludePatterns = PatternsCollectionHelper.GetPatternsCollection(_rawProject, _projectDirectory, _projectFilePath, "exclude")
                                                          .Concat(excludeBuiltIns);
            var contentBuiltIns = PatternsCollectionHelper.GetPatternsCollection(_rawProject, _projectDirectory, _projectFilePath, "contentBuiltIn", DefaultContentsBuiltInPatterns);
            var compileBuiltIns = PatternsCollectionHelper.GetPatternsCollection(_rawProject, _projectDirectory, _projectFilePath, "compileBuiltIn", DefaultCompileBuiltInPatterns);
            var resourceBuiltIns = PatternsCollectionHelper.GetPatternsCollection(_rawProject, _projectDirectory, _projectFilePath, "resourceBuiltIn", DefaultResourcesBuiltInPatterns);

            // TODO: The legacy names will be retired in the future.
            var legacyPublishExcludePatternName = "bundleExclude";
            var legacyPublishExcludePatternToken = _rawProject.ValueAsJsonObject(legacyPublishExcludePatternName);
            if (legacyPublishExcludePatternToken != null)
            {
                _publishExcludePatterns = PatternsCollectionHelper.GetPatternsCollection(_rawProject, _projectDirectory, _projectFilePath, legacyPublishExcludePatternName, DefaultPublishExcludePatterns);

                warnings?.Add(new FileFormatMessage(
                    string.Format("Property \"{0}\" is deprecated. It is replaced by \"{1}\".", legacyPublishExcludePatternName, "publishExclude"),
                    _projectFilePath,
                    CompilationMessageSeverity.Warning,
                    legacyPublishExcludePatternToken));
            }
            else
            {
                _publishExcludePatterns = PatternsCollectionHelper.GetPatternsCollection(_rawProject, _projectDirectory, _projectFilePath, "publishExclude", DefaultPublishExcludePatterns);
            }

            _sharedPatternsGroup = PatternGroup.Build(_rawProject, _projectDirectory, _projectFilePath, "shared", legacyName: null, warnings: warnings, fallbackIncluding: DefaultSharedPatterns, additionalExcluding: excludePatterns);

            _resourcePatternsGroup = PatternGroup.Build(_rawProject, _projectDirectory, _projectFilePath, "resource", "resources", warnings: warnings, additionalIncluding: resourceBuiltIns, additionalExcluding: excludePatterns);

            _preprocessPatternsGroup = PatternGroup.Build(_rawProject, _projectDirectory, _projectFilePath, "preprocess", legacyName: null, warnings: warnings, fallbackIncluding: DefaultPreprocessPatterns, additionalExcluding: excludePatterns)
                .ExcludeGroup(_sharedPatternsGroup)
                .ExcludeGroup(_resourcePatternsGroup);

            _compilePatternsGroup = PatternGroup.Build(_rawProject, _projectDirectory, _projectFilePath, "compile", "code", warnings: warnings, additionalIncluding: compileBuiltIns, additionalExcluding: excludePatterns)
                .ExcludeGroup(_sharedPatternsGroup)
                .ExcludeGroup(_preprocessPatternsGroup)
                .ExcludeGroup(_resourcePatternsGroup);

            _contentPatternsGroup = PatternGroup.Build(_rawProject, _projectDirectory, _projectFilePath, "content", "files", warnings: warnings, additionalIncluding: contentBuiltIns, additionalExcluding: excludePatterns.Concat(_publishExcludePatterns))
                .ExcludeGroup(_compilePatternsGroup)
                .ExcludeGroup(_preprocessPatternsGroup)
                .ExcludeGroup(_sharedPatternsGroup)
                .ExcludeGroup(_resourcePatternsGroup);

            _namedResources = NamedResourceReader.ReadNamedResources(_rawProject, _projectFilePath);

            _initialized = true;
            _rawProject = null;
        }

        public IEnumerable<string> SourceFiles
        {
            get
            {
                EnsureInitializePatternGroups();
                return _compilePatternsGroup.SearchFiles(_projectDirectory).Distinct();
            }
        }

        public IEnumerable<string> PreprocessSourceFiles
        {
            get
            {
                EnsureInitializePatternGroups();
                return _preprocessPatternsGroup.SearchFiles(_projectDirectory).Distinct();
            }
        }

        public IDictionary<string, string> ResourceFiles
        {
            get
            {
                EnsureInitializePatternGroups();

                var resources = _resourcePatternsGroup
                    .SearchFiles(_projectDirectory)
                    .Distinct()
                    .ToDictionary(res => res, res => (string)null);

                NamedResourceReader.ApplyNamedResources(_namedResources, resources);

                return resources;
            }
        }

        public IEnumerable<string> SharedFiles
        {
            get
            {
                EnsureInitializePatternGroups();
                return _sharedPatternsGroup.SearchFiles(_projectDirectory).Distinct();
            }
        }

        public IEnumerable<string> GetFilesForBundling(bool includeSource, IEnumerable<string> additionalExcludePatterns)
        {
            EnsureInitializePatternGroups();
            var patternGroup = new PatternGroup(ContentPatternsGroup.IncludePatterns,
                                                ContentPatternsGroup.ExcludePatterns.Concat(additionalExcludePatterns),
                                                ContentPatternsGroup.IncludeLiterals);
            if (!includeSource)
            {
                foreach (var excludedGroup in ContentPatternsGroup.ExcludePatternsGroup)
                {
                    patternGroup.ExcludeGroup(excludedGroup);
                }
            }

            return patternGroup.SearchFiles(_projectDirectory);
        }

        internal PatternGroup CompilePatternsGroup { get { return _compilePatternsGroup; } }

        internal PatternGroup SharedPatternsGroup { get { return _sharedPatternsGroup; } }

        internal PatternGroup ResourcePatternsGroup { get { return _resourcePatternsGroup; } }

        internal PatternGroup PreprocessPatternsGroup { get { return _preprocessPatternsGroup; } }

        internal PatternGroup ContentPatternsGroup { get { return _contentPatternsGroup; } }
    }
}

﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Framework.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet;

namespace Microsoft.Framework.PackageManager.SourceControl
{
    internal class SourceCommand
    {
        private const string SourcesFolderName = "sources";

        private const string SourcesEnvironmentVariable = "DNX_SOURCES_DIR";

        private readonly Reports _reports;
        private readonly string _packageId;

        private string _solutionRoot;

        public SourceCommand(string packageId, Reports reports)
        {
            if (string.IsNullOrEmpty(packageId))
            {
                throw new ArgumentNullException(nameof(packageId));
            }
            if (reports == null)
            {
                throw new ArgumentNullException(nameof(reports));
            }

            _packageId = packageId;
            _reports = reports;
        }

        public string SourcesFolder { get; set; }

        public string ProjectFile { get; set; }

        public string PackagesFolder { get; set; }

        public bool Execute()
        {
            var projectFile = ResolveProjectFile();
            if (string.IsNullOrEmpty(projectFile))
            {
                return false;
            }

            _solutionRoot = ProjectResolver.ResolveRootDirectory(projectFile);

            var packagesFolder = ResolvePackagesFolder();
            if (string.IsNullOrEmpty(packagesFolder))
            {
                return false;
            }

            var packageVersion = ResolvePackageVersion(projectFile);
            if (packageVersion == null)
            {
                return false;
            }

            var packageResolver = new DefaultPackagePathResolver(packagesFolder);
            var packageFolder = packageResolver.GetPackageDirectory(_packageId, packageVersion);
            packageFolder = Path.Combine(packagesFolder, packageFolder);

            var snapshotInfo = ReadRepositoryInfo(packageFolder);
            if (snapshotInfo == null)
            {
                return false;
            }

            string repoType;
            if (!snapshotInfo.TryGetValue(Constants.RepoTypeKey, out repoType))
            {
                _reports.WriteError("Repository type information is missing from the repository information file.");
                return false;
            }

            var provider = SourceControlProviderFactory.ResolveProvider(repoType, _reports);
            if (provider == null)
            {
                _reports.WriteError($"Unknown repository type '{provider}'");
                return false;
            }

            var sourcesFolder = ResolveSourcesFolder();
            var sourceFolderName = provider.CreateShortFolderName(snapshotInfo);
            var sourceDestinationFullPath = Path.Combine(sourcesFolder, sourceFolderName);

            if (!Directory.Exists(sourceDestinationFullPath))
            {
                _reports.WriteInformation($"Downloading sources in '{sourceDestinationFullPath}'...");
                if (!provider.GetSources(sourceDestinationFullPath, snapshotInfo))
                {
                    return false;
                }
            }
            else
            {
                _reports.WriteInformation($"Sources already found in '{sourceDestinationFullPath}'");
            }

            var srcFolder = provider.GetSourceFolderPath(snapshotInfo);
            srcFolder = Path.Combine(sourceDestinationFullPath, srcFolder);
            if (!Directory.Exists(srcFolder))
            {
                _reports.WriteError($"The source code folder '{srcFolder}' is missing.");
                return false;
            }

            var globalFile = GlobalSettings.GetGlobalFilePath(_solutionRoot);
            if (!File.Exists(globalFile))
            {
                _reports.WriteError($"The '{GlobalSettings.GlobalFileName}' is missing from '{_solutionRoot}'.");
                return false;
            }

            _reports.Verbose.WriteLine($"Updating {GlobalSettings.GlobalFileName}...");
            ModifyJson(globalFile, jObj =>
                {
                    var projects = jObj["projects"] as JArray;
                    if (projects == null)
                    {
                        projects = new JArray();
                        projects.Add(srcFolder);
                        jObj.Add("projects", projects);
                    }
                    else
                    {
                        if (!projects.Any(t => t.ToString().Equals(srcFolder)))
                        {
                            projects.Add(srcFolder);
                        }
                    }
                });

            return true;
        }

        private string ResolveProjectFile()
        {
            var filePath = ProjectFile ?? Path.GetFullPath(Runtime.Project.ProjectFileName);
            if (!File.Exists(filePath))
            {
                _reports.WriteError($"Cannot find the project file '{filePath}'.");
                return null;
            }

            return filePath;
        }

        private string ResolvePackagesFolder()
        {
            var packagesFolder = PackagesFolder;
            if (string.IsNullOrEmpty(packagesFolder))
            {
                packagesFolder = NuGetDependencyResolver.ResolveRepositoryPath(_solutionRoot);
            }

            if (!Directory.Exists(packagesFolder))
            {
                _reports.WriteError($"Cannot find the packages folder '{packagesFolder}'.");
                return null;
            }

            return packagesFolder;
        }

        private string ResolveSourcesFolder()
        {
            // Command line args have the highest priority ...
            var sourcesFolder = SourcesFolder;
            if (string.IsNullOrEmpty(sourcesFolder))
            {
                // ... then environment variables
                sourcesFolder = Environment.GetEnvironmentVariable(SourcesEnvironmentVariable);
                if (string.IsNullOrEmpty(sourcesFolder))
                {
                    // ... and finally the default path
                    sourcesFolder = Path.Combine(PathUtilities.RuntimeHomeFolder, SourcesFolderName);
                }
            }

            return sourcesFolder;
        }

        private SemanticVersion ResolvePackageVersion(string projectFile)
        {
            var projectFolder = Path.GetDirectoryName(projectFile);
            var projectLockFile = Path.Combine(projectFolder, LockFileFormat.LockFileName);

            if (!File.Exists(projectLockFile))
            {
                _reports.WriteError("The project lock file is missing. Restore the packages to generate the file.");
                return null;
            }

            var lockFileReader = new LockFileFormat();
            var lockFile = lockFileReader.Read(projectLockFile);

            var packageVersion = lockFile
                ?.Libraries
                ?.FirstOrDefault(lib => string.Equals(_packageId, lib.Name, StringComparison.OrdinalIgnoreCase))
                ?.Version;

            if (packageVersion == null)
            {
                _reports.WriteError($"The project is not using the '{_packageId}' package. Sources can be retrieved only for packages used by the project.");
                return null;
            }

            return packageVersion;
        }

        private IDictionary<string, string> ReadRepositoryInfo(string packageFolder)
        {
            var snapshotFilePath = Path.Combine(packageFolder, Constants.SnapshotInfoFileName);
            if (!File.Exists(snapshotFilePath))
            {
                _reports.WriteError($"The repository information file '{snapshotFilePath}' does not exist.");
                return null;
            }

            var snapshotContent = File.ReadAllText(snapshotFilePath);
            var snapshotInfo = JsonConvert.DeserializeObject<Dictionary<string, string>>(snapshotContent);

            return snapshotInfo;
        }

        private static void ModifyJson(string jsonFile, Action<JObject> modifier)
        {
            var jsonObject = JObject.Parse(File.ReadAllText(jsonFile));
            modifier(jsonObject);
            File.WriteAllText(jsonFile, jsonObject.ToString());
        }
    }
}

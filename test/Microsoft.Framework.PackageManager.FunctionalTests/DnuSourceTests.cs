﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Framework.CommonTestUtils;
using Microsoft.Framework.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Framework.PackageManager.FunctionalTests
{
    [Collection(nameof(PackageManagerFunctionalTestCollection))]
    public class DnuSourceTests
    {
        [Theory]
        [MemberData("RuntimeComponents")]
        public void GitBuildGeneratesSourceInformation(string flavor, string os, string architecture)
        {
            const string fakeRepoName = "https://example.com/example.git";

            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                var testProjectFolder = ScafoldEmptyProject(
                    testEnv.RootDir,
                    "TestPackage",
                    _ => @"{
                        ""frameworks"": {
                            ""dnx451"": { }
                        },
                        ""repository"": {
                            ""type"": ""git"",
                            ""url"": """ + fakeRepoName + @"""
                        }
                    }");

                InitGitRepoAndCommitAll(testEnv.RootDir);

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    "restore",
                    arguments: null,
                    workingDir: testEnv.RootDir);
                Assert.Equal(0, exitCode);

                var outputFolder = Path.Combine(testProjectFolder, "bin");
                exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    $"pack --out {outputFolder}",
                    arguments: null,
                    workingDir: testProjectFolder);
                Assert.Equal(0, exitCode);

                var repoInfoFile = Path.Combine(
                    outputFolder,
                    "Debug",
                    SourceControl.Constants.SnapshotInfoFileName);

                Assert.True(File.Exists(repoInfoFile));

                var repoInfo = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(repoInfoFile));

                Assert.Equal("git", repoInfo[SourceControl.Constants.RepoTypeKey]);
                Assert.Equal(fakeRepoName, repoInfo["url"]);
                Assert.False(string.IsNullOrEmpty(repoInfo["commit"]));
                Assert.Equal("src", repoInfo["path"]);
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void GitBuildInNonGitRepoDoesntGenerateSourceInformation(string flavor, string os, string architecture)
        {
            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                var testProjectFolder = ScafoldEmptyProject(
                    testEnv.RootDir,
                    "TestPackage",
                    _ => @"{
                        ""frameworks"": {
                            ""dnx451"": { }
                        },
                        ""repository"": {
                            ""type"": ""git"",
                            ""url"": ""https://example.com/example.git""
                        }
                    }");

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    "restore",
                    arguments: null,
                    workingDir: testEnv.RootDir);
                Assert.Equal(0, exitCode);

                var outputFolder = Path.Combine(testProjectFolder, "bin");
                exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    $"pack --out {outputFolder}",
                    arguments: null,
                    workingDir: testProjectFolder);
                Assert.Equal(0, exitCode);

                var repoInfoFile = Path.Combine(
                    outputFolder,
                    "Debug",
                    SourceControl.Constants.SnapshotInfoFileName);

                Assert.False(File.Exists(repoInfoFile));
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void GitCanInstallPackageWithSourceInformation(string flavor, string os, string architecture)
        {
            using (var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture))
            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                var testPackageFolder = ScafoldEmptyProject(
                    testEnv.RootDir,
                    "TestPackage",
                    _ => @"{
                        ""frameworks"": {
                            ""dnx451"": { }
                        },
                        ""repository"": {
                            ""type"": ""git"",
                            ""url"": """ + testEnv.RootDir.Replace('\\', '/') + @"""
                        }
                    }");

                InitGitRepoAndCommitAll(testEnv.RootDir);

                var exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    "restore",
                    arguments: null,
                    workingDir: testPackageFolder);
                Assert.Equal(0, exitCode);

                var outputFolder = Path.Combine(testPackageFolder, "bin");
                exitCode = DnuTestUtils.ExecDnu(
                    runtimeHomeDir,
                    $"pack --out {outputFolder}",
                    arguments: null,
                    workingDir: testPackageFolder);
                Assert.Equal(0, exitCode);

                outputFolder = Path.Combine(outputFolder, "Debug").Replace('\\', '/');

                using (var installEnv = new DnuTestEnvironment(runtimeHomeDir))
                {
                    var consumerProjectFolder = ScafoldEmptyProject(
                       installEnv.RootDir,
                       "Client",
                       _ => @"{
                            ""dependencies"": {
                                ""TestPackage"": ""1.0.0""
                            },
                            ""frameworks"": {
                                ""dnx451"": {
                                }
                            },
                        }");

                    var packagesDestinationFolder = Path.Combine(runtimeHomeDir, "packages");
                    exitCode = DnuTestUtils.ExecDnu(
                        runtimeHomeDir,
                        $"restore --fallbacksource \"{ outputFolder }\" --packages \"{ packagesDestinationFolder }\"",
                        arguments: null,
                        workingDir: consumerProjectFolder);
                    Assert.Equal(0, exitCode);

                    var sourcesDestinationFolder = Path.Combine(runtimeHomeDir, "sources");
                    exitCode = DnuTestUtils.ExecDnu(
                        runtimeHomeDir,
                        $"source TestPackage --packages \"{ packagesDestinationFolder }\"  --sources \"{ sourcesDestinationFolder }\"",
                        arguments: null,
                        workingDir: consumerProjectFolder);
                    Assert.Equal(0, exitCode);

                    var installProjectGlobalFile = Path.Combine(installEnv.RootDir, GlobalSettings.GlobalFileName);
                    var globalFile = JsonConvert.DeserializeObject(File.ReadAllText(installProjectGlobalFile)) as JObject;
                    var projects = globalFile["projects"] as JArray;

                    Assert.Equal(2, projects.Count);
                    var sourceFolder = projects
                        .First(prj => prj.ToString().StartsWith(sourcesDestinationFolder, StringComparison.Ordinal))
                        .ToString();

                    Assert.True(Directory.Exists(sourceFolder));
                }
            }
        }

        public static IEnumerable<object[]> RuntimeComponents
        {
            get
            {
                return TestUtils.GetRuntimeComponentsCombinations();
            }
        }

        private static void InitGitRepoAndCommitAll(string folder)
        {
            string stdOut, stdErr;

            int exitCode = TestUtils.Exec(
                   program: "git",
                   commandLine: "init",
                   workingDir: folder,
                   stdOut: out stdOut,
                   stdErr: out stdErr);
            Assert.Equal(0, exitCode);

            exitCode = TestUtils.Exec(
               program: "git",
               commandLine: "add .",
               workingDir: folder,
               stdOut: out stdOut,
               stdErr: out stdErr);
            Assert.Equal(0, exitCode);

            exitCode = TestUtils.Exec(
               program: "git",
               commandLine: "commit -m \"Test commit\"",
               workingDir: folder,
               stdOut: out stdOut,
               stdErr: out stdErr);
            Assert.Equal(0, exitCode);
        }

        private static string ScafoldEmptyProject(string destination, string projectName, Func<string, string> projectJsonFileContent)
        {
            File.WriteAllText(Path.Combine(destination, GlobalSettings.GlobalFileName),
               @"{
                    ""projects"": [""src""]
                }");

            var testProjectFolder = Path.Combine(
                               destination,
                               "src",
                               projectName)
                           .Replace('\\', '/');
            Directory.CreateDirectory(testProjectFolder);

            File.WriteAllText(
                $"{testProjectFolder}/project.json",
                projectJsonFileContent(testProjectFolder));

            return testProjectFolder;
        }
    }
}

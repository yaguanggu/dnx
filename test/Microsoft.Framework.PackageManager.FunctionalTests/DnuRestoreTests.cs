using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Framework.FunctionalTestUtils;
using Microsoft.Framework.Runtime;
using Xunit;

namespace Microsoft.Framework.PackageManager
{
    public class DnuRestoreTests
    {
        public static IEnumerable<object[]> RuntimeComponents
        {
            get
            {
                return TestUtils.GetRuntimeComponentsCombinations();
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuRestore_NoArgs(string flavor, string os, string architecture)
        {
            string expected = @"Restoring packages for {0}\project.json
Writing lock file {0}\project.lock.json
Restore complete,";
            string stdOut;
            string stdError;
            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);
            int exitCode;

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                File.WriteAllText(string.Format("{0}/project.json", testEnv.RootDir), "{}");
                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", "", out stdOut, out stdError, environment: null, workingDir: testEnv.RootDir);

                Assert.Empty(stdError);
                Assert.StartsWith(string.Format(expected, testEnv.RootDir), stdOut);
                Assert.Equal(0, exitCode);
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuRestore_IgnoreBadSource(string flavor, string os, string architecture)
        {
            string expected1 = @"Restoring packages for {0}\project.json
  GET http://b/FindPackagesById()?Id='PackageName'.
Warning: FindPackagesById: PackageName
  Response status code does not indicate success: 401 (Unauthorized).
  GET http://b/FindPackagesById()?Id='PackageName'.
Warning: FindPackagesById: PackageName
  Response status code does not indicate success: 401 (Unauthorized).
  GET http://b/FindPackagesById()?Id='PackageName'.
Failed to retrieve information from remote source 'http://b/'
Unable to locate PackageName >= 1.0.0-*
Writing lock file {0}\project.lock.json
Restore complete,";
            string expected2 = @"ms elapsed
Errors in {0}\project.json
    Unable to locate PackageName >= 1.0.0-*
";
            string stdOut;
            string stdError;
            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);
            int exitCode;

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                File.WriteAllText(string.Format("{0}/project.json", testEnv.RootDir), @"{ ""dependencies"": { ""PackageName"": ""1.0.0-*""} }");
                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", "-s http://b --ignore-failed-sources", out stdOut, out stdError, environment: null, workingDir: testEnv.RootDir);

                Assert.Empty(stdError);
                Assert.StartsWith(string.Format(expected1, testEnv.RootDir), stdOut);
                Assert.EndsWith(string.Format(expected2, testEnv.RootDir), stdOut);
                Assert.Equal(1, exitCode);
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuRestore_Lock(string flavor, string os, string architecture)
        {
            string expectedLock = @"{
  ""locked"": true,
  ""version"": -9998,
  ""projectFileDependencyGroups"": {
    """": [
      ""PackageName >= 1.0.0-*""
    ]
  },
  ""libraries"": {}
}";
            string stdOut;
            string stdError;
            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);
            int exitCode;

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                File.WriteAllText(string.Format("{0}/project.json", testEnv.RootDir), @"{ ""dependencies"": { ""PackageName"": ""1.0.0-*""} }");
                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", "--lock", out stdOut, out stdError, environment: null, workingDir: testEnv.RootDir);

                Assert.Empty(stdError);
                Assert.Equal(1, exitCode);

                string actual = File.ReadAllText(string.Format("{0}/project.lock.json", testEnv.RootDir));
                Assert.Equal(expectedLock, actual);
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuRestore_LockedFile(string flavor, string os, string architecture)
        {
            string expected = @"Restoring packages for {0}\project.json
Following lock file {0}\project.lock.json
Restore complete,";

            string stdOut;
            string stdError;
            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);
            int exitCode;

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                File.WriteAllText(string.Format("{0}/project.json", testEnv.RootDir), @"{ ""dependencies"": { ""PackageName"": ""1.0.0-*""} }");
                DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", "--lock", out stdOut, out stdError, environment: null, workingDir: testEnv.RootDir);
                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", "", out stdOut, out stdError, environment: null, workingDir: testEnv.RootDir);

                Assert.Empty(stdError);
                Assert.StartsWith(string.Format(expected, testEnv.RootDir), stdOut);
                Assert.Equal(0, exitCode);
            }
        }
    }
}
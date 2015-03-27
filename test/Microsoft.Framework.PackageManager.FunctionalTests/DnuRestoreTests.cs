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

            File.WriteAllText(string.Format("{0}/project.json", runtimeHomeDir), "{}");
            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", "", out stdOut, out stdError, environment: null, workingDir: runtimeHomeDir);

                Assert.Empty(stdError);
                Assert.StartsWith(string.Format(expected, runtimeHomeDir), stdOut);
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

            File.WriteAllText(string.Format("{0}/project.json", runtimeHomeDir), @"{ ""dependencies"": { ""PackageName"": ""1.0.0-*""} }");
            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", "-s http://b --ignore-failed-sources", out stdOut, out stdError, environment: null, workingDir: runtimeHomeDir);

                Assert.Empty(stdError);
                Assert.StartsWith(string.Format(expected1, runtimeHomeDir), stdOut);
                Assert.EndsWith(string.Format(expected2, runtimeHomeDir), stdOut);
                Assert.Equal(1, exitCode);
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

            File.WriteAllText(string.Format("{0}/project.json", runtimeHomeDir), @"{ ""dependencies"": { ""PackageName"": ""1.0.0-*""} }");
            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", "--lock", out stdOut, out stdError, environment: null, workingDir: runtimeHomeDir);
                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "restore", "", out stdOut, out stdError, environment: null, workingDir: runtimeHomeDir);

                Assert.Empty(stdError);
                Assert.StartsWith(string.Format(expected, runtimeHomeDir), stdOut);
                Assert.Equal(0, exitCode);
            }
        }
    }
}
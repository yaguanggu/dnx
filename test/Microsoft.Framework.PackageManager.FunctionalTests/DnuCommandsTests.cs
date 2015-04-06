using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Framework.FunctionalTestUtils;
using Microsoft.Framework.Runtime;
using Xunit;

namespace Microsoft.Framework.PackageManager
{
    public class DnuCommandsTests
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
        public void DnuCommands_NoArgs(string flavor, string os, string architecture)
        {
            string DnuCommandsText =
            @"
Usage: dnu commands [options] [command]

Options:
  -?|-h|--help  Show help information

Commands:
  install    Installs application commands
  help       Show help information
  uninstall  Uninstalls application commands

Use ""commands help [command]"" for more information about a command.
";
            string stdOut;
            string stdError;
            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);
            int exitCode;

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "commands", "", out stdOut, out stdError);

                Assert.Empty(stdError);
                Assert.Equal(DnuCommandsText, stdOut);
                Assert.Equal(2, exitCode);
            }
        }
    }
}
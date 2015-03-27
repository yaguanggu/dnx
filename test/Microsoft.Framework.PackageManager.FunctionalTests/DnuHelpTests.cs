using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Framework.FunctionalTestUtils;
using Microsoft.Framework.Runtime;
using Xunit;

namespace Microsoft.Framework.PackageManager
{
    public class DnuHelpTests
    {
        private static readonly string DnuHelpText =
            @"Microsoft .NET Development Utility v{0}

Usage: dnu [options] [command]

Options:
  -v|--verbose  Show verbose output
  -?|-h|--help  Show help information
  --version     Show version information

Commands:
  restore   Restore packages
  help      Show help information
  publish   Publish application for deployment
  pack      Build NuGet packages for the project in given directory
  build     Produce assemblies for the project in given directory
  install   Install the given dependency
  packages  Commands related to managing local and remote packages folders
  list      Print the dependencies of a given project
  commands  Commands related to managing application commands (add, remove)
  wrap      Wrap a csproj into a project.json, which can be referenced by project.json files

Use ""dnu help [command]"" for more information about a command.
";

        private static readonly string DnuCommandsText =
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

        public static IEnumerable<object[]> RuntimeComponents
        {
            get
            {
                return TestUtils.GetRuntimeComponentsCombinations();
            }
        }

        private static void DnuHelpHelper(string flavor, string os, string architecture, string command, string arguments, string expected, int expectedExit)
        {
            string stdOut;
            string stdError;
            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);
            var runtimeRoot = Directory.EnumerateDirectories(Path.Combine(runtimeHomeDir, "runtimes"), Constants.RuntimeNamePrefix + "*").First();
            runtimeRoot = Path.GetFileName(runtimeRoot);
            var version = runtimeRoot.Substring(runtimeRoot.IndexOf('.') + 1);
            int exitCode;

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, command, arguments, out stdOut, out stdError);

                Assert.Empty(stdError);
                Assert.Equal(string.Format(expected, version), stdOut);
                Assert.Equal(expectedExit, exitCode);
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void Dnu_NoCommand(string flavor, string os, string architecture)
        {
            DnuHelpHelper(flavor, os, architecture, "", "", DnuHelpText, 2);
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void Dnu_HelpVariations(string flavor, string os, string architecture)
        {
            string stdOut;
            string stdError;
            var runtimeHomeDir = TestUtils.GetRuntimeHomeDir(flavor, os, architecture);
            var runtimeRoot = Directory.EnumerateDirectories(Path.Combine(runtimeHomeDir, "runtimes"), Constants.RuntimeNamePrefix + "*").First();
            runtimeRoot = Path.GetFileName(runtimeRoot);
            var version = runtimeRoot.Substring(runtimeRoot.IndexOf('.') + 1);
            int exitCode;

            using (var testEnv = new DnuTestEnvironment(runtimeHomeDir))
            {
                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "-?", "", out stdOut, out stdError);

                Assert.Empty(stdError);
                Assert.Equal(string.Format(DnuHelpText, version), stdOut);
                Assert.Equal(0, exitCode);

                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "-h", "", out stdOut, out stdError);

                Assert.Empty(stdError);
                Assert.Equal(string.Format(DnuHelpText, version), stdOut);
                Assert.Equal(0, exitCode);

                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "--help", "", out stdOut, out stdError);

                Assert.Empty(stdError);
                Assert.Equal(string.Format(DnuHelpText, version), stdOut);
                Assert.Equal(0, exitCode);

                exitCode = DnuTestUtils.ExecDnu(runtimeHomeDir, "help", "", out stdOut, out stdError);

                Assert.Empty(stdError);
                Assert.Equal(string.Format(DnuHelpText, version), stdOut);
                Assert.Equal(0, exitCode);
            }
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuHelp_Restore(string flavor, string os, string architecture)
        {
            string expected = @"Microsoft .NET Development Utility v{0}

Usage: dnu restore [arguments] [options]

Arguments:
  [root]  Root of all projects to restore. It can be a directory, a project.json, or a global.json.

Options:
  -s|--source <FEED>          A list of packages sources to use for this command
  -f|--fallbacksource <FEED>  A list of packages sources to use as a fallback
  -p|--proxy <ADDRESS>        The HTTP proxy to use when retrieving packages
  --no-cache                  Do not use local cache
  --packages                  Path to restore packages
  --ignore-failed-sources     Ignore failed remote sources if there are local packages meeting version requirements
  --quiet                     Do not show output such as HTTP request/cache information
  --lock                      Creates dependencies file with locked property set to true. Overwrites file if it exists.
  --unlock                    Creates dependencies file with locked property set to false. Overwrites file if it exists.
  --parallel                  Restores in parallel when more than one project.json is discovered.
  -?|-h|--help                Show help information
";
            DnuHelpHelper(flavor, os, architecture, "help restore", "", expected, 0);
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuHelp_Help(string flavor, string os, string architecture)
        {
            string expected = @"Microsoft .NET Development Utility v{0}

Usage: dnu help [arguments]

Arguments:
  [command]  Command that help information explains
";
            DnuHelpHelper(flavor, os, architecture, "help help", "", expected, 0);
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuHelp_Publish(string flavor, string os, string architecture)
        {
            string expected = @"Microsoft .NET Development Utility v{0}

Usage: dnu publish [arguments] [options]

Arguments:
  [project]  Path to project, default is current directory

Options:
  -o|--out <PATH>                  Where does it go
  --configuration <CONFIGURATION>  The configuration to use for deployment (Debug|Release|{{Custom}})
  --no-source                      Compiles the source files into NuGet packages
  --runtime <RUNTIME>              Name or full path of the runtime folder to include, or ""active"" for current runtime on PATH
  --native                         Build and include native images. User must provide targeted CoreCLR runtime versions along with this option.
  --wwwroot <NAME>                 Name of public folder in the project directory
  --wwwroot-out <NAME>             Name of public folder in the output, can be used only when the '--wwwroot' option or 'webroot' in project.json is specified
  --quiet                          Do not show output such as source/destination of published files
  -?|-h|--help                     Show help information
";
            DnuHelpHelper(flavor, os, architecture, "help publish", "", expected, 0);
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuHelp_Pack(string flavor, string os, string architecture)
        {
            string expected = @"Microsoft .NET Development Utility v{0}

Usage: dnu pack [arguments] [options]

Arguments:
  [project]  Project to pack, default is current directory

Options:
  --framework <TARGET_FRAMEWORK>   A list of target frameworks to build.
  --configuration <CONFIGURATION>  A list of configurations to build.
  --out <OUTPUT_DIR>               Output directory
  --dependencies                   Copy dependencies
  --quiet                          Do not show output such as source/destination of nupkgs
  -?|-h|--help                     Show help information
";
            DnuHelpHelper(flavor, os, architecture, "help pack", "", expected, 0);
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuHelp_Build(string flavor, string os, string architecture)
        {
            string expected = @"Microsoft .NET Development Utility v{0}

Usage: dnu build [arguments] [options]

Arguments:
  [project]  Project to build, default is current directory

Options:
  --framework <TARGET_FRAMEWORK>   A list of target frameworks to build.
  --configuration <CONFIGURATION>  A list of configurations to build.
  --out <OUTPUT_DIR>               Output directory
  --quiet                          Do not show output such as dependencies in use
  -?|-h|--help                     Show help information
";
            DnuHelpHelper(flavor, os, architecture, "help build", "", expected, 0);
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuHelp_Install(string flavor, string os, string architecture)
        {
            string expected = @"Microsoft .NET Development Utility v{0}

Usage: dnu install [arguments] [options]

Arguments:
  [name]     Name of the dependency to add
  [version]  Version of the dependency to add, default is the latest version.
  [project]  Path to project, default is current directory

Options:
  -s|--source <FEED>          A list of packages sources to use for this command
  -f|--fallbacksource <FEED>  A list of packages sources to use as a fallback
  -p|--proxy <ADDRESS>        The HTTP proxy to use when retrieving packages
  --no-cache                  Do not use local cache
  --packages                  Path to restore packages
  --ignore-failed-sources     Ignore failed remote sources if there are local packages meeting version requirements
  --quiet                     Do not show output such as HTTP request/cache information
  -?|-h|--help                Show help information
";
            DnuHelpHelper(flavor, os, architecture, "help install", "", expected, 0);
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuHelp_Packages(string flavor, string os, string architecture)
        {
            string expected = @"Microsoft .NET Development Utility v{0}

Usage: dnu packages [options] [command]

Options:
  -?|-h|--help  Show help information

Commands:
  add   Add a NuGet package to the specified packages folder
  help  Show help information
  push  Incremental copy of files from local packages to remote location
  pull  Incremental copy of files from remote location to local packages

Use ""dnu help [command]"" for more information about a command.
";
            DnuHelpHelper(flavor, os, architecture, "help packages", "", expected, 0);
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuHelp_List(string flavor, string os, string architecture)
        {
            string expected = @"Microsoft .NET Development Utility v{0}

Usage: dnu list [arguments] [options]

Arguments:
  [project]  Path to project, default is current directory

Options:
  -a|--assemblies                 Show the assembly files that are depended on by given project
  --framework <TARGET_FRAMEWORK>  Show dependencies for only the given frameworks
  --runtime <PATH>                The folder containing all available framework assemblies
  --hide-dependents               Hide the immediate dependents of libraries referenced in the project
  --filter <PATTERN>              Filter the libraries referenced by the project base on their names. The matching pattern supports * and ?
  -?|-h|--help                    Show help information
";
            DnuHelpHelper(flavor, os, architecture, "help list", "", expected, 0);
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuHelp_Commands(string flavor, string os, string architecture)
        {
            string expected = @"Microsoft .NET Development Utility v{0}

Usage: dnu commands [options] [command]

Options:
  -?|-h|--help  Show help information

Commands:
  install    Installs application commands
  help       Show help information
  uninstall  Uninstalls application commands

Use ""dnu help [command]"" for more information about a command.
";
            DnuHelpHelper(flavor, os, architecture, "help commands", "", expected, 0);
        }

        [Theory]
        [MemberData("RuntimeComponents")]
        public void DnuHelp_Wrap(string flavor, string os, string architecture)
        {
            string expected = @"Microsoft .NET Development Utility v{0}

Usage: dnu wrap [arguments] [options]

Arguments:
  [path]  Path to csproj to be wrapped

Options:
  --configuration <CONFIGURATION>  Configuration of wrapped project, default is 'debug'
  --msbuild <PATH>                 Path to MSBuild, default is '%ProgramFiles%\MSBuild\14.0\Bin\MSBuild.exe'
  -i|--in-place                    Generate or update project.json files in project directories of csprojs
  -f|--framework                   Target framework of assembly to be wrapped
  -?|-h|--help                     Show help information
";
            DnuHelpHelper(flavor, os, architecture, "help wrap", "", expected, 0);
        }
    }
}
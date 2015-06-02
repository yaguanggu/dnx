﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Text;
using Microsoft.Framework.Runtime;

namespace Microsoft.Framework.PackageManager.SourceControl
{
    internal static class ProcessUtilities
    {
        public static bool ExecutableExists(string executableName)
        {
            string whereApp;
            if (PlatformHelper.IsWindows)
            {
                whereApp = "where";
            }
            else
            {
                whereApp = "whereis";
            }

            return RunApp(whereApp, executableName);
        }

        public static bool RunApp(string executable, string arguments = null, string workingDirectory = null)
        {
            string stdOut;
            string stdErr;

            return RunApp(executable, arguments, workingDirectory, out stdOut, out stdErr);
        }

        public static bool RunApp(string executable, string arguments, string workingDirectory, out string stdOut, out string stdErr)
        {
            StringBuilder output = new StringBuilder();
            StringBuilder errors = new StringBuilder();

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo()
                {                    
                    FileName = executable,
                    Arguments = arguments,

                    RedirectStandardOutput = true,
                    RedirectStandardError = true,

                    UseShellExecute = false,
#if DNX451
                    WindowStyle = ProcessWindowStyle.Hidden,
#else
                    CreateNoWindow = true
#endif
                };
                
                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    startInfo.WorkingDirectory = workingDirectory;
                }

                Process proc = new Process();
                proc.StartInfo = startInfo;
                proc.Start();

                proc.EnableRaisingEvents = true;

                proc.OutputDataReceived += (sender, e) =>
                {
                    output.Append(e.Data);
                };
                proc.ErrorDataReceived += (sender, e) =>
                {
                    errors.Append(e.Data);
                };

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                proc.WaitForExit();

                return proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                errors.AppendLine(ex.ToString());
                return false;
            }
            finally
            {
                stdOut = output.ToString();
                stdErr = errors.ToString();
            }
        }
    }
}

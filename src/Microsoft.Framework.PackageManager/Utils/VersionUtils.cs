// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Framework.Runtime;
using Microsoft.Framework.Runtime.Infrastructure;

namespace Microsoft.Framework.PackageManager
{
    public static class VersionUtils
    {
        private static readonly Lazy<string> _activeRuntime = new Lazy<string>(GetActiveRuntimeName);

        public static string ActiveRuntimeFullName
        {
            get
            {
                return _activeRuntime.Value;
            }
        }

        private static string GetActiveRuntimeName()
        {
            string pathVariable = Environment.GetEnvironmentVariable("PATH");

            if (!string.IsNullOrEmpty(pathVariable))
            {
                var isWindows = ((IRuntimeEnvironment)CallContextServiceLocator.Locator.ServiceProvider.GetService(typeof(IRuntimeEnvironment))).OperatingSystem == "Windows";
                string dnuExecutable = isWindows ? "dnu.cmd" : "dnu";

                foreach (string folder in pathVariable.Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string dnuPath = Path.Combine(folder, dnuExecutable);
                    if (File.Exists(dnuPath) &&
                        string.Equals("bin", Directory.GetParent(dnuPath).Name))
                    {
                        // We found it
                        return Directory.GetParent(folder).Name;
                    }
                }
            }

            return null;
        }
    }
}
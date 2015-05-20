// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

// TODO: delete this after System.Environment.GetFolderPath() is available on CoreCLR
#if DNXCORE50
namespace Microsoft.Framework.PackageManager.Internal
{
    internal static class Environment
    {
        public static string NewLine
        {
            get
            {
                return System.Environment.NewLine;
            }
        }

        public static string GetEnvironmentVariable(string variable)
        {
            return System.Environment.GetEnvironmentVariable(variable);
        }

        public static string ExpandEnvironmentVariables(string name)
        {
            return System.Environment.ExpandEnvironmentVariables(name);
        }

        public static string GetFolderPath(SpecialFolder folder)
        {
            switch (folder)
            {
                case SpecialFolder.ProgramFilesX86:
                    return GetEnvironmentVariable("PROGRAMFILES(X86)");
                case SpecialFolder.ProgramFiles:
                    return GetEnvironmentVariable("PROGRAMFILES");
                case SpecialFolder.UserProfile:
                    var userProfileFolder = GetEnvironmentVariable("USERPROFILE");
                    if (string.IsNullOrEmpty(userProfileFolder))
                    {
                        userProfileFolder = GetEnvironmentVariable("HOME");
                    }
                    return userProfileFolder;
                case SpecialFolder.ApplicationData:
                    return GetEnvironmentVariable("APPDATA");
                    //return GetEnvironmentVariable("PROGRAMDATA"); ???
                default:
                    return null;
            }
        }

        public enum SpecialFolder
        {
            LocalApplicationData,
            CommonApplicationData,
            ApplicationData,
            ProgramFilesX86,
            ProgramFiles,
            UserProfile
        }
    }
}
#endif
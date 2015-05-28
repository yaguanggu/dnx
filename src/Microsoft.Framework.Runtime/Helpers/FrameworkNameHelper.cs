// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.Versioning;
using Microsoft.Framework.Runtime.Common.Impl;
using NuGet;

namespace Microsoft.Framework.Runtime.Helpers
{
    public static class FrameworkNameHelper
    {
        public static FrameworkName ParseFrameworkName(string targetFramework)
        {
            // Fast path for runtime code path, these 3 short names are the runnable tfms
            // We fall back to regular parsing in other scenarios (build/dth)
            if (targetFramework == FrameworkNames.ShortNames.Dnx451)
            {
                return new FrameworkName(FrameworkNames.LongNames.Dnx, new Version(4, 5, 1));
            }
            else if (targetFramework == FrameworkNames.ShortNames.Dnx46)
            {
                return new FrameworkName(FrameworkNames.LongNames.Dnx, new Version(4, 6));
            }
            else if (targetFramework == FrameworkNames.ShortNames.DnxCore50)
            {
                return new FrameworkName(FrameworkNames.LongNames.DnxCore, new Version(5, 0));
            }

            if (targetFramework.Contains("+"))
            {
                var portableProfile = NetPortableProfile.Parse(targetFramework);

                if (portableProfile != null &&
                    portableProfile.FrameworkName.Profile != targetFramework)
                {
                    return portableProfile.FrameworkName;
                }

                return VersionUtility.UnsupportedFrameworkName;
            }

            if (targetFramework.IndexOf(',') != -1)
            {
                // Assume it's a framework name if it contains commas
                // e.g. .NETPortable,Version=v4.5,Profile=Profile78
                return new FrameworkName(targetFramework);
            }

            return VersionUtility.ParseFrameworkName(targetFramework);
        }

        public static string MakeDefaultTargetFrameworkDefine(Tuple<string, FrameworkName> frameworkDefinition)
        {
            var shortName = frameworkDefinition.Item1;
            var targetFramework = frameworkDefinition.Item2;

            if (VersionUtility.IsPortableFramework(targetFramework))
            {
                return null;
            }

            return shortName.ToUpperInvariant();
        }
    }
}
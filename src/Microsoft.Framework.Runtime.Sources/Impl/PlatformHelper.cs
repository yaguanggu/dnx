// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Framework.Runtime.Infrastructure;

namespace Microsoft.Framework.Runtime
{
    internal static class PlatformHelper
    {
        private static Lazy<string> _osName = new Lazy<string>(() =>
                    ((IRuntimeEnvironment)CallContextServiceLocator
                    .Locator
                    .ServiceProvider
                    .GetService(typeof(IRuntimeEnvironment))).OperatingSystem);

        public static bool IsMono
        {
            get
            {
                return _osName.Value == "Darwin";
            }
        }

        public static bool IsWindows
        {
            get
            {
                return _osName.Value == "Windows";
            }
        }
    }
}
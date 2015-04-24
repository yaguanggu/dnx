// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Framework.Runtime
{
    internal static class RuntimeEnvironmentHelper
    {
        private static Lazy<bool> _isMono = new Lazy<bool>(() => ((IRuntimeEnvironment)Services.Value.GetService(typeof(IRuntimeEnvironment))).RuntimeType == "Mono");
        private static Lazy<bool> _isWindows = new Lazy<bool>(() => ((IRuntimeEnvironment)Services.Value.GetService(typeof(IRuntimeEnvironment))).OperatingSystem == "Windows");

        private static Lazy<IServiceProvider> Services = new Lazy<IServiceProvider>(() => Infrastructure.CallContextServiceLocator.Locator.ServiceProvider);

        public static bool IsWindows
        {
            get
            {
                return _isWindows.Value;
            }
        }

        public static bool IsMono
        {
            get
            {
                return _isMono.Value;
            }
        }
    }
}

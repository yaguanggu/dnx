// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Framework.Runtime;
#if DNXCORE50
using Environment = Microsoft.Framework.PackageManager.Internal.Environment;
#endif

namespace NuGet
{
    public class CommandLineMachineWideSettings : IMachineWideSettings
    {
        Lazy<IEnumerable<Settings>> _settings;

        public CommandLineMachineWideSettings()
        {
            string baseDirectory;
            if (PlatformHelper.IsWindows)
            {
                baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            }
            else
            {
                // Only super users have write access to common app data folder on *nix,
                // so we use roaming local app data folder instead
                baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            _settings = new Lazy<IEnumerable<NuGet.Settings>>(
                () => NuGet.Settings.LoadMachineWideSettings(
                    new PhysicalFileSystem(baseDirectory)));
        }

        public IEnumerable<Settings> Settings
        {
            get
            {
                return _settings.Value;
            }
        }
    }
}

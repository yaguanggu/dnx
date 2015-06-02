// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Framework.Runtime.Json;

namespace Microsoft.Framework.Runtime
{
    public class GlobalSettings
    {
        public const string GlobalFileName = "global.json";

        public IList<string> ProjectSearchPaths { get; private set; }
        public string PackagesPath { get; private set; }
        public string FilePath { get; private set; }

        public static bool TryGetGlobalSettings(string path, out GlobalSettings globalSettings)
        {
            globalSettings = null;
            string globalJsonPath = null;

            if (Path.GetFileName(path) == GlobalFileName)
            {
                globalJsonPath = path;
                path = Path.GetDirectoryName(path);
            }
            else if (!HasGlobalFile(path))
            {
                return false;
            }
            else
            {
                globalJsonPath = Path.Combine(path, GlobalFileName);
            }

            globalSettings = new GlobalSettings();

            try
            {
                using (var fs = File.OpenRead(globalJsonPath))
                {
                    var reader = new StreamReader(fs);
                    var jobject = JsonDeserializer.Deserialize(reader) as JsonObject;

                    if (jobject == null)
                    {
                        throw new InvalidOperationException("The JSON file can't be deserialized to a JSON object.");
                    }

                    var projectSearchPaths = jobject.ValueAsStringArray("projects") ??
                                             jobject.ValueAsStringArray("sources") ??
                                             new string[] { };

                    globalSettings.ProjectSearchPaths = new List<string>(projectSearchPaths);
                    globalSettings.PackagesPath = jobject.ValueAsString("packages");
                    globalSettings.FilePath = globalJsonPath;
                }
            }
            catch (Exception ex)
            {
                throw FileFormatException.Create(ex, globalJsonPath);
            }

            return true;
        }

        public static string GetGlobalFilePath(string folder)
        {
            return Path.Combine(folder, GlobalFileName);
        }

        public static bool HasGlobalFile(string path)
        {
            string projectPath = Path.Combine(path, GlobalFileName);

            return File.Exists(projectPath);
        }

    }
}

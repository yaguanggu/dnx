// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

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
            globalSettings.ProjectSearchPaths = new List<string>();
            globalSettings.FilePath = globalJsonPath;

            // Global.json is tiny
            var json = File.ReadAllText(globalJsonPath);
            var reader = new JsonTextReader(new StringReader(json));
            
            ReadObject(reader, globalSettings);

            return true;
        }

        private static void ReadList(string propertyName, JsonTextReader reader, GlobalSettings globalSettings)
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.Comment:
                        break;
                    case JsonToken.String:
                        if (string.Equals(propertyName, "sources") ||
                            string.Equals(propertyName, "projects"))
                        {
                            globalSettings.ProjectSearchPaths.Add(reader.Value.ToString());
                        }
                        break;
                    default:
                        if (string.Equals(propertyName, "sources") ||
                            string.Equals(propertyName, "projects"))
                        {
                            throw new FileFormatException(string.Format("'{0}' only supports string values.", propertyName))
                            {
                                Path = globalSettings.FilePath,
                                Line = reader.LineNumber,
                                Column = reader.LinePosition
                            };
                        }
                        else
                        {
                            ReadValue(reader, globalSettings);
                        }
                        break;
                    case JsonToken.EndArray:
                        return;
                }
            }

            throw new FileFormatException(string.Format("Unexpected end when reading '{0}'", propertyName))
            {
                Path = globalSettings.FilePath,
                Line = reader.LineNumber,
                Column = reader.LinePosition
            };
        }

        private static void ReadObject(JsonTextReader reader, GlobalSettings globalSettings)
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.PropertyName:
                        string propertyName = reader.Value.ToString();

                        if (!reader.Read())
                        {
                            throw new FileFormatException(string.Format("Unexpected end when reading '{0}'", propertyName))
                            {
                                Path = globalSettings.FilePath,
                                Line = reader.LineNumber,
                                Column = reader.LinePosition
                            };
                        }

                        if (string.Equals(propertyName, "packages"))
                        {
                            if (reader.TokenType != JsonToken.String)
                            {
                                throw new FileFormatException(string.Format("'{0}' only supports string values.", propertyName))
                                {
                                    Path = globalSettings.FilePath,
                                    Line = reader.LineNumber,
                                    Column = reader.LinePosition
                                };
                            }

                            globalSettings.PackagesPath = reader.Value.ToString();
                        }
                        else if (string.Equals(propertyName, "sources") ||
                                 string.Equals(propertyName, "projects"))
                        {
                            ReadList(propertyName, reader, globalSettings);
                        }
                        else
                        {
                            ReadValue(reader, globalSettings);
                        }

                        break;
                    case JsonToken.Comment:
                        break;
                    case JsonToken.EndObject:
                        return;
                }
            }

            throw new FileFormatException(string.Format("Unexpected end when reading 'global.json'"))
            {
                Path = globalSettings.FilePath,
                Line = reader.LineNumber,
                Column = reader.LinePosition
            };
        }

        private static void ReadValue(JsonTextReader reader, GlobalSettings globalSettings)
        {
            while (reader.TokenType == JsonToken.Comment)
            {
                if (!reader.Read())
                {
                    throw new FileFormatException(string.Format("Unexpected end when reading 'global.json'"))
                    {
                        Path = globalSettings.FilePath,
                        Line = reader.LineNumber,
                        Column = reader.LinePosition
                    };
                }

            }

            switch (reader.TokenType)
            {
                case JsonToken.StartObject:
                    ReadObject(reader, globalSettings);
                    break;
                case JsonToken.StartArray:
                    ReadList(propertyName: null, reader: reader, globalSettings: globalSettings);
                    break;
                default:
                    break;
            }

        }

        public static bool HasGlobalFile(string path)
        {
            string projectPath = Path.Combine(path, GlobalFileName);

            return File.Exists(projectPath);
        }

    }
}

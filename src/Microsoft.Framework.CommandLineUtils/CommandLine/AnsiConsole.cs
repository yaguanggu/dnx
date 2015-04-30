// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;

namespace Microsoft.Framework.Runtime.Common.CommandLine
{
    internal class AnsiConsole
    {
        private AnsiConsole(TextWriter writer)
        {
            Writer = writer;
#if !DNXCORE50
            OriginalForegroundColor = Console.ForegroundColor;
#endif
        }

        private int _boldRecursion;

        public static AnsiConsole Output = new AnsiConsole(Console.Out);

        public static AnsiConsole Error = new AnsiConsole(Console.Error);

        public TextWriter Writer { get; }

        public ConsoleColor OriginalForegroundColor { get; }
        
        private void SetColor(ConsoleColor color)
        {
            Console.ForegroundColor = (ConsoleColor)(((int)Console.ForegroundColor & 0x08) | ((int)color & 0x07));
        }

        private void SetBold(bool bold)
        {
            _boldRecursion += bold ? 1 : -1;
            if (_boldRecursion > 1 || (_boldRecursion == 1 && !bold))
            {
                return;
            }

            Console.ForegroundColor = (ConsoleColor)((int)Console.ForegroundColor ^ 0x08);
        }

        public void WriteLine(string message)
        {
            Console.WriteLine(message);
return;
            
        }
    }
}

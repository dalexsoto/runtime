// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace SwiftStressGenerator
{
    /// <summary>
    /// Resolves the real mangled entry-point names for the generated Swift
    /// functions by compiling the generated source with swiftc, listing the
    /// exported symbols with nm, and matching them back to source functions via
    /// xcrun swift-demangle. This guarantees correct entry points without
    /// reimplementing Swift name mangling.
    /// </summary>
    internal static class ManglingResolver
    {
        public static void Resolve(SuiteModel model, string swiftFilePath)
        {
            string tempDir = Directory.CreateTempSubdirectory("SwiftStressGenerator").FullName;
            try
            {
                string dylibPath = Path.Combine(tempDir, $"lib{model.Name}.dylib");
                var swiftcArgs = new List<string> { "swiftc", "-module-name", model.Name };
                if (model.LibraryEvolution)
                    swiftcArgs.Add("-enable-library-evolution");
                swiftcArgs.AddRange(["-emit-library", swiftFilePath, "-o", dylibPath]);
                Run("xcrun", swiftcArgs);

                // Global defined symbols only; Swift symbols start with _$s.
                string nmOutput = Run("nm", ["-gU", dylibPath]);
                var symbols = new List<string>();
                foreach (string line in nmOutput.Split('\n'))
                {
                    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 && parts[^1].StartsWith("_$s", StringComparison.Ordinal))
                        symbols.Add(parts[^1]);
                }
                if (symbols.Count == 0)
                    throw new InvalidOperationException($"no Swift symbols found in {dylibPath}");

                // swift-demangle emits one demangled line per input line, in order.
                string demangledOutput = Run("xcrun", ["swift-demangle", "-compact"], string.Join("\n", symbols) + "\n");
                string[] demangled = demangledOutput.Split('\n');

                foreach (FunctionModel fn in model.Functions)
                {
                    string prefix = $"{model.Name}.{fn.SwiftFuncName}(";
                    string? match = null;
                    for (int i = 0; i < symbols.Count && i < demangled.Length; i++)
                    {
                        if (!demangled[i].StartsWith(prefix, StringComparison.Ordinal))
                            continue;
                        if (match != null && match != symbols[i])
                            throw new InvalidOperationException($"ambiguous symbols for {fn.SwiftFuncName}: {match} and {symbols[i]}");
                        match = symbols[i];
                    }
                    fn.MangledName = match?.TrimStart('_')
                        ?? throw new InvalidOperationException($"no exported symbol found for {fn.SwiftFuncName}");
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        private static string Run(string fileName, IEnumerable<string> arguments, string? stdin = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                UseShellExecute = false,
            };
            foreach (string arg in arguments)
                psi.ArgumentList.Add(arg);

            using Process process = Process.Start(psi)
                ?? throw new InvalidOperationException($"failed to start {fileName}");
            if (stdin != null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"'{fileName} {string.Join(' ', arguments)}' failed with exit code {process.ExitCode}:\n{stderr}");
            }
            return stdout;
        }
    }
}

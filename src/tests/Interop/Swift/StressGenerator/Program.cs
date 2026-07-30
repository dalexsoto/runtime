// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SwiftStressGenerator
{
    internal static class Program
    {
        private const string Usage = """
            Deterministic Swift ABI stress-test generator.

            Usage:
              dotnet run --project src/tests/Interop/Swift/StressGenerator -- \
                  --seed <int> --count <N> --suite <args|returns|callbacks|calli|invalid> \
                  --out <dir> [--library-evolution] [--swiftself-fraction <0..1>]

            The suite name (Swift module, C# class, project name) is derived from the
            basename of --out. A fixed seed produces byte-identical output; the mangled
            entry points are extracted from a probe compilation of the generated Swift
            source (swiftc + nm + swift-demangle), so swiftc must be available via xcrun.
            """;

        private static int Main(string[] args)
        {
            int? seed = null;
            int? count = null;
            string? suiteArg = null;
            string? outDir = null;
            bool libraryEvolution = false;
            bool differential = false;
            double swiftSelfFraction = SuiteBuilder.DefaultSwiftSelfFraction;

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--seed":
                            seed = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                            break;
                        case "--count":
                            count = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                            break;
                        case "--suite":
                            suiteArg = Next(args, ref i);
                            break;
                        case "--out":
                            outDir = Next(args, ref i);
                            break;
                        case "--differential":
                            differential = true;
                            break;
                        case "--library-evolution":
                            libraryEvolution = true;
                            break;
                        case "--swiftself-fraction":
                            swiftSelfFraction = double.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                            break;
                        case "--help" or "-h":
                            Console.WriteLine(Usage);
                            return 0;
                        default:
                            throw new ArgumentException($"unknown option '{args[i]}'");
                    }
                }

                if (seed is null || count is null || suiteArg is null || outDir is null)
                    throw new ArgumentException("--seed, --count, --suite and --out are required");
                if (count <= 0)
                    throw new ArgumentException("--count must be positive");
                if (swiftSelfFraction is < 0 or > 1)
                    throw new ArgumentException("--swiftself-fraction must be between 0 and 1");

                SuiteKind kind = suiteArg switch
                {
                    "args" => SuiteKind.Args,
                    "returns" => SuiteKind.Returns,
                    "callbacks" => SuiteKind.Callbacks,
                    "calli" => SuiteKind.Calli,
                    "invalid" => SuiteKind.Invalid,
                    _ => throw new ArgumentException($"unknown suite '{suiteArg}'"),
                };

                string fullOutDir = Path.GetFullPath(outDir);
                string name = SanitizeName(Path.GetFileName(Path.TrimEndingDirectorySeparator(fullOutDir)));

                SuiteModel model = SuiteBuilder.Build(kind, name, seed.Value, count.Value, libraryEvolution, swiftSelfFraction);

                Directory.CreateDirectory(fullOutDir);
                string swiftPath = Path.Combine(fullOutDir, $"{name}.swift");
                WriteFile(swiftPath, SwiftEmitter.Emit(model));

                // Probe-compile the Swift source to extract the real mangled
                // entry points before the C# side can be emitted.
                ManglingResolver.Resolve(model, swiftPath);

                if (differential)
                {
                    return DifferentialRunner.Run(model, swiftPath, fullOutDir);
                }

                string csPath = Path.Combine(fullOutDir, $"{name}.cs");
                string cmakePath = Path.Combine(fullOutDir, "CMakeLists.txt");
                string csprojPath = Path.Combine(fullOutDir, $"{name}.csproj");
                WriteFile(csPath, CSharpEmitter.Emit(model));
                WriteFile(cmakePath, ProjectEmitter.EmitCMakeLists(model));
                WriteFile(csprojPath, ProjectEmitter.EmitCsproj());

                Console.WriteLine($"Generated suite '{name}' ({model.SuiteArg}, seed {seed.Value.ToString(CultureInfo.InvariantCulture)}, {model.Functions.Count.ToString(CultureInfo.InvariantCulture)} functions{(libraryEvolution ? ", library evolution" : "")}):");
                Console.WriteLine($"  {swiftPath}");
                Console.WriteLine($"  {csPath}");
                Console.WriteLine($"  {cmakePath}");
                Console.WriteLine($"  {csprojPath}");
                return 0;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Console.Error.WriteLine();
                Console.Error.WriteLine(Usage);
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                return 1;
            }
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
                throw new ArgumentException($"missing value for '{args[i]}'");
            return args[++i];
        }

        private static string SanitizeName(string raw)
        {
            var sb = new StringBuilder();
            foreach (char c in raw)
            {
                if (char.IsAsciiLetterOrDigit(c) || c == '_')
                    sb.Append(c);
            }
            if (sb.Length == 0 || char.IsAsciiDigit(sb[0]))
                sb.Insert(0, "Swift");
            return sb.ToString();
        }

        private static void WriteFile(string path, string contents)
        {
            // UTF-8 without BOM, LF only (the emitters never produce '\r').
            File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}

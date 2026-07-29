// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SwiftStressGenerator
{
    /// <summary>
    /// Compiler differential mode: compares the physical parameter list the
    /// Swift compiler actually emits (swiftc -emit-ir, swiftcc definitions)
    /// against the normative lowering simulation for every generated
    /// function. IR parameter types map 1:1 to lowered elements, so any
    /// divergence between the documented algorithm and the compiler shows up
    /// as a type-list mismatch.
    /// </summary>
    internal static class DifferentialRunner
    {
        public static int Run(SuiteModel model, string swiftFilePath, string outDir)
        {
            string irPath = Path.Combine(outDir, $"{model.Name}.ll");
            var swiftcArgs = new List<string> { "swiftc", "-module-name", model.Name };
            if (model.LibraryEvolution)
                swiftcArgs.Add("-enable-library-evolution");
            swiftcArgs.AddRange(["-emit-ir", swiftFilePath, "-o", irPath]);
            RunProcess("xcrun", swiftcArgs);

            string irText = File.ReadAllText(irPath);
            Dictionary<string, ParsedSignature> actual = ParseSwiftccDefinitions(irText);

            int failures = 0;
            foreach (FunctionModel func in model.Functions)
            {
                if (func.MangledName == null || func.SelfStruct != null)
                    continue; // self parameters carry the swiftself attribute; out of scope here

                if (!actual.TryGetValue(func.MangledName, out ParsedSignature? signature))
                {
                    Console.WriteLine($"FAIL {func.SwiftFuncName}: no swiftcc definition for {func.MangledName}");
                    failures++;
                    continue;
                }

                List<string> actualParams = signature.ParameterTypes;
                if (func.Kind == FunctionKind.CallbackNormal)
                {
                    // Reverse-direction differential: the closure invocation
                    // inside the fixture carries the lowered callback
                    // argument list a managed UnmanagedCallersOnly
                    // implementation must match (plus swiftself context).
                    if (!ValidateCallbackCallSite(func, irText))
                        failures++;
                    continue;
                }

                if (func.Kind == FunctionKind.Returns && func.ReturnTree != null)
                {
                    // Return-lowering differential: direct returns appear as
                    // the scalar or literal-struct IR return type; over-cap
                    // returns become void with an sret pointer parameter.
                    if (!ValidateReturn(func, signature))
                        failures++;
                    continue;
                }

                List<string> expected = func.Params.SelectMany(p => ExpectedIrTypes(p.Type)).ToList();
                if (!expected.SequenceEqual(actualParams))
                {
                    Console.WriteLine($"FAIL {func.SwiftFuncName}:");
                    Console.WriteLine($"  expected ({expected.Count}): {string.Join(", ", expected)}");
                    Console.WriteLine($"  actual   ({actualParams.Count}): {string.Join(", ", actualParams)}");
                    failures++;
                }
            }

            int compared = model.Functions.Count(f => f.MangledName != null && f.SelfStruct == null);
            Console.WriteLine($"Differential: {compared - failures}/{compared} functions match swiftc IR lowering.");
            return failures == 0 ? 0 : 1;
        }

        private static bool ValidateCallbackCallSite(FunctionModel func, string irText)
        {
            // Locate the fixture function's body and the indirect swiftcc
            // call that carries the swiftself context: its argument types
            // are the reverse lowering under test.
            int defineIndex = irText.IndexOf("@\"" + func.MangledName + "\"(", StringComparison.Ordinal);
            if (defineIndex < 0)
            {
                Console.WriteLine($"FAIL {func.SwiftFuncName}: no definition for {func.MangledName}");
                return false;
            }

            int bodyEnd = irText.IndexOf("\n}", defineIndex, StringComparison.Ordinal);
            string body = irText[defineIndex..(bodyEnd < 0 ? irText.Length : bodyEnd)];

            Match? call = null;
            foreach (Match candidate in Regex.Matches(body, @"call swiftcc [^(]+\((?<args>.*)\)"))
            {
                if (candidate.Groups["args"].Value.Contains("swiftself", StringComparison.Ordinal))
                {
                    call = candidate; // constructor calls precede the closure call
                    break;
                }
            }

            if (call == null)
            {
                Console.WriteLine($"FAIL {func.SwiftFuncName}: no swiftself closure call site found");
                return false;
            }

            var actualArgs = new List<string>();
            foreach (string arg in SplitTopLevel(call.Groups["args"].Value))
            {
                string trimmed = arg.Trim();
                if (trimmed.Length == 0 || trimmed.Contains("swiftself", StringComparison.Ordinal))
                    continue; // the context is not part of the lowered list
                actualArgs.Add(trimmed.Split(' ')[0]);
            }

            List<string> expected = func.Params.SelectMany(p => ExpectedIrTypes(p.Type)).ToList();
            if (!expected.SequenceEqual(actualArgs))
            {
                Console.WriteLine($"FAIL {func.SwiftFuncName} (callback call site):");
                Console.WriteLine($"  expected ({expected.Count}): {string.Join(", ", expected)}");
                Console.WriteLine($"  actual   ({actualArgs.Count}): {string.Join(", ", actualArgs)}");
                return false;
            }

            return true;
        }

        private static bool ValidateReturn(FunctionModel func, ParsedSignature signature)
        {
            var decl = (StructDecl)func.ReturnTree!.Type;
            LoweringResult lowering = SwiftLoweringSimulator.Lower(decl);

            if (lowering.ByReference)
            {
                bool ok = signature.ReturnType == "void" && signature.FirstParameterHasSret;
                if (!ok)
                {
                    Console.WriteLine($"FAIL {func.SwiftFuncName}: expected sret return, " +
                        $"got ret '{signature.ReturnType}' sret={signature.FirstParameterHasSret}");
                }
                return ok;
            }

            string expected = lowering.IrElements.Count == 1
                ? lowering.IrElements[0]
                : "{ " + string.Join(", ", lowering.IrElements) + " }";
            if (signature.ReturnType != expected)
            {
                Console.WriteLine($"FAIL {func.SwiftFuncName}: expected return '{expected}', got '{signature.ReturnType}'");
                return false;
            }

            return true;
        }

        private static IEnumerable<string> ExpectedIrTypes(TypeRef type)
        {
            if (type is PrimitiveTypeRef prim)
            {
                yield return Primitives.Size(prim.Kind) switch
                {
                    1 => "i8",
                    2 => "i16",
                    4 => prim.Kind == PrimitiveKind.Float ? "float" : "i32",
                    _ => prim.Kind == PrimitiveKind.Double ? "double" : "i64",
                };
                yield break;
            }

            var decl = (StructDecl)type;
            LoweringResult lowering = SwiftLoweringSimulator.Lower(decl);
            if (lowering.ByReference)
            {
                yield return "ptr";
                yield break;
            }

            foreach (string element in lowering.IrElements)
                yield return element;
        }

        private sealed record ParsedSignature(string ReturnType, List<string> ParameterTypes, bool FirstParameterHasSret);

        private static Dictionary<string, ParsedSignature> ParseSwiftccDefinitions(string ir)
        {
            var result = new Dictionary<string, ParsedSignature>();
            var define = new Regex(
                "^define\\s+(?:[a-z_]+\\s+)*?swiftcc\\s+(?<ret>.+?)\\s+@\"?(?<name>[^\"(]+)\"?\\((?<params>.*)\\)",
                RegexOptions.Multiline);

            foreach (Match m in define.Matches(ir))
            {
                var paramTypes = new List<string>();
                bool firstHasSret = false;
                bool first = true;
                foreach (string param in SplitTopLevel(m.Groups["params"].Value))
                {
                    string trimmed = param.Trim();
                    if (trimmed.Length == 0)
                        continue;
                    if (first)
                    {
                        firstHasSret = trimmed.Contains("sret", StringComparison.Ordinal);
                        first = false;
                    }
                    paramTypes.Add(trimmed.Split(' ')[0]);
                }

                result[m.Groups["name"].Value] = new ParsedSignature(
                    m.Groups["ret"].Value.Trim(), paramTypes, firstHasSret);
            }

            return result;
        }

        /// <summary>Split on commas that are not inside parentheses/braces (attribute payloads).</summary>
        private static IEnumerable<string> SplitTopLevel(string text)
        {
            int depth = 0;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c is '(' or '{' or '[' or '<')
                    depth++;
                else if (c is ')' or '}' or ']' or '>')
                    depth--;
                else if (c == ',' && depth == 0)
                {
                    yield return text[start..i];
                    start = i + 1;
                }
            }

            if (start < text.Length)
                yield return text[start..];
        }

        private static void RunProcess(string fileName, IEnumerable<string> arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string arg in arguments)
                psi.ArgumentList.Add(arg);

            using Process process = Process.Start(psi)
                ?? throw new InvalidOperationException($"failed to start {fileName}");
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"{fileName} failed: {stderr}");
        }
    }
}

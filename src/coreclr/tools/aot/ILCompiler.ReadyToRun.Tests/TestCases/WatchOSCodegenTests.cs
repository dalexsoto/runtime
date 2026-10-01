// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ILCompiler.ReadyToRun.Tests.TestCasesRunner;
using Xunit;
using Xunit.Abstractions;

namespace ILCompiler.ReadyToRun.Tests.TestCases;

public class WatchOSCodegenTests(ITestOutputHelper output)
{
    [ConditionalTheory(typeof(TestPaths), nameof(TestPaths.IsNotWasmTarget))]
    [InlineData("watchos")]
    [InlineData("watchossimulator")]
    [InlineData("ios")]
    public void SignalFreeCodegenIsSelectedOnlyForWatchOS(string targetOS)
    {
        string directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "WatchCodegen.dll");
            var compiler = new R2RTestCaseCompiler([.. Directory.GetFiles(TestPaths.LibrariesDir, "*.dll")]);
            compiler.CompileAssembly(
                "WatchCodegen",
                [R2RTestCaseCompiler.ReadEmbeddedSource("WatchOS/SignalFree.cs")],
                input);

            List<string> arguments =
            [
                input,
                "--targetos:" + targetOS,
                "--targetarch:arm64",
                "--out:" + Path.Combine(directory, "WatchCodegen.r2r.dll"),
                "--reference:" + Path.Combine(TestPaths.LibrariesDir, "*.dll"),
                "--optimize",
                "--parallelism:1",
                "--codegenopt:JitDisasm=WatchCodegen:*",
            ];
            R2RCompilationResult result = new R2RDriver(output).Compile(arguments);
            Assert.True(result.Success, result.StandardOutput + result.StandardError);

            string disassembly = result.StandardOutput + result.StandardError;
            if (targetOS == "ios")
            {
                Assert.DoesNotContain("CORINFO_HELP_POLL_GC", disassembly);
                Assert.DoesNotContain("CORINFO_HELP_THROWNULLREF", MethodBody(disassembly, "ReadField"));
                return;
            }

            string[] accesses =
            [
                "ReadField", "WriteField", "ReadLargeField", "ArrayLength", "ArrayAddress",
                "InterfaceCall", "GenericVirtualCall", "BindVirtualMethod", "InvokeDelegate",
                "AtomicAccess", "VectorLoad", "CopyStruct",
            ];
            foreach (string method in accesses)
            {
                string body = MethodBody(disassembly, method);
                Assert.True(
                    body.Contains("CORINFO_HELP_THROWNULLREF", StringComparison.Ordinal),
                    $"Expected a software null throw in {method}:\n{body}");
                Assert.DoesNotMatch(@"\bldr\s+[wx]zr\s*,", body);
            }

            Assert.Contains("CORINFO_HELP_POLL_GC", MethodBody(disassembly, "EntryOnly"));
            foreach (string method in new[] { "SelfLoop", "ConditionalLoop", "LeaveLoop", "CatchEntry" })
            {
                string body = MethodBody(disassembly, method);
                Assert.True(
                    Regex.Matches(body, "CORINFO_HELP_POLL_GC").Count >= 2,
                    $"Expected entry and loop/handler GC polls in {method}:\n{body}");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string MethodBody(string disassembly, string method)
    {
        Match match = Regex.Match(
            disassembly,
            @"(?ms)^; Assembly listing for method WatchCodegen:" + Regex.Escape(method) +
            @"\(.*?(?=^; Assembly listing for method |\z)");
        Assert.True(match.Success, $"No disassembly found for {method}:\n{disassembly}");
        return match.Value;
    }
}

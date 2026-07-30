// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.DotNet.RemoteExecutor;
using Xunit;

namespace System.Runtime.CompilerServices.Tests
{
    public static class RuntimeFeatureTests
    {
        [Fact]
        public static void PortablePdb()
        {
            Assert.True(RuntimeFeature.IsSupported("PortablePdb"));
        }

        [Fact]
        [ActiveIssue("https://github.com/dotnet/runtime/issues/124344", typeof(PlatformDetection), nameof(PlatformDetection.IsAppleMobile), nameof(PlatformDetection.IsCoreCLR))]
        public static void DynamicCode()
        {
            Assert.Equal(RuntimeFeature.IsDynamicCodeSupported, RuntimeFeature.IsSupported("IsDynamicCodeSupported"));
            Assert.Equal(RuntimeFeature.IsDynamicCodeCompiled, RuntimeFeature.IsSupported("IsDynamicCodeCompiled"));

            if (RuntimeFeature.IsDynamicCodeCompiled)
            {
                Assert.True(RuntimeFeature.IsDynamicCodeSupported);
            }
        }

        [Fact]
        [SkipOnMono("IsDynamicCodeCompiled returns false in cases where mono doesn't support these features")]
        [ActiveIssue("https://github.com/dotnet/runtime/issues/124344", typeof(PlatformDetection), nameof(PlatformDetection.IsAppleMobile), nameof(PlatformDetection.IsCoreCLR))]
        // The CoreCLR interpreter is not a JIT, so RuntimeFeature.IsDynamicCodeCompiled is false.
        [ActiveIssue("https://github.com/dotnet/runtime/issues/123011", typeof(PlatformDetection), nameof(PlatformDetection.IsCoreClrInterpreter))]
        public static void DynamicCode_Jit()
        {
            if (PlatformDetection.IsNativeAot)
            {
                Assert.False(RuntimeFeature.IsDynamicCodeSupported);
                Assert.False(RuntimeFeature.IsDynamicCodeCompiled);
            }
            else if (PlatformDetection.IsAppleMobile)
            {
                Assert.True(RuntimeFeature.IsDynamicCodeSupported);
                Assert.False(RuntimeFeature.IsDynamicCodeCompiled);
            }
            else
            {
                Assert.True(RuntimeFeature.IsDynamicCodeSupported);
                Assert.True(RuntimeFeature.IsDynamicCodeCompiled);
            }
        }

        [Fact]
        [PlatformSpecific(TestPlatforms.Browser)]
        public static void DynamicCode_Browser()
        {
            Assert.True(RuntimeFeature.IsDynamicCodeSupported);
            Assert.False(RuntimeFeature.IsDynamicCodeCompiled);
        }

        public static IEnumerable<object[]> GetStaticFeatureNames()
        {
            foreach (var field in typeof(RuntimeFeature).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral)
                    continue;

                // The Swift interop capability profiles are platform-conditional;
                // they are covered by SwiftInteropProfiles below.
                string value = (string)field.GetRawConstantValue();
                if (value.StartsWith("SwiftInterop.", StringComparison.Ordinal))
                    continue;

                yield return new object[] { value };
            }
        }

        [Theory]
        [MemberData(nameof(GetStaticFeatureNames))]
        public static void StaticDataMatchesDynamicProbing(string probedValue)
        {
            Assert.True(RuntimeFeature.IsSupported(probedValue));
        }

        [Fact]
        public static void SwiftInteropProfiles()
        {
            // The profiles are reported by CoreCLR-family runtimes on Apple
            // ARM64 targets and by no one else; Mono never reports them.
            bool expected = PlatformDetection.IsApplePlatform
                && PlatformDetection.IsArm64Process
                && !PlatformDetection.IsMonoRuntime;

            Assert.Equal(expected, RuntimeFeature.IsSupported(RuntimeFeature.SwiftInteropSync1));
            Assert.Equal(expected, RuntimeFeature.IsSupported(RuntimeFeature.SwiftInteropValues1));
            Assert.Equal(expected, RuntimeFeature.IsSupported(RuntimeFeature.SwiftInteropGenerics1));
            Assert.Equal(expected, RuntimeFeature.IsSupported(RuntimeFeature.SwiftInteropProxies1));
            Assert.Equal(expected, RuntimeFeature.IsSupported(RuntimeFeature.SwiftInteropAsyncThunk1));

            // SwiftInterop.AsyncDirect1 is reserved: no constant exists for it
            // and no runtime may report it before its contract is specified.
            Assert.False(RuntimeFeature.IsSupported("SwiftInterop.AsyncDirect1"));
        }

        [ConditionalTheory(typeof(RemoteExecutor), nameof(RemoteExecutor.IsSupported))]
        [InlineData(true)]
        [InlineData(false)]
        public static void DynamicCode_ContextSwitch(bool isDynamicCodeSupported)
        {
            RemoteInvokeOptions options = new RemoteInvokeOptions();
            options.RuntimeConfigurationOptions.Add("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", isDynamicCodeSupported.ToString());

            // IsDynamicCodeCompiled on Mono interpreter always returns false
            bool isDynamicCodeCompiled = PlatformDetection.IsMonoInterpreter ? false : isDynamicCodeSupported;

            using RemoteInvokeHandle remoteHandle = RemoteExecutor.Invoke(static (isDynamicCodeSupportedString, isDynamicCodeCompiledString) =>
            {
                bool isDynamicCodeSupported = bool.Parse(isDynamicCodeSupportedString);
                Assert.Equal(isDynamicCodeSupported, RuntimeFeature.IsDynamicCodeSupported);

                bool isDynamicCodeCompiled = bool.Parse(isDynamicCodeCompiledString);
                Assert.Equal(isDynamicCodeCompiled, RuntimeFeature.IsDynamicCodeCompiled);
            }, isDynamicCodeSupported.ToString(), isDynamicCodeCompiled.ToString(), options);
        }
    }
}

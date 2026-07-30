// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Internal.IL;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;
using Internal.TypeSystem.Interop;
using Xunit;

namespace ILCompiler.Compiler.Tests
{
    // Validation parity between the CoreCLR VM and the managed AOT type
    // system for Swift signatures: the marshalling-requirement decision
    // (which is the fail-closed gate for non-blittable Swift signatures)
    // must match the VM's MarshalingRequired/IsValidForGenericMarshalling
    // table, including the Swift hardware-vector allowance. The structural
    // special-argument rules (duplicate SwiftSelf, SwiftSelf<T> placement,
    // SwiftError shape) are enforced by the same RyuJIT code under both
    // runtimes and are runtime-tested by SwiftInvalidCallConv.
    public class SwiftValidationParityTests
    {
        private static (EcmaModule Module, MetadataType Type) LoadAssets()
        {
            var target = new TargetDetails(TargetArchitecture.ARM64, TargetOS.OSX, TargetAbi.NativeAot);
            var context = new CompilerTypeSystemContext(target, SharedGenericsMode.CanonicalReferenceTypes, DelegateFeature.All);

            context.InputFilePaths = new Dictionary<string, string> {
                { "Test.CoreLib", @"Test.CoreLib.dll" },
                { "ILCompiler.Compiler.Tests.Assets", @"ILCompiler.Compiler.Tests.Assets.dll" },
                };
            context.ReferenceFilePaths = new Dictionary<string, string>();

            context.SetSystemModule(context.GetModuleForSimpleName("Test.CoreLib"));
            var module = (EcmaModule)context.GetModuleForSimpleName("ILCompiler.Compiler.Tests.Assets");
            var type = module.GetType("ILCompiler.Compiler.Tests.Assets.SwiftValidationParity"u8, "Signatures"u8);
            return (module, type);
        }

        public static IEnumerable<object[]> Cases()
        {
            // method name, marshalling required for a Swift signature,
            // marshalling required for a default signature (both mirroring
            // the VM's decisions for the same shapes).
            yield return new object[] { "PrimitivesOnly", false, false };
            yield return new object[] { "TakesBlittableStruct", false, false };
            yield return new object[] { "TakesPointer", false, false };
            yield return new object[] { "TakesObject", true, true };
            yield return new object[] { "TakesBool", true, true };
            // The Swift hardware-vector allowance: Vector64/128 are valid
            // Swift lowerings, while the generic-marshalling exclusion
            // rejects them for ordinary P/Invokes.
            yield return new object[] { "TakesVector64", false, true };
            yield return new object[] { "TakesVector128", false, true };
            // Vector256 has no Swift lowering: fails closed in both worlds.
            yield return new object[] { "TakesVector256", true, true };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void MarshallingRequirementMatchesTheVmTable(string methodName, bool swiftRequires, bool defaultRequires)
        {
            (EcmaModule module, MetadataType type) = LoadAssets();
            MethodDesc method = type.GetMethod(System.Text.Encoding.UTF8.GetBytes(methodName), null);
            Assert.NotNull(method);

            Assert.Equal(swiftRequires,
                MarshalHelpers.IsMarshallingRequired(method.Signature, module, isSwiftSignature: true));
            Assert.Equal(defaultRequires,
                MarshalHelpers.IsMarshallingRequired(method.Signature, module, isSwiftSignature: false));
        }
    }
}

# Swift ABI stress-test generator

A deterministic generator for Swift interop ABI stress suites, fulfilling the
[roadmap](../../../../../docs/design/interop/swift/roadmap.md) Phase 1 items
"Check in a deterministic stress-test generator" and "Generate argument,
return, callback, and invalid-signature suites from one seedable model".

From a single seeded model it emits a matched Swift/C# test pair in the same
style as the hand-maintained suites in this directory (`SwiftAbiStress`,
`SwiftRetAbiStress`, `SwiftCallbackAbiStress`, `SwiftSelfContext`,
`SwiftInvalidCallConv`): a `<Name>.swift` library source, a `<Name>.cs` xunit
test source, a `CMakeLists.txt`, and a `<Name>.csproj`.

This is a developer tool, not a test project. It is excluded from test
discovery by the empty `Directory.Build.props`/`Directory.Build.targets` in
this directory (which detach it from the test build infrastructure) and by the
`DisabledProjects` entry in `src/tests/Common/dirs.proj`.

## Usage

```sh
./dotnet.sh run --project src/tests/Interop/Swift/StressGenerator -- \
    --seed <int> --count <N> --suite <args|returns|callbacks|calli|invalid> \
    --out <dir> [--library-evolution] [--swiftself-fraction <0..1>]
```

- `--seed` seeds the model. The same seed, count, suite, and options produce
  byte-identical output.
- `--count` is the number of generated test functions.
- `--suite` selects what is generated:
  - `args`: forward P/Invokes with 1-10 parameters mixing primitives
    (`Int8`...`UInt64`, `Int`/`UInt`, `Float`, `Double`) and generated
    `@frozen` structs (up to 3 nesting levels, 1-8 fields). The Swift side
    combines every received scalar leaf with an FNV-1a hash (the same hasher
    the existing suites use); the C# side asserts the exact hash, which the
    generator computes by simulating the hash at generation time.
  - `returns`: Swift functions returning generated frozen structs built from
    deterministic constants; C# asserts each field.
  - `callbacks`: reverse P/Invokes mirroring `SwiftCallbackAbiStress` (Swift
    calls a managed `UnmanagedCallersOnly[CallConvSwift]` function pointer;
    exceptions from argument asserts propagate through an
    `ExceptionDispatchInfo` slot addressed by the `SwiftSelf` context). A
    configurable fraction of callbacks (`--swiftself-fraction`, default 0.25)
    instead takes `SwiftSelf<T>` as the last parameter, mirroring the
    `SwiftSelfContext` reverse tests, alternating between an enregistered `T`
    (at most 4 lowered elements, passed as a trailing ordinary argument) and a
    by-reference `T` (more than 4 lowered elements, passed through the closure
    context register); these callbacks hash arguments plus self leaves and the
    test asserts the generator-computed hash.
  - `calli`: the same shapes as `args`, but invoked through
    `delegate* unmanaged[Swift]` function pointers obtained via
    `NativeLibrary.GetExport` (the `SwiftSelfContext` `TestDirectCalli*`
    pattern) instead of P/Invoke declarations.
  - `invalid`: signatures that must throw `InvalidProgramException`
    (duplicate `SwiftSelf`, duplicate `SwiftError`, `SwiftError` by value,
    `SwiftSelf<T>` not last, `SwiftSelf` + `SwiftSelf<T>`), with seeded filler
    arguments. Forward P/Invokes cover all five shapes; reverse
    `UnmanagedCallersOnly` variants (triggered via
    `RuntimeHelpers.PrepareMethod` and skipped under the CoreCLR interpreter)
    cover the shapes `SwiftInvalidCallConv` proves are validated when the
    native entry stub is compiled.
- `--out` is the output directory. Its basename becomes the suite name: the
  Swift module name, the C# class name, and the project name. Because the
  module name participates in Swift name mangling, regenerating into a
  directory with a different basename changes every entry point.
- `--library-evolution` passes `-enable-library-evolution` both in the
  generated `CMakeLists.txt` and in the generator's own probe compilation.
  Mangled names do not change, but the ABI of non-`@frozen` types would; all
  generated structs are `@frozen`, so this exercises frozen-type lowering
  under resilience.

## Determinism guarantee

A fixed set of options produces byte-identical files, run to run and machine
to machine:

- randomness comes from a splitmix64 generator seeded only by `--seed` and the
  suite kind (not `System.Random`, whose algorithm is not stable across .NET
  versions), and no `Guid`/`DateTime`/`HashCode` inputs are used;
- all numeric formatting uses the invariant culture;
- files are written with `\n` line endings and UTF-8 without BOM;
- generated headers record the generating command line, never a timestamp.

The only environmental input is the Swift toolchain, which is used solely to
resolve mangled entry-point names: the generator compiles the generated Swift
source (`xcrun swiftc -module-name <Name> -emit-library`) into a temporary
directory, lists exported symbols with `nm -gU`, and maps them back to
functions with `xcrun swift-demangle`. Mangled names are a stable function of
the module name and signatures, so this does not affect determinism in
practice; it guarantees entry-point correctness without reimplementing Swift
name mangling.

## Generated shapes

Struct generation deliberately stresses the rules of the
[physical-lowering specification](../../../../../docs/design/interop/swift/lowering.md):

- interior padding (small integers directly before naturally aligned 8-byte
  scalars, exercising opaque gap bridging);
- floats and doubles mixed with integers;
- at-cap shapes (exactly 4 lowered elements, direct) and over-cap shapes
  (more than 4, by reference), classified with a built-in simulator of the
  normative lowering algorithm;
- homogeneous float/double aggregates;
- inline arrays: C# `[InlineArray]` paired with a Swift homogeneous tuple
  field (`var elements: (T, T, ...)`), the same encoding `SwiftInlineArray`
  uses, since Swift has no fixed-size array syntax for this purpose.

Only natural, sequentially laid out structs are generated. Packed shapes that
would place 8-byte scalars at 4-byte offsets are quarantined by lowering.md
(OQ-1/V22-V24 record the resolved semantics); the generator does not emit
explicit-layout or packed shapes, so every generated struct is opaque-safe by
construction.

## Regenerating a checked-in suite

1. Run the generator with the suite's recorded seed/count/options (each
   generated file's header records the exact command line) into
   `src/tests/Interop/Swift/<Name>`.
2. If the suite is new, register its native component in
   `src/tests/Interop/CMakeLists.txt` (an `add_subdirectory(Swift/<Name>)`
   entry in the `CLR_CMAKE_TARGET_APPLE` block); without it the test build
   fails with "native project files are missing".
3. Build the native components and the test:

   ```sh
   ./src/tests/build.sh -checked skipmanaged -skipgeneratelayout
   ./src/tests/build.sh -checked -test:Interop/Swift/<Name>/<Name>.csproj
   ```

4. Run it:

   ```sh
   export CORE_ROOT=<repo>/artifacts/tests/coreclr/osx.arm64.Checked/Tests/Core_Root
   <repo>/artifacts/tests/coreclr/osx.arm64.Checked/Interop/Swift/<Name>/<Name>.sh
   ```

Regeneration with the same options is byte-identical, so `git diff` directly
shows the effect of generator changes on the checked-in suites.

## Requirements

- macOS with a Swift toolchain reachable through `xcrun` (the generated
  suites target the same swiftc the repo's Swift interop tests use).
- No NuGet dependencies; the tool builds against the BCL only and runs with
  the repo-local SDK via `./dotnet.sh run`.

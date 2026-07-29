# Swift binding-manifest schema

This document is the normative schema for the versioned, target-specific
binding manifest introduced in [architecture.md](architecture.md)
"Compiler-produced binding manifest". It defines what a conforming producer
(the extraction/merge tool) must emit and what a conforming consumer (the
binding generator and downstream tooling) must require, ignore, or reject.

The field set is grounded in the Phase 3 extraction prototype
(`manifest-prototype/FINDINGS.md`): StoreKit and CryptoKit manifests built
purely from compiler outputs — symbol graphs, `swift-api-digester` ABI/API
descriptors, and `.tbd` stubs — with byte-identical reproduction across
independent extraction runs.

## Scope

- One manifest document describes exactly one `(module, target triple)` pair.
- Multi-target models are a consumer operation: merge per-target manifests
  keyed by USR and keep only semantically compatible declarations
  (architecture.md "Compatibility policy"). The prototype validated the key:
  1,117 shared StoreKit USRs across macOS/iOS with zero conflicts.
- The manifest is a build-time contract only. The runtime and support library
  never load or interpret manifest files at runtime; the only manifest data
  that exists at runtime is the compiled-in constant set consumed by the
  capability handshake (architecture.md "Security model for binding inputs
  and assets").
- Manifests are untrusted generator inputs. Every consumer parser applies the
  size, depth, and grammar rules in architecture.md "Untrusted generator
  inputs"; malformed input is rejected deterministically, never repaired.

## Document form and determinism

- Encoding: JSON, UTF-8, no BOM.
- Top-level object fields: `schemaVersion`, `generator`, `module`,
  `declarations`.
- `generator` is `{ "name", "version" }` identifying the producing tool
  build; it participates in the binding identity.
- `declarations` is sorted by USR ordinal (byte-wise ascending). Object keys
  are emitted in schema order. Identical inputs must produce byte-identical
  manifests; this is required for the identity hashes in architecture.md
  "Output identity".
- No absolute filesystem paths, hostnames, usernames, or timestamps may
  appear anywhere in the document. File references use sanitized leaf names
  only. (The prototype confirmed symbol graphs leak `file:///Applications/…`
  URIs unless location output is suppressed; producers must normalize.)
- Raw symbol graphs are order-nondeterministic; producers sort-normalize all
  sources at ingestion before hashing or merging.

## Schema versioning

### Version number

- `schemaVersion` is a positive integer. The first shipped version is `1`.
- There is no minor or patch component and no version negotiation. A consumer
  supports an explicit set of integers.

### Additive-only evolution within a version

Within a single `schemaVersion`, the schema may change only additively:

- New optional fields may be defined and emitted.
- Open vocabularies (classification reason codes, symbol-absence codes,
  profile strings) may gain values.
- Nothing else changes. A field never changes name, JSON type, meaning, or
  requiredness within a version. Closed vocabularies (declaration kinds,
  ownership values, classification buckets, symbol roles, provenance values)
  never gain values within a version.

Any other change — removal, rename, type or meaning change, a new required
field, a new closed-vocabulary value — defines `schemaVersion + 1`.

#### Registered additive extensions

Additive fields emitted by a conforming producer are recorded here so that
other consumers know they exist and the schema owner can review them. Adding a
row does not grant approval; per the ownership table in this directory's
README, a manifest schema change wants sign-off from the schema owner and the
generator owner.

| Field | Where | Producer | Purpose | Status |
|---|---|---|---|---|
| `isObjC` (optional boolean) | type and member records | dotnet/macios `swift-import` | Records Objective-C exposure. Schema v1 gives protocol records no `typeClassification`, so without it a consumer cannot distinguish an `@objc` protocol from a pure-Swift one — which forces the entire Objective-C-identity path of the registrar seam to fail closed on every protocol. Sourced from the ABI descriptor's `declAttributes: ["ObjC"]` (required source), cross-checked against the symbol graph's `@objc` declaration fragment, with disagreement fatal per the sourcing rules below. **Absence means unknown, not false**: only an explicit `false` licenses the pure-Swift path. | emitted; **pending schema-owner sign-off** |

### Unknown fields and unknown values

- A consumer that supports version `N` must ignore unknown object fields in a
  version-`N` document. This is the room that makes additive evolution work.
- A consumer must never ignore an unknown value inside a closed vocabulary.
  Unknown kind, ownership, isolation, provenance, symbol-role, or
  classification values fail closed: the affected declaration is treated as
  `Unsupported` (or thunk-classified where README design rule 10 permits),
  or the manifest is rejected. A consumer never coerces an unknown value to a
  known one.
- Unknown values in open vocabularies (reason codes) are legal; consumers
  treat an unknown reason code as opaque and preserve it in reports.
- A producer emits only fields defined for its declared `schemaVersion`.
  There is no experimental-field escape hatch.

### Consumer rejection and `SWIFT0003`

- Every build-time consumer declares its supported schema-version set and
  rejects a manifest whose `schemaVersion` is outside that set with a
  deterministic diagnostic. There is no best-effort or partial parse.
- The binding generator stamps the consumed `schemaVersion` into the
  generated binding constants (`SwiftBindingManifest.SchemaVersion` in
  architecture.md "Runtime capability and version handshake").
- At runtime, handshake check 1 compares that integer against the support
  library's supported set and rejects with `SWIFT0003` before any Swift
  invocation. It is the same integer end to end; the runtime never sees the
  manifest document itself.
- Like the handshake codes, schema versions are contract: a shipped version's
  meaning never changes, and retired versions are never reused.

## Module record

| Field | Required | Meaning |
|---|---|---|
| `name` | yes | Swift module name |
| `platform` | yes | SDK platform identifier (`macosx`, `iphoneos`, …) |
| `targetTriple` | yes | Exact `-target` triple passed to every extractor, recorded by the producer. Never copied from symbol-graph `module.platform`, which reports the SDK version rather than the requested deployment target (prototype quirk Q4) |
| `deploymentTarget` | yes | Minimum OS version, duplicated from the triple as `"major.minor"` |
| `sdk` | yes | `{ "canonicalName": "macosx27.0", "version": "27.0", "build": "…" }`. Identity only — the SDK directory leaf name and version, never an absolute path |
| `swiftCompiler` | yes | Full compiler version string including the swiftlang and clang build identifiers, e.g. `"Apple Swift version 6.4 (swiftlang-6.4.0.23.5 clang-2100.3.23.3)"` |
| `swiftAbiVersion` | yes | Integer from the `.tbd` (`swift-abi-version`) |
| `libraryEvolution` | yes | Boolean. Library-evolution/resilience mode is ABI-significant even when manglings are unchanged; a build-mode mismatch must fail generation or loading before any call executes (architecture.md "Compatibility policy") |
| `bindingMode` | yes | `"library-evolution"` or `"exact-binary"` (third-party support modes) |
| `installName` | frameworks | `.tbd` install name |
| `linkMode` | yes | `"dynamic"` or `"static"` |
| `tbdTargets` | yes | Target slices as listed in the `.tbd`. Apple SDK `.tbd`s may carry only `arm64e` (and `x86_64`) slices with no plain `arm64` slice (prototype quirk Q3); symbol-presence validation for arm64 reads the arm64e slice under an explicit arm64→arm64e equivalence rule |
| `crossImportOverlays` | yes | Array (may be empty) of `{ "declaringModule", "bystanders": [...], "ingested": bool }`. Overlays auto-expanded by the extractor (quirk Q2) are recorded here even when excluded from `declarations` |
| `hashAlgorithm` | yes | `"sha256"` |
| `sources` | yes | Per-source provenance; see below |
| `exactBinaryHashes` | `bindingMode = "exact-binary"` | Content hashes of the bound binary module and its relevant transitive binaries |

### Source-tool provenance

Each entry in `sources` records which extractor produced which input:

| Field | Meaning |
|---|---|
| `role` | Closed vocabulary: `symbolGraph`, `abiDescriptor`, `apiDescriptor`, `tbd`, `crossImport`, `loweringProbe` |
| `tool` | Producing tool and version, e.g. `"swift-api-digester (swiftlang-6.4.0.23.5)"`. The producer of a format is pinned: `swift-frontend -emit-abi-descriptor-path` and `swift-api-digester -dump-sdk -abi` emit the same format but diverge node-for-node (opaque results), so the tool identity is part of the contract |
| `arguments` | Extraction flags relevant to output semantics (`-minimum-access-level public`, `-abi`, `-avoid-location`, …), path-stripped |
| `files` | Sanitized leaf names of the ingested files |
| `normalization` | How the raw bytes were canonicalized before hashing, e.g. `"sorted-by-usr"` for symbol graphs, `"tbd-v5-json"` for stubs |
| `contentHash` | Hash of the normalized bytes, using `hashAlgorithm` |

The four roles `symbolGraph`, `abiDescriptor`, `apiDescriptor`, and `tbd` are
mandatory for every module. `apiDescriptor` exists solely for the
client-emitted set: `api ∖ abi` identifies `@_alwaysEmitIntoClient`
declarations; `abi ∖ api` identifies ABI-internal (`@usableFromInline`)
declarations. `loweringProbe` is reserved for SIL/IR extraction and probe
validation outputs.

These hashes identify the extraction inputs. The full binding identity
(architecture.md "Output identity") — generated-native ABI version, generated
managed/native asset hashes — is computed downstream over this manifest plus
the generated artifacts; those values are not fields of this schema.

## Declaration records

Every record is keyed by USR. USRs are the primary key, the cross-source
merge key, and the cross-target merge key. Mangled names are derived data,
recorded with provenance and cross-checked, never used as identity.

### Common fields

| Field | Required | Meaning |
|---|---|---|
| `usr` | yes | Primary key, unique within the document |
| `name` | yes | Display name, e.g. `products(for:)` |
| `path` | yes | Dotted declaration path, e.g. `StoreKit.Product.products(for:)` |
| `kind` | yes | Closed vocabulary: `Struct`, `Enum`, `EnumCase`, `Class`, `Actor`, `Protocol`, `Func`, `Operator`, `Init`, `Deinit`, `Var`, `Subscript`, `Accessor`, `TypeAlias`, `AssociatedType`, `Unknown`. A producer that cannot map a compiler kind emits `Unknown` and classifies the declaration `Unsupported` with `UNSUP-UNKNOWN-KIND` |
| `parent` | nested decls | USR of the lexical parent |
| `visibility` | yes | `"public"` or `"abi-internal"` (`@usableFromInline`) |
| `sources` | yes | Subset of the module source roles that described this declaration. `["symbolGraph"]` alone is the missing-from-ABI-descriptor signal; presence in `apiDescriptor` but not `abiDescriptor` is the client-emitted signal |
| `availability` | yes | Array of `{ "domain", "introduced"?, "deprecated"?, "obsoleted"?, "unavailable"?, "message"? }`; versions are `"major.minor[.patch]"` strings. Sourced from structured symbol-graph availability; the ABI descriptor's `intro_*` strings are a cross-check only |
| `genericSig` | generic decls | Canonical generic signature from the ABI descriptor |
| `sugaredGenericSig` | generic decls | Sugared form, display only |
| `isFromExtension` | when true | Declared in an extension |
| `classification` | yes | See "Classification" |
| `reason` | yes | Stable reason code |
| `requiredProfiles` | supported decls | See "Required profiles" |

### Callable fields

| Field | Required | Meaning |
|---|---|---|
| `isAsync` | yes | Boolean |
| `throws` | yes | `"none"`, `"untyped"`, or `"typed"` |
| `thrownTypeUsr` | `throws = "typed"` | USR of the thrown error type |
| `isolation` | yes | `{ "kind": "nonisolated" \| "mainActor" \| "globalActor" \| "actorInstance" \| "unknown", "actorTypeUsr"? }` |
| `isStatic` | when true | Static/type-level member |
| `selfKind` | methods | `"Mutating"` or `"NonMutating"` |
| `initKind` | initializers | `"Designated"`, `"Convenience"`, `"Required"` |
| `parameters` | yes (may be empty) | See below |
| `result` | value-returning | See below |
| `requiresReabstraction` | closure-typed signatures | `"no"`, `"yes"`, or `"unknown"`. No current source states reabstraction requirements; version-1 producers emit `"unknown"`, which selects a thunk |

### Semantic facts and their required source

The extraction sources disagree in capability, so the schema pins where each
fact must come from. A manifest populated from a forbidden source is
non-conforming.

| Fact | Field | Required source | Forbidden source |
|---|---|---|---|
| Async | `isAsync` | Symbol-graph keyword fragments | `abi.json` (has no async flag); mangled-name `Ya` suffix is a cross-check only |
| Typed throws | `throws`, `thrownTypeUsr` | Symbol-graph `throws(E)` fragments | `abi.json` (`throwing: true` erases the type); mangled `YK` is a cross-check only |
| Actor isolation | `isolation` | Symbol-graph attribute fragments (`@MainActor`, …) | `abi.json` `declAttributes: ["Custom"]` (no attribute identity) |
| Explicit ownership | `ownership` with `"explicit"` | ABI descriptor `paramValueOwnership` | Symbol-graph keyword fragments (display only) |
| Mangled names | `symbols` | ABI descriptor `mangledName` + accessor nodes, `.tbd` correlation | Demangling display names; hand-mangling |
| Availability | `availability` | Symbol-graph availability mixins | ABI descriptor `intro_*` strings (cross-check only) |

Where a fact is available from two sources, disagreement is a producer error
and the extraction fails; the producer never picks a winner silently.

If future compilers add first-class `isAsync`, isolation, and thrown-type
fields to the ABI descriptor, only this sourcing table changes; the manifest
fields are already first-class.

### Parameters, result, and ownership

Each `parameters` entry:

| Field | Required | Meaning |
|---|---|---|
| `name` | yes | External argument label (`"_"` when absent) |
| `internalName` | optional | Internal parameter name |
| `typeUsr` | direct classifications | Precise type identity (symbol-graph `preciseIdentifier`). Absent for unresolved or generic-parameter types; a declaration without full type-identity resolution is never classified direct |
| `typeName` | yes | Display type |
| `ownership` | yes | See below |
| `noescape` | function-typed | Boolean |
| `hasDefaultArg` | when true | Boolean |

`result` carries `typeUsr`, `typeName`, and `ownership`.

Ownership is an object, and the explicit-versus-default distinction is
mandatory:

```json
"ownership": { "value": "borrowed", "provenance": "default-convention" }
```

- `value` is the closed vocabulary `borrowed`, `owned`, `inout` (parameter
  positions) and `owned`, `autoreleased` (result positions), mapped from the
  ABI-model ownership table (abi-model.md "Ownership model").
- `provenance` is `"explicit"` when the ABI descriptor spelled it
  (`consuming`/`borrowing`/`inout` → `Owned`/`Shared`/`InOut`) or
  `"default-convention"` when the producer computed it from the documented
  Swift calling conventions (+0 guaranteed arguments, +1 owned returns and
  initializer arguments). Defaults are convention-derived facts, not
  descriptor facts, and the tag preserves that distinction for validation.
- Every parameter and result has an ownership value; blanks are not
  permitted. An unknown ownership value fails closed: thunk or reject
  (README design rule 10).

### Generic context

Generic callables carry the verified direct-call context order
(abi-model.md "Verified direct generic convention"):

```json
"genericContext": {
  "metadataArguments": ["τ_0_0"],
  "witnessTables": [{ "genericParameter": "τ_0_0", "protocolName": "Swift.Hashable" }]
}
```

`metadataArguments` lists the generic parameters in metadata-argument
order; `witnessTables` lists conformance requirements in declaration
order, one witness table each. Both derive from the ABI descriptor's
canonical generic signature.

### Module pointer-authentication fields

The module record carries `ptrauth`: `{ "schema": "none" }` for ARM64
(non-ptrauth) targets, or `{ "schema": "arm64e-v<N>", "table": "<file>" }`
referencing the versioned discriminator table
(`ptrauth-discriminators.json`). Consumers fail closed on unknown schemas
and on categories missing from the table.

### Existential representations

Existential-typed parameters and results record a closed representation
vocabulary (probe-verified layouts): `opaque` (40-byte container: 3-word
buffer, payload metadata, one witness table per protocol — composition
tables in canonical sorted protocol order, not source order), `class`
(16-byte {object, witness table}), `error` (error box word), and
`extended` (parameterized existentials; thunk-only). Managed dispatch is
permitted only for inline opaque payloads; every other case routes
through thunks.

> **Gap (measured 2026-07-18, Xcode 27 beta 2 / Swift 6.4,
> `arm64-apple-macos15.0`): the `class` layout does not cover `@objc`
> protocols.** An existential over an `@objc` protocol is class-bound, so it
> falls under `class` by this vocabulary — but it measures **8 bytes, a bare
> class reference with no witness table**, because an `@objc` protocol has no
> Swift witness table at all (its conformance lives only in the Objective-C
> class's protocol list). The stated `{object, witness table}` pair is correct
> only for a **non-`@objc`** class-bound protocol.
>
> This matters because the vocabulary is *closed*: a consumer meeting this
> case does not see an unknown value it must reject, it sees a known one
> (`class`) with a documented layout that is wrong for the shape in hand, and
> marshalling to that layout reads a word past the value. Measured widths on
> this target: `@objc P` = 8, single non-`@objc` class-bound `P` = 16, two-Swift-protocol
> composition = 24, `Any` = 32, `any Error` = 8 (the 40-byte `opaque` figure
> above corresponds to a single-protocol non-class-bound existential and is
> consistent with these).
>
> A future schema revision should either split the `class` case by
> `@objc`-ness or record the width explicitly; until then, producers and
> consumers must derive the width from the protocol's `@objc`-ness rather than
> from the representation name alone. Evidence and harness:
> `swift-abi-probe/sources/objc-identity` (finding 5), consumed by
> dotnet/macios `docs/swift/api-projection.md`.

### Symbols

`symbols` records every symbol the binding may reference, with per-symbol
provenance. Each symbol entry is:

```json
{ "symbol": "_$s…", "inTbd": true, "source": "abiDescriptor", "rule": "…" }
```

- `symbol` is the exact linker symbol including the leading underscore.
- `inTbd` is required on every entry: whether the symbol exists in the
  module's `.tbd` export set for the manifest's target (via the arm64e
  equivalence rule where applicable). Direct imports are permitted only when
  the deployment target guarantees the symbol.
- `source` is the closed vocabulary `abiDescriptor`, `derived`, `tbd`,
  `loweringProbe`, `generator`. `derived` entries name the derivation in
  `rule`. `inTbd` applies to framework symbols; `generatedThunk` entries
  (source `generator`) live in the generated native asset and are covered by
  the native-asset identity hash (`SWIFT0005`) instead.

Symbol roles:

| Role | Applies to | Notes |
|---|---|---|
| `entry` | callables | The callable entry point. For initializers this is the allocating init (`…fC`); the ABI descriptor records the initializing init (`…fc`), and the producer must apply the correction (`source: "derived"`, `rule: "allocating-init"`). The prototype measured 38 StoreKit false negatives without it |
| `accessors.get` / `.set` / `.read` / `.modify` | `Var`, `Subscript` | Per-accessor symbols from the ABI descriptor's accessor nodes. Presence of `read`/`modify` marks a coroutine-accessor requirement (thunk tier) |
| `dispatchThunk` | overridable class members | Dispatch thunk (`…Tj`) selected for resilient class dispatch |
| `generatedThunk` | thunk-classified decls | The deterministic entry symbol of the generated fallback thunk. Populated by the generator stage, not by extraction; direct-versus-thunk selection remains a build-time decision and this role never patches a direct call at runtime |
| `asyncFunctionPointer` | `isAsync` decls | The `…Tu` async-function-pointer record; required whenever the entry symbol is exported (41/41 StoreKit macOS async members correlate) |
| `metadataAccessor` | types | `…Ma` |
| `nominalTypeDescriptor` | types | `…Mn` |
| `fullMetadata` | types | `…N` |
| `valueWitnessTable` | value types | `…WV` |
| `fieldOffsets` | class stored fields | Per-field direct field-offset symbols (`…Wvd`) where exported |

When an expected role has no symbol, the producer records the absence with a
stable code instead of omitting it silently:

| Code | Meaning |
|---|---|
| `SYM-ABSENT-FROZEN-ENUM-CASE` | Value-level case; no symbol by design |
| `SYM-ABSENT-PROTOCOL-REQUIREMENT` | Witness dispatch; no direct symbol |
| `SYM-ABSENT-BACK-DEPLOYED` | `@backDeployed` fallback body |
| `SYM-ABSENT-CLIENT-EMITTED` | `@_alwaysEmitIntoClient`; no ABI presence |
| `SYM-ABSENT-NOT-EXPORTED` | Expected but not found; fail-closed signal |

### Classification and reason codes

`classification` uses the member vocabulary from architecture.md "Type and
member classification": `ObjectiveC`, `DirectSwift`,
`DirectSwiftWithManagedMarshalling`, `GeneratedSwiftThunk`,
`ClientEmittedSwift`, `FacadeOnly`, `Unsupported`.

`reason` is a stable code naming the rule that produced the classification
(`THUNK-ASYNC`, `DIRECT-MARSHAL-RESILIENT-VALUE`,
`UNSUP-MISSING-FROM-ABI-DESCRIPTOR`, …). Reason codes are contract in the
same way as handshake diagnostic codes: never reused, only retired. The code
registry is versioned with this schema. `Unsupported` reasons additionally
name the dependency that would make the declaration supportable.

### Required profiles

`requiredProfiles` lists the exact `SwiftInterop.*` strings
(abi-model.md "Named ABI profiles") the declaration's classification
requires, as a transitively closed set (a declaration requiring
`SwiftInterop.AsyncThunk1` also lists `SwiftInterop.Values1` and
`SwiftInterop.Sync1`). The generator computes each binding's
`RequiredProfiles` handshake constant as the union of the per-declaration
sets it actually emitted. Profile strings are an open vocabulary; the
handshake compares exact strings by set containment.

### Pointer authentication

Standard Swift ABI pointer-authentication discriminators are deliberately not
manifest data (see "What is deliberately not in the manifest"). The optional
per-declaration `pointerAuth` record exists only for indirect code pointers a
binding must call whose scheme is not covered by the standard runtime table —
generated closure/context schemes or future ABI features:

```json
"pointerAuth": {
  "category": "…",
  "sourceStorage": "…",
  "addressDiscriminated": true,
  "requiresNativeThunk": true
}
```

`category` names a standard pointer category resolved against the runtime
native-helper table, plus the pointer's source storage and whether address
discrimination applies. An unknown category or schema fails closed — the
declaration is thunk-classified or rejected, per abi-model.md "Pointer
authentication". Authenticated pointers are never described as copyable or
cacheable.

### Reserved: lowering record

`lowering` is an optional record reserved in version 1 for the complete
ordered lowered argument list of a direct call: metadata and witness
arguments in exact compiler order, pack shapes, fulfillment-derived
arguments, and self/error/indirect-result/async-context positions. It may be
populated only from a `loweringProbe` (or future call-plan emitter) source.
Until it is specified:

- a generic declaration is never classified `DirectSwift` without a
  `lowering` record, because no descriptor states witness order;
- non-generic direct candidates need no `lowering` record — the runtime's
  lowering algorithm ([lowering.md](lowering.md)) computes physical lowering
  from the manifest's type identities and ownership.

The prototype measured 98.8% of StoreKit direct candidates as
descriptor-complete without this record; it exists for the remainder and for
the future direct-generic tier.

## Type records

Type declarations (`Struct`, `Enum`, `Class`, `Actor`, `Protocol`) carry the
common fields plus:

| Field | Required | Meaning |
|---|---|---|
| `typeClassification` | yes | Closed vocabulary from architecture.md: `FrozenTrivialValue`, `FrozenNontrivialValue`, `ResilientValue`, `NativeSwiftObject`, `ObjectiveCObject`, `Actor`, `ClassicExistential`, `ExtendedExistential`, `Unsupported` |
| `reason` | yes | Stable reason code, as for members |
| `isFrozen` | Swift nominal types | From the ABI descriptor's `Frozen` attribute |
| `isEnumExhaustive` | enums | From `isEnumExhaustive` |
| `conformances` | yes (may be empty) | Protocol USRs from the ABI descriptor |
| `superclass` | classes | USR |
| `layout` | frozen value types | See below |
| `symbols` | yes | Type symbols (`metadataAccessor`, `nominalTypeDescriptor`, `fullMetadata`, `valueWitnessTable`) with `inTbd` bits |

`layout` records only what the descriptors state for frozen types:

```json
"layout": {
  "fields": [
    { "name": "…", "order": 0, "typeUsr": "…", "hasStorage": true }
  ]
}
```

- `order` is the descriptor's `fixedbinaryorder` — stored-property order for
  frozen types.
- Field-offset symbols, where exported, appear under `symbols.fieldOffsets`.
- Numeric sizes, strides, alignments, and byte offsets are never recorded:
  no descriptor states them, resilient layout must not be inferred (README
  design rule 6), and frozen physical lowering is the runtime algorithm's
  job, validated by probes ([lowering.md](lowering.md)).

Resilient types get no `layout` record at all; their storage is metadata/VWT
territory at runtime.

## Fail-closed rules

1. **Never silently drop a public declaration.** Every public declaration
   from any ingested source appears exactly once in `declarations` with a
   classification and reason. The producer reconciles counts as a
   postcondition: symbol-graph public declarations, ABI-descriptor
   declarations, and manifest records must balance, and per-framework
   coverage reports are emitted from the same data.
2. **`UNSUP-MISSING-FROM-ABI-DESCRIPTOR`.** A Swift declaration present in
   the symbol graph but omitted by the ABI descriptor (and not
   Clang-imported, not client-emitted) is recorded with
   `classification: "Unsupported"` and this reason code. It is never dropped
   and its signature is never reconstructed from demangling (README design
   rule 1). The prototype measured 45 (macOS) to 73 (iOS) such StoreKit
   declarations per target — real, recurring, and per-target-variable.
3. **Unknown data selects thunk or reject.** Unknown ownership values,
   pointer-authentication categories or schemas, declaration kinds, or
   isolation kinds must select a generated thunk where the thunk tier can
   absorb them, or classify the declaration `Unsupported`; they never select
   a direct classification (README design rule 10, abi-model.md "Pointer
   authentication").
4. **Consumers never coerce.** A consumer encountering an unknown
   closed-vocabulary value treats the declaration as `Unsupported` or rejects
   the manifest; it never maps the value to a known one.
5. **Cross-source disagreement is fatal.** Mangled-name suffix cross-checks
   (`Ya`, `YK`) that contradict symbol-graph facts, or `inTbd` claims that
   contradict the `.tbd`, fail the extraction deterministically.
6. **Handshake alignment.** Nothing in a manifest can widen runtime support:
   the compiled-in schema version, profile set, and identity hashes are
   validated by the runtime handshake (`SWIFT0001`–`SWIFT0006`) before any
   Swift invocation, and handshake failure never selects a different ABI.

## What is deliberately not in the manifest

- **Standard pointer-authentication discriminators.** Fixed constants from
  the Swift ABI headers live in the versioned runtime native-helper table
  (abi-model.md "Pointer authentication"). The manifest identifies pointer
  category and source storage; per-declaration ptrauth data is reserved for
  nonstandard schemes only.
- **SIL-level lowering detail.** Physical register assignment, stack homing,
  frozen-aggregate register lowering, and reabstraction lowering are the
  runtime lowering algorithm's output and the probe validation tool's
  subject, not manifest data. The reserved `lowering` record carries only
  argument identity and order, never register assignments.
- **Anything derivable at runtime.** Sizes, strides, alignments, resilient
  field offsets, enum tag values, VWT contents, and metadata state come from
  runtime metadata and value-witness operations (README design rules 5–6).
  Hard-coding them would break under library evolution by design.
- **Downstream identity values.** The generated-native ABI version and
  generated asset hashes belong to the binding identity computed after
  generation (architecture.md "Output identity"), not to this document.
- **Environment identity.** Absolute paths, machine names, and timestamps are
  banned by the determinism rules.
- **Demangled display of symbols.** Derivable; display names are already
  first-class fields.

## Worked example

One synchronous function and one async method, abbreviated to the load-bearing
fields (real StoreKit macOS declarations from the prototype corpus).

Synchronous, direct with managed marshalling:

```json
{
  "usr": "s:8StoreKit03AppA17MerchandisingKindV18subscriptionBundleyACSSFZ",
  "name": "subscriptionBundle(_:)",
  "path": "StoreKit.AppStoreMerchandisingKind.subscriptionBundle(_:)",
  "kind": "Func",
  "parent": "s:8StoreKit03AppA17MerchandisingKindV",
  "visibility": "public",
  "sources": ["symbolGraph", "abiDescriptor", "tbd"],
  "availability": [{ "domain": "macOS", "introduced": "26.2" }],
  "isAsync": false,
  "throws": "none",
  "isolation": { "kind": "nonisolated" },
  "isStatic": true,
  "selfKind": "NonMutating",
  "parameters": [
    {
      "name": "_",
      "typeUsr": "s:SS",
      "typeName": "Swift.String",
      "ownership": { "value": "borrowed", "provenance": "default-convention" }
    }
  ],
  "result": {
    "typeUsr": "s:8StoreKit03AppA17MerchandisingKindV",
    "typeName": "StoreKit.AppStoreMerchandisingKind",
    "ownership": { "value": "owned", "provenance": "default-convention" }
  },
  "symbols": {
    "entry": {
      "symbol": "_$s8StoreKit03AppA17MerchandisingKindV18subscriptionBundleyACSSFZ",
      "inTbd": true,
      "source": "abiDescriptor"
    }
  },
  "classification": "DirectSwiftWithManagedMarshalling",
  "reason": "DIRECT-MARSHAL-RESILIENT-VALUE",
  "requiredProfiles": ["SwiftInterop.Sync1", "SwiftInterop.Values1"]
}
```

Async, generic, thunk-classified:

```json
{
  "usr": "s:8StoreKit7ProductV8products3forSayACGx_tYaKSlRzSS7ElementRtzlFZ",
  "name": "products(for:)",
  "path": "StoreKit.Product.products(for:)",
  "kind": "Func",
  "parent": "s:8StoreKit7ProductV",
  "visibility": "public",
  "sources": ["symbolGraph", "abiDescriptor", "tbd"],
  "availability": [{ "domain": "macOS", "introduced": "12.0" }],
  "genericSig": "<τ_0_0 where τ_0_0 : Swift.Collection, τ_0_0.Element == Swift.String>",
  "sugaredGenericSig": "<Identifiers where Identifiers : Swift.Collection, Identifiers.Element == Swift.String>",
  "isAsync": true,
  "throws": "untyped",
  "isolation": { "kind": "nonisolated" },
  "isStatic": true,
  "selfKind": "NonMutating",
  "isFromExtension": true,
  "parameters": [
    {
      "name": "for",
      "typeName": "τ_0_0",
      "ownership": { "value": "borrowed", "provenance": "default-convention" }
    }
  ],
  "result": {
    "typeName": "[StoreKit.Product]",
    "ownership": { "value": "owned", "provenance": "default-convention" }
  },
  "symbols": {
    "entry": {
      "symbol": "_$s8StoreKit7ProductV8products3forSayACGx_tYaKSlRzSS7ElementRtzlFZ",
      "inTbd": true,
      "source": "abiDescriptor"
    },
    "asyncFunctionPointer": {
      "symbol": "_$s8StoreKit7ProductV8products3forSayACGx_tYaKSlRzSS7ElementRtzlFZTu",
      "inTbd": true,
      "source": "derived",
      "rule": "async-function-pointer"
    }
  },
  "classification": "GeneratedSwiftThunk",
  "reason": "THUNK-ASYNC",
  "requiredProfiles": [
    "SwiftInterop.Sync1",
    "SwiftInterop.Values1",
    "SwiftInterop.AsyncThunk1"
  ]
}
```

`isAsync`, `throws`, and `isolation` in both records come from symbol-graph
fragments; `abi.json` cannot state any of them. The `Ya`/`YK` mangling
suffixes and the `Tu` correlation serve only as cross-checks.

## Open questions

The extraction prototype could not settle the following; each is tracked
against the roadmap Phase 3 decision items rather than resolved here.

1. **Default ownership conventions.** `default-convention` ownership values
   are computed from the documented Swift conventions, not read from any
   descriptor; only 40 of 953 StoreKit members carry explicit annotations.
   Open: whether the convention rules are pinned per `swiftAbiVersion`, per
   compiler build, or per schema version, and what differential probe battery
   must pass before direct classifications may rely on them. Upstream ask:
   per-declaration defaults in `abi.json`.
2. **Witness and pack argument ordering.** No descriptor states metadata and
   witness-table argument order, pack shapes, or fulfillment-derived
   arguments. The `lowering` record is reserved but unspecified: its producer
   (a version-pinned call-plan emitter versus SIL/IR probe extraction) is the
   roadmap's open "call-plan" decision, and until it lands generic
   declarations stay thunk-classified.
3. **Reabstraction thunk identification.** No source states when a
   closure-typed signature requires reabstraction; `requiresReabstraction`
   is `"unknown"` in version 1 and pessimizes to the thunk tier. Needs
   SIL-level extraction or a compiler-provided fact.
4. **Coroutine accessor ABI.** Descriptors expose only the accessor kind
   (`read`/`modify`), not the yield-once ABI details; whether the schema ever
   carries more than the kind depends on the direct-coroutine decision.
5. **Cross-import overlay manifests.** Whether an overlay
   (`_StoreKit_SwiftUI`) is a separate manifest document or inlined
   declarations; the extractor auto-expands overlays with no opt-out, so the
   producer boundary needs a rule before Phase 10.
6. **arm64→arm64e slice equivalence.** `inTbd` for arm64 currently reads the
   arm64e slice because Apple SDK `.tbd`s carry no plain arm64 slice; the
   equivalence rule needs revalidation per Xcode release before it is frozen
   as normative.

## Emitter conformance (2026-07-13)

The generator now emits the full module-identity block documented above,
derived from recorded inputs and never guessed:

- `platform` and `deploymentTarget` are parsed from the requested target
  TRIPLE (never from the symbol graph's `module.platform`, which reports
  the SDK version — quirk Q4); an unrecognized triple fails generation.
- `swiftAbiVersion`, `installName`, and `tbdTargets` come from the `.tbd`
  when one is supplied. Real evidence for StoreKit on macOS: ABI version
  7, and `tbdTargets: [arm64e-macos, x86_64-macos]` — i.e. **no plain
  arm64 slice**, which is quirk Q3 recorded as data rather than prose,
  making the arm64→arm64e symbol-presence equivalence rule auditable.
- `libraryEvolution`/`bindingMode`, `linkMode`, and `hashAlgorithm` are
  emitted per the schema; `crossImportOverlays` is present (empty until
  overlay ingestion records entries).

## The arm64 slice, per platform (measured 2026-07-13)

Quirk Q3 ("Apple SDK `.tbd`s may carry only `arm64e`") is real but **not
uniform**. Measured from the emitted manifests of all seven targets
(`docs/design/interop/swift/evidence/`):

| target | `.tbd` slices | plain arm64 slice? |
|---|---|---|
| macOS (arm64 and arm64e) | `arm64e-macos`, `x86_64-macos` | **no** |
| iOS device | `arm64e-ios` | **no** |
| Mac Catalyst | `arm64e-maccatalyst`, `x86_64-maccatalyst` | **no** |
| tvOS device | `arm64-tvos`, `arm64e-tvos` | **YES** |
| iOS simulator | `arm64-ios-simulator`, `x86_64-ios-simulator` | yes |
| tvOS simulator | `arm64-tvos-simulator`, `x86_64-tvos-simulator` | yes |

So the **arm64 → arm64e equivalence rule is REQUIRED for macOS, iOS device,
and Mac Catalyst — and is NOT needed for tvOS device**, which ships a genuine
arm64 slice. A validator that assumes "device SDKs never have arm64" would be
wrong on tvOS; one that assumes they always do would be wrong on the other
three. The manifests record the real slice list per target, so the rule is
applied from data rather than from a generalization.

Swift ABI version is **7** on every target.

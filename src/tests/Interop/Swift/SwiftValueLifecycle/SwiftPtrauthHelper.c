// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// The shared native helper for pointer-authenticated indirect targets
// (roadmap Phase 4; support-library.md "Source-address invocation
// discipline"). Managed code passes the SLOT ADDRESS a witness lives at;
// the helper loads the pointer at its source address and invokes it.
//
// On arm64 the load is plain. On arm64e the loaded pointer would carry a
// PAC signature that must be authenticated with the key and discriminator
// from ptrauth-discriminators.json — those values are not yet
// hardware-verified (every discriminator is null), so this build FAILS
// CLOSED: the helper is the mandatory path for ptrauth targets, and until
// the table is populated it refuses rather than guessing.

#include <stdint.h>
#include <stdlib.h>

#if defined(__arm64e__)
#error "SwiftPtrauthHelper requires hardware-verified discriminators (ptrauth-discriminators.json); fail closed."
#endif

typedef void DestroyFn(void *value, void *metadata);
typedef void *InitFn(void *dest, void *src, void *metadata);
typedef uint32_t GetEnumTagFn(const void *value, uint32_t emptyCases, void *metadata);
typedef void StoreEnumTagFn(void *value, uint32_t whichCase, uint32_t emptyCases, void *metadata);

// Each entry point receives the address of the witness SLOT, not the
// witness pointer, so the (future) authentication happens exactly at the
// source storage address.

void SwiftPtrauth_Destroy(void **slotAddr, void *value, void *metadata) {
    ((DestroyFn *)*slotAddr)(value, metadata);
}

void *SwiftPtrauth_InitializeWithCopy(void **slotAddr, void *dest, void *src, void *metadata) {
    return ((InitFn *)*slotAddr)(dest, src, metadata);
}

void *SwiftPtrauth_InitializeWithTake(void **slotAddr, void *dest, void *src, void *metadata) {
    return ((InitFn *)*slotAddr)(dest, src, metadata);
}

uint32_t SwiftPtrauth_GetEnumTagSinglePayload(void **slotAddr, const void *value,
                                              uint32_t emptyCases, void *metadata) {
    return ((GetEnumTagFn *)*slotAddr)(value, emptyCases, metadata);
}

void SwiftPtrauth_StoreEnumTagSinglePayload(void **slotAddr, void *value, uint32_t whichCase,
                                            uint32_t emptyCases, void *metadata) {
    ((StoreEnumTagFn *)*slotAddr)(value, whichCase, emptyCases, metadata);
}

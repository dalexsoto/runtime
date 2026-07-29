// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Standard Foundation value types used pervasively by StoreKit (roadmap
// Phase 9 "Bind standard Foundation values"). UUID/Date/Data expose their
// own metadata accessors; Decimal is an IMPORTED ObjC type (NSDecimal) with
// no exported Swift Ma, so its metadata is reached through a shim — that
// asymmetry is the point of this fixture.

import Foundation

public func decimalMetadata() -> Any.Type { Decimal.self }
public func uuidMetadata() -> Any.Type { UUID.self }
public func dateMetadata() -> Any.Type { Date.self }
public func dataMetadata() -> Any.Type { Data.self }

// UUID: construct from 16 bytes, read them back.
public func makeUuid(_ bytes: UnsafePointer<UInt8>, _ out: UnsafeMutableRawPointer) {
    var t = uuid_t(bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7],
                   bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15])
    out.assumingMemoryBound(to: UUID.self).initialize(to: UUID(uuid: t))
}

public func uuidBytes(_ p: UnsafeRawPointer, _ out: UnsafeMutablePointer<UInt8>) {
    let u = p.assumingMemoryBound(to: UUID.self).pointee
    withUnsafeBytes(of: u.uuid) { raw in
        for i in 0..<16 { out[i] = raw[i] }
    }
}

// Date: the reference-date form is the ABI-stable one.
public func makeDate(_ secondsSinceReferenceDate: Double, _ out: UnsafeMutableRawPointer) {
    out.assumingMemoryBound(to: Date.self).initialize(to: Date(timeIntervalSinceReferenceDate: secondsSinceReferenceDate))
}

public func dateSeconds(_ p: UnsafeRawPointer) -> Double {
    return p.assumingMemoryBound(to: Date.self).pointee.timeIntervalSinceReferenceDate
}

// Data: heap-backed, so lifecycle through the VWT actually matters.
public func makeData(_ bytes: UnsafePointer<UInt8>, _ count: Int64, _ out: UnsafeMutableRawPointer) {
    out.assumingMemoryBound(to: Data.self).initialize(
        to: Data(bytes: bytes, count: Int(count)))
}

public func dataCount(_ p: UnsafeRawPointer) -> Int64 {
    return Int64(p.assumingMemoryBound(to: Data.self).pointee.count)
}

public func dataByte(_ p: UnsafeRawPointer, _ index: Int64) -> UInt8 {
    return p.assumingMemoryBound(to: Data.self).pointee[Int(index)]
}

// Decimal: StoreKit prices are Decimal. Construct from a string and read the
// value back as a double for checking.
public func makeDecimal(_ utf8: UnsafePointer<UInt8>, _ len: Int64, _ out: UnsafeMutableRawPointer) {
    let s = String(decoding: UnsafeBufferPointer(start: utf8, count: Int(len)), as: UTF8.self)
    out.assumingMemoryBound(to: Decimal.self).initialize(to: Decimal(string: s)!)
}

public func decimalDouble(_ p: UnsafeRawPointer) -> Double {
    return NSDecimalNumber(decimal: p.assumingMemoryBound(to: Decimal.self).pointee).doubleValue
}

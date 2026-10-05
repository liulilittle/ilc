// fnptrfield.cs -- unit-test fixture for the P-D3 fnptr FIELD decoder fix.
//
// A `delegate*` INSTANCE field was silently downgraded to an int32_t storage
// slot by MetadataLoader.DecodeFieldSignature (0x1B -> default -> (Int32,""))
// and TypeEmitter.DecodeNestedFieldSignature; a static fnptr field hit the
// same silent downgrade in DecodeExtendedStaticType. Writing a void* into an
// int32_t slot fails to compile; READING it truncates the x64 address.
// P-DX P0 pattern: the silent storage must be LOUDLY rejected (the model has
// no void* field carrier). wiring a real void*-membership storage is a
// follow-up -- LOUD is the hard gate that forbids silent truncation.
//
// NOTE: lives in tests/fixtures (unit-only; never an e2e case -- its
// transpile is EXPECTED to fail).
using System;
namespace Cases { public class FnPtrField {
    public unsafe delegate*<int, int> F;
    public static int Increment(int x) => x + 1;
    public unsafe static void Main() {
        var o = new FnPtrField();
        o.F = &Increment;
        Console.WriteLine(o.F(1));
    }
}}
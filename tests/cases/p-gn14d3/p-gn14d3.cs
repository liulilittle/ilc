// p-gn14d3.cs -- GN-14 (b) probe: a STATIC call on a closed generic instantiation
// whose owner clone has NO local variable and NO newobj (e.g. Host9<string>.Shared()).
//
// HEAD defect: the CodeGenerator clone-demand scan (CodeGenerator.cs:732) only
// inspected NEWOBJ instructions, so an owner instantiated solely by a static
// call / callvirt / ldftn / field reference was never registered via EnsureClone.
// The call site resolves to the shared object clone
// (Cases_Host9_1_object_Shared_...) while only the definition-default
// (Cases_Host9_1_int) is emitted -> g++ "was not declared".
//
// Fix: relax the scan to every instruction carrying a MethodToken or FieldToken
// operand and register the owner TypeSpec clone (MemberRef parent TypeSpec ->
// TryClassGenericInstanceKind).
//------------------------------------------------------------------------------
using System;

namespace Cases {

class Host9<T> {
    public static bool Shared() {
        T v = default(T);
        return v == null;
    }
}

public static class PGn14D3 {
    public static void Main() {
        Console.WriteLine(Host9<string>.Shared());
    }
}}
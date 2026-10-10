// p-gn14gdffp.cs -- GN-14 (a-1) probe: a 0x13 (VAR) generic argument nested
// inside a GenericInst (0x15) method-generic argument, resolved through the
// P-D2 #3 GDFFP typeId name table (MethodRefResolver.GenericInstArgName).
//
// HEAD defect: GenericInstArgName's 0x13/0x1E arm returned the literal "int"
// WITHOUT consuming the compressed VAR/MVAR parameter index. The following
// argument slot then read that index byte (0x00) and threw
//   "generic argument byte 0x00 in P-D2 #3 GDFFP typeId naming"
// aborting the fold. The three sibling name tables (StackFold/CodeGenerator
// ReadM3VarArg, MethodRefResolver.ReadM3VarArg) already consume the index
// (GN-12/GN-13); this is the one lagging arm in the same family.
//
// GetDelegateForFunctionPointer<Func<T,int>> is the P-D2 #3 GDFFP path (a
// SUPPORTED generic instantiation -- GenericArgFullNames names the Func`2
// GenericInst argument). The null pointer is caught so the probe output is
// independent of the runtime's null-fnptr exception choice; the point is that
// the enclosing body folds (no M5 stub) and runs.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases {

class HostF<T> {
    public bool Make() {
        try { Marshal.GetDelegateForFunctionPointer<Func<T, int>>(IntPtr.Zero); }
        catch (Exception) { }
        return true;
    }
}

public static class PGn14Gdffp {
    public static void Main() {
        var h = new HostF<string>();
        Console.WriteLine(h.Make());
    }
}}
//------------------------------------------------------------------------------
// ipaudit2.cs -- Marshal.SizeOf bool/char probe (G6 audit J).
//
// net48 default CharSet is Ansi: Marshal.SizeOf(typeof(bool)) == 4 (the Win32
// 4-byte BOOL) and Marshal.SizeOf(typeof(char)) == 1 (Ansi char), while
// sizeof(bool)==1 / sizeof(char)==2 in managed memory. The ILC runtime records
// the managed sizes instead (marshalext2.cpp:59-82: Boolean -> 1, Char -> 2),
// and the object form (icall_marshal_size_of_object, marshalext4.cpp:148-157)
// reuses the same table, so SizeOf((object)true) would report 1 (net48: 4) and
// SizeOf((object)'x') would report 2 (net48: 1). The typeof(Type) form goes
// through the registered-struct name lookup
// (il2cpp_marshal_size_of_struct_named), which only knows Point/Rectangle/
// Message -- a non-registered type raises NotSupportedException instead of
// returning the marshal size (a further divergence).
//
// Every line prints a raw int so the oracle/ILC diff is byte-stable.
//
// net48 reference output:
//   bool_type=4
//   char_type=1
//   bool_obj=4
//   char_obj=1
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases { public static class IpAudit2 {
    public static void Main() {
        Console.WriteLine("bool_type=" + Marshal.SizeOf(typeof(bool)));
        Console.WriteLine("char_type=" + Marshal.SizeOf(typeof(char)));
        Console.WriteLine("bool_obj=" + Marshal.SizeOf((object)true));
        Console.WriteLine("char_obj=" + Marshal.SizeOf((object)'x'));
    }
}}

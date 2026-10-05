//------------------------------------------------------------------------------
// ipaudit3.cs -- Marshal.SizeOf registered-struct family probe (G6 audit J).
//
// net48 x64 reference: Rectangle=16, Point=8, System.Windows.Forms.Message=40
// (Message fields hwnd/msg/wParam/lParam/result, x64 padded). The ILC layout
// registry (pinvokestruct.cpp / il2cpp_pinvoke_struct.h) models the Win32
// tagMSG layout instead: hwnd/message/wParam/lParam/time/pt and is
// pointer-width-dependent -- 48 on x64 / 28 on x86. That registry is the
// runtime's single layout authority for the SizeOf(typeof(X)) icalls
// (il2cpp_marshal_size_of_struct_named), so Message is expected to report 48
// on x64 while net48 reports 40 -- the documented divergence recorded but
// deliberately NOT probed in marshaldeep.cs:45-52; this case is the probe.
// Rectangle/Point are expected to match net48 byte-for-byte (16/8).
//
// net48 x64 reference output:
//   rect=16
//   point=8
//   msg=40
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases { public static class IpAudit3 {
    public static void Main() {
        Console.WriteLine("rect=" + Marshal.SizeOf(typeof(System.Drawing.Rectangle)));
        Console.WriteLine("point=" + Marshal.SizeOf(typeof(System.Drawing.Point)));
        Console.WriteLine("msg=" + Marshal.SizeOf(typeof(System.Windows.Forms.Message)));
    }
}}

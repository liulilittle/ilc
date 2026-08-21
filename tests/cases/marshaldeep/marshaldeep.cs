//------------------------------------------------------------------------------
// marshaldeep.cs -- System.Runtime.InteropServices.Marshal DOMAIN deep probe 2
// (e2e batch C45): the WIRED marshal surface -- AllocHGlobal/FreeHGlobal
// memory allocation, Copy(int[]/byte[] <-> native) as the M4 "struct = block of
// consecutive int32 slots" round-trip, PtrToStringAnsi/Uni +
// StringToHGlobalUni, UnsafeAddrOfPinnedArrayElement -- byte-exact vs the
// net48 oracle.
//
// D-MAR-1 (2026-08-12): the SizeOf/OffsetOf sections (S10/S11) pin the
// REGISTERED struct family semantics against net48 -- System.Drawing.Rectangle
// (16 bytes; private fields x/y/width/height at 0/4/8/12) and
// System.Drawing.Point (8 bytes; x/y at 0/4). Both the generic forms
// (SizeOf<T>() / OffsetOf<T>(string)) and the Type forms (SizeOf(typeof(T)) /
// OffsetOf(typeof(T), string)) are exercised. net48 field-name discipline:
// OffsetOf is FIELD-based and case-sensitive -- "x"/"y"/"width"/"height" are
// the declared (private) field names; "X"/"Width" are properties and THROW.
// System.Windows.Forms.Message is deliberately NOT probed: its registry MSG
// layout (Win32 tagMSG, 48/28) diverges from net48's Message (result field,
// 40/20) -- documented in il2cpp_pinvoke_struct.h.
//
// Wired expression surface exercised (MethodMap.Marshal.cs, runtime.cpp /
// pinvokeext.cpp / marshalext3.cpp):
//   AllocHGlobal(int)                       -> icall_marshal_alloc_hglobal
//   FreeHGlobal(IntPtr)                     -> icall_marshal_free_hglobal
//   Copy(int[]/byte[] src, start, IntPtr, len) / Copy(IntPtr, int[]/byte[],
//        start, len)                        -> copy_to/from_unmanaged (int32
//        stride -- the M4 struct-buffer model)
//   PtrToStringAnsi(IntPtr)                 -> il2cpp_pinvoke_string_from_utf8
//   StringToHGlobalUni(string)              -> il2cpp_pinvoke_string_to_utf16_hglobal
//   PtrToStringUni(IntPtr)                  -> il2cpp_pinvoke_string_from_utf16
//   UnsafeAddrOfPinnedArrayElement(Array,i) -> icall_marshal_unsafe_addr_of_pinned_array_element
//   SizeOf<T>() / SizeOf(Type)              -> il2cpp_marshal_size_of_struct_* /
//                                              il2cpp_marshal_size_of_struct_named
//   OffsetOf<T>(string) / OffsetOf(Type,s)  -> il2cpp_marshal_offset_of_struct_* /
//                                              il2cpp_marshal_offset_of_struct_named
//   IntPtr.ToInt64() / IntPtr op_Equality   -> il2cpp_intptr_* (intptr.h)
//   GetLastWin32Error()                     -> il2cpp_pinvoke_get_last_error
//
// net48-shape discipline (marshalcase/hglobalops precedent): the diff prints
// only bools / relative address deltas -- NEVER absolute pointer values
// (addresses are ASLR-randomized per run, so only deltas within ONE array are
// asserted: elem i+1 is exactly +4 bytes from elem i in an int[]). The
// GetLastWin32Error value is timing-dependent: only its int SHAPE is checked.
//
// RECORDED DIVERGENCES (documented, NOT probed): PtrToStructure /
// StructureToPtr remain LOUD -- the byte semantics exist in the runtime
// (il2cpp_marshal_struct_*_load/store, marshalext3.cpp, unit-tested in
// test_marshalstruct.cpp), but a PtrToStructure result / ref-T argument needs
// a struct-typed local/result temp, which requires the CodeGenerator
// TypeCppName / result-kind extension (read-only file -- see
// MethodMap.Marshal.cs). System.Windows.Forms.Message SizeOf/OffsetOf follow
// the registry's Win32 MSG layout, not net48's Message. StringToHGlobalAnsi
// also LOUD-rejects (il2cpp_marshal_ext2.h absent from the generated include
// chain) -- only the UTF-16 hglobal + ANSI readback face is wired.
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases { public static class MarshalDeep {
    public static void Main()
    {
        // ============ S1: AllocHGlobal memory allocation ============
        IntPtr p = Marshal.AllocHGlobal(4 * 3);      // 3 int32 slots
        Console.WriteLine("alloc_nz=" + (p.ToInt64() != 0));
        Marshal.FreeHGlobal(p);
        Console.WriteLine("free_ok=" + true);

        // ============ S2: struct round-trip via int[] Copy (M4 slots) ============
        IntPtr b = Marshal.AllocHGlobal(4 * 3);
        int[] src = { 7, 8, 9 };                     // a 3-field int32 struct
        Marshal.Copy(src, 0, b, 3);
        int[] back = new int[3];
        Marshal.Copy(b, back, 0, 3);
        Console.WriteLine("rt_fields=" + (back[0] == 7 && back[1] == 8 && back[2] == 9));
        Marshal.FreeHGlobal(b);

        // ============ S3: Copy offset/partial variants ============
        IntPtr o = Marshal.AllocHGlobal(4 * 4);
        int[] src2 = { 1, 2, 3, 4, 5 };
        Marshal.Copy(src2, 2, o, 2);                 // native fields {3,4}
        int[] back2 = new int[2];
        Marshal.Copy(o, back2, 0, 2);
        Console.WriteLine("off_fields=" + (back2[0] == 3 && back2[1] == 4));
        Marshal.FreeHGlobal(o);

        // ============ S4: byte[] copy round-trip ============
        IntPtr c = Marshal.AllocHGlobal(4);
        byte[] bs = { 11, 22, 33, 44 };
        Marshal.Copy(bs, 0, c, 4);
        byte[] bc = new byte[4];
        Marshal.Copy(c, bc, 0, 4);
        Console.WriteLine("bytes_rt=" + (bc[0] == 11 && bc[3] == 44));
        Marshal.FreeHGlobal(c);

        // ============ S5: PtrToStringAnsi on a native buffer ============
        IntPtr q = Marshal.AllocHGlobal(8);
        Marshal.Copy(new int[] { 0x00006968, 0 }, 0, q, 2); // "hi\0\0"
        Console.WriteLine("ansi_hi=" + (Marshal.PtrToStringAnsi(q) == "hi"));
        Marshal.FreeHGlobal(q);

        // ============ S6: StringToHGlobalUni <-> PtrToStringUni round-trip ============
        IntPtr h = Marshal.StringToHGlobalUni("abc");
        Console.WriteLine("uni_abc=" + (Marshal.PtrToStringUni(h) == "abc"));
        Marshal.FreeHGlobal(h);
        IntPtr h2 = Marshal.StringToHGlobalUni("d\u00E9f\u4E2D"); // accented + BMP
        Console.WriteLine("uni_bmp=" + (Marshal.PtrToStringUni(h2) == "d\u00E9f\u4E2D"));
        Marshal.FreeHGlobal(h2);
        IntPtr hn = Marshal.StringToHGlobalUni(null);
        Console.WriteLine("uni_null=" + (hn == IntPtr.Zero));
        Marshal.FreeHGlobal(hn);

        // ============ S7: UnsafeAddrOfPinnedArrayElement (relative deltas) ============
        int[] arr = { 10, 20, 30, 40 };
        IntPtr a0 = Marshal.UnsafeAddrOfPinnedArrayElement(arr, 0);
        IntPtr a1 = Marshal.UnsafeAddrOfPinnedArrayElement(arr, 1);
        IntPtr a3 = Marshal.UnsafeAddrOfPinnedArrayElement(arr, 3);
        Console.WriteLine("addr_nz=" + (a0.ToInt64() != 0));
        Console.WriteLine("addr_delta4=" + (a1.ToInt64() - a0.ToInt64() == 4));
        Console.WriteLine("addr_delta12=" + (a3.ToInt64() - a0.ToInt64() == 12));

        // ============ S8: GetLastWin32Error shape (value is timing dependent) ============
        int e = Marshal.GetLastWin32Error();
        Console.WriteLine("err_shape=" + (e >= int.MinValue && e <= int.MaxValue));

        // ============ S9: alloc/free pairing stress (no crash) ============
        bool ok = true;
        for (int i = 0; i < 20; i++)
        {
            IntPtr m = Marshal.AllocHGlobal(16);
            if (m.ToInt64() == 0) ok = false;
            Marshal.FreeHGlobal(m);
        }
        Console.WriteLine("stress=" + ok);

        // ============ S10: SizeOf/OffsetOf, GENERIC forms (registered struct
        // family -- D-MAR-1) ============
        int szRect = Marshal.SizeOf<System.Drawing.Rectangle>();
        int szPoint = Marshal.SizeOf<System.Drawing.Point>();
        Console.WriteLine("size_rect=" + (szRect == 16));
        Console.WriteLine("size_point=" + (szPoint == 8));
        // net48: OffsetOf returns IntPtr (native int), not int.
        long offRectY = Marshal.OffsetOf<System.Drawing.Rectangle>("y").ToInt64();
        long offRectH = Marshal.OffsetOf<System.Drawing.Rectangle>("height").ToInt64();
        long offPointX = Marshal.OffsetOf<System.Drawing.Point>("x").ToInt64();
        Console.WriteLine("off_rect_y=" + (offRectY == 4));
        Console.WriteLine("off_rect_h=" + (offRectH == 12));
        Console.WriteLine("off_point_x=" + (offPointX == 0));

        // ============ S11: SizeOf/OffsetOf, Type forms (typeof folds to the
        // full-name string -- the ILC Type VALUE) ============
        int szRectT = Marshal.SizeOf(typeof(System.Drawing.Rectangle));
        int szPointT = Marshal.SizeOf(typeof(System.Drawing.Point));
        Console.WriteLine("size_rect_type=" + (szRectT == 16));
        Console.WriteLine("size_point_type=" + (szPointT == 8));
        long offRectW = Marshal.OffsetOf(typeof(System.Drawing.Rectangle), "width").ToInt64();
        long offPointY = Marshal.OffsetOf(typeof(System.Drawing.Point), "y").ToInt64();
        Console.WriteLine("off_rect_type_w=" + (offRectW == 8));
        Console.WriteLine("off_point_type_y=" + (offPointY == 4));
    }
}}

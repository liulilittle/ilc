//------------------------------------------------------------------------------
// ipaudit1.cs -- Marshal.StructureToPtr fDeleteOld=true probe (G6 audit J)
//                + Marshal.Read/Write primitive family coverage (marshalrw).
//
// Audit finding (marshalext3.cpp, il2cpp_marshal_struct_store): when
// fDeleteOld is true the runtime must NOT free the caller-owned block; the
// net48 contract only releases embedded reference members (a no-op for a
// blittable struct). The store icall implements exactly that.
//
// The probe: AllocHGlobal a block, StructureToPtr a one-int struct with
// fDeleteOld=true, then read the int back through the ORIGINAL pointer.
// net48 keeps the block: readback == 0x55667788.
//
// marshalrw (2026-09-13): one exercise per Marshal.Read/Write primitive type.
// The 3-arg Write forms take an explicit byte offset; the 2-arg Write form
// writes at offset 0; Read mirrors it (1-arg = offset 0). net48 semantics:
// no bounds checks and no exception paths -- the case stays inside its own
// 64-byte buffer, so only the values matter.
//
// net48 reference output (oracle):
//   ptr_nonzero=True
//   byte=171
//   int16=-12345
//   int32=287454020
//   int64_ok=True
//   intptr_ok=True
//   readback=1432778632
//   done=True
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases { public static class IpAudit1 {
    [StructLayout(LayoutKind.Sequential)]
    private struct OneInt { public int Value; }

    public static void Main() {
        IntPtr p = Marshal.AllocHGlobal(64);
        Console.WriteLine("ptr_nonzero=" + (p.ToInt64() != 0));

        Marshal.WriteByte(p, 0, 0xAB);
        int b = Marshal.ReadByte(p, 0);
        Console.WriteLine("byte=" + b);

        Marshal.WriteInt16(p, 2, -12345);
        int s = Marshal.ReadInt16(p, 2);
        Console.WriteLine("int16=" + s);

        Marshal.WriteInt32(p, 4, 0x11223344);
        int i = Marshal.ReadInt32(p, 4);
        Console.WriteLine("int32=" + i);

        Marshal.WriteInt64(p, 0x1122334455667788L);
        long l = Marshal.ReadInt64(p);
        Console.WriteLine("int64_ok=" + (l == 0x1122334455667788L));

        Marshal.WriteIntPtr(p, 16, new IntPtr(0x0DEADBEEFL));
        IntPtr q = Marshal.ReadIntPtr(p, 16);
        Console.WriteLine("intptr_ok=" + (q.ToInt64() == 0x0DEADBEEFL));

        Marshal.WriteInt32(p, 0);
        OneInt one = new OneInt();
        one.Value = 0x55667788;
        Marshal.StructureToPtr(one, p, true);
        int back = Marshal.ReadInt32(p);
        Console.WriteLine("readback=" + back);
        Marshal.FreeHGlobal(p);
        Console.WriteLine("done=True");
    }
}}

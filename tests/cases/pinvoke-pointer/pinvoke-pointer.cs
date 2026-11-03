// pinvoke-pointer.cs -- UNSAFE POINTER parameters and returns across a
// DllImport boundary (the "P:" signature encoding).
//
// net48 does NO marshalling here at all: a pointer parameter is already a raw
// address, so the callee sees the caller's exact address, writes through it
// change managed storage, and a pointer the callee returns keeps its full
// 64-bit value. Before this case the transpiler LOUD-rejected `P:` params and
// silently declared `P:` returns int32_t (an x64 address truncation).
//
// Every value printed below is a property that only holds if the address
// really crossed the boundary intact.
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static unsafe class PInvokePointer
    {
        [DllImport("ptrlib", EntryPoint = "ptr_identity")]
        static extern int* PtrIdentity(int* p);

        [DllImport("ptrlib", EntryPoint = "ptr_deref")]
        static extern int PtrDeref(int* p);

        [DllImport("ptrlib", EntryPoint = "ptr_store")]
        static extern void PtrStore(int* p, int v);

        [DllImport("ptrlib", EntryPoint = "ptr_table")]
        static extern int* PtrTable();

        [DllImport("ptrlib", EntryPoint = "ptr_at")]
        static extern int* PtrAt(int* p, int n);

        [DllImport("ptrlib", EntryPoint = "ptr_void_identity")]
        static extern void* PtrVoidIdentity(void* p);

        [DllImport("ptrlib", EntryPoint = "ptr_void_deref")]
        static extern int PtrVoidDeref(void* p);

        [DllImport("ptrlib", EntryPoint = "ptr_slot")]
        static extern int* PtrSlot();

        public static void Main()
        {
            int v = 41;
            int* pv = &v;

            // identity: the callee must see the exact same address
            int* back = PtrIdentity(pv);
            Console.WriteLine("ident.same=" + ((long)back == (long)pv ? 1 : 0));

            // deref: the callee reads the MANAGED storage
            Console.WriteLine("deref=" + PtrDeref(pv));
            Console.WriteLine("deref.null=" + PtrDeref(null));

            // store: the callee write is visible in managed storage
            PtrStore(pv, 7);
            Console.WriteLine("store.seen=" + v);

            // a pointer RETURNED by native code keeps its 64-bit width
            int* t = PtrTable();
            Console.WriteLine("table.width64=" + ((long)t > 0xFFFFFFFFL ? 1 : 0));
            Console.WriteLine("table.0=" + t[0]);
            Console.WriteLine("table.3=" + t[3]);

            // native pointer arithmetic, then managed indexing
            int* t2 = PtrAt(t, 2);
            Console.WriteLine("at.2=" + t2[0]);
            Console.WriteLine("at.delta=" + ((long)t2 - (long)t));

            // void* round-trip and cast-back deref
            void* vp = PtrVoidIdentity(pv);
            Console.WriteLine("void.same=" + ((long)vp == (long)pv ? 1 : 0));
            Console.WriteLine("void.deref=" + PtrVoidDeref(pv));
            Console.WriteLine("void.null=" + (PtrVoidIdentity(null) == null ? 1 : 0));

            // a native pointer to a native single slot
            int* s = PtrSlot();
            Console.WriteLine("slot=" + *s);
        }
    }
}

//------------------------------------------------------------------------------
// hglobalops.cs -- HGlobal family deep probe (e2e batch C28, "hglobal deep probe:
// StringToHGlobalUni / GlobalFree pairing -- after the GlobalAlloc fix"). Built AFTER
// commit 177bbe0 (GlobalAlloc/GMEM_FIXED on the Windows hglobal alloc side +
// GlobalFree on the free side) to VERIFY the allocator pairing end-to-end and
// probe its edges WITHOUT a clipboard/window (clipops already covers the
// SetClipboardData path -- this case is the pure alloc/readback/free surface).
//
// The runtime surface under test (all Windows-path, 177bbe0):
//   * Marshal.StringToHGlobalUni   -> il2cpp_pinvoke_string_to_utf16_hglobal
//     (pinvokeext.cpp:216): GlobalAlloc(GMEM_FIXED|GMEM_ZEROINIT), a DIRECT
//     pointer usable both as a char16_t* AND as a global handle.
//   * Marshal.PtrToStringUni       -> il2cpp_pinvoke_string_from_utf16
//     (pinvokeext.cpp, "p16" cast): reads the UTF-16 buffer until the NUL.
//   * Marshal.AllocHGlobal         -> icall_marshal_alloc_hglobal
//     (runtime.cpp:132): GlobalAlloc(GMEM_FIXED|GMEM_ZEROINIT) on Windows.
//   * Marshal.FreeHGlobal          -> icall_marshal_free_hglobal
//     (runtime.cpp:148): GlobalFree on Windows (matches the alloc side).
//   * Marshal.Copy(int[],...)      -> icall_marshal_copy_to/from_unmanaged
//     (runtime.cpp, int32 stride): int[] payload read/write through the
//     pointer carrier.
//   * kernel32 GlobalFree P/Invoke -> the SAME global-heap free the clipboard
//     uses; calling it on a StringToHGlobalUni buffer must succeed (returns
//     NULL on success) -- the exact pairing that was 0xC0000374 before the fix.
//
// net48 ground truth: StringToHGlobalUni/AllocHGlobal on .NET Framework are
// GlobalAlloc (LMEM_FIXED) backed, so GlobalFree is the correct free -- every
// pairing probe is True. The ILC side diverges ONLY if the allocator family
// regresses (a malloc'd pointer handed to GlobalFree = heap corruption).
// Note: Marshal.StringToHGlobalAnsi is deliberately NOT probed -- the
// transpiler mapping throws NotSupportedException (MethodMap.Marshal.cs:151,
// il2cpp_marshal_ext2.h absent from the generated include chain) -- recorded,
// not used (a case using it would abort at transpile).
//------------------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;

namespace Cases
{
    public static class Hglobalops
    {
        // kernel32 GlobalFree: the global-heap free (returns NULL on success).
        // P/Invoke shape mirrors clipops.cs (GlobalFree(IntPtr) -> IntPtr).
        [DllImport("kernel32", EntryPoint = "GlobalFree")]
        public static extern IntPtr GlobalFree(IntPtr hMem);

        // round-trip helper: alloc Uni -> read back -> free (wired surface).
        static string HgRoundtrip(string s)
        {
            IntPtr h = Marshal.StringToHGlobalUni(s);
            string r = Marshal.PtrToStringUni(h);
            Marshal.FreeHGlobal(h);
            return r;
        }

        public static void Main()
        {
            // ============ S1: StringToHGlobalUni -> PtrToStringUni roundtrip ============
            Console.WriteLine(HgRoundtrip("Hello HG") == "Hello HG"); // True
            Console.WriteLine(HgRoundtrip("") == "");                 // True
            Console.WriteLine(HgRoundtrip("abc123") == "abc123");     // True
            Console.WriteLine(HgRoundtrip("m1 aA09 _-") == "m1 aA09 _-"); // True

            // ============ S2: null input -> IntPtr.Zero; FreeHGlobal(Zero) no-op ============
            IntPtr hn = Marshal.StringToHGlobalUni(null);
            Console.WriteLine(hn == IntPtr.Zero);  // True
            Marshal.FreeHGlobal(hn);               // no-op (null is a no-op)

            // ============ S3: GlobalFree pairing -- the 177bbe0 fix ============
            // StringToHGlobalUni's buffer IS a global handle: kernel32 GlobalFree
            // must free it and return NULL (0) on success.
            IntPtr h1 = Marshal.StringToHGlobalUni("gf-pair");
            Console.WriteLine(h1 != IntPtr.Zero);  // True
            IntPtr gf1 = GlobalFree(h1);
            Console.WriteLine(gf1 == IntPtr.Zero); // True (freed from the same global heap)

            // ============ S4: AllocHGlobal + int[] Copy roundtrip ============
            IntPtr p = Marshal.AllocHGlobal(4 * 4);
            int[] src = { 10, 20, 30, 40 };
            Marshal.Copy(src, 0, p, 4);
            int[] dst = new int[4];
            Marshal.Copy(p, dst, 0, 4);
            Marshal.FreeHGlobal(p);
            Console.WriteLine(dst[0] == 10 && dst[1] == 20 && dst[2] == 30 && dst[3] == 40); // True

            // ============ S5: PtrToStringAnsi on an ASCII buffer ============
            // Write "AAAA\0" (4 A's + 4 NUL bytes) via int[] copy, read back
            // with PtrToStringAnsi (UTF-8 decode of pure ASCII == identity).
            IntPtr ab = Marshal.AllocHGlobal(8);
            int[] ascii = { 0x41414141, 0 };
            Marshal.Copy(ascii, 0, ab, 2);
            string ansiRead = Marshal.PtrToStringAnsi(ab);
            Marshal.FreeHGlobal(ab);
            Console.WriteLine(ansiRead == "AAAA"); // True

            // ============ S6: stress -- 50 alloc/free Uni pairs (no crash) ============
            bool ok = true;
            for (int i = 0; i < 50; i++)
            {
                string s = "stress-" + i;
                IntPtr h = Marshal.StringToHGlobalUni(s);
                string r = Marshal.PtrToStringUni(h);
                if (r != s) ok = false;
                Marshal.FreeHGlobal(h);
            }
            Console.WriteLine(ok); // True

            // ============ S7: mixed alloc family interleave (Uni + HGlobal) ============
            bool ok2 = true;
            for (int i = 0; i < 20; i++)
            {
                IntPtr hu = Marshal.StringToHGlobalUni("u" + i);
                IntPtr hb = Marshal.AllocHGlobal(16);
                if (hu == IntPtr.Zero || hb == IntPtr.Zero) ok2 = false;
                Marshal.FreeHGlobal(hu);
                Marshal.FreeHGlobal(hb);
            }
            Console.WriteLine(ok2); // True
        }
    }
}

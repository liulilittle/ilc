//------------------------------------------------------------------------------
// resprobe.cs -- RESOURCE PROBE e2e reference case (net48 oracle, byte-for-byte).
// Created in R1 (2026-10-07), resource-batch A -- pins the THREE embedded
// resource forms (string / image / large-binary blob) as a net48 byte-level
// oracle. It is the R1 foundation that later R2 (satellite culture chain) and
// R3 (ResourceManager.GetObject/GetStream) stages build on.
//
// The three forms, all embedded into a single exe:
//   1. STRING key/value pairs -- Strings.resources -> the .resources blob,
//      queried through ResourceManager.GetString (invariant). Hit /
//      missing-key -> null.
//   2. IMAGE bytes -- img.png embedded as a RAW manifest resource
//      (LogicalName resprobe.img.png), returned by GetManifestResourceStream
//      as original payload bytes, verified byte-for-byte as hex + Length.
//   3. LARGE BINARY blob -- blob.bin (>1KB, 2048 bytes) embedded raw
//      (LogicalName resprobe.blob.bin), stream-read in full, verified
//      byte-for-byte as hex + Length -- proves the big-blob path beyond the
//      32-byte resdeep snapshot.
//
// R3 (batch B): the SAME .resources blob carries a ByteArray(32) entry
// ("imgBytesFile" -- the png as System.Byte[]) and a Stream(33) entry
// ("imgStream" -- the same png via ResXFileRef/MemoryStream). The probes run
// ResourceManager.GetObject (string + byte[] + miss) and ResourceManager
// .GetStream (stream, byte-identity hex). net48 GetObject for the byte[]
// returns System.Byte[]; GetStream returns the read-only stream; both must
// reproduce byte-for-byte on the ILC runtime.
//
// Every query is either a bool line (a diff failure shows exactly which
// assertion flipped) or the hex/length of the payload (a diff failure shows
// the exact byte drift). Hex output is lowercase fixed-width ("x2"), fully
// culture-invariant, so net48 and the ILC runtime must agree byte-for-byte.
//------------------------------------------------------------------------------
using System;
using System.IO;
using System.Resources;

namespace Cases
{
    public static class Resprobe
    {
        private static string Hex(byte[] data)
        {
            // hex via the int ToString("x2") path -- the byte -> int widening is
            // value-identical for 0..255 and stays inside the transpiler's
            // supported numeric-format subset (fmtround/int64deep prove
            // int/long "x2"). byte.ToString("x2") itself is an unrelated BCL
            // numeric-format mapping gap (MethodMap.Format custom branch has no
            // System.Byte arm), NOT a resource-chain gap -- R1 pins the RESOURCE
            // bytes, so the probe stays on the supported format surface.
            var sb = new System.Text.StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++) sb.Append(((int)data[i]).ToString("x2"));
            return sb.ToString();
        }

        public static void Main()
        {
            System.Reflection.Assembly asm = typeof(Resprobe).Assembly;

            // ===== 1. GetManifestResourceNames -- all three forms present =====
            string[] names = asm.GetManifestResourceNames();
            Console.WriteLine(names.Length == 3);                          // True (strings + img + blob)
            bool hasRes = false, hasImg = false, hasBlob = false;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == "resprobe.Strings.resources") hasRes = true;
                if (names[i] == "resprobe.img.png") hasImg = true;
                if (names[i] == "resprobe.blob.bin") hasBlob = true;
            }
            Console.WriteLine(hasRes && hasImg && hasBlob);               // True (all three names)

            // ===== 2. STRING form -- ResourceManager.GetString (invariant) =====
            ResourceManager rm = new ResourceManager("resprobe.Strings", asm);
            Console.WriteLine(rm.GetString("Greeting") == "Hello Resprobe");  // True (hit)
            Console.WriteLine(rm.GetString("AppName") == "ResprobeFixture");  // True (second key)
            Console.WriteLine(rm.GetString("Missing") == null);               // True (miss -> null)

            // ===== 3. IMAGE form -- img.png via GetManifestResourceStream =====
            Stream sImg = asm.GetManifestResourceStream("resprobe.img.png");
            Console.WriteLine(sImg != null);                             // True (hit)
            byte[] img = new byte[(int)sImg.Length];
            int nImg = sImg.Read(img, 0, img.Length);
            Console.WriteLine(nImg == img.Length);                       // True (full read)
            Console.WriteLine(img.Length);                               // 214 (payload size)
            Console.WriteLine(Hex(img));                                 // full png bytes as hex

            // ===== 4. LARGE BINARY form -- blob.bin via GetManifestResourceStream =====
            Stream sBlob = asm.GetManifestResourceStream("resprobe.blob.bin");
            Console.WriteLine(sBlob != null);                            // True (hit)
            Console.WriteLine(sBlob.Length);                             // 2048 (>1KB, beyond 32B snapshot)
            byte[] blob = new byte[(int)sBlob.Length];
            int nBlob = sBlob.Read(blob, 0, blob.Length);
            Console.WriteLine(nBlob == blob.Length);                     // True (full read)
            Console.WriteLine(Hex(blob));                                // full 2048-byte blob as hex

            // ===== 5. Miss semantics =====
            Console.WriteLine(asm.GetManifestResourceStream("resprobe.no.such") == null); // True (miss -> null)

// ===== 6. R7 -- stream buffer-boundary semantics (net48 oracle) =====
            // net48 GetManifestResourceStream returns a read-only
            // UnmanagedMemoryStream; the ILC runtime returns a MemoryStream-
            // backed object (resdeep GAPS D-RD-1). The class IDENTITY is a
            // documented divergence -- the probe AVOIDS it (probe avoidance)
            // and pins the BUFFER-BOUNDS contract that net48 validates at
            // call time (Read 越界抛; Position 越界等):
            //   * offset/count negative            -> ArgumentOutOfRangeException
            //   * offset + count > buffer.Length   -> ArgumentException
            //   * count beyond the stream remainder but INSIDE the buffer ->
            //     fewer bytes (returned, no throw -- "does not clamp to the
            //     stream's remaining bytes")
            //   * Read at end-of-stream            -> 0
            //   * null buffer                      -> ArgumentNullException
            //   * Position negative                -> ArgumentOutOfRangeException
            //   * Position beyond the end          -> allowed; next Read -> 0
            // Every error row prints e.GetType().Name so a wrong exception
            // TYPE flips the oracle diff byte-for-byte.

            // 6a. count > buffer.Length -> ArgumentException, Position unchanged
            Stream sB = asm.GetManifestResourceStream("resprobe.blob.bin");
            Console.WriteLine(sB != null);                              // True (hit)
            Console.WriteLine(sB.Length);                               // 2048
            byte[] small = new byte[4];
            try { sB.Read(small, 0, 100); Console.WriteLine("rb-count-no-throw"); }
            catch (Exception e) { Console.WriteLine(e.GetType().Name); } // ArgumentException
            Console.WriteLine(sB.Position);                             // 0 (unchanged)

            // 6b. offset + count > buffer.Length -> ArgumentException
            Stream sC = asm.GetManifestResourceStream("resprobe.blob.bin");
            byte[] buf10 = new byte[10];
            try { sC.Read(buf10, 8, 5); Console.WriteLine("rb-off-no-throw"); }
            catch (Exception e) { Console.WriteLine(e.GetType().Name); } // ArgumentException
            Console.WriteLine(sC.Position);                             // 0 (unchanged)

            // 6c. count beyond the stream remainder but inside the buffer:
            // the read returns the AVAILABLE bytes (8), it does not throw.
            sC.Position = 2040;                                         // 8 bytes remain
            byte[] big = new byte[64];
            int nBig = sC.Read(big, 0, 64);
            Console.WriteLine(nBig == 8);                               // True (8 of 64)
            Console.WriteLine(sC.Position);                             // 2048

            // 6d. Read at end-of-stream -> 0
            int nEof = sC.Read(big, 0, 64);
            Console.WriteLine(nEof == 0);                               // True (EOF -> 0)
            Console.WriteLine(sC.Position);                             // 2048

            // 6e. negative offset -> ArgumentOutOfRangeException
            try { sC.Read(big, -1, 0); Console.WriteLine("rb-negoff-no-throw"); }
            catch (Exception e) { Console.WriteLine(e.GetType().Name); } // ArgumentOutOfRangeException

            // 6f. negative count -> ArgumentOutOfRangeException
            try { sC.Read(big, 0, -1); Console.WriteLine("rb-negcount-no-throw"); }
            catch (Exception e) { Console.WriteLine(e.GetType().Name); } // ArgumentOutOfRangeException

            // 6g. null buffer -> ArgumentNullException
            try { sC.Read(null, 0, 0); Console.WriteLine("rb-null-no-throw"); }
            catch (Exception e) { Console.WriteLine(e.GetType().Name); } // ArgumentNullException

            // 6h. Position = -1 -> ArgumentOutOfRangeException
            try { sC.Position = -1; Console.WriteLine("pos-neg-no-throw"); }
            catch (Exception e) { Console.WriteLine(e.GetType().Name); } // ArgumentOutOfRangeException

            // 6i. Position beyond the end is allowed; the next Read -> 0
            sC.Position = 9999;
            byte[] nb4 = new byte[4];
            int nFar = sC.Read(nb4, 0, 4);
            Console.WriteLine(nFar == 0);                               // True (EOF at far pos)
            Console.WriteLine(sC.Position);                             // 9999

            // 6j. offset == buffer.Length, count == 0 -> 0 (no throw)
            Stream sJ = asm.GetManifestResourceStream("resprobe.blob.bin");
            byte[] lb4 = new byte[4];
            int nL0 = sJ.Read(lb4, 4, 0);
            Console.WriteLine(nL0 == 0);                                // True

            // 6k. offset == buffer.Length, count > 0 -> ArgumentException
            try { sJ.Read(lb4, 4, 1); Console.WriteLine("rb-edge-no-throw"); }
            catch (Exception e) { Console.WriteLine(e.GetType().Name); } // ArgumentException
            // ===== 7. R3 -- ResourceManager.GetObject / GetStream =====
            // String resource via GetObject (net48 returns the string itself).
            Console.WriteLine(((string)rm.GetObject("Greeting")) == "Hello Resprobe"); // True (string through GetObject)
            // ByteArray(32) resource via GetObject -> non-null (the byte[] is
            // the EXACT png payload -- byte identity is asserted at the runtime
            // unit-test level, test_resources.cpp BytesEqual, and cross-checked
            // below against the stream form).
            Console.WriteLine(rm.GetObject("imgBytesFile") != null);         // True (byte[] hit)
            Console.WriteLine(rm.GetObject("Missing") == null);              // True (miss -> null)
            // Stream(33) resource via GetStream -- byte-for-byte identity with
            // the raw manifest form above (the SAME png, so this hex line MUST
            // equal the section-3 img hex line).
            Stream sR3 = rm.GetStream("imgStream");
            Console.WriteLine(sR3 != null);                                  // True (stream hit)
            byte[] r3 = new byte[(int)sR3.Length];
            int nR3 = sR3.Read(r3, 0, r3.Length);
            Console.WriteLine(nR3 == r3.Length);                             // True (full read)
            Console.WriteLine(r3.Length);                                    // 214 (payload size)
            Console.WriteLine(Hex(r3));                                      // full png bytes as hex
        }
    }
}

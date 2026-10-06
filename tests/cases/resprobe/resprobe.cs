//------------------------------------------------------------------------------
// resprobe.cs -- RESOURCE PROBE e2e reference case (net48 oracle, byte-for-byte).
// Created in R1 (2026-10-07), resource-batch A -- pins the THREE embedded
// resource forms (string / image / large-binary blob) as a net48 byte-level
// oracle. It is the R1 foundation that later R2 (satellite culture chain) and
// R3 (ResourceManager.GetObject/GetStream) stages build on.
//
// The three forms, all embedded into a single exe:
//   1. STRING key/value pairs -- Strings.resx -> the .resources blob, queried
//      through ResourceManager.GetString (invariant). Hit / missing-key -> null.
//   2. IMAGE bytes -- img.png embedded as a RAW manifest resource
//      (LogicalName resprobe.img.png), returned by GetManifestResourceStream
//      as original payload bytes, verified byte-for-byte as hex + Length.
//   3. LARGE BINARY blob -- blob.bin (>1KB, 2048 bytes) embedded raw
//      (LogicalName resprobe.blob.bin), stream-read in full, verified
//      byte-for-byte as hex + Length -- proves the big-blob path beyond the
//      32-byte resdeep snapshot.
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
        }
    }
}

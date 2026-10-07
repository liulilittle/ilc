//------------------------------------------------------------------------------
// resr6.cs -- R6 NON-ASCII KEY / BASE-NAME e2e reference case (net48 oracle,
// byte-for-byte). Resource batch C (W11), 2026-10-07.
//
// R6 upgrades the resource key/base-name comparison to UTF-8: the runtime now
// encodes a managed lookup string to UTF-8 and compares it ordinally against
// the .resources name section (UTF-16 -> UTF-8) and the registry names (UTF-8
// manifest names). This case pins the whole non-ASCII surface against net48:
//
//   1. NON-ASCII KEYS in the invariant .resources blob, through
//      ResourceManager.GetString: Latin-1 ("Greeting_de" / "Schlüssel"),
//      CJK ("中文键"), Cyrillic ("Русский"). Values are non-ASCII too
//      ("Überraschung", "中文值", "Привет") so the value byte path is pinned
//      end-to-end.
//   2. ORDINAL miss semantics: a case-mutated key ("GREETING_DE") and a
//      prefix key ("中键") are BOTH null (net48 key lookup is ordinal -- no
//      case folding, no prefix match), so the UTF-8 compare must be exact.
//   3. NON-ASCII MANIFEST resource name: "resr6.数据.bin" is embedded raw;
//      GetManifestResourceNames must contain it and
//      GetManifestResourceStream("resr6.数据.bin") must return the exact
//      payload bytes (registry lookup is UTF-8 strcmp).
//   4. NON-ASCII BASE NAME: a ResourceManager over "resr6.Résumé" resolves
//      "resr6.Résumé.resources" (the ctor validates the base name in UTF-8;
//      the chain builds the UTF-8 resource name).
//
// Every probe is a bool/hex/length line (all-ASCII output) so net48 and the
// ILC runtime agree byte-for-byte under the harness's UTF-8 capture pipe.
//------------------------------------------------------------------------------
using System;
using System.IO;
using System.Resources;

namespace Cases
{
    public static class Resr6
    {
        private static string Hex(byte[] data)
        {
            var sb = new System.Text.StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i++) sb.Append(((int)data[i]).ToString("x2"));
            return sb.ToString();
        }

        public static void Main()
        {
            System.Reflection.Assembly asm = typeof(Resr6).Assembly;

            // ===== 1. Non-ASCII keys via ResourceManager.GetString (invariant) =====
            ResourceManager rm = new ResourceManager("resr6.Strings", asm);
            Console.WriteLine(rm.GetString("Greeting") == "Hello R6");         // True (ASCII control key)
            Console.WriteLine(rm.GetString("Greeting_de") == "Überraschung");  // True (Latin-1 key, non-ASCII value)
            Console.WriteLine(rm.GetString("Schlüssel") == "wert");            // True (umlaut key)
            Console.WriteLine(rm.GetString("中文键") == "中文值");              // True (CJK key/value)
            Console.WriteLine(rm.GetString("Русский") == "Привет");            // True (Cyrillic key/value)

            // ===== 2. Ordinal miss semantics (UTF-8 compare is exact) =====
            Console.WriteLine(rm.GetString("GREETING_DE") == null);            // True (case-fold miss)
            Console.WriteLine(rm.GetString("中键") == null);                    // True (prefix key miss)

            // ===== 3. Non-ASCII manifest resource name =====
            string[] names = asm.GetManifestResourceNames();
            bool hasCn = false, hasStrings = false;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == "resr6.数据.bin") hasCn = true;
                if (names[i] == "resr6.Strings.resources") hasStrings = true;
            }
            Console.WriteLine(hasCn && hasStrings);                            // True (both names present)

            Stream s = asm.GetManifestResourceStream("resr6.数据.bin");
            Console.WriteLine(s != null);                                      // True (non-ASCII name hit)
            Console.WriteLine(s.Length);                                       // 10 (payload size)
            byte[] buf = new byte[(int)s.Length];
            int n = s.Read(buf, 0, buf.Length);
            Console.WriteLine(n == buf.Length);                                // True (full read)
            Console.WriteLine(Hex(buf));                                       // cafebabe01020304aa55
            Console.WriteLine(asm.GetManifestResourceStream("resr6.不存在.bin") == null); // True (unknown non-ASCII name -> null)

            // ===== 4. Non-ASCII base name (ResourceManager ctor + chain) =====
            ResourceManager rmb = new ResourceManager("resr6.Résumé", asm);
            Console.WriteLine(rmb.GetString("Greeting") == "Bonjour");         // True (non-ASCII base name hit)
            Console.WriteLine(rmb.GetString("Missing") == null);               // True (miss under non-ASCII base)
        }
    }
}

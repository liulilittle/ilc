//------------------------------------------------------------------------------
// resdeep.cs -- RESOURCE DEEP e2e reference case 2 (net48 oracle, byte-for-byte).
// Created in e2e batch C37 (2026-08-11), AFTER the resource-surface closure
// (commit 272bb57: GetManifestResourceStream now returns a real MemoryStream
// over the raw payload -- the resops case's stream receiver is usable for the
// FULL stream surface; commit c5e164e: the 2-arg GetString culture overload is
// b23-wired, NOT a fall-through).
//
// resops covered names/stream-hit/GetString/culture-fallback basics. THIS case
// goes DEEP on the STREAM receiver that the MemoryStream return now unlocks:
//
//   1. STREAM METADATA: Length == payload size, Position starts at 0,
//      CanRead == true -- the manifest stream is a seekable MemoryStream on
//      net48 and a MemoryStream-backed stream object on the ILC runtime.
//   2. FULL-PATH Read: a single Read(byte[], 0, len) pulls the WHOLE payload;
//      Position advances to the end; a further Read returns 0 (EOF contract).
//   3. CHUNKED Read: repeated partial reads (3-byte chunks) reassemble the
//      payload exactly; Position tracks the chunk cursor; Length stays stable
//      while reading.
//   4. UNKNOWN NAME: GetManifestResourceStream("no.such") -> null (never
//      throws) -- the miss path.
//   5. RM CULTURE-FALLBACK COMBINATION: interleaved with the stream probes,
//      a ResourceManager over the SAME manifest exercises GetString(name),
//      GetString(name, culture) for several unrelated cultures (en-US / fr-FR /
//      de-DE) and the explicit invariant -- every culture collapses to the
//      invariant blob (no satellite assemblies), byte-identical on net48 and
//      on the invariant-pinned ILC model. Missing keys / empty key -> null.
//
// The net48 oracle documents the INTENDED semantics; every line is a labeled
// bool so a diff failure shows exactly which assertion flipped.
//------------------------------------------------------------------------------
using System;
using System.Globalization;
using System.IO;
using System.Resources;

namespace Cases
{
    public static class Resdeep
    {
        public static void Main()
        {
            // ===== 1. Stream metadata: Length / Position / CanRead =====
            Stream s = typeof(Resdeep).Assembly.GetManifestResourceStream("resdeep.payload.bin");
            Console.WriteLine(s != null);                              // True (hit)
            long len0 = s.Length;
            Console.WriteLine(len0 == 32);                             // True (Length == payload size)
            long pos0 = s.Position;
            Console.WriteLine(pos0 == 0);                              // True (starts at 0)
            Console.WriteLine(s.CanRead);                              // True (readable)

            // ===== 2. Full-path Read: one call pulls the whole payload =====
            byte[] all = new byte[32];
            int n = s.Read(all, 0, 32);
            Console.WriteLine(n == 32);                                // True (full read)
            bool patternOk = true;
            for (int i = 0; i < 32; i++)
            {
                if (all[i] != (byte)i) patternOk = false;              // payload is 00 01 ... 1F
            }
            Console.WriteLine(patternOk);                              // True (content exact)
            long posAfter = s.Position;
            Console.WriteLine(posAfter == 32);                         // True (Position advanced)
            byte[] tail = new byte[4];
            int eof = s.Read(tail, 0, 4);
            Console.WriteLine(eof == 0);                               // True (EOF read returns 0)

            // ===== 3. Chunked partial reads reassemble the payload =====
            Stream s2 = typeof(Resdeep).Assembly.GetManifestResourceStream("resdeep.payload.bin");
            byte[] acc = new byte[32];
            byte[] scratch = new byte[3];
            int offset = 0;
            while (offset < 32)
            {
                int r = s2.Read(scratch, 0, 3);                        // 3-byte chunks into a scratch buffer
                if (r <= 0) break;
                for (int k = 0; k < r; k++) acc[offset + k] = scratch[k];
                offset += r;
            }
            Console.WriteLine(offset == 32);                           // True (all bytes read)
            bool chunkOk = true;
            for (int i = 0; i < 32; i++)
            {
                if (acc[i] != (byte)i) chunkOk = false;
            }
            Console.WriteLine(chunkOk);                                // True (chunks == payload)
            long posChunk = s2.Position;
            Console.WriteLine(posChunk == 32);                         // True (Position tracks chunks)
            long lenAfter = s2.Length;
            Console.WriteLine(lenAfter == 32);                         // True (Length stable)

            // ===== 4. Unknown resource name -> null (miss, never throws) =====
            Stream sMiss = typeof(Resdeep).Assembly.GetManifestResourceStream("resdeep.no.such");
            Console.WriteLine(sMiss == null);                          // True (miss -> null)

            // ===== 5. RM culture-fallback combination (invariant blob) =====
            ResourceManager rm = new ResourceManager("resdeep.Strings", typeof(Resdeep).Assembly);
            Console.WriteLine(rm.GetString("Title") == "Resdeep Title");    // True (hit)
            Console.WriteLine(rm.GetString("Greeting") == "Hello Resdeep"); // True (second key)
            CultureInfo enUS = new CultureInfo("en-US");
            CultureInfo frFR = new CultureInfo("fr-FR");
            CultureInfo deDE = new CultureInfo("de-DE");
            Console.WriteLine(rm.GetString("Title", enUS) == "Resdeep Title"); // True (en-US -> invariant)
            Console.WriteLine(rm.GetString("Title", frFR) == "Resdeep Title"); // True (fr-FR -> invariant)
            Console.WriteLine(rm.GetString("Title", deDE) == "Resdeep Title"); // True (de-DE -> invariant)
            Console.WriteLine(rm.GetString("Greeting", enUS) == "Hello Resdeep"); // True (culture path, 2nd key)
            Console.WriteLine(rm.GetString("Title", CultureInfo.InvariantCulture) == "Resdeep Title"); // True (explicit invariant)
            Console.WriteLine(rm.GetString("Missing", enUS) == null);         // True (miss with culture)
            Console.WriteLine(rm.GetString("Missing") == null);               // True (miss no culture)
            Console.WriteLine(rm.GetString("") == null);                      // True (empty key)

            // ===== 6. Stream + RM interplay: independent views over one pool =====
            Stream s3 = typeof(Resdeep).Assembly.GetManifestResourceStream("resdeep.payload.bin");
            Console.WriteLine(s3 != null);                             // True (stream again)
            long len3 = s3.Length;
            Console.WriteLine(len3 == 32);                             // True (RM work did not move the stream)
            Console.WriteLine(rm.GetString("Title", frFR) == "Resdeep Title"); // True (RM still resolves after stream use)
            byte[] hdr = new byte[2];
            int hn = s3.Read(hdr, 0, 2);
            Console.WriteLine(hn == 2 && hdr[0] == 0x00 && hdr[1] == 0x01);   // True (stream independent cursor)
        }
    }
}

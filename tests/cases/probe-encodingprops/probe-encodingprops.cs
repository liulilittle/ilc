// probe-encodingprops.cs -- System.Text.Encoding's property surface.
//
// The Encoding singletons already work (get_UTF8/get_Unicode/get_ASCII/
// get_BigEndianUnicode return real objects and GetBytes/GetString round-trip).
// What was missing is the METADATA surface: the code-page getters and the name
// getters. Every one of these failed to transpile with "no BCL mapping in M5",
// and because Encoding's body is not in the transpiled assembly the failure was
// a whole-method loss.
//
// The runtime models an encoding as { klass, page, emitBom }, so the code-page
// getters are direct reads and the name getters are a page -> string table.
// This probe pins the values net48 answers for each of the four modelled pages.
//
// Determinism: fixed literals and integers only.
//------------------------------------------------------------------------------
using System;
using System.Text;

internal static class Program
{
    private static void Main()
    {
        Encoding[] all = { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.ASCII };

        foreach (Encoding e in all)
        {
            Console.WriteLine("---");
            Console.WriteLine("cp=" + e.CodePage);
            Console.WriteLine("web=" + e.WebName);
            Console.WriteLine("name=" + e.EncodingName);
            Console.WriteLine("header=" + e.HeaderName);
            Console.WriteLine("body=" + e.BodyName);
            Console.WriteLine("win=" + e.WindowsCodePage);
            Console.WriteLine("single=" + e.IsSingleByte);
            Console.WriteLine("readonly=" + e.IsReadOnly);
            Console.WriteLine("browserdisplay=" + e.IsBrowserDisplay);
            Console.WriteLine("browsersave=" + e.IsBrowserSave);
            Console.WriteLine("mailnewsdisplay=" + e.IsMailNewsDisplay);
            Console.WriteLine("mailnewssave=" + e.IsMailNewsSave);
            Console.WriteLine("alwaysnorm=" + e.IsAlwaysNormalized());
            Console.WriteLine("clone_cp=" + ((Encoding)e.Clone()).CodePage);
            Console.WriteLine("equals_self=" + e.Equals(e));
            Console.WriteLine("equals_other=" + e.Equals(Encoding.UTF8));
            Console.WriteLine("hash_nonzero=" + (e.GetHashCode() != 0));
        }

        Console.WriteLine("=== default ===");
        Console.WriteLine("def_cp=" + Encoding.Default.CodePage);
        Console.WriteLine("def_web=" + Encoding.Default.WebName);

        Console.WriteLine("=== static factories ===");
        Console.WriteLine("get_cp_utf8=" + Encoding.GetEncoding(65001).CodePage);
        Console.WriteLine("get_cp_unicode=" + Encoding.GetEncoding(1200).CodePage);
        Console.WriteLine("get_name_utf8=" + Encoding.GetEncoding("utf-8").CodePage);
        Console.WriteLine("get_name_ascii=" + Encoding.GetEncoding("us-ascii").CodePage);
        Console.WriteLine("get_utf32_cp=" + Encoding.UTF32.CodePage);
        Console.WriteLine("get_utf7_cp=" + Encoding.UTF7.CodePage);

        Console.WriteLine("=== counts ===");
        Console.WriteLine("maxbyte=" + Encoding.UTF8.GetMaxByteCount(10));
        Console.WriteLine("maxchar=" + Encoding.UTF8.GetMaxCharCount(10));
    }
}
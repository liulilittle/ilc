using System;
using System.IO;
using System.Text;
namespace Cases { public static class Prog {
    static void I(string id, int v) { Console.WriteLine("[" + id + "] " + v); }
    static void S(string id, string v) { Console.WriteLine("[" + id + "] " + v); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    static string Hex(byte[] b) {
        const string H = "0123456789abcdef";
        var sb = new StringBuilder(b.Length * 2);
        for (int i = 0; i < b.Length; i++) { sb.Append(H[b[i] >> 4]); sb.Append(H[b[i] & 15]); }
        return sb.ToString();
    }
    const string Dir = "diffio_tmp";
    static string P(string n) { return Dir + "/" + n; }
    static void Clean() {
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { }
    }
    static void PathApi() {
        S("path.getext.basic", Path.GetExtension("a/b/c.txt"));
        S("path.getext.none", Path.GetExtension("a/b/c"));
        S("path.getext.dot", Path.GetExtension("a/b/.hidden"));
        S("path.changeext", Path.ChangeExtension("a/b/c.txt", ".md"));
        S("path.changeext.null", Path.ChangeExtension("a/b/c.txt", null) == null ? "<null>" : Path.ChangeExtension("a/b/c.txt", null));
        S("path.combine.two", Path.Combine("x", "y.txt"));
        S("path.combine.abs", Path.Combine("x", "y\\z"));
        S("file.getfilename", Path.GetFileName("a/b/c.txt"));
        S("file.getfilename.none", Path.GetFileName("a/b/") == "" ? "<empty>" : Path.GetFileName("a/b/"));
    }
    static void TextApi() {
        File.WriteAllText(P("t1.txt"), "hello");
        S("io.readalltext.roundtrip", File.ReadAllText(P("t1.txt")));
        I("io.readalltext.len", File.ReadAllText(P("t1.txt")).Length);
        File.AppendAllText(P("t1.txt"), "!");
        S("io.appendalltext", File.ReadAllText(P("t1.txt")));
        File.WriteAllText(P("t2.txt"), "h\u00e9llo\u4e2d");
        S("io.readalltext.utf8", File.ReadAllText(P("t2.txt")));
        I("io.readalltext.utf8.len", File.ReadAllText(P("t2.txt")).Length);
        File.WriteAllLines(P("t3.txt"), new string[] { "l1", "l2", "" });
        string[] ls = File.ReadAllLines(P("t3.txt"));
        I("io.readalllines.count", ls.Length);
        S("io.readalllines.join", string.Join("|", ls));
        I("io.writealllines.bytes", (int)new FileInfo(P("t3.txt")).Length);
    }
    static void BytesApi() {
        File.WriteAllBytes(P("b1.bin"), new byte[] { 0, 1, 255, 0x80 });
        S("io.readallbytes.hex", Hex(File.ReadAllBytes(P("b1.bin"))));
        I("io.readallbytes.len", File.ReadAllBytes(P("b1.bin")).Length);
        File.WriteAllBytes(P("b2.bin"), new byte[0]);
        I("io.readallbytes.empty", File.ReadAllBytes(P("b2.bin")).Length);
    }
    static void ExistsApi() {
        I("io.exists.before", File.Exists(P("nope.txt")) ? 1 : 0);
        File.WriteAllText(P("e1.txt"), "x");
        I("io.exists.after", File.Exists(P("e1.txt")) ? 1 : 0);
        I("io.dir.exists.before", Directory.Exists(P("sub")) ? 1 : 0);
        Directory.CreateDirectory(P("sub"));
        I("io.dir.exists.after", Directory.Exists(P("sub")) ? 1 : 0);
        Directory.CreateDirectory(P("sub/deep/deeper"));
        I("io.dir.create.nested", Directory.Exists(P("sub/deep/deeper")) ? 1 : 0);
        I("io.dir.exists.nested", Directory.Exists(P("sub/deep/deeper")) ? 1 : 0);
    }
    static void MutateApi() {
        File.WriteAllText(P("m1.txt"), "abc");
        File.Copy(P("m1.txt"), P("m2.txt"));
        S("io.copy.content", File.ReadAllText(P("m2.txt")));
        File.Move(P("m2.txt"), P("m3.txt"));
        I("io.move.old", File.Exists(P("m2.txt")) ? 1 : 0);
        I("io.move.new", File.Exists(P("m3.txt")) ? 1 : 0);
        File.Delete(P("m3.txt"));
        I("io.delete.exists", File.Exists(P("m3.txt")) ? 1 : 0);
        File.Delete(P("missing.txt"));
        I("io.delete.missing.ok", 1);
        Directory.Delete(P("sub"), true);
        I("io.dir.delete.exists", Directory.Exists(P("sub")) ? 1 : 0);
    }
    static void AttrApi() {
        File.WriteAllText(P("a1.txt"), "attr");
        I("io.getattr.exists", (File.GetAttributes(P("a1.txt")) & FileAttributes.Archive) == FileAttributes.Archive ? 1 : 0);
        File.SetAttributes(P("a1.txt"), FileAttributes.ReadOnly);
        I("io.setattr.readonly", (File.GetAttributes(P("a1.txt")) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly ? 1 : 0);
        File.SetAttributes(P("a1.txt"), FileAttributes.Normal);
        I("io.setattr.normal", (File.GetAttributes(P("a1.txt")) & FileAttributes.ReadOnly) == 0 ? 1 : 0);
    }
    static void InfoApi() {
        File.WriteAllText(P("i1.txt"), "12345");
        var fi = new FileInfo(P("i1.txt"));
        I("io.fileinfo.length", (int)fi.Length);
        I("io.getlastwrite.valid", (File.GetLastWriteTime(P("i1.txt")) > new DateTime(2000, 1, 1)) ? 1 : 0);
    }
    static void Errors() {
        try { S("io.readalltext.missing", File.ReadAllText(P("missing.txt"))); }
        catch (FileNotFoundException) { X("io.readalltext.missing", "FileNotFoundException"); }
        catch (DirectoryNotFoundException) { X("io.readalltext.missing", "DirectoryNotFoundException"); }
    }
    public static void Main() {
        Console.WriteLine("-- shard io001 start --");
        Clean();
        Directory.CreateDirectory(Dir);
        PathApi();
        TextApi();
        BytesApi();
        ExistsApi();
        MutateApi();
        AttrApi();
        InfoApi();
        Errors();
        Clean();
        Console.WriteLine("-- shard io001 end --");
    }
}}

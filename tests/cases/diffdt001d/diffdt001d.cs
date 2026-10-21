//------------------------------------------------------------------------------
// diffdt001d -- the FILETIME bridge edge matrix (diffdt001b follow-up).
//
// net48 oracle (machine zone: China Standard Time, no DST) for:
//   * fileTime = 0 and 1 (the 1601-01-01 epoch itself),
//   * negative fileTime (ArgumentOutOfRangeException, paramName "fileTime"),
//   * the DateTime.MaxValue edge: FromFileTime(max) CLAMPS to MaxValue
//     (net48 does NOT throw) while FromFileTime(max + 1) throws,
//   * ToFileTime / ToFileTimeUtc on Kind=Utc / Unspecified / Local values
//     (net48 ToFileTime == ToUniversalTime().ToFileTimeUtc(), so Unspecified
//     is treated as a LOCAL wall clock while ToFileTimeUtc keeps its ticks),
//   * a full FromFileTime -> ToFileTime round trip on a Local value.
//
// A DST zone cannot be selected for FromFileTime/ToFileTime (they always use
// the machine zone), so the DST instance coverage lives in
// tests/runtime/test_time.cpp (property assertions against the per-date
// offset model) and in the WSL probe recorded in .opencode/tmp/dt-fix.md.
//------------------------------------------------------------------------------
using System;
namespace Cases { public static class Prog {
    static void L(string id, long v) { Console.WriteLine("[" + id + "] " + v); }
    static void X(string id, string k) { Console.WriteLine("[" + id + "] EX " + k); }
    public static void Main() {
        Console.WriteLine("-- shard dt001d filetime edge start --");

        try { L("dt.edge.fromlocal.0", DateTime.FromFileTime(0L).Ticks); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.fromlocal.0", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.fromutc.0", DateTime.FromFileTimeUtc(0L).Ticks); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.fromutc.0", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.fromlocal.1", DateTime.FromFileTime(1L).Ticks); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.fromlocal.1", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.fromlocal.neg", DateTime.FromFileTime(-1L).Ticks); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.fromlocal.neg", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.fromutc.neg", DateTime.FromFileTimeUtc(-1L).Ticks); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.fromutc.neg", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.fromlocal.max", DateTime.FromFileTime(2650467743999999999L).Ticks); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.fromlocal.max", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.fromutc.max", DateTime.FromFileTimeUtc(2650467743999999999L).Ticks); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.fromutc.max", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.fromlocal.max1", DateTime.FromFileTime(2650467744000000000L).Ticks); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.fromlocal.max1", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.fromutc.max1", DateTime.FromFileTimeUtc(2650467744000000000L).Ticks); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.fromutc.max1", "ArgumentOutOfRangeException"); }

        L("dt.edge.fromlocal.kind", (int)DateTime.FromFileTime(132223104000000000L).Kind);
        L("dt.edge.fromutc.kind", (int)DateTime.FromFileTimeUtc(132223104000000000L).Kind);

        DateTime u = new DateTime(637185764967890000L, DateTimeKind.Utc);
        DateTime s = new DateTime(637185764967890000L, DateTimeKind.Unspecified);
        DateTime loc = new DateTime(637185764967890000L, DateTimeKind.Local);
        try { L("dt.edge.utc.tofiletime", u.ToFileTime()); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.utc.tofiletime", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.utc.tofiletimeutc", u.ToFileTimeUtc()); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.utc.tofiletimeutc", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.uns.tofiletime", s.ToFileTime()); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.uns.tofiletime", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.uns.tofiletimeutc", s.ToFileTimeUtc()); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.uns.tofiletimeutc", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.local.tofiletime", loc.ToFileTime()); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.local.tofiletime", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.local.tofiletimeutc", loc.ToFileTimeUtc()); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.local.tofiletimeutc", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.local.roundtrip", DateTime.FromFileTime(loc.ToFileTime()).Ticks); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.local.roundtrip", "ArgumentOutOfRangeException"); }
        L("dt.edge.local.rt.kind", (int)DateTime.FromFileTime(loc.ToFileTime()).Kind);

        DateTime pre = new DateTime(504911231999999999L);
        try { L("dt.edge.pre.uns.tofiletime", pre.ToFileTime()); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.pre.uns.tofiletime", "ArgumentOutOfRangeException"); }
        try { L("dt.edge.pre.utc.tofiletimeutc", pre.ToFileTimeUtc()); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.pre.utc.tofiletimeutc", "ArgumentOutOfRangeException"); }
        DateTime preLocal = new DateTime(504911231999999999L, DateTimeKind.Local);
        try { L("dt.edge.pre.local.tofiletime", preLocal.ToFileTime()); }
        catch (ArgumentOutOfRangeException) { X("dt.edge.pre.local.tofiletime", "ArgumentOutOfRangeException"); }
        Console.WriteLine("-- shard dt001d filetime edge end --");
    }
}}

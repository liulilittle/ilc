// environment2.cs -- System.Environment e2e reference case (net48 baseline).
//
// Exercises the Environment surface that the runtime icalls in
// environment.cpp implement: GetEnvironmentVariable / SetEnvironmentVariable,
// CurrentDirectory, TickCount and ProcessorCount. The reference run (this
// file, executed with .NET Framework 4.8) is the semantic authority for the
// icall contract; the transpiled output must reproduce it byte-for-byte once
// the BCL mapping lands.
//
// Determinism notes:
//   * Environment variable probes use a fixed, process-local name and always
//     end with the variable deleted, so re-runs are idempotent.
//   * CurrentDirectory is printed verbatim -- diffable when the reference
//     run and the transpiled run share a working directory.
//   * The TickCount probe samples the counter across several 20ms sleeps and
//     prints deterministic booleans only: tick_nondec (every delta >= 0) and
//     tick_advance (at least one delta >= 1 across the ~100ms window). Raw
//     deltas are NOT printed -- they depend on the OS tick granularity
//     (10-16ms) and vary run to run, which would make expected.txt flaky.
//     tick_advance is physically guaranteed: the 5x20ms sleeps span several
//     tick quanta, so the counter must advance at least once.
//   * ProcessorCount is a small positive integer on every supported host.
//
// The empty-string SetEnvironmentVariable probe documents the net48
// Framework semantics: setting an empty value DELETES the variable (the
// subsequent read returns null) -- the icall must reproduce this.

using System;
using System.Threading;

namespace Cases { public static class Environment2Case {
    public static void Main() {
        // --- GetEnvironmentVariable / SetEnvironmentVariable round trip ---
        Environment.SetEnvironmentVariable("IL2CPP_E2E_ENV", "hello env");
        Console.WriteLine("get=" + Environment.GetEnvironmentVariable("IL2CPP_E2E_ENV"));
        Environment.SetEnvironmentVariable("IL2CPP_E2E_ENV", "second value");
        Console.WriteLine("get2=" + Environment.GetEnvironmentVariable("IL2CPP_E2E_ENV"));
        Environment.SetEnvironmentVariable("IL2CPP_E2E_ENV", null);
        Console.WriteLine("missing_after_null=" +
            (Environment.GetEnvironmentVariable("IL2CPP_E2E_ENV") == null ? 1 : 0));
        Environment.SetEnvironmentVariable("IL2CPP_E2E_ENV", "");
        Console.WriteLine("missing_after_empty=" +
            (Environment.GetEnvironmentVariable("IL2CPP_E2E_ENV") == null ? 1 : 0));
        Environment.SetEnvironmentVariable("IL2CPP_E2E_ENV", null);
        Console.WriteLine("missing_final=" +
            (Environment.GetEnvironmentVariable("IL2CPP_E2E_ENV") == null ? 1 : 0));

        // --- CurrentDirectory (verbatim; diffable from a shared CWD) ---
        Console.WriteLine("cwd=" + Environment.CurrentDirectory);

        // --- TickCount: monotonic across multiple sampled sleeps ---
        // Only deterministic predicates are printed (see header notes).
        int t1 = Environment.TickCount;
        Thread.Sleep(20);
        int t2 = Environment.TickCount;
        Console.WriteLine("tick_mono=" + (t2 >= t1 ? 1 : 0));
        int prev = Environment.TickCount;
        int nondec = 1;
        int advanced = 0;
        for (int i = 0; i < 5; i++) {
            Thread.Sleep(20);
            int cur = Environment.TickCount;
            if (cur - prev < 0) nondec = 0;
            if (cur - prev > 0) advanced = 1;
            prev = cur;
        }
        Console.WriteLine("tick_nondec=" + nondec);
        Console.WriteLine("tick_advance=" + advanced);

        // --- ProcessorCount ---
        Console.WriteLine("procs=" + Environment.ProcessorCount);
    }
}}

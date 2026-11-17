"""W11 F4-a acceptance harness: prove the 32-slot reference-field limit is a REAL,
load-bearing guard, and that the CultureInfo path does NOT trip it.

WHY THIS EXISTS
---------------
`TypeEmitter.LayoutInstanceFields` throws when a reference field lands on slot >= 32
(the 32-bit `refFieldBitmap` cannot express it, and gc.cpp scans `slot < 32` only).
That is P1+ ("documented limitation"): 31 reference fields are fine, 32 are rejected.

Two things must stay true, and both are easy to break silently:

  A. The boundary is REAL and EXACT (31 ok / 32 rejected) and it COUNTS BASE-CLASS
     FIELDS. `LayoutInstanceFields` recurses into `t.BaseType` and starts the own
     slot at `t.BaseType.InstanceSize / PointerSize` (TypeEmitter.cs:1074-1078), so a
     derived type with few own fields can still exceed slot 31 through its base.

  B. A normal `CultureInfo` program must TRANSPILE OK. mscorlib types never reach
     `EmitClassStruct` (CodeGenerator.cs:1262 iterates only the INPUT assembly's
     types; BCL owners resolve through runtime stubs / "not wired in M5"), so the
     mscorlib types that WOULD exceed 32 slots do not matter here. If this ever
     regresses, something started translating BCL types and the P1+ note is wrong.

The two checks pull in opposite directions on purpose: A fails if the guard is
weakened, B fails if the guard is over-applied. A test that only did one of them
would pass on a wrong implementation.

USAGE
  python tools/audit/check_f4a_limit.py --repo F:\\il2cpp
  python tools/audit/check_f4a_limit.py --repo F:\\il2cpp --keep     (keep scratch dir)

EXIT
  0 = both checks pass
  1 = at least one check failed (details on stdout)
  2 = harness could not run (missing transpiler / toolchain)

NOTES FOR MAINTAINERS
  * Every case is built with `dotnet build` into its OWN directory and the
    transpiler is invoked as `dotnet exec <il2cppc.dll>`, never through an apphost
    `.exe` in a shared output dir (a shared bin/ makes the .exe embed the wrong
    assembly name -- that bug already cost this audit one round).
  * The transpiler must already be built: src/il2cppc/bin/Debug/net8.0/il2cppc.dll
"""
import argparse
import io
import os
import re
import shutil
import subprocess
import sys
import tempfile

TRANSPILER_REL = os.path.join("src", "il2cppc", "bin", "Debug", "net8.0", "il2cppc.dll")

CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <AssemblyName>{name}</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup><Compile Include="{name}.cs" /></ItemGroup>
</Project>
"""


def run(cmd, cwd=None):
    return subprocess.run(
        cmd, cwd=cwd, capture_output=True, text=True,
        encoding="utf-8", errors="replace",
    )


def build_case(repo, scratch, tag, source, name="p"):
    """Build one net48 case in its own dir; return the path to <name>.exe."""
    d = os.path.join(scratch, tag)
    os.makedirs(d, exist_ok=True)
    io.open(os.path.join(d, name + ".cs"), "w", encoding="utf-8").write(source)
    io.open(os.path.join(d, name + ".csproj"), "w", encoding="utf-8").write(
        CSPROJ.format(name=name))
    r = run(["dotnet", "build", os.path.join(d, name + ".csproj"),
             "-c", "Release", "--nologo", "-v", "q"], cwd=repo)
    exe = os.path.join(d, "bin", "Release", "net48", name + ".exe")
    if r.returncode != 0 or not os.path.exists(exe):
        return None, r.stdout + r.stderr
    return exe, ""


def transpile(repo, exe, outdir):
    """Run the transpiler. Returns (exit_code, combined_output)."""
    os.makedirs(outdir, exist_ok=True)
    r = run(["dotnet", "exec", os.path.join(repo, TRANSPILER_REL), exe, outdir], cwd=repo)
    return r.returncode, r.stdout + r.stderr


def ref_field_class(prefix, n, suffix=""):
    return "\n".join("    public object %s%02d;" % (prefix, i) for i in range(n)) + suffix


# --- case sources ------------------------------------------------------------

def flat_case(n):
    """n reference fields on ONE type (no inheritance)."""
    return """using System;
namespace Cases {
  public class Flat {
%s
  }
  public static class Prog {
    public static void Main(){ var o = new Flat(); Console.WriteLine(o.r%02d == null); }
  }
}
""" % (ref_field_class("r", n), n - 1)


def derived_case(base_n, own_n):
    """base_n reference fields on B, own_n on D : B  (own slots start at base_n)."""
    return """using System;
namespace Cases {
  public class B {
%s
  }
  public class D : B {
%s
  }
  public static class Prog {
    public static void Main(){ var o = new D(); Console.WriteLine(o.x%02d == null); }
  }
}
""" % (ref_field_class("b", base_n), ref_field_class("x", own_n), own_n - 1)


CULTURE_SRC = """using System;
using System.Globalization;
public static class P {
  public static void Main(){
    var ci = CultureInfo.GetCultureInfo("zh-CN");
    Console.WriteLine(ci.Name);
    Console.WriteLine(ci.DateTimeFormat.ShortDatePattern);
    Console.WriteLine(ci.NumberFormat.CurrencySymbol);
    Console.WriteLine(new DateTime(2026, 11, 16).ToString("D", ci));
  }
}
"""


def f4a_error(out):
    return "refFieldBitmap cannot represent slots >= 32" in out or "lands on slot" in out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo", default=".")
    ap.add_argument("--keep", action="store_true")
    args = ap.parse_args()
    repo = os.path.abspath(args.repo)

    tr = os.path.join(repo, TRANSPILER_REL)
    if not os.path.exists(tr):
        print("HARNESS-ERROR: transpiler not built: " + tr)
        return 2

    scratch = tempfile.mkdtemp(prefix="f4a-", dir=os.path.join(repo, ".opencode", "tmp") if
                               os.path.isdir(os.path.join(repo, ".opencode", "tmp")) else None)
    failures = []
    try:
        # ---- A1: flat 31 ref fields must TRANSPILE (exit 0, no F4-a error)
        exe, err = build_case(repo, scratch, "flat31", flat_case(31))
        if exe is None:
            failures.append("A1 build failed: " + err.strip()[:300])
        else:
            rc, out = transpile(repo, exe, os.path.join(scratch, "flat31", "gen"))
            ok = (rc == 0 and not f4a_error(out))
            print("[A1] flat 31 ref fields -> transpile rc=%d, f4a_error=%s  %s"
                  % (rc, f4a_error(out), "PASS" if ok else "FAIL"))
            if not ok:
                failures.append("A1: 31 reference fields should transpile cleanly")

        # ---- A2: flat 32 ref fields must be REJECTED (boundary is exact)
        exe, err = build_case(repo, scratch, "flat32", flat_case(32))
        if exe is None:
            failures.append("A2 build failed: " + err.strip()[:300])
        else:
            rc, out = transpile(repo, exe, os.path.join(scratch, "flat32", "gen"))
            ok = (rc != 0 and f4a_error(out))
            print("[A2] flat 32 ref fields -> transpile rc=%d, f4a_error=%s  %s"
                  % (rc, f4a_error(out), "PASS" if ok else "FAIL"))
            if not ok:
                failures.append("A2: 32 reference fields must be rejected (slot >= 32)")

        # ---- A3: base chain is counted (base 20 + own 11 ok / own 12 rejected)
        exe, err = build_case(repo, scratch, "derived11", derived_case(20, 11))
        if exe is None:
            failures.append("A3 build failed: " + err.strip()[:300])
        else:
            rc, out = transpile(repo, exe, os.path.join(scratch, "derived11", "gen"))
            ok = (rc == 0 and not f4a_error(out))
            print("[A3] base 20 + own 11 (max abs slot 31) -> rc=%d  %s"
                  % (rc, "PASS" if ok else "FAIL"))
            if not ok:
                failures.append("A3: base 20 + own 11 must transpile (slot 31 is legal)")

        exe, err = build_case(repo, scratch, "derived12", derived_case(20, 12))
        if exe is None:
            failures.append("A4 build failed: " + err.strip()[:300])
        else:
            rc, out = transpile(repo, exe, os.path.join(scratch, "derived12", "gen"))
            ok = (rc != 0 and f4a_error(out) and "x11" in out)
            print("[A4] base 20 + own 12 (x11 -> abs slot 32) -> rc=%d, names x11=%s  %s"
                  % (rc, "x11" in out, "PASS" if ok else "FAIL"))
            if not ok:
                failures.append("A4: base fields must count toward the slot budget "
                                "(base 20 + own 12 must be rejected at x11)")

        # ---- B: a real CultureInfo program must NOT trip the guard
        exe, err = build_case(repo, scratch, "culture", CULTURE_SRC, name="app")
        if exe is None:
            failures.append("B build failed: " + err.strip()[:300])
        else:
            rc, out = transpile(repo, exe, os.path.join(scratch, "culture", "gen"))
            ok = (rc == 0 and not f4a_error(out))
            print("[B ] CultureInfo.GetCultureInfo(\"zh-CN\") -> rc=%d, f4a_error=%s  %s"
                  % (rc, f4a_error(out), "PASS" if ok else "FAIL"))
            if not ok:
                failures.append("B: mscorlib types must not be laid out by TypeEmitter "
                                "(CultureInfo program must transpile)")

    finally:
        if args.keep:
            print("scratch kept: " + scratch)
        else:
            shutil.rmtree(scratch, ignore_errors=True)

    print("=" * 70)
    if failures:
        print("F4-a ACCEPTANCE: FAIL (%d)" % len(failures))
        for f in failures:
            print("  - " + f)
        return 1
    print("F4-a ACCEPTANCE: PASS (5 checks)")
    print("  A1/A2 = exact 31/32 boundary")
    print("  A3/A4 = base-class fields counted toward the slot budget")
    print("  B     = CultureInfo path does not trip the guard")
    return 0


if __name__ == "__main__":
    sys.exit(main())

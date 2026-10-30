#!/usr/bin/env pwsh
# WinForms P/Invoke marshal-shape probe (audit evidence generator).
#
# For each marshal shape the net48 WinForms/Drawing P/Invokes use, this script
# emits a minimal net48 assembly, transpiles it with il2cppc, and reports
# PASS/FAIL plus the exact il2cppc diagnostic. A FAIL is the red evidence that
# the shape has no marshalling bridge yet; a PASS means the emitter accepted it
# (and, when a C++ toolchain is supplied, that the generated code compiles).
#
# usage: ./run-marshal-probe.ps1 [-WorkDir <dir>] [-Il2cppc <exe>] [-CMake <exe>] [-Keep]
#
# When -CMake is supplied (or cmake is on PATH) the generated project is also
# CONFIGURED AND BUILT, so a "TRANSPILE-PASS" is upgraded to C++-BUILD-OK /
# C++-BUILD-FAIL. That distinction matters: a P/Invoke shape can transpile
# cleanly and still emit uncompilable C++ (the by-value struct RETURN does
# exactly that), and that is a different gap from a LOUD rejection.
param(
    [string]$WorkDir = "",
    [string]$Il2cppc = "",
    [string]$CMake = "",
    [switch]$Keep
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $WorkDir) { $WorkDir = Join-Path ([IO.Path]::GetTempPath()) "wf-marshal-probe" }
if (-not $Il2cppc) { $Il2cppc = Join-Path $Root "src/il2cppc/bin/Release/net8.0/il2cppc.exe" }
if (-not (Test-Path $Il2cppc)) { throw "il2cppc not found: $Il2cppc (build src/il2cppc first)" }
if (-not $CMake) {
    $c = Get-Command cmake -ErrorAction SilentlyContinue
    if ($c) { $CMake = $c.Source }
}
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

$probes = [ordered]@{
    'scalar+intptr+string' = @'
using System;
using System.Runtime.InteropServices;
public static class P {
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetWindowTextW(IntPtr h, string s, int n);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string n);
    public static void Main() {
        Console.WriteLine(GetModuleHandle(null) == IntPtr.Zero ? "null" : "handle");
        Console.WriteLine(GetWindowTextW(IntPtr.Zero, "x", 1));
    }
}
'@
    'struct by-ref out' = @'
using System;
using System.Runtime.InteropServices;
public struct RECTP { public int left, top, right, bottom; }
public static class P {
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool GetClientRect(IntPtr h, ref RECTP rc);
    public static void Main() { var r = new RECTP(); Console.WriteLine(GetClientRect(IntPtr.Zero, ref r)); }
}
'@
    'struct by-value param' = @'
using System;
using System.Runtime.InteropServices;
public struct POINTP { public int x, y; }
public static class P {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, POINTP p);
    public static void Main() { Console.WriteLine(SendMessage(IntPtr.Zero, 0, IntPtr.Zero, new POINTP()) == IntPtr.Zero ? "zero" : "nz"); }
}
'@
    'struct by-value return' = @'
using System;
using System.Runtime.InteropServices;
public struct SZP { public int cx, cy; }
public static class P {
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    static extern SZP Query(); 
    public static void Main() { Console.WriteLine(Query().cx); }
}
'@
    'byte[] buffer param' = @'
using System;
using System.Runtime.InteropServices;
public static class P {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool GetKeyboardState(byte[] state);
    public static void Main() { Console.WriteLine(GetKeyboardState(new byte[256])); }
}
'@
    'char[] buffer param' = @'
using System;
using System.Runtime.InteropServices;
public static class P {
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetWindowTextW(IntPtr h, char[] buf, int n);
    public static void Main() { Console.WriteLine(GetWindowTextW(IntPtr.Zero, new char[8], 8)); }
}
'@
    'StringBuilder buffer param' = @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class P {
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    public static void Main() { Console.WriteLine(GetWindowTextW(IntPtr.Zero, new StringBuilder(), 8)); }
}
'@
    'HandleRef param' = @'
using System;
using System.Runtime.InteropServices;
public static class P {
    [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool DeleteObject(HandleRef h);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr GetDC(HandleRef h);
    public static void Main() {
        var hr = new HandleRef(null, IntPtr.Zero);
        Console.WriteLine(DeleteObject(hr));
        Console.WriteLine(GetDC(hr) == IntPtr.Zero ? "zero" : "nz");
    }
}
'@
    'delegate param (callback)' = @'
using System;
using System.Runtime.InteropServices;
public static class P {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool EnumWindows(EnumProc cb, IntPtr l);
    public static void Main() { Console.WriteLine(EnumWindows((h, l) => true, IntPtr.Zero)); }
}
'@
    'string out param' = @'
using System;
using System.Runtime.InteropServices;
public static class P {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern void GetStr(out string s);
    public static void Main() { string s; GetStr(out s); Console.WriteLine(s); }
}
'@
    'IntPtr[] array param' = @'
using System;
using System.Runtime.InteropServices;
public static class P {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool SystemParametersInfoW(int a, int b, IntPtr[] c, int d);
    public static void Main() { Console.WriteLine(SystemParametersInfoW(0, 0, new IntPtr[1], 0)); }
}
'@
    'bool[] array param' = @'
using System;
using System.Runtime.InteropServices;
public static class P {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool SystemParametersInfoW(int a, int b, bool[] c, bool d);
    public static void Main() { Console.WriteLine(SystemParametersInfoW(0, 0, new bool[1], false)); }
}
'@
    'ref int / ref IntPtr' = @'
using System;
using System.Runtime.InteropServices;
public static class P {
    [DllImport("gdi32.dll", CharSet = CharSet.Auto)]
    static extern int GetObject(IntPtr h, int n, ref int v);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    static extern int GdipDrawImage(IntPtr g, IntPtr i, float x, float y);
    [DllImport("gdiplus.dll", CharSet = CharSet.Unicode)]
    static extern int GdipGetImageWidth(IntPtr i, out uint w);
    public static void Main() {
        int v = 0; uint w;
        Console.WriteLine(GetObject(IntPtr.Zero, 4, ref v));
        Console.WriteLine(GdipDrawImage(IntPtr.Zero, IntPtr.Zero, 1f, 2f));
        Console.WriteLine(GdipGetImageWidth(IntPtr.Zero, out w));
    }
}
'@
    'fnptr param' = @'
using System;
using System.Runtime.InteropServices;
public static unsafe class P {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SetTimer(IntPtr h, int id, int ms, IntPtr cb);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr CallWindowProc(IntPtr prev, IntPtr h, int m, IntPtr w, IntPtr l);
    public static void Main() {
        Console.WriteLine(SetTimer(IntPtr.Zero, 0, 100, IntPtr.Zero) == IntPtr.Zero ? "zero" : "nz");
        Console.WriteLine(CallWindowProc(IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero ? "zero" : "nz");
    }
}
'@
}

$results = @()
foreach ($name in $probes.Keys) {
    $safe = ($name -replace '[^A-Za-z0-9]', '_')
    $dir = Join-Path $WorkDir $safe
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -LiteralPath (Join-Path $dir 'p.cs') -Value $probes[$name] -Encoding UTF8
    $csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <OutputType>Exe</OutputType>
    <LangVersion>7.3</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <AssemblyName>$safe</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <DebugType>portable</DebugType>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="p.cs" />
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $dir "$safe.csproj") -Value $csproj -Encoding UTF8
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $build = & dotnet build (Join-Path $dir "$safe.csproj") -c Release -v q --nologo 2>&1
    $buildCode = $LASTEXITCODE
    if ($buildCode -ne 0) {
        $results += [pscustomobject]@{ Shape = $name; Result = 'CSC-FAIL'; Detail = ($build | Select-Object -Last 1) }
        $ErrorActionPreference = $prevEap
        continue
    }
    $exe = Join-Path $dir "bin/Release/net48/$safe.exe"
    $gen = Join-Path $dir 'gen'
    $tp = & $Il2cppc $exe $gen 2>&1
    $tpCode = $LASTEXITCODE
    $ErrorActionPreference = $prevEap
    if ($tpCode -ne 0) {
        $msg = ($tp | Where-Object { $_ -match 'il2cppc error:' } | Select-Object -First 1)
        if (-not $msg) { $msg = ($tp | Select-Object -Last 1) }
        $results += [pscustomobject]@{ Shape = $name; Result = 'TRANSPILE-FAIL'; Detail = "$msg".Trim() }
        continue
    }
    $results += [pscustomobject]@{ Shape = $name; Result = 'TRANSPILE-PASS'; Detail = 'generated' }
    if (-not $CMake) { continue }

    $ErrorActionPreference = "Continue"
    $bdir = Join-Path $gen 'build_mingw'
    & $CMake -S $gen -B $bdir -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release *> (Join-Path $gen 'cmake.log')
    if ($LASTEXITCODE -ne 0) {
        $ErrorActionPreference = $prevEap
        $results += [pscustomobject]@{ Shape = $name; Result = 'CMAKE-CONFIG-FAIL'; Detail = "$($gen)/cmake.log" }
        continue
    }
    & $CMake --build $bdir -j 8 *> (Join-Path $gen 'build.log')
    $bc = $LASTEXITCODE
    $ErrorActionPreference = $prevEap
    if ($bc -ne 0) {
        $firstErr = (Select-String -Path (Join-Path $gen 'build.log') -Pattern 'error:' | Select-Object -First 1)
        $detail = if ($firstErr) { $firstErr.Line.Trim() } else { "$($gen)/build.log" }
        $results += [pscustomobject]@{ Shape = $name; Result = 'C++-BUILD-FAIL'; Detail = $detail }
        continue
    }
    $results += [pscustomobject]@{ Shape = $name; Result = 'C++-BUILD-OK'; Detail = 'built' }
}

$results | ForEach-Object { "{0}`t{1}`t{2}" -f $_.Shape, $_.Result, $_.Detail }
if (-not $Keep) { Write-Host "artifacts kept at $WorkDir" }

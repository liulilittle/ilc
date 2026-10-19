#------------------------------------------------------------------------------
# verify-manifest.ps1 -- assert that an AOT-built Windows executable carries an
# embedded application manifest (RT_MANIFEST, resource type 24, resource id 1)
# and that the manifest is well-formed XML carrying the expected
# requestedExecutionLevel / uiAccess.
#
# The resource is read WITHOUT executing the image: LoadLibraryEx is called with
# LOAD_LIBRARY_AS_DATAFILE (0x2), so an elevated (requireAdministrator) binary is
# inspected safely -- no UAC prompt, no process start.
#
# usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-manifest.ps1 `
#       -Exe <path\to\il2cpp_gen_exe.exe> [-Level requireAdministrator] `
#       [-UiAccess false] [-ExpectSha256 <hex>] [-ExpectContains <text>] `
#       [-ResourceId 1]
#
# -ExpectSha256 hashes the RAW resource bytes, so a caller can pin the exact
# manifest it generated. This is the only way to prove the resource is OURS:
# MinGW/MSVC always embed a DEFAULT asInvoker manifest, so "-Level asInvoker"
# alone also matches the linker default and must not be treated as proof.
#
# Exit codes: 0 = manifest present and matching; 1 = missing/mismatch; 2 = bad
# usage (bad path, unreadable file, missing argument).
#------------------------------------------------------------------------------
param(
    [string]$Exe = "",
    [string]$Level = "",
    [string]$UiAccess = "",
    [string]$ExpectSha256 = "",
    [string]$ExpectContains = "",
    [string]$ExpectIdentity = "",
    [int]$ResourceId = 1
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Exe)) {
    Write-Host "usage: verify-manifest.ps1 -Exe <exe> [-Level <level>] [-UiAccess <bool>] [-ExpectSha256 <hex>] [-ExpectContains <text>] [-ResourceId <id>]"
    exit 2
}
if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Host "FAIL: executable not found: $Exe"
    exit 2
}
if (Test-Path -LiteralPath $Exe -PathType Container) {
    Write-Host "FAIL: not a file: $Exe"
    exit 2
}

$signature = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class PeManifest
{
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    private const uint RT_MANIFEST = 24;

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr res);
    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr data);
    [DllImport("kernel32", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr res);

    public static string filePath;

    // Raw resource bytes; null when the resource is absent.
    public static byte[] ReadBytes(int id)
    {
        IntPtr module = LoadLibraryExW(filePath, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
        if (module == IntPtr.Zero)
            throw new InvalidOperationException("LoadLibraryEx failed, Win32 error " + Marshal.GetLastWin32Error());
        try
        {
            IntPtr res = FindResourceW(module, new IntPtr(id), new IntPtr(RT_MANIFEST));
            if (res == IntPtr.Zero) return null;
            uint size = SizeofResource(module, res);
            IntPtr data = LockResource(LoadResource(module, res));
            if (data == IntPtr.Zero) return null;
            byte[] buffer = new byte[size];
            Marshal.Copy(data, buffer, 0, (int)size);
            return buffer;
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    public static string ToText(byte[] buffer)
    {
        int offset = (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF) ? 3 : 0;
        return Encoding.UTF8.GetString(buffer, offset, buffer.Length - offset);
    }
}
'@

try {
    Add-Type -TypeDefinition $signature -Language CSharp | Out-Null
    [PeManifest]::filePath = (Resolve-Path -LiteralPath $Exe).Path
    $bytes = [PeManifest]::ReadBytes($ResourceId)
} catch {
    Write-Host "FAIL: cannot read resources from ${Exe}: $($_.Exception.Message)"
    exit 2
}

if ($null -eq $bytes) {
    Write-Host "FAIL: RT_MANIFEST id=$ResourceId not found in $Exe"
    exit 1
}

$text = [PeManifest]::ToText($bytes)
Write-Host "OK: RT_MANIFEST id=$ResourceId present ($($bytes.Length) bytes)"

$fail = $false

# A manifest the loader accepts is always well-formed XML. Parsing it here turns
# "the exe refuses to start (SideBySide id 59)" into a build-time failure.
try {
    $null = [xml]$text
} catch {
    Write-Host "FAIL: embedded manifest is not well-formed XML: $($_.Exception.Message)"
    Write-Host "----- manifest -----"
    Write-Host $text
    $fail = $true
}

if ($Level) {
    $needle = 'requestedExecutionLevel level="' + $Level + '"'
    if ($text -notlike "*$needle*") {
        Write-Host "FAIL: expected $needle"
        $fail = $true
    } else {
        Write-Host "OK: $needle"
    }
}
if ($UiAccess) {
    $needle = 'uiAccess="' + $UiAccess + '"'
    if ($text -notlike "*$needle*") {
        Write-Host "FAIL: expected $needle"
        $fail = $true
    } else {
        Write-Host "OK: $needle"
    }
}
if ($ExpectIdentity) {
    # Pass the NAME only (no quotes): a quoted argument does not survive a
    # powershell -File process boundary intact.
    $needle = 'name="' + $ExpectIdentity + '"'
    if (-not $text.Contains($needle)) {
        Write-Host "FAIL: manifest assemblyIdentity is not $needle"
        $fail = $true
    } else {
        Write-Host "OK: $needle"
    }
}
if ($ExpectContains) {
    if (-not $text.Contains($ExpectContains)) {
        Write-Host "FAIL: manifest does not contain '$ExpectContains'"
        $fail = $true
    } else {
        Write-Host "OK: manifest contains '$ExpectContains'"
    }
}
if ($ExpectSha256) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $actual = ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString("x2") }) -join ""
    } finally {
        $sha.Dispose()
    }
    if ($actual -ne $ExpectSha256.ToLowerInvariant()) {
        Write-Host "FAIL: resource sha256 $actual, expected $($ExpectSha256.ToLowerInvariant())"
        $fail = $true
    } else {
        Write-Host "OK: resource sha256 matches"
    }
}

if ($fail) {
    Write-Host "----- manifest -----"
    Write-Host $text
    exit 1
}
exit 0

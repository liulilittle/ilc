#------------------------------------------------------------------------------
# verify-manifest.ps1 -- assert that an AOT-built Windows executable carries an
# embedded application manifest (RT_MANIFEST, resource type 24, resource id 1)
# and that the manifest text contains the expected requestedExecutionLevel.
#
# The resource is read WITHOUT executing the image: LoadLibraryEx is called with
# LOAD_LIBRARY_AS_DATAFILE (0x2), so an elevated (requireAdministrator) binary is
# inspected safely -- no UAC prompt, no process start.
#
# usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-manifest.ps1 `
#       -Exe <path\to\il2cpp_gen_exe.exe> [-Level requireAdministrator] [-UiAccess false]
#
# Exit codes: 0 = manifest present and matching; 1 = missing/mismatch; 2 = bad usage.
#------------------------------------------------------------------------------
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Level = "",
    [string]$UiAccess = "",
    [int]$ResourceId = 1
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Error "executable not found: $Exe"
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

    public static string Read(int id, out int size)
    {
        size = 0;
        IntPtr module = LoadLibraryExW(filePath, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
        if (module == IntPtr.Zero) throw new InvalidOperationException("LoadLibraryEx failed: " + Marshal.GetLastWin32Error());
        try
        {
            IntPtr res = FindResourceW(module, new IntPtr(id), new IntPtr(RT_MANIFEST));
            if (res == IntPtr.Zero) return null;
            uint bytes = SizeofResource(module, res);
            IntPtr data = LockResource(LoadResource(module, res));
            if (data == IntPtr.Zero) return null;
            size = (int)bytes;
            byte[] buffer = new byte[bytes];
            Marshal.Copy(data, buffer, 0, (int)bytes);
            // Manifests are UTF-8 in practice; strip a BOM when present.
            int offset = (buffer.Length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF) ? 3 : 0;
            return Encoding.UTF8.GetString(buffer, offset, buffer.Length - offset);
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    public static string filePath;
}
'@

Add-Type -TypeDefinition $signature -Language CSharp | Out-Null
[PeManifest]::filePath = (Resolve-Path -LiteralPath $Exe).Path

$size = 0
$text = [PeManifest]::Read($ResourceId, [ref]$size)
if ($null -eq $text) {
    Write-Host "FAIL: RT_MANIFEST id=$ResourceId not found in $Exe"
    exit 1
}

Write-Host "OK: RT_MANIFEST id=$ResourceId present ($size bytes)"

$fail = $false
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

if ($fail) { exit 1 }
exit 0

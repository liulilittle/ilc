<#
archive-gate.ps1 -- post-build gate for the il2cpp runtime archive (draft).

Checks:
  1) llvm-ar member count == source-list line count
  2) llvm-nm --defined-only contains every sentinel symbol

Exit codes: 0 = PASS, 1 = violation.
Invoked by run-tests.ps1 / sweep-cases.ps1 only when IL2CPP_ARCHIVE_GATE=1
(NDK/Android pipeline). Tool paths overridable via IL2CPP_LLVM_AR / IL2CPP_LLVM_NM.
#>
param(
    [Parameter(Mandatory = $true)][string]$Archive,
    [Parameter(Mandatory = $true)][string]$SourceList,
    [string]$Ar = $env:IL2CPP_LLVM_AR,
    [string]$Nm = $env:IL2CPP_LLVM_NM,
    [string[]]$Sentinels = @('System_Object_class', 'il2cpp_class_register', 'il2cpp_allocator')
)
$ErrorActionPreference = 'Continue'
if (-not $Ar) { $Ar = 'D:\android\sdk\ndk\21.1.6352462\toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-ar.exe' }
if (-not $Nm) { $Nm = 'D:\android\sdk\ndk\21.1.6352462\toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-nm.exe' }
$fail = 0
if (-not (Test-Path -LiteralPath $Archive)) { Write-Host "GATE_FAIL: archive missing: $Archive"; exit 1 }
if (-not (Test-Path -LiteralPath $Ar)) { Write-Host "GATE_FAIL: llvm-ar missing: $Ar"; exit 1 }
if (-not (Test-Path -LiteralPath $Nm)) { Write-Host "GATE_FAIL: llvm-nm missing: $Nm"; exit 1 }

$members = @(& $Ar t $Archive 2>$null)
$srcCount = @(Get-Content -LiteralPath $SourceList | Where-Object { $_.Trim() -ne '' }).Count
if ($members.Count -ne $srcCount) {
    Write-Host "GATE_FAIL: member_count=$($members.Count) expected_source_count=$srcCount"
    $fail++
} else {
    Write-Host "GATE_OK: member_count=$($members.Count)=source_count=$srcCount"
}
$defined = & $Nm --defined-only $Archive 2>&1 | Out-String
foreach ($s in $Sentinels) {
    if ($defined -match [regex]::Escape($s)) { Write-Host "GATE_OK: sentinel '$s' present" }
    else { Write-Host "GATE_FAIL: sentinel '$s' missing"; $fail++ }
}
if ($fail -gt 0) { Write-Host "GATE_RESULT: FAIL ($fail violations)"; exit 1 }
Write-Host "GATE_RESULT: PASS"
exit 0
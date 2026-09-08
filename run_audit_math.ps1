param([string]$dll = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\mscorlib.dll")
$out = "E:\il2cpp\audit_out.txt"
dotnet run --project E:\il2cpp\src\il2cppc -- audit-internalcalls $dll 2>&1 | Tee-Object $out
Write-Host "---OUT---"
Get-Content $out | Select-String -Pattern "Math|SUMMARY|GAP|ERR|OK|IGAP" | Select-Object -First 100

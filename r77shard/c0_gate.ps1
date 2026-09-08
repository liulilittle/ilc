$ErrorActionPreference = "Continue"
dotnet build E:/il2cpp/src/il2cppc/il2cppc.csproj -v minimal > E:/il2cpp/out/c0_gate.log 2>&1
$code = $LASTEXITCODE
Add-Content -LiteralPath E:/il2cpp/out/c0_gate.log -Value ("EXIT:" + $code)
exit $code

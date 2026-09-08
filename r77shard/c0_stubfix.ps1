dotnet build E:/il2cpp/src/il2cppc/il2cppc.csproj -v minimal > E:/il2cpp/out/c0_stubfix.log 2>&1
$code = $LASTEXITCODE
Add-Content -Path E:/il2cpp/out/c0_stubfix.log -Value "EXIT=$code"

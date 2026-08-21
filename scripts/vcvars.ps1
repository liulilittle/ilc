# 定位 VS2022 Enterprise 并导入 cl.exe 环境
$vs = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise"
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found at $vcvars" }
$env:VCToolsInstallDir = ""
& cmd /c "`"$vcvars`" >nul 2>&1 && set" | ForEach-Object {
    if ($_ -match "^([^=]+)=(.*)$") { Set-Item -Path "env:$($matches[1])" -Value $matches[2] }
}

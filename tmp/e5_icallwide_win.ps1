# Regression round E: re-transpile icallwide x9 with the CURRENT transpiler, Windows build+run.
$ErrorActionPreference = "Continue"
Set-Location E:\il2cpp
foreach ($tag in @("net48","nc21","nc31","net5","net6","net7","net8","net9","net10")) {
    $dll = "tmp/icallwide/icallwide_$tag.dll"
    if (-not (Test-Path $dll)) { Write-Output "icallwide/$tag NO DLL"; continue }
    & dotnet run --project src/il2cppc -c Release --no-build -- $dll "tmp/icallwide/gen_$tag" 1>$null 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Output "icallwide/${tag}: TRANSPILE FAIL"; continue }
    if (-not (Test-Path "tmp/icallwide/build_win_$tag")) { cmake -S "tmp/icallwide/gen_$tag" -B "tmp/icallwide/build_win_$tag" -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release *> $null }
    cmake --build "tmp/icallwide/build_win_$tag" -j 8 1>$null 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Output "icallwide/${tag}: BUILD FAIL"; continue }
    $out = & "tmp/icallwide/build_win_$tag/il2cpp_gen_exe.exe" 2>&1
    [System.IO.File]::WriteAllText("E:\il2cpp\tmp\parity\icallwide_${tag}_win.txt", ($out -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
    Write-Output "icallwide/${tag}: EXIT=$LASTEXITCODE LAST=[$($out[-1])]"
}

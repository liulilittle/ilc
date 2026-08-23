# probe3 Windows: build+run per version, save output for parity.
$ErrorActionPreference = "Continue"
Set-Location E:\il2cpp
foreach ($tag in @("net48","nc21","nc31","net5","net6","net7","net8","net9","net10")) {
    $gen = "tmp/probe3/gen_$tag"
    if (-not (Test-Path $gen)) { Write-Output "probe3/$tag NO GEN"; continue }
    if (-not (Test-Path "tmp/probe3/build_win_$tag")) {
        cmake -S $gen -B "tmp/probe3/build_win_$tag" -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release *> $null
    }
    cmake --build "tmp/probe3/build_win_$tag" -j 8 1>$null 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Output "probe3/$tag BUILD FAIL"; continue }
    $out = & "tmp/probe3/build_win_$tag/il2cpp_gen_exe.exe" 2>&1
    [System.IO.File]::WriteAllText("E:\il2cpp\tmp\parity\probe3_${tag}_win.txt", ($out -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
    Write-Output "probe3/${tag}: EXIT=$LASTEXITCODE LAST=[$($out[-1])]"
}

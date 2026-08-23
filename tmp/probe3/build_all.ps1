# probe3 matrix: compile x9 versions, transpile, Windows build+run.
$ErrorActionPreference = "Stop"
Set-Location E:\il2cpp
$csc = "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll"
$src = "tmp/probe3/probe3.cs"

$versions = @(
    @{ tag = "net48"; refs = @("C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll"); defines = @("-define:LEGACY") },
    @{ tag = "nc21";  refs = @(Get-ChildItem "tmp/netall" -Recurse -Filter "System.Private.CoreLib.dll" | Where-Object FullName -match "2\.1" | Select-Object -First 1 -ExpandProperty FullName) },
    @{ tag = "nc31";  refs = @(Get-ChildItem "tmp/netall" -Recurse -Filter "System.Private.CoreLib.dll" | Where-Object FullName -match "3\.1" | Select-Object -First 1 -ExpandProperty FullName) },
    @{ tag = "net5";  refs = @(Get-ChildItem "tmp/netall" -Recurse -Filter "System.Private.CoreLib.dll" | Where-Object FullName -match "5\.0" | Select-Object -First 1 -ExpandProperty FullName) },
    @{ tag = "net6";  refs = @(Get-ChildItem "tmp/netall" -Recurse -Filter "System.Private.CoreLib.dll" | Where-Object FullName -match "6\.0" | Select-Object -First 1 -ExpandProperty FullName) },
    @{ tag = "net7";  refs = @(Get-ChildItem "tmp/netall" -Recurse -Filter "System.Private.CoreLib.dll" | Where-Object FullName -match "7\.0" | Select-Object -First 1 -ExpandProperty FullName) },
    @{ tag = "net8";  refs = @("C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.5\System.Private.CoreLib.dll") },
    @{ tag = "net9";  refs = @(Get-ChildItem "tmp/net910" -Recurse -Filter "System.Private.CoreLib.dll" | Where-Object FullName -match "9\.0" | Select-Object -First 1 -ExpandProperty FullName) },
    @{ tag = "net10"; refs = @(Get-ChildItem "tmp/net910" -Recurse -Filter "System.Private.CoreLib.dll" | Where-Object FullName -match "10\.0" | Select-Object -First 1 -ExpandProperty FullName) }
)

foreach ($v in $versions) {
    $tag = $v.tag
    if (-not $v.refs[0]) { Write-Output "probe3/$tag SKIP (no CoreLib)"; continue }
    $dir = Split-Path $v.refs[0] -Parent
    $extra = @(Get-ChildItem $dir -Filter "System.*.dll" -ErrorAction SilentlyContinue | Where-Object { $_.Name -in @("System.Runtime.dll", "System.Console.dll") } | ForEach-Object { "-r:$($_.FullName)" })
    $refArgs = @($v.refs | ForEach-Object { "-r:$_" })
    $refArgs += $extra
    $defines = @($v.defines) + @( ) | Where-Object { $_ }
    Write-Output "CMD: csc $($refArgs -join ' ') [$defines] $src"
    & dotnet $csc -nologo -nostdlib+ -noconfig -target:exe "-out:tmp/probe3/probe3_$tag.dll" @refArgs $defines $src 2>&1 | Select-Object -First 3
    if ($LASTEXITCODE -ne 0) { Write-Output "probe3/$tag CSC FAIL"; continue }
    & dotnet run --project src/il2cppc -c Release --no-build -- "tmp/probe3/probe3_$tag.dll" "tmp/probe3/gen_$tag" 2>&1 | Select-String "error" | Select-Object -First 2 | ForEach-Object { Write-Output "probe3/$tag TRANSPILE $_" }
    if ($LASTEXITCODE -ne 0) { Write-Output "probe3/$tag TRANSPILE FAIL"; continue }
}
Write-Output "compile+transpile done"

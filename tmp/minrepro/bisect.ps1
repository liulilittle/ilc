$ErrorActionPreference = 'Stop'
$csc = "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll"
$v8 = "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.5"
$src = "E:\il2cpp\tmp\icallwide\icallwide.cs"
$sections = @('WideString','WideStringBuilder','WideMath','WideDecimal','WideFormat','WideParse','WideArray','WideGC','WideThread','WideVersion','WideDateTime','WideTimeSpan','WideChar','WideEnum','WideObject','WideCollections')

$text = [System.IO.File]::ReadAllText($src)

function TryVariant($name, $disabled) {
  $t = $text
  foreach ($s in $disabled) { $t = $t -replace ("        $s\(\);"), ("        // disabled $s();") }
  $p = "E:\il2cpp\tmp\minrepro\$name.cs"
  $dll = "E:\il2cpp\tmp\minrepro\$name`_net8.dll"
  [System.IO.File]::WriteAllText($p, $t, [System.Text.UTF8Encoding]::new($false))
  & dotnet $csc -nologo -nostdlib+ -noconfig -target:exe "-out:$dll" `
    "-r:$v8\System.Private.CoreLib.dll" "-r:$v8\System.Runtime.dll" "-r:$v8\System.Console.dll" `
    "-r:$v8\System.Runtime.Extensions.dll" "-r:$v8\System.Threading.Thread.dll" `
    "-r:$v8\System.Collections.NonGeneric.dll" "-r:$v8\System.Globalization.dll" $p 2>&1 | Out-Null
  if ($LASTEXITCODE -ne 0) { return "COMPILE_FAIL" }
  $out = cmd /c "dotnet E:\il2cpp\src\il2cppc\bin\Release\net8.0\il2cppc.dll $dll E:\il2cpp\tmp\minrepro\gen_$name 2>&1"
  if ($LASTEXITCODE -eq 0) { return "TRANSPILE_OK" }
  $first = ($out | Where-Object { $_ -match 'il2cppc error' } | Select-Object -First 1)
  return "FAIL: $first"
}

# stage 1: disable second half
$r1 = TryVariant 'b_half1' @('WideFormat','WideParse','WideArray','WideGC','WideThread','WideVersion','WideDateTime','WideTimeSpan','WideChar','WideEnum','WideObject','WideCollections')
Write-Output "first-half-only (String..Decimal): $r1"
$r2 = TryVariant 'b_half2' @('WideString','WideStringBuilder','WideMath','WideDecimal')
Write-Output "second-half-only (Format..Collections): $r2"

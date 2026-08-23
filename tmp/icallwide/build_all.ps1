$ErrorActionPreference = 'Stop'
$csc = "C:\Program Files\dotnet\sdk\8.0.205\Roslyn\bincore\csc.dll"
$src = "E:\il2cpp\tmp\icallwide\icallwide.cs"
$out = "E:\il2cpp\tmp\icallwide"
$netall = "E:\il2cpp\tmp\netall"
$n910 = "E:\il2cpp\tmp\net910"

function Compile($tag, $refs, $defines) {
  $cargs = @($csc, '-nologo', '-nostdlib+', '-noconfig', '-target:exe', '-langversion:latest',
    "-out:$out\icallwide_$tag.dll")
  if ($defines) { $cargs += "-define:$defines" }
  foreach ($r in $refs) { $cargs += "-r:$r" }
  $cargs += $src
  $p = & dotnet @cargs 2>&1
  if ($LASTEXITCODE -ne 0) { Write-Output "${tag}: COMPILE FAIL"; Write-Output ($p | Select-Object -First 12) }
  else { Write-Output "${tag}: OK" }
}

$ra = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
Compile 'net48' @("$ra\mscorlib.dll", "$ra\System.dll", "$ra\System.Core.dll") 'LEGACY'

$v21 = "$netall\netcore21\shared\Microsoft.NETCore.App\2.1.30"
$v31 = "$netall\netcore31\shared\Microsoft.NETCore.App\3.1.32"
$v5  = "$netall\net5\shared\Microsoft.NETCore.App\5.0.17"
$v6  = "$netall\net6\shared\Microsoft.NETCore.App\6.0.36"
$v7  = "$netall\net7\shared\Microsoft.NETCore.App\7.0.20"
$v8  = "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.5"
$v9  = "$n910\net9\shared\Microsoft.NETCore.App\9.0.19"
$v10 = "$n910\net10\shared\Microsoft.NETCore.App\10.0.11"

function ModernRefs($v) { return @(
  "$v\System.Private.CoreLib.dll",
  "$v\System.Runtime.dll",
  "$v\System.Console.dll",
  "$v\System.Runtime.Extensions.dll",
  "$v\System.Threading.Thread.dll",
  "$v\System.Collections.NonGeneric.dll",
  "$v\System.Globalization.dll"
) }

Compile 'nc21' (ModernRefs $v21) ''
Compile 'nc31' (ModernRefs $v31) ''
Compile 'net5' (ModernRefs $v5)  'NEWMATH'
Compile 'net6' (ModernRefs $v6)  'NEWMATH'
Compile 'net7' (ModernRefs $v7)  'NEWMATH'
Compile 'net8' (ModernRefs $v8)  'NEWMATH'
Compile 'net9' (ModernRefs $v9)  'NEWMATH'
Compile 'net10'(ModernRefs $v10) 'NEWMATH'

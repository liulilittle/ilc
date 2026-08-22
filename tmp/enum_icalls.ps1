$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Reflection.Metadata
$asmPath = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll"
$asm = [System.Reflection.Assembly]::LoadFrom($asmPath)
$ic = [System.Reflection.MethodImplAttributes]::InternalCall
$out = @()
foreach ($t in $asm.GetTypes()) {
    if (-not $t.IsPublic) { continue }
    foreach ($m in $t.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::Instance)) {
        try {
            $impl = $m.GetMethodImplementationFlags()
        } catch { continue }
        if (($impl -band $ic) -eq $ic) {
            $owner = $t.FullName
            $name = $m.Name
            $ret = $m.ReturnType.Name
            $params = ($m.GetParameters() | ForEach-Object { $_.ParameterType.Name }) -join ','
            $static = if ($m.IsStatic) { 'S' } else { 'I' }
            $out += "$owner::$name($params):${ret}:${static}"
        }
    }
}
$out | Sort-Object | Set-Content tmp/icalls_net48_mscorlib.txt
"TOTAL InternalCall in net48 mscorlib: $($out.Count)"

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Reflection.Metadata
Add-Type -AssemblyName System.Reflection.PortableExecutable
$path = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll"
$bytes = [System.IO.File]::ReadAllBytes($path)
$stream = [System.IO.MemoryStream]::new($bytes)
$peReader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
$mdReader = [System.Reflection.Metadata.MetadataExtensions]::GetMetadataReader($peReader)
$internalCall = [System.Reflection.Metadata.MethodImplementationAttributes]::InternalCall
$out = @()
$names = @{}
foreach ($h in $mdReader.TypeDefinitions) {
    $td = $mdReader.GetTypeDefinition($h)
    $ns = $mdReader.GetString($td.Namespace)
    $tn = $mdReader.GetString($td.Name)
    $owner = if ($ns) { "$ns.$tn" } else { $tn }
    foreach ($mh in $td.GetMethods()) {
        $md = $mdReader.GetMethodDefinition($mh)
        $impl = $md.GetImplementationAttributes()
        if (($impl -band $internalCall) -eq $internalCall) {
            $name = $mdReader.GetString($md.Name)
            $out += "$owner::$name"
        }
    }
}
$out = $out | Sort-Object -Unique
$out | Set-Content tmp/icalls_net48_mscorlib.txt
"TOTAL InternalCall in net48 mscorlib: $($out.Count)"

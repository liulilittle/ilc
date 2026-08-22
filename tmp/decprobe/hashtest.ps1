$rtDir = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
Add-Type -Path (Join-Path $rtDir "System.Reflection.Metadata.dll") | Out-Null
Add-Type -Path (Join-Path $rtDir "System.Collections.Immutable.dll") | Out-Null
$fs = [System.IO.File]::OpenRead("E:\il2cpp\tmp\decprobe\decprobe.dll")
$pe = [System.Reflection.PortableExecutable.PEReader]::new($fs)
$md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
try {
    $mh = [System.Reflection.Metadata.MetadataTokens]::MemberReferenceHandle(6)
    Write-Output "handle ok: $mh"
    $p = $md.GetMemberReference($mh)
    Write-Output "name: $($md.GetString($p.Name))"
    Write-Output "parent kind: $($p.Parent.Kind)"
    if ($p.Parent.Kind -eq [System.Reflection.Metadata.HandleKind]::TypeReference) {
        $tr = $md.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$p.Parent)
        Write-Output "parent: $($md.GetString($tr.Namespace)).$($md.GetString($tr.Name))"
    }
} catch {
    Write-Output "ERR: $($_.Exception.GetType().FullName): $($_.Exception.Message)"
}
$pe.Dispose(); $fs.Dispose()

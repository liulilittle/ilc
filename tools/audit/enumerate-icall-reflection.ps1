<#
.SYNOPSIS
  Channel 1 of the net48 mscorlib InternalCall audit: runtime reflection.

.DESCRIPTION
  Loads the installed .NET Framework 4.8 mscorlib.dll and enumerates every
  type (public and non-public) and every method plus constructor, keeping the
  ones whose MethodImplementationFlags carry MethodImplAttributes.InternalCall
  (0x1000). Emits one TSV row per icall:
      declaringType <TAB> methodName <TAB> signature <TAB> visibility <TAB> isCtor

  PowerShell 5.1 note: MethodInfo.GetCustomAttribute does not exist on this
  host, so attributes are read through [System.Attribute]::GetCustomAttribute.
  The reflection channel does not actually need attributes; the helper is kept
  because the surrounding tooling uses it for the UnmanagedCallersOnly probes.

  Must run under Windows PowerShell 5.1 (Desktop CLR), NOT pwsh 7: the script
  LoadFrom()s the Framework mscorlib and needs the same runtime identity as the
  assembly it inspects.
#>
[CmdletBinding()]
param(
    [string] $MscorlibPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll',
    [Parameter(Mandatory = $true)][string] $OutFile
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $MscorlibPath)) {
    throw "mscorlib not found: $MscorlibPath"
}

$asm = [System.Reflection.Assembly]::LoadFrom($MscorlibPath)

$flagsCtor = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
$internalCall = [System.Reflection.MethodImplAttributes]::InternalCall
$inv = [System.Globalization.CultureInfo]::InvariantCulture

function Format-ParamType([Type] $t) {
    if ($null -eq $t) { return '?' }
    if ($t.IsByRef) { return (Format-ParamType $t.GetElementType()) + '&' }
    if ($t.IsPointer) { return (Format-ParamType $t.GetElementType()) + '*' }
    if ($t.IsArray) {
        $rank = $t.GetArrayRank()
        if ($rank -eq 1) { return (Format-ParamType $t.GetElementType()) + '[]' }
        return (Format-ParamType $t.GetElementType()) + '[' + (',' * ($rank - 1)) + ']'
    }
    # A method's generic parameter must render exactly like the metadata
    # channel renders it ("!0" for a type parameter, "!!0" for a method
    # parameter). Reflected with an empty FullName it would otherwise print
    # nothing, which would make the two channels' join keys differ on
    # System.WeakReference`1 for no real reason.
    if ($t.IsGenericParameter) {
        $prefix = if ($t.DeclaringMethod) { '!!' } else { '!' }
        return $prefix + $t.GenericParameterPosition
    }
    if ($t.IsGenericType -and $null -eq $t.FullName) {
        $def = $t.GetGenericTypeDefinition().FullName
        $args = @($t.GetGenericArguments() | ForEach-Object { Format-ParamType $_ })
        return $def + '<' + ($args -join ', ') + '>'
    }
    return $t.FullName
}

function Format-Signature([System.Reflection.MethodBase] $m) {
    $rt = 'System.Void'
    if ($m -is [System.Reflection.MethodInfo]) {
        $rt = Format-ParamType $m.ReturnType
    }
    $parts = @()
    foreach ($p in $m.GetParameters()) {
        $parts += (Format-ParamType $p.ParameterType)
    }
    return $rt + ' (' + ($parts -join ', ') + ')'
}

function Get-TypeVisibility([Type] $t) {
    if ($t.IsNested) {
        if ($t.IsNestedPublic) { return 'public' }
        if ($t.IsNestedFamORAssem) { return 'protected internal' }
        if ($t.IsNestedFamily) { return 'protected' }
        if ($t.IsNestedAssembly) { return 'internal' }
        return 'private'
    }
    if ($t.IsPublic) { return 'public' }
    return 'internal'
}

$types = $asm.GetTypes()
$rows = New-Object System.Collections.Generic.List[string]
$typeCount = 0

foreach ($t in $types) {
    $typeCount++
    $tv = Get-TypeVisibility $t
    $members = @()
    try {
        $members += $t.GetMethods($flagsCtor)
    } catch { }
    try {
        $members += $t.GetConstructors($flagsCtor)
    } catch { }

    foreach ($m in $members) {
        if (($m.GetMethodImplementationFlags() -band $internalCall) -eq 0) { continue }

        $isCtor = $m.IsConstructor
        $mv = if ($m.IsPublic) { 'public' }
              elseif ($m.IsFamily) { 'protected' }
              elseif ($m.IsFamilyOrAssembly) { 'protected internal' }
              elseif ($m.IsAssembly) { 'internal' }
              else { 'private' }
        $stat = if ($m.IsStatic) { 'static' } else { 'instance' }

        $rows.Add((
            $t.FullName + "`t" +
            $m.Name + "`t" +
            (Format-Signature $m) + "`t" +
            $tv + '/' + $mv + '/' + $stat + "`t" +
            $(if ($isCtor) { 'ctor' } else { 'method' })
        ))
    }
}

$rows | Sort-Object -CaseSensitive | Set-Content -LiteralPath $OutFile -Encoding UTF8

Write-Output ("types=" + $typeCount)
Write-Output ("icalls=" + $rows.Count)
Write-Output ("out=" + $OutFile)

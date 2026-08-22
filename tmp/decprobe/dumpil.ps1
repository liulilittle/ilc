# dumpil.ps1 -- minimal ECMA-335 IL disassembler using System.Reflection.Metadata
# Usage: pwsh -File dumpil.ps1 <assembly.dll>
param([string]$AsmPath = "decprobe.dll")

$rtDir = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
Add-Type -Path (Join-Path $rtDir "System.Reflection.Metadata.dll") | Out-Null
Add-Type -Path (Join-Path $rtDir "System.Collections.Immutable.dll") | Out-Null

# opcode -> operand kind. N=none, S=uint8 var, SB=sbyte branch, B=int32 branch,
# I4=int32, I8=int64, R4, R8, TOK=int32 token, STR=token, SW=switch
$ops = @{
0x00='nop|N';0x01='break|N';0x02='ldarg.0|N';0x03='ldarg.1|N';0x04='ldarg.2|N';0x05='ldarg.3|N'
0x06='ldloc.0|N';0x07='ldloc.1|N';0x08='ldloc.2|N';0x09='ldloc.3|N';0x0a='stloc.0|N';0x0b='stloc.1|N'
0x0c='stloc.2|N';0x0d='stloc.3|N';0x0e='ldarg.s|S';0x0f='ldarga.s|S';0x10='starg.s|S';0x11='ldloc.s|S'
0x12='ldloca.s|S';0x13='stloc.s|S';0x14='ldnull|N';0x15='ldc.i4.m1|N';0x16='ldc.i4.0|N';0x17='ldc.i4.1|N'
0x18='ldc.i4.2|N';0x19='ldc.i4.3|N';0x1a='ldc.i4.4|N';0x1b='ldc.i4.5|N';0x1c='ldc.i4.6|N';0x1d='ldc.i4.7|N'
0x1e='ldc.i4.8|N';0x1f='ldc.i4.s|SB';0x20='ldc.i4|I4';0x21='ldc.i8|I8';0x22='ldc.r4|R4';0x23='ldc.r8|R8'
0x25='dup|N';0x26='pop|N';0x27='jmp|TOK';0x28='call|TOK';0x29='calli|TOK';0x2a='ret|N'
0x2b='br.s|SB';0x2c='brfalse.s|SB';0x2d='brtrue.s|SB';0x2e='beq.s|SB';0x2f='bge.s|SB';0x30='bgt.s|SB'
0x31='ble.s|SB';0x32='blt.s|SB';0x33='bne.un.s|SB';0x34='bge.un.s|SB';0x35='bgt.un.s|SB';0x36='ble.un.s|SB'
0x37='blt.un.s|SB';0x38='br|B';0x39='brfalse|B';0x3a='brtrue|B';0x3b='beq|B';0x3c='bge|B';0x3d='bgt|B'
0x3e='ble|B';0x3f='blt|B';0x40='bne.un|B';0x41='bge.un|B';0x42='bgt.un|B';0x43='ble.un|B';0x44='blt.un|B'
0x45='switch|SW';0x46='ldind.i1|N';0x47='ldind.u1|N';0x48='ldind.i2|N';0x49='ldind.u2|N';0x4a='ldind.i4|N'
0x4b='ldind.u4|N';0x4c='ldind.i8|N';0x4d='ldind.i|N';0x4e='ldind.r4|N';0x4f='ldind.r8|N';0x50='ldind.ref|N'
0x51='stind.ref|N';0x52='stind.i1|N';0x53='stind.i2|N';0x54='stind.i4|N';0x55='stind.i8|N';0x56='stind.r4|N'
0x57='stind.r8|N';0x58='add|N';0x59='sub|N';0x5a='mul|N';0x5b='div|N';0x5c='div.un|N';0x5d='rem|N'
0x5e='rem.un|N';0x5f='and|N';0x60='or|N';0x61='xor|N';0x62='shl|N';0x63='shr|N';0x64='shr.un|N'
0x65='neg|N';0x66='not|N';0x67='conv.i1|N';0x68='conv.i2|N';0x69='conv.i4|N';0x6a='conv.i8|N'
0x6b='conv.r4|N';0x6c='conv.r8|N';0x6d='conv.u4|N';0x6e='conv.u8|N';0x6f='callvirt|TOK';0x70='cpobj|TOK'
0x71='ldobj|TOK';0x72='ldstr|STR';0x73='newobj|TOK';0x74='castclass|TOK';0x75='isinst|TOK';0x76='conv.r.un|N'
0x78='unbox|TOK';0x79='throw|N';0x7a='ldfld|TOK';0x7b='ldflda|TOK';0x7c='stfld|TOK';0x7d='ldsfld|TOK'
0x7e='ldsflda|TOK';0x7f='stsfld|TOK';0x80='stobj|TOK';0x81='conv.ovf.i1.un|N';0x82='conv.ovf.i2.un|N'
0x83='conv.ovf.i4.un|N';0x84='conv.ovf.i8.un|N';0x85='conv.ovf.u1.un|N';0x86='conv.ovf.u2.un|N'
0x87='conv.ovf.u4.un|N';0x88='conv.ovf.u8.un|N';0x89='conv.ovf.i.un|N';0x8a='conv.ovf.u.un|N'
0x8b='box|TOK';0x8c='newarr|TOK';0x8d='ldlen|N';0x8e='ldelema|TOK';0x8f='ldelem.i1|N';0x90='ldelem.u1|N'
0x91='ldelem.i2|N';0x92='ldelem.u2|N';0x93='ldelem.i4|N';0x94='ldelem.u4|N';0x95='ldelem.i8|N'
0x96='ldelem.i|N';0x97='ldelem.r4|N';0x98='ldelem.r8|N';0x99='ldelem.ref|N';0x9a='stelem.i|N'
0x9b='stelem.i1|N';0x9c='stelem.i2|N';0x9d='stelem.i4|N';0x9e='stelem.i8|N';0x9f='stelem.r4|N'
0xa0='stelem.r8|N';0xa1='stelem.ref|N';0xa2='ldelem|TOK';0xa3='stelem|TOK';0xa4='unbox.any|TOK'
0xb3='conv.ovf.i1|N';0xb4='conv.ovf.u1|N';0xb5='conv.ovf.i2|N';0xb6='conv.ovf.u2|N';0xb7='conv.ovf.i4|N'
0xb8='conv.ovf.u4|N';0xb9='conv.ovf.i8|N';0xba='conv.ovf.u8|N';0xc2='ckfinite|N';0xc6='ldftn|TOK'
0xd0='ldtoken|TOK';0xd6='add.ovf|N';0xd7='add.ovf.un|N';0xd8='mul.ovf|N';0xd9='mul.ovf.un|N'
0xda='sub.ovf|N';0xdb='sub.ovf.un|N';0xdc='endfinally|N';0xdd='leave|B';0xde='leave.s|SB'
0xdf='stind.i|N';0xe0='conv.u|N'
}
$feOps = @{0x00='arglist|N';0x01='ceq|N';0x02='cgt|N';0x03='cgt.un|N';0x04='clt|N';0x05='clt.un|N'
0x09='ldarg|V16';0x0a='ldarga|V16';0x0b='starg|V16';0x0c='ldloc|V16';0x0d='ldloca|V16';0x0e='stloc|V16'
0x0f='localloc|N';0x11='endfilter|N';0x12='unaligned|S';0x13='volatile|N';0x14='tail|N'
0x15='initobj|TOK';0x16='constrained|TOK';0x17='cpblk|N';0x18='initblk|N';0x1a='rethrow|N'
0x1c='sizeof|TOK';0x1d='refanyval|TOK';0x1e='ckfinite|N'}

$fs = [System.IO.File]::OpenRead((Resolve-Path $AsmPath))
$pe = [System.Reflection.PortableExecutable.PEReader]::new($fs)
$md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)

# Build token->name maps by iterating tables in ROW ORDER (token = 0xTT000000 | row).
$tokMap = @{}
$row = 1
foreach ($h in $md.MemberReferences) {
    $p = $md.GetMemberReference($h)
    $nm = $md.GetString($p.Name); $pn = ""
    try {
        if ("$($p.Parent.Kind)" -eq 'TypeReference') {
            $tr = $md.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$p.Parent)
            $ns = $md.GetString($tr.Namespace)
            if ($ns) { $pn = "$ns." }
            $pn += "$($md.GetString($tr.Name))::"
        } else { $pn = "<$($p.Parent.Kind)>::" }
    } catch { $pn = "?" }
    $tokMap[0x0A000000 -bor $row] = "$pn$nm"
    $row++
}
$row = 1
foreach ($h in $md.FieldDefinitions) {
    $f = $md.GetFieldDefinition($h)
    $tokMap[0x04000000 -bor $row] = "field $($md.GetString($f.Name))"
    $row++
}
$row = 1
foreach ($h in $md.MethodDefinitions) {
    $m2 = $md.GetMethodDefinition($h)
    $tokMap[0x06000000 -bor $row] = "methoddef $($md.GetString($m2.Name))"
    $row++
}
$row = 1
foreach ($h in $md.TypeReferences) {
    $t2 = $md.GetTypeReference($h)
    $tokMap[0x01000000 -bor $row] = "typeref $($md.GetString($t2.Namespace)).$($md.GetString($t2.Name))"
    $row++
}

function Resolve-Token([int]$tok) {
    if ($tokMap.ContainsKey($tok)) { return $tokMap[$tok] }
    return "tok(0x{0:X8})" -f $tok
}

foreach ($tdh in $md.TypeDefinitions) {
    $td = $md.GetTypeDefinition($tdh)
    $tname = $md.GetString($td.Namespace) + "." + $md.GetString($td.Name)
    "== TYPE $tname =="
    foreach ($fdh in $td.GetFields()) {
        $fd = $md.GetFieldDefinition($fdh)
        "  FIELD $($md.GetString($fd.Name))"
        # print RVA data blob if present (decimal const backing data)
        if ($fd.GetRelativeVirtualAddress() -ne 0) {
            $blobOff = $fd.GetRelativeVirtualAddress()
            "    (has field RVA data at rva 0x{0:X})" -f $blobOff | Write-Output
        }
    }
    foreach ($mdh in $td.GetMethods()) {
        $m = $md.GetMethodDefinition($mdh)
        "  METHOD $($md.GetString($m.Name))"
        $body = [System.Reflection.Metadata.PEReaderExtensions]::GetMethodBody($pe, [int]$m.RelativeVirtualAddress)
        $il = $body.GetILBytes(); $len = $il.Length; $i = 0
        while ($i -lt $len) {
            $off = $i; $b = $il[$i]; $i++
            if ($b -eq 0xFE) { $b2 = $il[$i]; $i++; $ent = $feOps[[int]$b2]; $name = "?fe_$('{0:X2}' -f $b2)" }
            else { $ent = $ops[[int]$b] }
            if (-not $ent) { "    IL_{0:X4}: ?? 0x{1:X2}" -f $off, $b; continue }
            $parts = $ent.Split('|'); $name = $parts[0]; $kind = $parts[1]
            switch ($kind) {
                'N'   { $txt = "" }
                'S'   { $v = $il[$i]; $i += 1; $txt = " $v" }
                'V16' { $v = [BitConverter]::ToUInt16($il, $i); $i += 2; $txt = " $v" }
                'SB'  { $v = [sbyte]$il[$i]; $i += 1; $txt = " IL_{0:X4}" -f ($i + $v) }
                'B'   { $v = [BitConverter]::ToInt32($il, $i); $i += 4; $txt = " IL_{0:X4}" -f ($i + $v) }
                'I4'  { $v = [BitConverter]::ToInt32($il, $i); $i += 4; $txt = " $v (0x{0:X})" -f $v }
                'I8'  { $v = [BitConverter]::ToInt64($il, $i); $i += 8; $txt = " $v" }
                'R4'  { $v = [BitConverter]::ToSingle($il, $i); $i += 4; $txt = " $v" }
                'R8'  { $v = [BitConverter]::ToDouble($il, $i); $i += 8; $txt = " $v" }
                'TOK' { $v = [BitConverter]::ToInt32($il, $i); $i += 4; $txt = " $(Resolve-Token $v)" }
                'STR' { $v = [BitConverter]::ToInt32($il, $i); $i += 4;
                        try { $usH=[System.Reflection.Metadata.UserStringHandle]::new($v -band 0xFFFFFF); $txt=" `"$($md.GetUserString($usH))`"" } catch { $txt=" str(0x{0:X8})" -f $v } }
                'SW'  { $n = [BitConverter]::ToInt32($il, $i); $i += 4; $base=$i + 4*$n
                        $ts=@(); for($k=0;$k -lt $n;$k++){ $d=[BitConverter]::ToInt32($il,$i); $i+=4; $ts += ("IL_{0:X4}" -f ($base+$d)) }
                        $txt = " (" + ($ts -join ", ") + ")" }
                default { $txt = " ?$kind" }
            }
            "    IL_{0:X4}: {1}{2}" -f $off, $name, $txt
        }
    }
}
$pe.Dispose(); $fs.Dispose()


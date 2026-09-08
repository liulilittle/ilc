path=r'E:\il2cpp\src\il2cppc\Emit\CodeGenerator.cs'
lines=open(path, encoding='utf-8', errors='ignore').readlines()
for i,line in enumerate(lines,1):
    if 'System.Boolean' in line or 'IsEnumReceiverType' in line or 'primName switch' in line or 'ResolveParamEncoding' in line or 'ResolveFromFullName' in line or 'FieldValueTypeKind' in line or 'FieldAddrCppKind' in line:
        print(f'{i:4d}: {line.rstrip()[:200]}')

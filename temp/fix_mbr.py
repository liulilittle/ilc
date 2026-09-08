path = r'E:\il2cpp\src\il2cppc\Il\MethodBodyReader.cs'
with open(path, encoding='utf-8') as f:
    content = f.read()

start = content.index('private static string LocalTypeKind(')
depth = 0
i = content.index('{', start)
while i < len(content):
    if content[i] == '{':
        depth += 1
    elif content[i] == '}':
        depth -= 1
        if depth == 0:
            break
    i += 1
end = i + 1

new_method = '''private static string LocalTypeKind(MetadataReader md, ref BlobReader sig, string name)
    {
        try
        {
            var b = sig.ReadByte();
            if (b is 0x0A or 0x0B) return "l";
            if (b is 0x0C or 0x0D) return "d";
            if (b is 0x0E or 0x1C) return "r";
            if (b is 0x11 or 0x12 or 0x40)
            {
                var th = sig.ReadTypeHandle();
                if (b == 0x11)
                {
                    if (th.Kind == HandleKind.TypeSpecification)
                    {
                        var ts = md.GetTypeSpecification((TypeSpecificationHandle)th);
                        var full = GenericInstantiationName(md, ts);
                        if (full != null) return "v:" + full;
                    }
                    else
                    {
                        return "v:" + md.GetFullName((TypeDefinitionHandle)th);
                    }
                }
                return "r";
            }
            if (b is 0x15)
            {
                var argc = sig.ReadCompressedInteger();
                var codes = new List<string>();
                var flat = true;
                for (var i = 0; i < argc; i++)
                {
                    var ab = sig.ReadByte();
                    switch (ab)
                    {
                        case 0x08 or 0x09: codes.Add("i"); break;
                        case 0x0A or 0x0B: codes.Add("l"); break;
                        case 0x0C or 0x0D: codes.Add("d"); break;
                        case 0x0E: codes.Add("s"); break;
                        case 0x11 or 0x12: _ = sig.ReadTypeHandle(); flat = false; break;
                        case 0x1D or 0x1F: _ = sig.ReadByte(); flat = false; break;
                        case 0x13 or 0x1E: _ = sig.ReadCompressedInteger(); flat = false; break;
                        case 0x15: flat = false; break;
                        default: codes.Add("i"); break;
                    }
                }
                if (flat)
                    return "tuple:" + string.Join(",", codes);
            }
            var argc2 = sig.ReadCompressedInteger();
            for (var i = 0; i < argc2; i++)
            {
                var ab = sig.ReadByte();
                if (ab is 0x11 or 0x12) _ = sig.ReadTypeHandle();
                else if (ab is 0x1D or 0x1F) _ = sig.ReadByte();
                else if (ab is 0x13 or 0x1E) _ = sig.ReadCompressedInteger();
            }
            return defName != null
                && (defName.StartsWith("System.Runtime.CompilerServices.TaskAwaiter`1", StringComparison.Ordinal)
                    || defName.StartsWith("System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1", StringComparison.Ordinal))
                ? "r"
                : "v";
        }
        catch (InvalidOperationException)
        {
            Console.Error.WriteLine($"warning: invalid compressed integer in local signature for {name} -> tolerant fallback to int32 slot (BCL-scale)");
            return "i";
        }
    }
'''

content = content[:start] + new_method + content[end:]
with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
print('SUCCESS')

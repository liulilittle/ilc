import pathlib, re

# bounded search
roots = [r"E:\il2cpp\src\runtime", r"E:\il2cpp\src\il2cppc"]
for root in roots:
    for p in pathlib.Path(root).rglob("*.cpp"):
        txt = p.read_text(encoding='utf-8', errors='ignore')
        if "icall_number_format_decimal" in txt or "icall_number_number_buffer_to_decimal" in txt:
            print("NUMBER found cpp", p)
            for i,line in enumerate(txt.splitlines(),1):
                if "icall_number" in line and "decimal" in line.lower():
                    print(i, line.strip())
    for p in pathlib.Path(root).rglob("*.h"):
        txt = p.read_text(encoding='utf-8', errors='ignore')
        if "icall_number_format_decimal" in txt or "icall_number_number_buffer_to_decimal" in txt:
            print("NUMBER found h", p)
            for i,line in enumerate(txt.splitlines(),1):
                if "icall_number" in line and "decimal" in line.lower():
                    print(i, line.strip())
    for p in pathlib.Path(root).rglob("*.cs"):
        txt = p.read_text(encoding='utf-8', errors='ignore')
        if "icall_number_format_decimal" in txt or "icall_number_number_buffer_to_decimal" in txt:
            print("NUMBER found cs", p)
            for i,line in enumerate(txt.splitlines(),1):
                if "icall_number" in line and "decimal" in line.lower():
                    print(i, line.strip())

# Also check decimal_format: does it include number?
print("check stubs")
print(pathlib.Path(r"E:\il2cpp\src\runtime\include\internal\stubs.h").read_text(encoding='utf-8')[1900:2500])
print(pathlib.Path(r"E:\il2cpp\src\runtime\src\stubs.cpp").read_text(encoding='utf-8')[800:1500])

# AuditInternalCalls: System.Decimal real internal calls = 6, all OK. So gap 0 for that layer.
# But we need to stat overall MethodMap vs runtime.

# Let's also check if audit-internalcalls sees System.Decimal as InternalCall? Check PEReader dump for Decimal
# Let's use python to dump Decimal methods from mscorlib via System.Reflection.Metadata

import pathlib as pl
dll = r"C:\Windows\Microsoft.NET\Framework\v4.0.30319\mscorlib.dll"
from pathlib import Path
# Use clr? Let's try to use System.Reflection.Metadata via python's clr? Instead run a small dotnet tool

# We'll write a small C# dump

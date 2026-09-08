import pathlib, re

# Need to check audit-internalcalls for System.Decimal
# Build the audit tool if needed? Look at previous audit approach.
# Look at tests/unit/MetadataLoaderTests.Audit.cs etc.
# Actually audit-internalcalls is implemented in il2cppc/Emit/AuditInternalCalls.cs
# but need a runner. Check existing tmp tasks

# For Decimal, we can also directly inspect mscorlib's InternalCall list for Decimal.
# Let's run audit via dotnet using the transpiler project? Look for how previous audit did it.

# Simpler: run the existing compiled audit? See taskaudit dirs
import glob, os

for p in pathlib.Path(r"E:\il2cpp\tmp").rglob("*.cs"):
    print(p)
    print(pathlib.Path(p).read_text(encoding='utf-8')[:800])
    print("---")
    if str(p).count("\\") > 10:
        break

# Check AuditInternalCalls usage grep
for f in pathlib.Path(r"E:\il2cpp").rglob("*.cs"):
    txt = f.read_text(encoding='utf-8', errors='ignore')
    if "AuditInternalCalls" in txt:
        print("found in", f)
        for line in txt.splitlines():
            if "AuditInternalCalls" in line:
                print(line.strip())

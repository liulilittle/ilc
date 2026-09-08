import pathlib, re

# Check existence of icall_decimal_fcall_to_int32
for p in pathlib.Path(r"E:\il2cpp\src").rglob("*.*"):
    txt = p.read_text(encoding='utf-8', errors='ignore')
    if "icall_decimal_fcall_to_int32" in txt:
        print("found", p)
        for i,line in enumerate(txt.splitlines(),1):
            if "icall_decimal_fcall" in line:
                print(i, line.strip())
        print("---")

# Also check icall_number_format_decimal and icall_number_number_buffer_to_decimal
# These were seen in audit-internalcalls as System.Number.FormatDecimal

for p in pathlib.Path(r"E:\il2cpp\src").rglob("*.*"):
    txt = p.read_text(encoding='utf-8', errors='ignore')
    if "icall_number_format_decimal" in txt or "icall_number_number_buffer_to_decimal" in txt:
        print("NUMBER found", p)
        for i,line in enumerate(txt.splitlines(),1):
            if "icall_number" in line and "decimal" in line.lower():
                print(i, line.strip())

# Check decimal.h vs decimal_format.h completeness
# Also check audit-internalcalls handling of DecimalCanonicalizeInternal

txt = pathlib.Path(r"E:\il2cpp\src\il2cppc\Emit\AuditInternalCalls.cs").read_text(encoding='utf-8')
print(txt[:500])

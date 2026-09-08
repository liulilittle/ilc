import re
import os
from collections import Counter

log_dir = r"E:\il2cpp\r77shard"
log_files = [
    os.path.join(log_dir, "r77shard1_shard1_errors.log"),
    os.path.join(log_dir, "r77shard2_shard2_errors.log"),
    os.path.join(log_dir, "r77shard3_shard3_errors.log"),
]

v_types = Counter()
generic_undeclared = Counter()
all_unknown = Counter()

for log_file in log_files:
    if not os.path.exists(log_file):
        continue
    
    with open(log_file, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.strip()
            if 'error:' not in line:
                continue
            
            # V_ prefixed unknown types
            if 'unknown type name' in line:
                m = re.search(r"unknown type name '([^']+)'", line)
                if m:
                    tn = m.group(1)
                    all_unknown[tn] += 1
                    if tn.startswith('V_'):
                        v_types[tn] += 1
            
            # Undeclared identifiers
            if 'undeclared identifier' in line:
                m = re.search(r"use of undeclared identifier '([^']+)'", line)
                if m:
                    tn = m.group(1)
                    if '_int' in tn or 'Generic' in tn or '`' in tn or tn.startswith('V_'):
                        generic_undeclared[tn] += 1

print("=== ALL UNKNOWN TYPES (Top 30) ===")
for tn, count in all_unknown.most_common(30):
    print(f"  {tn}: {count}")

print(f"\n=== V_ PREFIXED UNKNOWN TYPES (Top 20) ===")
for tn, count in v_types.most_common(20):
    print(f"  {tn}: {count}")

print(f"\n=== GENERIC UNDECLARED IDENTIFIERS (Top 20) ===")
for tn, count in generic_undeclared.most_common(20):
    print(f"  {tn}: {count}")

# Find examples of V_ errors
print("\n=== V_ ERROR EXAMPLES ===")
shown = set()
for log_file in log_files:
    with open(log_file, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            if 'unknown type name' in line:
                m = re.search(r"unknown type name '([^']+)'", line)
                if m:
                    tn = m.group(1)
                    if tn.startswith('V_') and tn not in shown:
                        shown.add(tn)
                        print(f"\n{tn}:")
                        print(f"  {line[:200]}")
                        if len(shown) >= 5:
                            break
    if len(shown) >= 5:
        break

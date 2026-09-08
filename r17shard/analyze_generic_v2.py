import re
import os
from collections import Counter, defaultdict

log_dir = r"E:\il2cpp\r77shard"
log_files = [
    os.path.join(log_dir, "r77shard1_shard1_errors.log"),
    os.path.join(log_dir, "r77shard2_shard2_errors.log"),
    os.path.join(log_dir, "r77shard3_shard3_errors.log"),
]

# Focus on generic-related errors
v_types = Counter()
generic_undeclared = Counter()
generic_redef = Counter()

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
                    if tn.startswith('V_'):
                        v_types[tn] += 1
            
            # Generic undeclared identifiers
            if 'undeclared identifier' in line:
                m = re.search(r"use of undeclared identifier '([^']+)'", line)
                if m:
                    tn = m.group[1] if '[' not in m.group(1) else m.group(1)
                    if '_int' in tn or 'Generic' in tn or '`' in tn:
                        generic_undeclared[tn] += 1

print("=== V_ PREFIXED UNKNOWN TYPES (Top 30) ===")
for tn, count in v_types.most_common(30):
    print(f"  {tn}: {count}")

print(f"\nTotal V_ unknown types: {sum(v_types.values())}")

print("\n=== GENERIC UNDECLARED IDENTIFIERS (Top 20) ===")
for tn, count in generic_undeclared.most_common(20):
    print(f"  {tn}: {count}")

print(f"\nTotal generic undeclared: {sum(generic_undeclared.values())}")

# Find specific V_ type examples
print("\n=== V_ TYPE ERROR EXAMPLES ===")
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
                        if len(shown) >= 10:
                            break
    if len(shown) >= 10:
        break

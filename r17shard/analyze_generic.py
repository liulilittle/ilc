import re
import os
from collections import Counter, defaultdict

log_dir = r"E:\il2cpp\r77shard"
log_files = [
    os.path.join(log_dir, "r77shard1_shard1_errors.log"),
    os.path.join(log_dir, "r77shard2_shard2_errors.log"),
    os.path.join(log_dir, "r77shard3_shard3_errors.log"),
]

# Generic-related error patterns
generic_patterns = [
    r'unknown type name.*V_',  # Value type prefixed with V_
    r'unknown type name.*Generic',
    r'unknown type name.*`',   # Generic type names with backtick
    r'undeclared identifier.*_int',
    r'undeclared identifier.*Generic',
    r'redefinition.*_int',
    r'no matching function.*_int',
    r'unknown type name.*Nested',
    r'unknown type name.*Inner',
    r'unknown type name.*Node',
    r'unknown type name.*Enumerator',
]

error_counter = Counter()
error_examples = defaultdict(list)
total_errors = 0

# Track V_ prefixed types specifically
v_types = Counter()

for log_file in log_files:
    if not os.path.exists(log_file):
        print(f"MISSING: {log_file}")
        continue
    
    print(f"Processing: {os.path.basename(log_file)}")
    
    with open(log_file, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.strip()
            if 'error:' not in line:
                continue
            
            total_errors += 1
            
            # Check for V_ prefixed types
            if 'unknown type name' in line:
                m = re.search(r"unknown type name '([^']+)'", line)
                if m:
                    type_name = m.group(1)
                    if type_name.startswith('V_'):
                        v_types[type_name] += 1
            
            # Categorize
            categorized = False
            for pat in generic_patterns:
                if re.search(pat, line, re.IGNORECASE):
                    error_counter[pat] += 1
                    if len(error_examples[pat]) < 3:
                        error_examples[pat].append(line[:250])
                    categorized = True
                    break
            
            if not categorized:
                error_counter['other'] += 1
                if len(error_examples['other']) < 3:
                    error_examples['other'].append(line[:250])

print(f"\n=== TOTAL ERRORS: {total_errors} ===")

print("\n=== GENERIC-RELATED ERROR PATTERNS ===")
for pat, count in error_counter.most_common(20):
    print(f"{pat}: {count}")

print("\n=== V_ PREFIXED TYPES (Top 20) ===")
for tn, count in v_types.most_common(20):
    print(f"  {tn}: {count}")

print("\n=== V_ TYPE EXAMPLES ===")
for tn, count in v_types.most_common(5):
    print(f"\n{tn} ({count}):")
    for log_file in log_files:
        with open(log_file, 'r', encoding='utf-8', errors='replace') as f:
            for line in f:
                if tn in line and 'error:' in line:
                    print(f"  {line[:200]}")
                    break

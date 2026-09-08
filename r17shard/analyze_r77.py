import re
import os
from collections import Counter, defaultdict

log_dir = r"E:\il2cpp\r77shard"
log_files = [
    os.path.join(log_dir, "r77shard1_shard1_errors.log"),
    os.path.join(log_dir, "r77shard2_shard2_errors.log"),
    os.path.join(log_dir, "r77shard3_shard3_errors.log"),
]

# Patterns for base class / inheritance related errors
inheritance_patterns = [
    r'base class',
    r'inheritance',
    r'derived',
    r'which are not related',
    r'cannot convert to base',
    r'no viable conversion',
    r'incompatible type',
    r'incompatible pointer',
    r'static_cast from',
    r'reinterpret_cast from',
    r'reinterpret_cast to',
    r'cannot cast',
    r'cannot initialize',
    r'assigning to',
    r'unknown type name',
    r'undeclared identifier',
    r'no member named',
    r'no matching function',
    r'no viable overloaded',
    r'invalid operands',
    r'ambiguous',
    r'too few arguments',
    r'too many arguments',
]

error_counter = Counter()
error_examples = defaultdict(list)
file_errors = Counter()
type_names = Counter()

for log_file in log_files:
    if not os.path.exists(log_file):
        print(f"MISSING: {log_file}")
        continue
    
    with open(log_file, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.strip()
            if 'error:' not in line:
                continue
            
            # Check for IThreadPoolWorkItem
            if 'IThreadPoolWorkItem' in line:
                if 'unknown type name' in line or 'undeclared identifier' in line:
                    # Extract file
                    m = re.match(r'^(.+?):\d+:\d+:\s*error:', line)
                    if m:
                        fname = m.group(1)
                        file_errors[fname] += 1
                    
                    # Extract type name
                    m = re.search(r"(System_Threading_IThreadPoolWorkItem[A-Za-z0-9_]*)", line)
                    if m:
                        type_names[m.group(1)] += 1
            
            # Categorize error
            for pat in inheritance_patterns:
                if re.search(pat, line, re.IGNORECASE):
                    error_counter[pat] += 1
                    if len(error_examples[pat]) < 3:
                        error_examples[pat].append(line[:200])
                    break

print("=== TOTAL ERRORS BY PATTERN ===")
for pat, count in error_counter.most_common(30):
    print(f"{pat}: {count}")

print("\n=== IThreadPoolWorkItem MISSING TYPES ===")
print(f"Total affected files: {len(file_errors)}")
for fname, count in file_errors.most_common():
    short = fname.split("\\")[-1] if "\\" in fname else fname
    print(f"  {short}: {count}")

print("\n=== IThreadPoolWorkItem Type Name Variants ===")
for tn, count in type_names.most_common():
    print(f"  {tn}: {count}")

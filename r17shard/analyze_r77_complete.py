import re
import os
from collections import Counter, defaultdict

log_dir = r"E:\il2cpp\r77shard"
log_files = [
    os.path.join(log_dir, "r77shard1_shard1_errors.log"),
    os.path.join(log_dir, "r77shard2_shard2_errors.log"),
    os.path.join(log_dir, "r77shard3_shard3_errors.log"),
]

# Error patterns to categorize
error_patterns = [
    'redefinition',
    'no member named',
    'unknown type name',
    'undeclared identifier',
    'incompatible pointer types',
    'incompatible pointer to integer',
    'incompatible type',
    'assigning to',
    'no viable conversion',
    'no viable overloaded',
    'no matching function',
    'cannot convert',
    'cannot cast',
    'cannot initialize',
    'static_cast from',
    'reinterpret_cast from',
    'reinterpret_cast to',
    'invalid operands',
    'array type',
    'ambiguous',
    'too few arguments',
    'too many arguments',
    'which are not related',
    'base class',
    'inheritance',
    'derived',
]

error_counter = Counter()
error_examples = defaultdict(list)
total_errors = 0

# IThreadPoolWorkItem specific analysis
tp_file_errors = Counter()
tp_type_names = Counter()
tp_total_unknown = 0

for log_file in log_files:
    if not os.path.exists(log_file):
        print(f"MISSING: {log_file}")
        continue
    
    print(f"Processing: {os.path.basename(log_file)}")
    
    with open(log_file, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            
            # Check for IThreadPoolWorkItem
            if 'IThreadPoolWorkItem' in line:
                if 'unknown type name' in line or 'undeclared identifier' in line:
                    tp_total_unknown += 1
                    # Extract file
                    m = re.match(r'^(.+?):\d+:\d+:\s*error:', line)
                    if m:
                        fname = m.group(1).split('\\')[-1] if '\\' in m.group(1) else m.group(1)
                        tp_file_errors[fname] += 1
                    
                    # Extract type name
                    m = re.search(r"(System_Threading_IThreadPoolWorkItem[A-Za-z0-9_]*)", line)
                    if m:
                        tp_type_names[m.group(1)] += 1
            
            # Count errors by pattern
            if 'error:' not in line:
                continue
            
            total_errors += 1
            categorized = False
            for pat in error_patterns:
                if pat in line:
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

print("\n=== ERROR PATTERN COUNTS (Top 25) ===")
for pat, count in error_counter.most_common(25):
    pct = count * 100 / total_errors if total_errors > 0 else 0
    print(f"{pat}: {count} ({pct:.1f}%)")

print("\n=== ERROR PATTERN EXAMPLES ===")
for pat, count in error_counter.most_common(15):
    print(f"\n--- {pat} ({count}) ---")
    for ex in error_examples[pat][:3]:
        print(f"  {ex}")

print("\n" + "="*60)
print("=== IThreadPoolWorkItem ANALYSIS ===")
print("="*60)

print(f"\nTotal 'unknown type name' errors for IThreadPoolWorkItem: {tp_total_unknown}")
print(f"Number of affected files: {len(tp_file_errors)}")

print("\n=== Per-file IThreadPoolWorkItem error counts ===")
for fname, count in tp_file_errors.most_common():
    print(f"  {fname}: {count}")

print("\n=== IThreadPoolWorkItem type name variants ===")
for tn, count in tp_type_names.most_common():
    print(f"  {tn}: {count}")

print("\n" + "="*60)
print("=== BASE CLASS / INHERITANCE ERRORS ===")
print("="*60)

inheritance_cats = ['base class', 'inheritance', 'derived', 'which are not related']
for cat in inheritance_cats:
    count = error_counter.get(cat, 0)
    print(f"\n{cat}: {count}")
    for ex in error_examples.get(cat, [])[:5]:
        print(f"  {ex}")

# Check if there are "cannot convert" errors that mention inheritance
print("\n=== CANNOT CONVERT with inheritance context ===")
for pat in ['cannot convert', 'no viable conversion', 'incompatible type']:
    examples_with_inheritance = [e for e in error_examples.get(pat, []) if 'base' in e.lower() or 'inherit' in e.lower() or 'related' in e.lower()]
    if examples_with_inheritance:
        print(f"\n{pat} (with inheritance context):")
        for ex in examples_with_inheritance:
            print(f"  {ex}")

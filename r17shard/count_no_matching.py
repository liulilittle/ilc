import re
import os
from collections import Counter

log_dir = r"E:\il2cpp\r78shard"
log_files = [
    os.path.join(log_dir, "r78shard1_shard1_errors.log"),
    os.path.join(log_dir, "r78shard2_shard2_errors.log"),
    os.path.join(log_dir, "r78shard3_shard3_errors.log"),
]

no_matching = Counter()
no_matching_examples = {}

for log_file in log_files:
    if not os.path.exists(log_file):
        continue
    
    with open(log_file, 'r', encoding='utf-8', errors='replace') as f:
        for line in f:
            if 'no matching function' in line:
                # Extract function name
                m = re.search(r"no matching function for call to '([^']+)'", line)
                if m:
                    fn = m.group(1)
                    no_matching[fn] += 1
                    if fn not in no_matching_examples:
                        no_matching_examples[fn] = line.strip()[:200]

print("=== NO MATCHING FUNCTION (Top 30) ===")
for fn, count in no_matching.most_common(30):
    print(f"{fn}: {count}")

print(f"\nTotal unique functions: {len(no_matching)}")
print(f"Total errors: {sum(no_matching.values())}")

print("\n=== EXAMPLES ===")
for fn, count in no_matching.most_common(10):
    print(f"\n{fn} ({count}):")
    print(f"  {no_matching_examples[fn]}")

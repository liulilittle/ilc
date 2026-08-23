#!/bin/bash
cd /mnt/e/il2cpp || exit 1
for t in test_decimal_format test_enumext test_parse; do
  echo "=== $t ==="
  ./build/linux_suite_fresh/$t 2>&1 | grep -iE 'fail|error|assert' | head -6
done

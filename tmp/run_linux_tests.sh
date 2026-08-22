#!/bin/bash
cd /mnt/e/il2cpp/build/linux_tests || exit 1
pass=0
fail=0
failed=""
for t in test_*; do
  [ -x "$t" ] || continue
  if ./"$t" >/dev/null 2>&1; then
    pass=$((pass+1))
  else
    fail=$((fail+1))
    failed="$failed $t"
  fi
done
echo "LINUX_PASS=$pass FAIL=$fail"
echo "FAILED:$failed"

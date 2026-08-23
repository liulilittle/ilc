#!/bin/bash
cd /mnt/e/il2cpp || exit 1
cmake --build build/linux_suite_fresh -j$(nproc) > /tmp/e2b.log 2>&1
echo "build=$?"
grep -ci error /tmp/e2b.log || true
pass=0; fail=0; failed=""
for exe in build/linux_suite_fresh/test_*; do
  [ -x "$exe" ] || continue
  "$exe" > /dev/null 2>&1
  if [ $? -eq 0 ]; then pass=$((pass+1)); else fail=$((fail+1)); failed="$failed $(basename $exe)"; fi
done
echo "E2 Linux runtime: PASS=$pass FAIL=$fail"
if [ $fail -gt 0 ]; then echo "FAILED:$failed"; fi

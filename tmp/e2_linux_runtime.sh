#!/bin/bash
# E2: Linux runtime suite -- fresh configure against LATEST sources, build, run all.
cd /mnt/e/il2cpp || exit 1
rm -rf build/linux_suite_fresh
cmake -S tests/runtime -B build/linux_suite_fresh -G "Unix Makefiles" -DCMAKE_BUILD_TYPE=Release > /tmp/e2_cfg.log 2>&1
if [ $? -ne 0 ]; then echo "CONFIGURE FAILED"; tail -10 /tmp/e2_cfg.log; exit 1; fi
cmake --build build/linux_suite_fresh -j$(nproc) > /tmp/e2_bld.log 2>&1
if [ $? -ne 0 ]; then echo "BUILD FAILED"; grep -i error /tmp/e2_bld.log | head -8; exit 1; fi
pass=0; fail=0; failed=""
for exe in build/linux_suite_fresh/test_*.exe; do
  "$exe" > /dev/null 2>&1
  if [ $? -eq 0 ]; then pass=$((pass+1)); else fail=$((fail+1)); failed="$failed $(basename $exe)"; fi
done
echo "E2 Linux runtime: PASS=$pass FAIL=$fail"
if [ $fail -gt 0 ]; then echo "FAILED:$failed"; fi

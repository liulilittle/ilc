#!/bin/bash
cd /mnt/e/il2cpp || exit 1
for tag in net8 net9 net10; do
  cmake -S "tmp/icallcase/gen_$tag" -B "tmp/icallcase/build_linux_$tag" -G "Unix Makefiles" -DCMAKE_BUILD_TYPE=Release > /dev/null 2>&1
  cmake --build "tmp/icallcase/build_linux_$tag" -j4 > "/tmp/bld_case_$tag.log" 2>&1
  if [ -f "tmp/icallcase/build_linux_$tag/il2cpp_gen_exe" ]; then
    "tmp/icallcase/build_linux_$tag/il2cpp_gen_exe" > "/tmp/run_case_$tag.txt" 2>&1
    rc=$?
    fails=$(grep -c '^FAIL' "/tmp/run_case_$tag.txt")
    verdict=$(grep -E 'ALL-PASS|FAILURES' "/tmp/run_case_$tag.txt")
    echo "${tag}: EXIT=$rc FAILS=$fails [$verdict]"
  else
    echo "${tag}: BUILD FAILED -- tail:"
    tail -5 "/tmp/bld_case_$tag.log"
  fi
done

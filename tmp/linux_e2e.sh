#!/bin/bash
cd /mnt/e/il2cpp || exit 1
for tag in net48 nc21 net7; do
  cmake -S "tmp/icallclassic/gen_$tag" -B "tmp/icallclassic/build_linux_$tag" -G 'Unix Makefiles' -DCMAKE_BUILD_TYPE=Release > /dev/null 2>&1
  cmake --build "tmp/icallclassic/build_linux_$tag" -j4 > "/tmp/bld_$tag.log" 2>&1
  if [ -f "tmp/icallclassic/build_linux_$tag/il2cpp_gen_exe" ]; then
    "tmp/icallclassic/build_linux_$tag/il2cpp_gen_exe" > "/tmp/run_$tag.txt" 2>&1
    rc=$?
    fails=$(grep -c '^FAIL' "/tmp/run_$tag.txt")
    verdict=$(grep -E 'ALL-PASS|FAILURES' "/tmp/run_$tag.txt")
    echo "${tag}: EXIT=$rc FAILS=$fails [$verdict]"
  else
    echo "${tag}: BUILD FAILED"
  fi
done

#!/bin/bash
cd /mnt/e/il2cpp || exit 1
status=0
for spec in "icallwide net48" "icallwide nc21" "icallwide nc31" "icallwide net5" "icallwide net6" "icallwide net7" "icallwide net8" "icallwide net9" "icallwide net10"; do
  set -- $spec
  proj=$1; tag=$2
  dir="tmp/$proj"
  cmake -S "$dir/gen_$tag" -B "$dir/build_$tag" -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release > "/tmp/cfg_${proj}_${tag}.log" 2>&1
  cmake --build "$dir/build_$tag" -j8 > "/tmp/bld_${proj}_${tag}.log" 2>&1
  if [ $? -ne 0 ]; then
    echo "${proj}/${tag}: BUILD FAILED -- tail:"
    tail -6 "/tmp/bld_${proj}_${tag}.log"
    status=1
    continue
  fi
  exe=$(ls "$dir/build_$tag"/il2cpp_gen_exe* 2>/dev/null | head -1)
  "$exe" > "tmp/parity/${proj}_${tag}_win.txt" 2>&1
  rc=$?
  fails=$(grep -c '^FAIL' "tmp/parity/${proj}_${tag}_win.txt")
  verdict=$(grep -E 'ALL-PASS|FAILURES' "tmp/parity/${proj}_${tag}_win.txt")
  echo "${proj}/${tag}: EXIT=$rc FAILS=$fails [$verdict]"
done
exit $status

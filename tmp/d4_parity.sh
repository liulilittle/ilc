#!/bin/bash
cd /mnt/e/il2cpp || exit 1
mkdir -p tmp/parity
status=0
run_one() {
  local proj=$1 tag=$2 dir=$3
  local exe="$dir/build_linux_$tag/il2cpp_gen_exe"
  if [ ! -x "$exe" ]; then
    # classic tags without build_linux_ dir yet (nc31/net5/net6 have them; net48/nc21/net7 too)
    echo "${proj}/${tag}: NO LINUX EXE ($exe)"
    status=1
    return
  fi
  "$exe" > "tmp/parity/${proj}_${tag}_linux.txt" 2>&1
  local rc=$?
  if [ $rc -ne 0 ]; then
    echo "${proj}/${tag}: RUN EXIT=$rc"
    status=1
    return
  fi
  if diff -q "tmp/parity/${proj}_${tag}_win.txt" "tmp/parity/${proj}_${tag}_linux.txt" > /dev/null 2>&1; then
    echo "${proj}/${tag}: IDENTICAL ($(wc -l < tmp/parity/${proj}_${tag}_linux.txt) lines)"
  else
    echo "${proj}/${tag}: DIFFERS:"
    diff "tmp/parity/${proj}_${tag}_win.txt" "tmp/parity/${proj}_${tag}_linux.txt" | head -10
    status=1
  fi
}
for tag in net48 nc21 nc31 net5 net6 net7; do
  run_one icallclassic "$tag" "tmp/icallclassic"
done
for tag in net8 net9 net10; do
  run_one icallcase "$tag" "tmp/icallcase"
done
exit $status

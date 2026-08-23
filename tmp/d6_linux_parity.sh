#!/bin/bash
cd /mnt/e/il2cpp || exit 1
status=0
for tag in net48 nc21 nc31 net5 net6 net7 net8 net9 net10; do
  dir="tmp/icallwide"
  if [ ! -f "$dir/build_linux_$tag/il2cpp_gen_exe" ] && [ ! -f "$dir/build_linux_$tag/Makefile" ]; then
    cmake -S "$dir/gen_$tag" -B "$dir/build_linux_$tag" -G "Unix Makefiles" -DCMAKE_BUILD_TYPE=Release > /dev/null 2>&1
  fi
  cmake --build "$dir/build_linux_$tag" -j4 > "/tmp/bldw_$tag.log" 2>&1
  if [ $? -ne 0 ]; then
    echo "LINUX BUILD FAIL $tag:"; tail -5 "/tmp/bldw_$tag.log"; status=1; continue
  fi
  "$dir/build_linux_$tag/il2cpp_gen_exe" > "tmp/parity/icallwide_${tag}_linux.txt" 2>&1
  rc=$?
  # normalize: strip CR from windows-side file for comparison
  tr -d '\r' < "tmp/parity/icallwide_${tag}_win.txt" > /tmp/win_norm.txt
  if diff -q /tmp/win_norm.txt "tmp/parity/icallwide_${tag}_linux.txt" > /dev/null 2>&1; then
    echo "icallwide/$tag LINUX: EXIT=$rc IDENTICAL-to-WIN ($(wc -l < tmp/parity/icallwide_${tag}_linux.txt) lines)"
  else
    echo "icallwide/$tag LINUX: EXIT=$rc DIFFERS:"
    diff /tmp/win_norm.txt "tmp/parity/icallwide_${tag}_linux.txt" | head -8
    status=1
  fi
done
exit $status

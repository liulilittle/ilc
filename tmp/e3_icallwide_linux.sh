#!/bin/bash
# E3: icallwide Linux -- DELETE stale build dirs, fresh configure+build+run, parity vs win.
cd /mnt/e/il2cpp || exit 1
status=0
for tag in net48 nc21 nc31 net5 net6 net7 net8 net9 net10; do
  rm -rf "tmp/icallwide/build_linux_$tag"
  cmake -S "tmp/icallwide/gen_$tag" -B "tmp/icallwide/build_linux_$tag" -G "Unix Makefiles" -DCMAKE_BUILD_TYPE=Release > /dev/null 2>&1
  cmake --build "tmp/icallwide/build_linux_$tag" -j$(nproc) > "/tmp/e3_$tag.log" 2>&1
  if [ $? -ne 0 ]; then echo "E3 BUILD FAIL $tag:"; grep -i error "/tmp/e3_$tag.log" | head -4; status=1; continue; fi
  "tmp/icallwide/build_linux_$tag/il2cpp_gen_exe" > "tmp/parity/icallwide_${tag}_linux.txt" 2>&1
  rc=$?
  tr -d '\r' < "tmp/parity/icallwide_${tag}_win.txt" > /tmp/w.txt
  tr -d '\r' < "tmp/parity/icallwide_${tag}_linux.txt" > /tmp/l.txt
  if diff -q /tmp/w.txt /tmp/l.txt > /dev/null 2>&1; then
    echo "icallwide/$tag: EXIT=$rc IDENTICAL ($(wc -l < /tmp/l.txt) lines)"
  else
    echo "icallwide/$tag: EXIT=$rc DIFFERS:"
    diff /tmp/w.txt /tmp/l.txt | head -6
    status=1
  fi
done
exit $status

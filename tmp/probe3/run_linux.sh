#!/bin/bash
# probe3 Linux: fresh configure+build+run per version, parity vs Windows output.
cd /mnt/e/il2cpp || exit 1
status=0
for tag in net48 nc21 nc31 net5 net6 net7 net8 net9 net10; do
  gen="tmp/probe3/gen_$tag"
  [ -d "$gen" ] || { echo "probe3/$tag NO GEN"; status=1; continue; }
  rm -rf "tmp/probe3/build_linux_$tag"
  cmake -S "$gen" -B "tmp/probe3/build_linux_$tag" -G "Unix Makefiles" -DCMAKE_BUILD_TYPE=Release > /dev/null 2>&1
  cmake --build "tmp/probe3/build_linux_$tag" -j$(nproc) > "/tmp/p3_$tag.log" 2>&1
  if [ $? -ne 0 ]; then echo "probe3/$tag BUILD FAIL:"; grep -i error "/tmp/p3_$tag.log" | head -4; status=1; continue; fi
  "tmp/probe3/build_linux_$tag/il2cpp_gen_exe" > "tmp/parity/probe3_${tag}_linux.txt" 2>&1
  rc=$?
  last=$(tail -1 "tmp/parity/probe3_${tag}_linux.txt")
  echo "probe3/$tag: EXIT=$rc LAST=[$last]"
done
exit $status

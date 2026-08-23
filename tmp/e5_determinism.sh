#!/bin/bash
# E5 determinism: run each exe 3x, all outputs must be identical.
cd /mnt/e/il2cpp || exit 1
status=0
check() {
  local exe="$1" name="$2"
  [ -x "$exe" ] || { echo "$name MISSING"; status=1; return; }
  "$exe" > /tmp/d1.txt 2>&1
  for i in 2 3; do
    "$exe" > "/tmp/d$i.txt" 2>&1
    if ! diff -q /tmp/d1.txt "/tmp/d$i.txt" > /dev/null 2>&1; then
      echo "$name: RUN $i DIFFERS"; diff /tmp/d1.txt "/tmp/d$i.txt" | head -4; status=1
    fi
  done
  echo "$name: 3x DETERMINISTIC"
}
for tag in net48 net8 net10; do
  check "tmp/icallwide/build_linux_$tag/il2cpp_gen_exe" "icallwide/$tag"
  check "tmp/probe3/build_linux_$tag/il2cpp_gen_exe" "probe3/$tag"
done
exit $status

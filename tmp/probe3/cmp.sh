#!/bin/bash
# probe3 parity: Windows vs Linux outputs (normalized CRLF).
cd /mnt/e/il2cpp || exit 1
status=0
for tag in net48 nc21 nc31 net5 net6 net7 net8 net9 net10; do
  tr -d '\r' < "tmp/parity/probe3_${tag}_win.txt" > /tmp/p3w.txt
  tr -d '\r' < "tmp/parity/probe3_${tag}_linux.txt" > /tmp/p3l.txt
  if diff -q /tmp/p3w.txt /tmp/p3l.txt > /dev/null 2>&1; then
    echo "probe3/$tag: IDENTICAL ($(wc -l < /tmp/p3l.txt) lines)"
  else
    echo "probe3/$tag: DIFFERS:"; diff /tmp/p3w.txt /tmp/p3l.txt | head -6; status=1
  fi
done
exit $status

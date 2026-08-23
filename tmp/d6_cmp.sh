#!/bin/bash
cd /mnt/e/il2cpp || exit 1
for t in net48 nc21 nc31 net5 net6 net7 net8 net9 net10; do
  tr -d '\r' < "tmp/parity/icallwide_${t}_win.txt" > /tmp/w.txt
  tr -d '\r' < "tmp/parity/icallwide_${t}_linux.txt" > /tmp/l.txt
  if diff -q /tmp/w.txt /tmp/l.txt > /dev/null 2>&1; then
    echo "icallwide/${t}: IDENTICAL"
  else
    echo "icallwide/${t}: DIFFERS"
    diff /tmp/w.txt /tmp/l.txt | head -6
  fi
done

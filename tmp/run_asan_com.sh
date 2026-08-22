#!/bin/bash
cd /mnt/e/il2cpp/build/linux_asan || exit 1
ASAN_OPTIONS=detect_leaks=0 ./test_com > /tmp/asan_com.txt 2>&1
echo "EXIT=$?"
grep "FAIL" /tmp/asan_com.txt

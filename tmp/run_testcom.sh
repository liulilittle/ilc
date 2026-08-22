#!/bin/bash
cd /mnt/e/il2cpp/build/linux_tests || exit 1
stdbuf -o0 -e0 ./test_com > /tmp/test_com_out.txt 2>&1
echo "EXIT=$?"
tail -8 /tmp/test_com_out.txt

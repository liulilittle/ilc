#!/bin/bash
cd /mnt/e/il2cpp/build/linux_tests || exit 1
./test_cultureinfo 2>&1 | tail -5
echo "EXIT=$?"

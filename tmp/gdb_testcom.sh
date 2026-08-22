#!/bin/bash
cd /mnt/e/il2cpp/build/linux_tests || exit 1
gdb -batch -ex run -ex "x/12i \$pc-40" -ex "info registers rax rbx rcx rdx rdi" ./test_com 2>&1 | tail -22

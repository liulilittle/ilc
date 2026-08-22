import io
path = r'tests\runtime\test_com.cpp'
lines = io.open(path, encoding='utf-8').read().split('\n')
out = []
i = 0
while i < len(lines):
    l = lines[i]
    if '[s24]' in l and 'std::fprintf' in l:
        # skip this fprintf line
        # if next line is a continuation (args) or fflush, skip those too
        i += 1
        # check continuation lines of multi-line fprintf
        while i < len(lines):
            nxt = lines[i]
            if 'std::fflush(stderr);' in nxt or (nxt.strip() and not nxt.startswith(' ') and 'scaleRet24' not in nxt and 'Expect' not in nxt and 'uint8_t' not in nxt and 'Memcpy' not in nxt and 'MakeVariant' not in nxt and 'std::memcpy' not in nxt and 'TestDispParams' not in nxt and 'const int32_t' not in nxt and 'wchar_t' not in nxt and 'void*' not in nxt and 'int32_t' not in nxt and 'uint16_t' not in nxt and 'double' not in nxt and 'uint8_t' not in nxt):
                # continuation line of the fprintf (heuristic)
                i += 1
            else:
                break
        continue
    out.append(l)
    i += 1
io.open(path, 'w', encoding='utf-8', newline='').write('\n'.join(out))
print('filtered')

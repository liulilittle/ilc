import io
path = r'tests\runtime\test_com.cpp'
s = io.open(path, encoding='utf-8').read()
df = {
    'std::fprintf(stderr, "[s23done] section 23 complete\\n");\n': '',
    'std::fprintf(stderr, "[s24] after object_new\\n"); std::fflush(stderr);\n': '',
    'std::fprintf(stderr, "[s24] after ctor\\n"); std::fflush(stderr);\n': '',
    'std::fprintf(stderr, "[s24] after ccw_create=%lld\\n", static_cast<long long>(autoCcw));\n        std::fflush(stderr);\n': '',
    'std::fprintf(stderr, "[s24] after register_invoke_dispatch\\n");\n        std::fflush(stderr);\n': '',
    'std::fprintf(stderr, "[s24] getIds24=%p\\n", reinterpret_cast<void*>(getIds24));\n        std::fflush(stderr);\n': '',
    'std::fprintf(stderr, "[s24] invoke24=%p\\n", reinterpret_cast<void*>(invoke24));\n        std::fflush(stderr);\n': '',
    'std::fprintf(stderr, "[s24] calling getIds24 scale\\n");\n        std::fflush(stderr);\n': '',
    'std::fprintf(stderr, "[s24] after getids expect scaleDisp=%d rateDisp=%d\\n", scaleDisp, rateDisp);\n        std::fflush(stderr);\n': '',
    'std::fprintf(stderr, "[s24] calling Scale invoke\\n");\n        std::fflush(stderr);\n': '',
    '        std::fprintf(stderr, "[s24] scale invoke ret=%d count=%d factor=%d label=%p\\n",\n                     scaleRet24, autoView->ScaleCount, autoView->LastFactorInt,\n                     reinterpret_cast<void*>(autoView->LastLabel));\n        std::fflush(stderr);\n': '',
    'std::fprintf(stderr, "[s24] after scale expect\\n");\n        std::fflush(stderr);\n': '',
    'std::fprintf(stderr, "[s24pre] entering section 24\\n");\n': '',
}
for k, v in df.items():
    s = s.replace(k, v)
io.open(path, 'w', encoding='utf-8', newline='').write(s)
print('cleanup done, scaleRet24 refs:', s.count('scaleRet24'))

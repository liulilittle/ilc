# 对等抽查审计：numberformat.h 声明 vs numberformat.cpp 定义

**审计时间:** 2026-09-06
**审计人:** 子代理 (只读审计，未修改任何文件)

---

## 一、统计

| 项目 | 数量 |
|------|------|
| `numberformat.h` 声明数 | **14** |
| `numberformat.cpp` 定义数 | **14** |
| 匹配数 | **14** |
| 不匹配数 | **0** |
| 覆盖率 | **100%** |

---

## 二、逐对对照

| # | 声明 (numberformat.h) | 定义 (numberformat.cpp) | 状态 |
|---|---|---|---|
| 1 | `Il2CppObject* il2cpp_number_format_general(int64_t value)` | `Il2CppObject* il2cpp_number_format_general(int64_t value)` | ✓ |
| 2 | `Il2CppObject* il2cpp_number_format_general_double(double value)` | `Il2CppObject* il2cpp_number_format_general_double(double value)` | ✓ |
| 3 | `Il2CppObject* il2cpp_number_format_fixed(int64_t value, int32_t precision)` | `Il2CppObject* il2cpp_number_format_fixed(int64_t value, int32_t precision)` | ✓ |
| 4 | `Il2CppObject* il2cpp_number_format_fixed_double(double value, int32_t precision)` | `Il2CppObject* il2cpp_number_format_fixed_double(double value, int32_t precision)` | ✓ |
| 5 | `Il2CppObject* il2cpp_number_format_ffixed(int64_t value, int32_t precision)` | `Il2CppObject* il2cpp_number_format_ffixed(int64_t value, int32_t precision)` | ✓ |
| 6 | `Il2CppObject* il2cpp_number_format_ffixed_double(double value, int32_t precision)` | `Il2CppObject* il2cpp_number_format_ffixed_double(double value, int32_t precision)` | ✓ |
| 7 | `Il2CppObject* il2cpp_number_format_hex(int64_t value, int32_t precision)` | `Il2CppObject* il2cpp_number_format_hex(int64_t value, int32_t precision)` | ✓ |
| 8 | `Il2CppObject* il2cpp_number_format_scientific(int64_t value, int32_t precision)` | `Il2CppObject* il2cpp_number_format_scientific(int64_t value, int32_t precision)` | ✓ |
| 9 | `Il2CppObject* il2cpp_number_format_scientific_double(double value, int32_t precision)` | `Il2CppObject* il2cpp_number_format_scientific_double(double value, int32_t precision)` | ✓ |
| 10 | `Il2CppObject* il2cpp_number_format_percent(double value, int32_t precision)` | `Il2CppObject* il2cpp_number_format_percent(double value, int32_t precision)` | ✓ |
| 11 | `Il2CppObject* il2cpp_number_format_percent_int(int64_t value, int32_t precision)` | `Il2CppObject* il2cpp_number_format_percent_int(int64_t value, int32_t precision)` | ✓ |
| 12 | `Il2CppObject* il2cpp_number_format_custom(int64_t value, Il2CppObject* format)` | `Il2CppObject* il2cpp_number_format_custom(int64_t value, Il2CppObject* format)` | ✓ |
| 13 | `Il2CppObject* il2cpp_number_format_custom_double(double value, Il2CppObject* format)` | `Il2CppObject* il2cpp_number_format_custom_double(double value, Il2CppObject* format)` | ✓ |
| 14 | `Il2CppObject* il2cpp_number_format_currency_symbol()` | `Il2CppObject* il2cpp_number_format_currency_symbol()` | ✓ |

---

## 三、结论

**声明-定义完全一致。** 14/14 函数签名匹配，无遗漏、无多余、无签名偏差。

`numberformat.h` 中每一个 `il2cpp_number_format_*` 函数都在 `numberformat.cpp` 中有精确对应的实现，且参数列表逐 token 对齐。

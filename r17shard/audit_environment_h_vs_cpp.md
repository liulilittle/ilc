# 对等抽查审计：il2cpp_environment.h 声明 vs environment.cpp 定义

**审计时间:** 2026-09-06
**审计人:** 子代理 (只读审计，未修改任何文件)

---

## 一、统计

| 项目 | 数量 |
|------|------|
| `il2cpp_environment.h` 声明数 | **18** |
| `environment.cpp` 定义数 | **18** |
| 匹配数 | **18** |
| 不匹配数 | **0** |
| 覆盖率 | **100%** |

---

## 二、逐对对照

| # | 声明 (il2cpp_environment.h) | 定义 (environment.cpp) | 状态 |
|---|---|---|---|
| 1 | `Il2CppObject* icall_environment_get_environment_variable(Il2CppObject* name)` | `Il2CppObject* icall_environment_get_environment_variable(Il2CppObject* name)` | ✓ |
| 2 | `int32_t icall_environment_set_environment_variable(Il2CppObject* name, Il2CppObject* value)` | `int32_t icall_environment_set_environment_variable(Il2CppObject* name, Il2CppObject* value)` | ✓ |
| 3 | `Il2CppObject* icall_environment_get_current_directory()` | `Il2CppObject* icall_environment_get_current_directory()` | ✓ |
| 4 | `int32_t icall_environment_set_current_directory(Il2CppObject* path)` | `int32_t icall_environment_set_current_directory(Il2CppObject* path)` | ✓ |
| 5 | `int32_t icall_environment_get_tick_count()` | `int32_t icall_environment_get_tick_count()` | ✓ |
| 6 | `int64_t icall_environment_get_tick_count64()` | `int64_t icall_environment_get_tick_count64()` | ✓ |
| 7 | `void icall_environment_fail_fast(Il2CppObject* message)` | `void icall_environment_fail_fast(Il2CppObject* message)` | ✓ |
| 8 | `int32_t icall_environment_get_current_managed_thread_id()` | `int32_t icall_environment_get_current_managed_thread_id()` | ✓ |
| 9 | `int32_t icall_environment_get_exit_code()` | `int32_t icall_environment_get_exit_code()` | ✓ |
| 10 | `int32_t icall_environment_set_exit_code(int32_t code)` | `int32_t icall_environment_set_exit_code(int32_t code)` | ✓ |
| 11 | `int32_t icall_environment_get_has_shutdown_started()` | `int32_t icall_environment_get_has_shutdown_started()` | ✓ |
| 12 | `int32_t icall_environment_get_processor_count()` | `int32_t icall_environment_get_processor_count()` | ✓ |
| 13 | `Il2CppObject* icall_environment_get_user_name()` | `Il2CppObject* icall_environment_get_user_name()` | ✓ |
| 14 | `Il2CppObject* icall_environment_get_machine_name()` | `Il2CppObject* icall_environment_get_machine_name()` | ✓ |
| 15 | `Il2CppObject* icall_environment_get_os_version()` | `Il2CppObject* icall_environment_get_os_version()` | ✓ |
| 16 | `Il2CppObject* icall_environment_get_version()` | `Il2CppObject* icall_environment_get_version()` | ✓ |
| 17 | `Il2CppObject* icall_environment_get_special_folder(int32_t folder_id)` | `Il2CppObject* icall_environment_get_special_folder(int32_t folder_id)` | ✓ |
| 18 | `Il2CppObject* icall_environment_get_command_line()` | `Il2CppObject* icall_environment_get_command_line()` | ✓ |

---

## 三、结论

**声明-定义完全一致。** 18/18 函数签名匹配，无遗漏、无多余、无签名偏差。

`il2cpp_environment.h` 中每一个 `icall_environment_*` 函数都在 `environment.cpp` 中有精确对应的实现，且参数列表逐 token 对齐。

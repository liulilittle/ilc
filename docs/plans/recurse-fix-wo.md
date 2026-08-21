# 施工单 批A-2 recurse（entry.cpp 生成器，CodeGenerator.cs 域）

日期：2026-08-06（批A 接线核查）
状态：**BLOCKED（CodeGenerator.cs 被并行代理独占）**

## 症状（本批实测，il2cppc.dll 7:45:55 直跑转译 EXIT=0）
- 转译已通过（非 transpile 失败）。
- 生成码 entry.cpp 无参调用带参 Main：
  ```
  // Cases_Recurse.cpp: IL2CPP_NOINLINE void Cases_Recurse_Main_9866ee88af8eb9db(Il2CppObject* arg0)
  // entry.cpp:      Cases_Recurse_Main_9866ee88af8eb9db();   <- no arg
  ```
- MinGW 构建错误（worklist #31 复现）：`no matching function for call to
  'Cases_Recurse_Main_9866ee88af8eb9db'`（声明 `(Il2CppObject* arg0)`，entry 无参调用）。

## 根因
CodeGenerator.cs 的 entry.cpp 发射（`il2cpp_entry_main` 内 `{MainCppName}();`）
不检查 Main 的形参表：Main(string[]) 生成 `(Il2CppObject* arg0)` 签名，
entry 调用点却不传实参。

## 修复动作（CodeGenerator.cs 域，等文件释放后执行）
- entry.cpp 发射点：Main 带参时发射 `{MainCppName}(nullptr);`。
- 约束：Main 只有 0 或 1 参（string[]）两种现实形态；1 参传 nullptr
  （本 runtime 无 argv 注入，worklist #31 原文即此方案）。
- 验证：`tests/cases/recurse` mingw 全链 e2e PASS（net48 oracle 逐字节）。

## 冲突证据
- CodeGenerator.cs 9:49:02 在途重构（git diff +123/-17，EmitBody 5 参错误
  使全树不可编译）——recurse 代理不能触碰。
- 本卡零 src 改动。仅本施工单。

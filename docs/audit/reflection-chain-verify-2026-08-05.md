# 反射链现状核查记录（typeinfo / invoker 域）

- 核查日期：2026-08-05 15:06
- 核查性质：只读验证（未修改任何 src 代码、未 git commit）
- 核查任务：反射链（typeinfo/invoker 域）——glob invoker.h/typeinfo.h；存在则
  cmake --build --target 对应测试 → 运行全绿；不存在报告"未建"（下一批）。

---

## 1. 核查结果摘要

| 域 | 状态 | 证据 |
|---|---|---|
| **invoker** | **未建（下一批）** | glob `**/invoker.*` → 无文件；src/runtime/src 无 invoker.cpp；include/internal 无 invoker.h |
| **typeinfo** | **已建 + 测试全绿** | typeinfo.h/cpp + test_typeinfo.cpp 存在；build exit 0；运行 exit 0 "all runtime typeinfo tests passed" |

## 2. invoker 域：未建（下一批）

- [BASH:glob **/invoker.*] → No files found。
- [BASH:Get-ChildItem src/runtime/src -Filter *.cpp] → 75 文件无 invoker.cpp；include/internal
  73 头无 invoker.h。
- [GREP:src invoker] → 唯一命中为 `typeinfo.h:52` 注释（"AOT static function address (or
  invoker wrapper)"）——仅为方法表的 fnPtr 字段注释，无任何 invoker 实现/声明/测试。
- 现有 "invoke" 相关文件均为 **P/Invoke**（pinvoke.cpp/pinvokeext.cpp/il2cpp_pinvoke*.h），
  与反射统一 Invoker 无关（区别于审计基线 reflection-audit.md §3 缺口 5"统一 Invoker"）。
- **结论：invoker 域未开始施工，按计划排下一批。**

## 3. typeinfo 域：已建 + 验证全绿

### 3.1 文件清单
| 文件 | 最近修改 | 说明 |
|---|---|---|
| src/runtime/include/internal/typeinfo.h | 2026-08-03 22:04 | M5.5 反射元数据表定义（Il2CppTypeInfo/MethodInfo/FieldInfo/PropertyInfo + 注册表 + 查询原语） |
| src/runtime/src/typeinfo.cpp | 2026-08-03 22:06 | 实现 |
| tests/runtime/test_typeinfo.cpp | 2026-08-04 08:38 | 单元测试（37 断言，无框架 plain exe） |
| tests/runtime/build_mingw/test_typeinfo.exe | 2026-08-05 06:55 | 预构建产物 |

### 3.2 构建验证
```
[BASH:cmake --build build_mingw --target test_typeinfo] → exit 0
  [ 96%] Built target il2cpp_runtime
  [100%] Linking CXX executable test_typeinfo.exe
  [100%] Built target test_typeinfo
```
源码（typeinfo.h 22:04 / typeinfo.cpp 22:06 / test 08:38）全部早于构建，无过期风险。

### 3.3 运行验证
```
[BASH:& ./build_mingw/test_typeinfo.exe] → "all runtime typeinfo tests passed"，EXIT=0
```
37 个断言全部通过（g_failures==0，任一失败即 FAIL + 非零退出）。

### 3.4 覆盖范围（test_typeinfo.cpp 头注释 + 断言分布）
| 组 | 断言数 | 覆盖 |
|---|---|---|
| 注册表 | 7 | register→lookup 全名解析、未注册名→null、null 名→null、重复注册幂等 |
| get_property | 9 | 自有命中、基类链继承、indexer、缺失/空表/null 入参→null |
| get_method | 10 | 继承命中、最派生遮蔽（.NET shadowing）、trimmed 条目（fnPtr null）仍按名查到、缺失→null |
| get_field | 7 | 自有/继承命中、静态字段（offset -1 + staticAddr）、缺失→null |
| 布局落地 | 4 | baseType 链、klass 回指、interfaces 表+count、name 一致 |
| **合计** | **37** | 全部 PASS |

### 3.5 反射链整体定位（衔接 reflection-audit.md 基线）
- 审计基线（2026-08-03）§3 缺口 #3「反射元数据表生成」→ **本批已落地**（typeinfo 域，
  Il2CppTypeInfo 静态表 + 注册表 + 简化查询原语）。
- 缺口 #5「统一 Invoker」→ **未建**（本核查结论）。
- 缺口 #6 MethodInfo/PropertyInfo/FieldInfo 对象模型 → 未建（后续）。
- typeinfo.h 注释明确了设计契约：直接调用代码从不走元数据表（性能）；查询防御式返回
  nullptr 不抛异常；属性 accessor 仅存名、复用方法表（单一权威，无重复 fnPtr）。

## 4. 证据索引
| 主题 | 证据 |
|---|---|
| invoker 无文件 | [BASH:glob **/invoker.*] → No files found |
| src/runtime 文件清单 | [BASH:Get-ChildItem src/runtime/src -Filter *.cpp → 75 文件无 invoker] |
| invoker 唯一文字命中 | [READ:src/runtime/include/internal/typeinfo.h:52 注释（非实现）] |
| typeinfo 源/测试存在 | [READ:typeinfo.h:1-136；READ:test_typeinfo.cpp:1-241] |
| 构建全绿 | [BASH:cmake --build build_mingw --target test_typeinfo] → Built target, exit 0 |
| 运行全绿 | [BASH:& ./build_mingw/test_typeinfo.exe] → "all runtime typeinfo tests passed", EXIT=0 |
| 断言数 | [BASH:Select-String Expect( → 37] |
| 缺口对照 | [READ:docs/audit/reflection-audit.md:72-84（缺口 3/5/6）] |

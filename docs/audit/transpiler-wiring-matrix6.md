# 转译器接线域报告 — 矩阵6 transpile 18 失败（2026-08-06）

分支 m1 HEAD=2cf02fa。可写域：MethodMap.*.cs / MethodRefResolver.cs / StackFold.cs / Expr.cs / MethodSignatureCoder.cs / MethodMapTests*.cs。
**已接线 2 项（transpile 修复 + e2e 确认），报告 16 项（只读根因，含 runtime 侧 + CodeGenerator/TypeEmitter 侧）。**
备份：.opencode/backups/transpiler-wiring-20260806-*。

## 已修复（接线落地 + 单测 + 验证）

| # | 用例 | 根因 | 修复 | e2e 重跑 |
|---|---|---|---|---|
| 3 | **iocase** | Path.Combine 3-arg params-array 形未接线：runtime icall 只收 (string,string) | MethodMap.IO.cs 3-arg → `NestedCombineMarker`；StackFold.cs 展开为嵌套 `icall_path_combine(combine(a,b),c)` CallExpr（work order #24 落地） | transpile ✅（临时用例 tmpcombine **PASS 逐字节**：`icall_path_combine(icall_path_combine(a,b),c)` 实证）→ 被 Directory.Delete(dir,true) 2-arg 潜在 bug 阻断（见下） |
| 6 | **delegatecb** | `Marshal.GetFunctionPointerForDelegate`（MethodSpec 泛型形）无 BCL 映射 | MethodMap.Marshal.cs `GetFunctionPointerForDelegate` → `icall_delegate_get_fn_ptr`（runtime 存在，il2cpp_delegate_ext.h:145，"ptr" ResultKind） | transpile ✅ → build FAIL（out-of-domain：void*→intptr_t + GC.WaitForPendingFinalizers） |

**单测**：MethodMapTests.IO.cs 3-arg 断言改为 marker；MethodMapTests.Marshal.cs 新增 MethodSpec 形测试。全量 unit **1093/1093 全绿**（基线 1092 + 新增 1）。build 0 警 0 错。

### iocase 附带发现（我的 Path.Combine 修复暴露的 pre-existing 潜在 bug）
- 原始 iocase 在 Path.Combine 死（transpile），从未触达 `Directory.Delete(dir, true)`（2-arg recursive）。修复后暴露：MethodMap.IO.cs `Delete` 无视 arity，把 2-arg 映射到 1-param 非递归 icall → 弹错参数（`icall_directory_delete(1LL)` 把 true 当 path，C++ build error）。
- 修复：`Directory.Delete` 按 count 分派——1-arg 照旧映射；2-arg LOUD 拒绝（wiring manual：不静默错值）。**runtime 侧缺口**：icall_directory_delete 非递归，删非空树需要 recursive icall（runtime 代理域）。

## 报告 16 项（只读根因）

| # | 用例 | 根因一句话 | 根因位置（只读） |
|---|---|---|---|
| 1 | **arrayext2** | `Array.FindIndex` runtime icall `icall_array_find_index_int32(arr,startIndex,count,value)` 收 VALUE，.NET 只收 `Predicate<T>` 委托——签名不可对接，且测试用 lambda（x=>x==20） | MethodMap.Array.cs:111 LOUD；runtime il2cpp_array_ext2.h:124 签名与 .NET API 不匹配 |
| 2 | **collchain** | `ArrayList.Remove(object)` 无 runtime icall——arraylist.h 只有 remove_at(index)；.NET 语义是删首个引用相等元素（需要 index_of+remove_at 复合或 runtime 新 icall） | MethodMap.ArrayList.cs:58 LOUD；arraylist.h:93 只有 remove_at |
| 4 | **excext** | `Exception.get_Message`：M3 异常载体 DotNetException 是 C++ type-tag+message，非托管 Exception 对象；catch 变量只绑定类型名字符串，message 不可达 | MethodMap.Exception.cs:26 LOUD；ExceptionEntryExpr 模型（StackFold.cs:154 + CodeGenerator catch 发射）无 message 数据 |
| 5 | **hashtable2/hashtablecase** | `Console.WriteLine(object)`（`t["one"]` boxed 值）无 tracked box 类型——runtime 无对象打印机 icall（il2cpp_console.h 只有 typed printer + write_line_s 强转 string），box 类型不可静态跟踪 | MethodMap.Console.cs:75 LOUD；runtime il2cpp_console.h 无 `icall_console_write_line_object` |
| 7 | **dictcase/linqcase** | 泛型实参 byte 0x0E(string)/0x02(bool) 无展开——GenericInstantiationName 只映射 0x08/0x13；且 Dictionary`2/LINQ 域整体未接线（MethodMap.Dictionary.cs 空骨架，runtime dictsi.h 是 plain struct 非 managed object） | StackFold.cs:2298 / MethodRefResolver.cs:747 / CodeGenerator.cs:2417（三处同款 byte 表，CodeGenerator 只读）；dictcase.cs GAP 记录 |
| 8 | **genlist** | `GenList.Id<string>`：模型只生成 T→int32 默认实例化，string 实参无 concrete-type expansion | MethodRefResolver.cs:361-375 EnsureInt32DefaultInstantiation LOUD；展开需 TypeEmitter/CodeGenerator 具体类型生成 |
| 9 | **encodingcase** | `Encoding.ASCII.GetChars(byte[])→char[]` 分配形无 runtime helper——il2cpp_encoding.h 只发 utf8_get_chars；ascii/unicode 页缺 | MethodMap.Encoding.cs:98-102 LOUD；runtime il2cpp_encoding.h:131 仅 `il2cpp_encoding_utf8_get_chars` |
| 10a | **webcase** | `WebClient.DownloadString(string)` 需单调用 runtime wrapper（http.h 引擎存在但返回 multi-field struct 需 C string） | MethodMap.Net.cs:173 LOUD；runtime http.h 需 `icall_web_client_download_string` wrapper（net-domain.md §4.4） |
| 10b | **streamcase** | `FileStream` ctor 未接线：il2cpp_stream_file_open 是 handle FACTORY 非对象型 ctor icall | MethodMap.StreamIO.cs LOUD（MapStreamCtor）；runtime 需对象化 icall + stream class 描述符 + BclAllocExpr（CodeGenerator） |
| 10c | **strcomp2** | `newarr` 元素类型 System.Char 不受 ArrayElementType 支持（CodeGenerator.cs:3180，String.Split char[] 场景） | CodeGenerator.cs:3141-3155 ArrayElementType 只收 Int32/Byte/String/Object |
| 10d | **refinvoke** | transpile 通过；build 失败——generated.h include 表缺 `typeinfo.h`（`il2cpp_typeinfo_get_method` undeclared） | CodeGenerator.cs generated.h 发射表（只读）缺 `#include "typeinfo.h"` |
| 10e | **reflectcase** | transpile 通过；build 失败——generated.h include 表缺 `reflect_base.h`（`il2cpp_type_get_type` undeclared） | CodeGenerator.cs generated.h 发射表（只读）缺 `#include "reflect_base.h"` |
| 10f | **numchain** | transpile 通过；build 失败——①generated.h 缺 `il2cpp_parse.h`（`icall_try_parse_double` undeclared）②loc12-14 未声明（TryParse out 形地址未计入局部声明） | ①CodeGenerator.cs include 表 ②CodeGenerator.cs:563-578 局部声明扫描（LocAddrExpr 嵌套在表达式内未发现） |
| 10g | **mathdeep** | transpile+build 通过；diff 失败——19 处 NaN/Inf 边界 expected False actual True（b_sinInf/b_logNaN/b_sqrtNaN 等，runtime Math 域） | runtime math.cpp NaN/Inf 返回语义（非转译器） |
| - | (delegatecb 剩余) | ①`icall_delegate_get_fn_ptr` 返回 void*，但 emitter "ptr" ResultKind 期望 intptr_t（t12=intptr_t=void* 赋值 error）②`GC.WaitForPendingFinalizers` 无 runtime icall ③多播 delegate 被 runtime 拒绝（il2cpp_delegate_ext.h 文档化） | ①CodeGenerator EmitIcallCallArgs/声明 ②runtime il2cpp_gc.h ③runtime 已文档化 |

## 结论
- **转绿 0 例完整 e2e**（iocase/delegatecb transpile 均修复，但被 out-of-domain 阻断：iocase 需 runtime recursive delete；delegatecb 需 emitter intptr_t cast + GC icall + 多播）。
- **transpile 修复 2 例**：iocase（Path.Combine 3-arg nested fold，work order #24 落地）+ delegatecb（GetFunctionPointerForDelegate 接线）。
- **单测 1093/1093 全绿**（+1），build 0 警 0 错，无新回归（pathext/iofmt/strpath/filechain 复跑 PASS）。
- 16 项报告均为 runtime 域 / CodeGenerator / TypeEmitter 只读根因，含 1 个新暴露潜在 bug（Directory.Delete 2-arg 递归）已 LOUD 化。

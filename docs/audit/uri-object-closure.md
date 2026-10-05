# Uri 对象化闭环报告（M6 G6 施工单，2026-08-05）

## 1. 方案

与 Version 同模式（version-object-closure.md），对象布局 + GLOBAL class 描述符 +
对象 icall 复用纯函数原语：

```
struct Il2CppUriObject {           // versionuri.h, namespace il2cpp
    Il2CppObject obj;              // 头（klass 指针，offset 0）
    Il2CppString* scheme;          // slot 1（offset 8）-- GC 引用
    Il2CppString* host;            // slot 2（offset 16）-- GC 引用
    Il2CppString* path;            // slot 3（offset 24）-- GC 引用；"" when none
};
```

- `System_Uri_class`：**GLOBAL scope** 描述符（versionuri.cpp:69-79）——instanceSize =
  sizeof(Il2CppUriObject)，**refFieldBitmap = 0b01110**（bits 1..3 = scheme/host/path），
  refFieldCount 3。三个字符串槽是 GC 根：压紧收集器按位图扫描并在 Gen0
  compaction 时重写槽位（与 stringbuilder buffer 的字符串引用同一契约，测试 17
  证伪了"槽位陈旧指针"风险）。
- `il2cpp_uri_class()`：namespace il2cpp accessor，lazy register。
- 布局 pin：三个 static_assert（scheme/host/path 必须坐在 bit 1/2/3 对应偏移）。
- icall 全在 `il2cpp::runtime`：ctor（parse + 存三槽）· get_scheme/host/path ·
  to_string；复用纯函数原语 il2cpp_uri_try_create / il2cpp_uri_t/ getters /
  il2cpp_uri_to_string（缓冲层保持原样）。
- 语义：保留原样（verbatim）——parse 保字、ToString 往返；query/fragment 不切分
  （"/p?a=1" 留在 path）；port 留在 host；无大小写归一化（versionuri.h 文档化简化）。
- 异常（net48 基线）：null 输入→ArgumentNullException；畸形串（无 "://"、空 scheme、
  非法 scheme 字符、空 host、非 ASCII）→ArgumentException；null receiver→
  NullReferenceException。
- GC 安全性：il2cpp_object_new 零初始化 + 位图扫描 → 未 ctor 实例读 null 槽；
  ctor 内三次分配各自可触发收集，部分初始化对象对收集器始终安全。

**转译器**：MethodMap.Uri.cs 映射恢复（.ctor(string)→icall_uri_ctor、
get_Scheme/get_Host/get_AbsolutePath→icall_uri_get_*、ToString→icall_uri_to_string）；
MethodMap.Object.cs 1-line（callvirt Object::ToString on Uri receiver →
icall_uri_to_string，Roslyn 形态）；CodeGenerator.cs generated.h 已含
`#include "versionuri.h"`。newobj fallback 直接发射
`il2cpp_object_new(&System_Uri_class)`（BclAllocExpr 条目非必需，未加——见 §4）。

## 2. 测试

| 层 | 结果 |
|---|---|
| dotnet build src/il2cppc | 0 err 0 warn |
| unit VersionUri filter | **22/22 PASS**（Uri 段：ctor/get_Scheme/get_Host/get_AbsolutePath/ToString 映射断言） |
| tests/runtime/test_versionobj.cpp（含 Uri 11-17 组） | **ALL PASS**（描述符身份/布局位图/往返/错误路径/null receiver/GC 生存+槽位重写） |
| tests/runtime/test_versionuri.exe（纯函数层回归） | **ALL PASS** |
| **uricase e2e**（run-tests.ps1 mingw） | **PASS 1/1**（net48 oracle 11 行逐字节一致） |

## 3. uricase 状态：PASS（补跑完成）

run-tests.ps1 全链路：net48 编译+参考运行 → 转译 rc=0 → cmake mingw 构建 → 运行 →
expected.txt 11/11 逐字节一致。生成代码验证：[READ:Cases_UriCase.cpp:43-94] —
newobj 全部发射 `il2cpp_object_new(&System_Uri_class)` + `icall_uri_ctor`，属性读取
`icall_uri_get_scheme/host/path`，`icall_uri_to_string` 正确。

## 4. 遗留/记录

- BclAllocExpr "System.Uri" 一致性条目：仍可后补（1 行，纯便利；功能上非必需——
  newobj fallback 已证编译+链接+运行正确）。
- 本任务未触碰跨域文件；工作区其余 M 文件均为并行代理在途改动（git status 复核过）。
- 全量矩阵（matrix4）重跑待并行代理收工后确认 versioncase/uricase 双双进阶 PASS。

## 5. 会话过程备注（验证为主，无实现改动）

接手时 Uri runtime（versionuri.cpp 3:42 / versionuri.h 3:36）与 MethodMap.Uri.cs
（3:45）已由并行代理实现但**未闭环验证**：build_mingw 无任何二进制（cmake 配置过、
构建从未跑完），test_versionobj.exe 是 8:43 重建产物。本会话补跑全部验证链 +
uricase e2e + 本报告。任务说明中的"方案"与现存实现逐点相符（Il2CppUriObject
字符串槽 + refFieldBitmap 0b01110 + System_Uri_class + icall_uri_ctor/
get_scheme/host/path/to_string + MethodMap 恢复映射），故无代码改动，只有验证。

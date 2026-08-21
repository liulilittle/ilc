# Version 对象化闭环报告（M6 G6 施工单，2026-08-05）

## 1. 方案

**对象布局**（runtime 侧为主，参照 Hashtable/ArrayList 对象+字段模式）：

```
struct Il2CppVersionObject {           // versionuri.h, namespace il2cpp
    Il2CppObject obj;                  // 头（klass 指针，offset 0）
    int32_t componentCount;            // 1..4（缺省 ctor 为 2）；offset 8
    int32_t components[4];             // major/minor/build/revision；offset 12
};
```

- `System_Version_class`：**GLOBAL scope** 描述符（versionuri.cpp 定义、versionuri.h extern）——
  与 System_String_class 同契约。generated 代码的 newobj fallback（`il2cpp_object_new(&System_Version_class)`）
  和 isinst/castclass（`&System_Version_class`）引用同一符号 → **BclAllocExpr 条目并非必需**。
- `il2cpp_version_class()`：namespace il2cpp 的 accessor，lazy register。
- icall 全部在 `il2cpp::runtime`，复用既有纯函数（parse/to_string/compare）为原语。
- 语义：count 是 ToString 往返唯一真相源；缺省组件存 -1，与 CompareTo missing-trailing=-1
  规则和 net48 getter 语义（Version(2,0).Build==-1）天然一致。

**icall 面**：ctor(string/default/2/3/4) · get_major/minor/build/revision · to_string ·
compare · equals · op_equality/op_inequality · parse（静态，自分配对象）。

**异常**（net48 基线）：null 输入→ArgumentNullException；畸形串→ArgumentException；
负组件→ArgumentOutOfRangeException；null receiver→NullReferenceException；
CompareTo(null)→1；CompareTo(非 Version)→ArgumentException。

**转译器**：MethodMap.Version.cs 全 LOUD → 恢复映射；MethodMap.Object.cs 1-line
（Object::ToString on Version receiver → icall_version_to_string——Roslyn 对 `v.ToString()`
发 callvirt Object::ToString，路由在 receiverType 分支）。

## 2. 测试

| 层 | 结果 |
|---|---|
| dotnet build src/il2cppc | 0 err |
| unit VersionUri filter | **22/22 PASS**（Version 段 throw→mapping 断言，Uri 段保持 LOUD） |
| unit 全量 | 705/707（2 个为并行代理预存在失败，git stash 验证非本任务；GuidSlotWiringTests 已被其代理自行修复） |
| tests/runtime/test_versionobj.cpp（新建） | **ALL PASS**（ctor/getters/to_string 往返/compare/equals/op/parse/错误路径/描述符身份/布局 static_assert，10 组覆盖） |
| runtime 全量 | **91/91 exes PASS** |

## 3. versioncase 状态：**PASS（从 transpile 失败 → 全绿）**

transpile rc=0 → 生成代码编译 0 err → run exit 0 → expected.txt **25/25 PASS** → net48 live diff **PASS**。
生成代码验证：[READ:gen/src/Cases_VersionCase.cpp:60-89] — `icall_version_ctor`/`ctor_2`/`ctor_3`/
`ctor_4`/`ctor_default`/getters/compare/equals/op_* 全部正确发射。

## 4. BclAllocExpr 两条：**未完成（冲突，按指令留给下批）**

- CodeGenerator.cs 被 Guid 值槽代理独占：任务开始时无 diff，期间持续被写入（结束时
  git M + mtime 02:41:04）→ 未触碰，指令判定成立。
- **Version 条目无功能缺口**：newobj fallback `il2cpp_object_new(&System_Version_class)`
  已证编译+链接+运行正确（global 符号已由本任务提供）。条目仅一致性便利，可后补。
- **Uri 条目不得提前加**：`il2cpp_object_new(&System_Uri_class)` 在 System_Uri_class
  定义前会链接失败——必须等 Uri runtime 对象化闭环（System_Uri_class + icall_uri_ctor/
  get_scheme/host/path/to_string + MethodMap.Uri.cs 恢复映射）之后。

## 5. 遗留

- Uri 对象化闭环（独立施工单）
- BclAllocExpr "System.Version" 一致性条目（CodeGenerator.cs 可用后 1 行）
- 2 个并行代理预存在单元失败（identity/GenericsSignature）
- 全量矩阵重跑（matrix4）确认 versioncase 进阶 PASS

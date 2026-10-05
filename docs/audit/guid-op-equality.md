# Guid op_Equality 分类修复 + guidcase 转译前进（2026-08-05 01:2x）

施工单 R2（e2e-recheck7）：Guid::op_Equality 被 M1 fallback 启发式
（MethodRefResolver.cs:314-316）误分类 INSTANCE → StackFold 在 guidcase
IL_00cd（`p == g1`）弹 this 栈空崩溃。

## 修复（独占 src/il2cppc/Emit/MethodMap.Guid.cs）

TryMapGuid 新增两个 case（照 String 域 op_Equality 先例
MethodMap.String.cs:73-92，值类型参数照本域 Equals 先例——"" 直通）：

| 成员 | CppName | 签名门 | 形态 |
|------|---------|--------|------|
| op_Equality | `il2cpp::il2cpp_guid_equals` | ps.Length==2 && ps[0]==ps[1]==0x11 | static, 2 参, casts `{"",""}` |
| op_Inequality | `!il2cpp::il2cpp_guid_equals` | 同上 | static, 2 参, C++ 逻辑非前缀（call 绑定紧于一元 !） |

- 签名 blob：`0x00, 0x02, 0x02, 0x11, 0x05, 0x11, 0x05`（bool ret，双 valuetype
  参数各带 TypeRef coded index 0x05）。
- 不匹配签名 → return null（本域惯例：TryParse/Equals/CompareTo 同）。
- guid.h 面 `bool il2cpp_guid_equals(const uint8_t a[16], const uint8_t b[16])`
  ——与 Equals(Guid) 同一 icall；op_Inequality 无独立 icall，照
  String.op_Inequality 的 `!` 前缀先例（该前缀安全论证同 String：
  il2cpp:: 分类门只查 CALLVIRT 接收者，op_* 恒为 static call）。

## 测试（tests/unit/MethodMapTests.Guid.cs +2）

- `Resolve_Guid_OpEquality_MapsToEqualsIcall`：CppName/IsInstance=false/
  ReturnsValue/ParamCount=2/ParamTypeNames `{"",""}` 全断言。
- `Resolve_Guid_OpInequality_MapsToNegatedEqualsIcall`：CppName 前缀 `!`。

## 验证证据

- [BASH:dotnet build src/il2cppc（Debug+Release）] → 0 错 0 警
- [BASH:dotnet test --filter MethodMapGuidTests] → 13/13 通过（11 旧 + 2 新）
- [BASH:dotnet test 全量] → 689/691；2 失败均为**他代理在途域**（stash 我的两文件后复跑仍失败）：
  MemberInfo get_Name（MethodMap.Type.cs/Object.cs 在途，d5a97f8 已文档化）、
  GenericsSignature callvirt get_Count（StackFold.cs 在途）。与 Guid 域无关。
- [BASH:转译 guidcase]（Release 重建 dll 直跑）：EXIT=2 但**卡点前进**——
  折叠已越过 IL_00cd `p == g1`（此前栈空崩溃点），崩溃移至 CodeGenerator
  发射期首个 constrained callvirt。

## guidcase 下一缺口（精确记录）

**当前前端阻塞：constrained + callvirt System.Object::ToString（值类型接收者）**
- guidcase IL 实证（ildasm）：`ldloca.s V_0; constrained. [mscorlib]System.Guid;
  callvirt instance string [mscorlib]System.Object::ToString()`（IL_0014/0022/00df，
  即 g1.ToString()/g2.ToString()/p.ToString()）。Roslyn 对值类型虚调发出
  **父为 System.Object** 的 MemberRef + constrained 前缀。
- IlDecoder.cs:216 已把 constrained 解码为 Nop（TypeToken 操作数保留），
  折叠层只看到普通 callvirt → 无 BCL 映射 → VirtualCallExpr →
  CodeGenerator.VirtualTarget（CodeGenerator.cs:1477）按 MemberRef 父解析
  owner = System.Object → "callvirt owner System.Object not in translated
  types" 抛 NotSupportedException。
- 同族已知缺口：MethodMap.Enum.cs:49-59（enum 值 ToString 的
  constrained/ldloca 未跟踪接收者）。修复方向：用 constrained 的 TypeToken
  操作数（或本地声明类型）填充 StackFold.cs:1234 重解析的 recvType →
  BclResolve → TryMapGuid("System.Guid","ToString",...) 直映射
  il2cpp_guid_to_string，绕开 vtable 发射。归属：StackFold/CodeGenerator 域。

**其后的值槽缺口（本任务原预判，仍在，但排在 callvirt 之后）**：
- CodeGenerator.cs:1308 ExprKind 的 ResultKind 白名单 `"l"|"d"|"dt"|"ts"`
  无 "guid" → NewGuid/Parse 结果落的本地 locKind 判 "val"（int32_t 声明），
  `p == g1`（IL_00ca-00cd：ldloc 值加载双操作数）发射的
  il2cpp_guid_equals(p, g1) 到 const uint8_t* 参数是 C++ 类型错误。
- 同族：Guid 无 TypeCppName / 无 temp 值槽类型（MethodMap.Guid.cs 头部
  SLOT-TYPE GAP 文档化）。归属：CodeGenerator 值槽/类型域。

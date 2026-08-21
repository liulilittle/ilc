# 施工单 批A-5 enumdeep（enum 里程碑剩余缺口，CodeGenerator.cs 域）

日期：2026-08-06（批A 接线核查）
状态：**BLOCKED（CodeGenerator.cs 被并行代理独占）**

## 本批实测（il2cppc.dll 7:45:55 直跑）
```
il2cppc error: call at offset 0x01CC: resolved System_Type_op_Equality_970a3bb1b564144f
   as INSTANCE (pops this) but the stack is empty -- likely a wrong static/instance
   classification @ StackFold.cs:1569
```
offset 0x01CC 远超 switch 区（169）——**switch 折叠已收工**（StackFold.cs:443
`case ILOpCode.Switch:` Ceq 比较链 + IlDecoder.cs:265 Switch 解码 + 标签 pass，
worklist §9.5 原阻点消除）。enumdeep 前推到 L129-130
`Enum.GetUnderlyingType(typeof(Color)) == typeof(int)` 的
**System.Type::op_Equality**（静态 2 参）被默认分类为 INSTANCE。

## 修复动作（MethodRefResolver Type 域 + CodeGenerator.cs 域）
1. **T**（MethodMap.Type.cs TryMapType）：加 `case "op_Equality"` 静态臂
   → `il2cpp::runtime::icall_type_equals`（MethodMap.Type.cs:78 已有
   instance 臂先例，icall 收 descriptor/name-string 双形）。静态 2 参：
   `I(..., 1, true, true, false, new[]{"",""})`（IsStatic=true）。
2. **CodeGenerator.cs**（被占）：enum ToString（constrained Object::ToString
   on enum receiver，KNOWN GAP L124-126）+ GetUnderlyingType 的 ldtoken/typeof
   enum 发射 + box enum 臂（CodeGenerator.cs:2089 `_ => throw`）——enum
   里程碑，MethodMap.Enum.cs REGISTER WIRING a-e 已文档化。
3. 修复 1 落地点后重跑：switch 三块（L87-106）应已全过，仅剩 L123-130
   的 KNOWN GAP 区。

## 冲突证据
- CodeGenerator.cs 9:49:02 在途重构（EmitBody 5 参错误，全树不可编译）。
- MethodMap.Type.cs 当前零改动（可等 CodeGenerator 释放后一并做）。
- 本卡零 src 改动。

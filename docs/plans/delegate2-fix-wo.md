# 施工单 批A-3 delegate2（Delegate::get_Method 接线，T+R 域）

日期：2026-08-06（批A 接线核查）
状态：**BLOCKED（CodeGenerator.cs 被并行代理独占 + runtime 缺 icall）**

## 症状（本批实测，il2cppc.dll 7:45:55 直跑）
```
il2cppc error: callvirt on non-instance method (token 167772179)
   at StackFold.cs:1440
```
token 167772179 = 0x0A000013 = **System.Delegate::get_Method**（metadata 探针实证）。

## 本批已推进
- **Ldsflda 已落地**（worklist #10 原阻点）：StackFold.cs:317 `case ILOpCode.Ldsflda`
  → `StaticFieldAddrExpr`（commit 81d25b6 + StackFoldTests 单测）。delegate2 的
  `"A:"+order` 5 处 Ldsflda 已不报错，转译前进到 get_Method。
- 现阻点 = `f.Method.Name`（delegate2.cs:34）：get_Method 无映射 → 回退生成名
  `System_Delegate_get_Method_*`（默认 IsInstance=false）→ callvirt 拒绝。

## 修复动作（三处，MethodMap.Delegate.cs + runtime + CodeGenerator.cs 域）
1. **runtime**（il2cpp_delegate_ext.h/cpp，delegateext 域）：新增
   `Il2CppObject* icall_delegate_get_method(Il2CppObject* d)`——返回方法名
   字符串（简化 MethodInfo 模型 = 名字字符串，MethodMap.Type.cs 先例）。
   数据源：委托 newobj 时生成码已知方法 CppName——发射 2-arg
   `il2cpp_delegate_new(&<method>, target)` 改 3-arg 带名字
   `il2cpp_delegate_new(&<method>, target, "<short name>")`？——或
   生成码在 delegate newobj 处发射注册
   `il2cpp_delegate_register_method(&<method>, "<name>")`（entry 集中注册，
   仿 class_register 形态；get_method 查 methodPtr→name 注册表；未注册回退
   LOUD）。**具体形态待 delegateext 域代理定**（worklist §9 delegateinst
   施工单同域同文件）。
2. **T**（MethodMap.Delegate.cs:25-26 旁）：`case "get_Method"` → 映射
   `il2cpp::runtime::icall_delegate_get_method`（instance，rv=true，
   returnTypeName "System.String"）——则 `f.Method` 得名字字符串，
   `.Name`（MemberInfo::get_Name）经 receiverType=="System.String"
   IdentityMarker 折叠闭合（MethodMap.CultureInfo.cs:27 先例）。
3. **CodeGenerator.cs**：若走注册表形态，需 entry/发射改动（被占）。

## 冲突证据
- CodeGenerator.cs 9:49:02 在途重构（EmitBody 5 参错误，全树不可编译）。
- delegateext 域 = delegateinst 施工单并行域（worklist §9），未收工。
- 本卡零 src 改动。

# GN-3 IMPLEMENT — 多维数组泛型实参转译(阶段①:转译端)

目标:使 `tests/cases/p-multidimarg` 探针的**转译**端从红(0x14 泛型实参 → M5 拒/Main stub)转绿(Main 全折叠、无 `TypeSpec 0x14 not GenericInst` 告警)。探针的 **byte-exact 运行**依赖 runtime 多维支持(阶段② commit)。

## 方案

多维数组(`int[,]`/`int[,,]`)在 M3 转译侧按 **引用槽(Il2CppArray\*)** 建模,泛型实参 token 采用 **element+rank 感知的四方一致契约**(与 SZARRAY `[<elem>]` 同构):

```
multidim 泛型实参 token = "[" + <elementSuffix> + <rank> + "]"
  int[,]  -> "[int2]"
  int[,,] -> "[int3]"
  Box<int[,]> -> 闭合克隆 Box_1__int2_
```

四方生产同一 token(调用端与 clone 合成端一致):StackFold.ReadM3MultidimArrayArg / CodeGenerator.ReadM3MultidimArrayArg / MethodRefResolver.ReadM3MultidimArg / MethodBodyReader.MultidimArrayArgSuffix(局部 kind 命名权威)。`GenericUsageAnalyzer.IsArrayArgToken`(`[`开头+`]`结尾)保持 per-instance 发射(不坍缩到 object)。

运行时契约(阶段②实现,本期转译器即发射):`il2cpp_array_new_multi_v<N>d(elemClass,elemSize,isRef,d0,...)`、`il2cpp_array_get/set_i4_<rank>d(Il2CppArray*, i, j, ...)`。

## 改动点(路径:行)

### src/il2cppc/Ir/StackFold.cs
- `ReadM3MultidimArrayArg`(StackFold.cs:4615 附近新增):0x14 泛型实参解码 → `[<elem><rank>]`;消费 element/rank/sizes/lobounds。
- `ReadM3Arg` 0x14 臂:分发到 ReadM3MultidimArrayArg。
- `GenericInstantiationName`(StackFold.cs:4591 附近):0x14 TypeSpec → ReadM3MultidimArrayArg(替代 throw "0x14 not GenericInst")。
- `GenericDefinitionNameOf`(StackFold.cs:4264 附近):0x14 TypeSpec → ReadM3MultidimArrayArg(delegate-Invoke owner 判定路径)。
- `TryDecodeMultidimCtor` / `MdElemSuffix` / `MultidimTypeName`(StackFold.cs:4569 附近新增):识别 `new int[2,2]` 的 multidim newobj ctor(MemberRef parent TypeSpec 头 0x14),取 element+rank。
- `EmitNewObj`(StackFold.cs:3588 附近):multidim ctor → 发射 `mdnew_<elem><rank>` CallExpr(dims 为参数,ReturnsRef),objTypes 记 `[<elem><rank>]` 供后续 Get/Set owner 路由。
- `FoldInitializeArray`(StackFold.cs:2103 附近)+ `EmitStelems` + `MultidimElemName`/`MdSuffixWidth`:`new int[2,2]{{...}}`(newobj + InitializeArray)按 RVA blob 行主序平铺发射 flat stelems(product(dims) 个元素)。

### src/il2cppc/Emit/CodeGenerator.cs
- `ReadM3MultidimArrayArg`(CodeGenerator.cs:6843 附近):0x14 泛型实参 → `[<elem><rank>]`(StackFold 的镜像)。
- `ReadM3Arg` 0x14 臂、`GenericInstantiationName`(CodeGenerator.cs:6742 附近)0x14 臂。
- `MdSuffixTable` + mdnew_ 发射(CodeGenerator.cs:8403 附近):`mdnew_<elem><rank>` → `il2cpp::il2cpp_array_new_multi_{v|ref}<N>d(nullptr, width, isRef, dims...)`,按 element suffix 解宽度/引用性。

### src/il2cppc/Emit/MethodRefResolver.cs
- `GenericInstArgName`(MethodRefResolver.cs:2154 附近)0x14 臂 → ReadM3MultidimArg。
- `GenericDefinitionName`(MethodRefResolver.cs:2342 附近)0x14 臂(multidim 数组为 MemberRef owner)。
- `GenericInstantiationName`(MethodRefResolver.cs:2503 附近)0x14 臂。
- `ReadM3MultidimArg`/`ConsumeComplexM3ArgPayload`(MethodRefResolver.cs:2623 附近):0x14 解码;复杂 element(嵌套泛型/SZARRAY/VAR)消费 payload 对齐。
- `TryMapMultidimAccessor` + `MdAccessorSuffixes`(MethodRefResolver.cs:528 附近)+ `ResolveMemberReference` 分发(623 附近):`int32_t[0...,0...]::Get(int32,int32)` / `Set(...)` → `il2cpp::il2cpp_array_get/set_i4_<rank>d`;casts 槽 0 = 接收者 "a"、索引/值 "i"(pc+1 长度,全部槽覆盖)。

### src/il2cppc/Il/MethodBodyReader.cs
- `SimpleGenericArgSuffix` 0x14 臂 → `MultidimArrayArgSuffix`(MethodBodyReader.cs:493 附近):局部 kind 命名权威产生 `[<elem><rank>]`,使 clone 合成与调用端一致。
- `MultidimArrayArgSuffix`(MethodBodyReader.cs:517 附近):0x14 blob 解码。

## 红绿证据

### 红(改前,基线 e0b0eaa3)
```
转译 p-multidimarg: method fold failed -> skipped
  (Cases.PMultiDimArg::Main: TypeSpec signature byte 0x14 not GenericInst(0x15) in M3 (newobj ctor-type resolution))
licm/inline/tempslot pre-scan 同错;Main 生成 "NOT TRANSPILED" stub(运行时抛 NotSupportedException)。
```

### 绿(改后)
```
转译 p-multidimarg: generated 6 type(s) -> 无任何告警;Main 全折叠
  IL newobj -> il2cpp::il2cpp_array_new_multi_v2d(nullptr, 4, false, 2LL, 2LL)   // int[,] 分配
  InitializeArray -> 4× flat stelem (RVA blob 行主序)
  Box<int[,]> -> Cases_Box_1__int2__class / _ctor / Set / Get (闭合克隆正确)
  bm.Get()[0,1] -> il2cpp::il2cpp_array_get_i4_2d(t1, static_cast<int32_t>(0), static_cast<int32_t>(1))
  cube[1,1,0]=7 -> il2cpp::il2cpp_array_set_i4_3d(loc1, 1, 1, 0, 7)
  bc.Get()[1,1,0] -> il2cpp::il2cpp_array_get_i4_3d(t1, 1, 1, 0)
```

## C0 / 测试
- `dotnet build src/il2cppc -c Debug`:0 警 0 错。
- `tests/unit`:2395/2395 通过(含 multidim fixture 签名解码)。

## 阶段②(未做,留待 runtime commit)
- runtime `Il2CppArray` 多维支持:`il2cpp_array_new_multi_v<N>d`、`il2cpp_array_get/set_i4_<N>d`(rank 存头、每维 length、data 行主序平铺、GC 扫描契约零改)。
- 探针 byte-exact 运行验收。

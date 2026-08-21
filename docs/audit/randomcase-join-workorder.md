# 施工单：randomcase（String.Join 泛型形态）— 2026-08-05 14:0x

## 状态
- **未修复，需施工**。String 域补全代理（MethodMap.String.cs mtime 12:39:55，已收工）
  本轮只补了 TrimStart/TrimEnd 1 参 + Equals StringComparison 形态，**未触及 Join**。
- 复现（官方 runner）：`scripts/run-tests.ps1 -Case randomcase` → **transpile FAIL**：
  `System.String::Join: String.Join with 1 arg(s) not wired in M5 (the runtime join takes (string, string[]))`
  （MethodRefResolver.cs:383 抛出；matrix 到 matrix4 一直同因 FAIL）。

## 用例形态
randomcase.cs:15 `Console.WriteLine(string.Join(",", buf))`，buf 为 `byte[]` →
net48 选择泛型重载 `String.Join<byte>(string, IEnumerable<byte>)`，IL 调用签名 blob：
`10 01 02 0E 0E 15 12 49 01 1E 00`
- `0x10` = GENERIC 调用约定（泛型方法调用）
- `0x01` = **GenericParameterCount**（byte 一个泛型实参）
- `0x02` = 真正参数个数
- `0x0E 0x0E` = ret string + 参数1 string
- `0x15 0x12 0x49 0x01 0x1E 0x00` = GENERICINST(IEnumerable`1, MVAR0)
  （MethodSpec 表为空——泛型实参经 call 指令 genArgs 携带，签名内为 MVAR）

## 根因 1（transpiler，主因）
MethodRefResolver.cs:533 RawSignature 只调 `sig.ReadSignatureHeader()`，**未消费泛型
方法签名的 GenericParameterCount** → `ReadCompressedInteger()` 读到 0x01 被当作参数个数，
ret 读到 0x02（参数个数）……整体错位 → ps.Length=1、ps[0]=0x0E → Join case
（MethodMap.String.cs:160 `ps.Length==2 && ps[0]==0x0E && ps[1]==0x1D`）不匹配 → throw。
**通用缺陷**：所有泛型方法调用（Join<T>、Array.Empty<T> 等）的 ps/count 均错位；
Array.Empty<char> 因 ps[0] 恰好落 0x1D 侥幸过（matrix4 strdeep 卡在 cmake_build 而非 transpile）。

**修复**：
```csharp
var hdr = sig.ReadSignatureHeader();
if (hdr.IsGeneric) _ = sig.ReadCompressedInteger(); // GenericParameterCount
var count = sig.ReadCompressedInteger();
```

## 根因 2（映射缺形态）
修复根因 1 后 Join<byte> 的 ps = [0x0E, 0x15]（string + GENERICINST）。MethodMap.String.cs:154-162
仅接受 `ps[1]==0x1D`（SZARRAY string[]）。需新增 0x15（IEnumerable<T>）形态接线。
runtime 侧已具备：stringext2.cpp:413 `icall_string_join(separator, valuesArray)` →
JoinFromArray 对非 string 元素走 icall_object_to_string（注释自述 string[]/object[] 同签名共用）。
byte[] 元素的 net48 语义（byte.ToString() 逐元素 + separator）需在施工时验证 JoinFromArray 的
非 string 元素路径（boxed byte → object_to_string 是否产出 "1" 而非 "49" 等问题）。

## 顺带加固
ReadParamByte（MethodRefResolver.cs:553）无 `0x1E`（MVAR）case——MVAR 后随 compressed index
未消费（残留 0x00）。当前分派字节 0x15 已返回、无碍，但泛型签名解析若复用偏移需补。

## 验证路径
1. 修 RawSignature → dotnet build src/il2cppc → `run-tests.ps1 -Case randomcase`
2. 若 Join 形态缺 → 报错应变为 "String.Join with 2 arg(s) not wired"（ps=[0x0E,0x15]）
3. 补 Join 0x15 形态 → transpile 过 → mingw 构建 → diff（expected = net48 参考运行
   `1,2,3,4` 逐字节 ToString 语义）
4. 回归面：泛型方法调用（Array.Empty<T>、Nullable<T> 方法等）——跑全量矩阵或
   strdeep/stringext 确认 RawSignature 修复不破坏现有映射。

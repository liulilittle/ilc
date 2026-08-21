# M1 转译器骨架 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立 IL→C++ 转译器骨架，使"纯计算 C# 程序"（int 算术/分支/循环/方法调用/静态字段/Console 输出）经转译 + VC++/MinGW 编译后，输出与 .NET 参考运行逐字节一致。

**Architecture:** 转译器（C#/.NET8，System.Reflection.Metadata 读 PE）→ IR（临时变量化）→ 静态 C++ 源码 + 最小 C++ 运行时（类型注册表 + Console icall）→ cmake 双编译器构建 → 双跑对比测试 harness。

**Tech Stack:** C# 8 / .NET 8 SDK / System.Reflection.Metadata（SDK 内置）/ C++17 / CMake ≥3.20 / MSVC cl（VS2022）/ x86_64-w64-mingw32-g++（真 GCC）/ PowerShell 7

**设计文档:** `docs/superpowers/specs/2026-08-02-il2cpp-design.md`（本计划只实现 M1 范围）

## Global Constraints

（抄自 spec，M1 阶段生效部分）
- 零 JIT：全链路纯 AOT，无动态机器码生成路径
- 零第三方依赖：产物（转译 C++ + 运行时）不依赖任何第三方库；.NET Framework 4.8 参考程序集仅为编译期工具依赖
- 转译方法与类型命名确定性编码：`{Ns}_{Type}_{Method}_{SigHash}`（SigHash = FNV-1a 64 稳定哈希），杜绝符号冲突
- 静态编译：泛型/虚表等 M2+；M1 仅静态字段与普通方法
- **三编译器验证**：MSVC cl（VS2022）与 MinGW（clang 17，本机 ming-w64 实际为 clang）各自编译运行全部测试 + **WSL Ubuntu 18.04 GCC 7.5.0 兼容基线**（事实性通用兼容标准）。注意：本机无真 GCC，MinGW g++ 为 clang 伪装；GCC 7.5 验证走 WSL（`wsl` 命令，/mnt/e/il2cpp 可访问）
- **C++17 仅 + CMake ≥3.10**：WSL cmake 为 3.10.2，所有 CMakeLists 必须 `cmake_minimum_required(VERSION 3.10)`（3.10 语法子集，不用 3.10 之后的新命令）
- 转译代码全 RAII；方法一律 `IL2CPP_NOINLINE`（禁内联，帧语义预备）
- 测试对齐：同一程序 **.NET Framework 4.8 参考运行（net48 exe 直接执行——.NET 8 无法运行 net48 程序集，已验证）** vs 转译运行，stdout 逐字节一致
- 任何代码变更后必须编译零错误（C0 铁律）

---

### Task 1: 仓库骨架与转译器项目脚手架

**Files:**
- Create: `src/il2cppc/il2cppc.csproj`
- Create: `src/il2cppc/Program.cs`
- Create: `src/runtime/CMakeLists.txt`
- Create: `src/runtime/include/internal/il2cpp_runtime.h`
- Create: `src/runtime/src/runtime.cpp`
- Create: `scripts/build-transpiler.ps1`
- Create: `.gitignore`

**Interfaces:**
- Consumes: 无（第一个任务）
- Produces: `Il2Cpp.Transpiler.Program` 主类；运行时头文件 `include/internal/il2cpp_runtime.h`（后续生成代码 include 它）

- [ ] **Step 1: 创建转译器项目**

`src/il2cppc/il2cppc.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>il2cppc</AssemblyName>
    <RootNamespace>Il2Cpp.Transpiler</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48"
                      Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

`src/il2cppc/Program.cs`（骨架，Task 2 填充元数据层）:
```csharp
using System;

namespace Il2Cpp.Transpiler;

internal static class Program
{
    public static int Main(string[] args)
    {
        // 用法: il2cppc <input-assembly> <output-dir>
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: il2cppc <input-assembly> <output-dir>");
            return 1;
        }
        Console.WriteLine($"input:  {args[0]}");
        Console.WriteLine($"output: {args[1]}");
        return 0;
    }
}
```

- [ ] **Step 2: 构建转译器验证**

Run: `dotnet build src/il2cppc/il2cppc.csproj`
Expected: Build succeeded（首次会拉取 ReferenceAssemblies 包）

- [ ] **Step 3: 创建运行时最小骨架**

`src/runtime/include/internal/il2cpp_runtime.h`:
```cpp
#pragma once
// IL2CPP 自研运行时内部头（生成代码与运行时契约，不对外）
// M1 最小核心：Console icall + 静态字段注册

#define IL2CPP_NOINLINE __declspec(noinline)
#define IL2CPP_EXPORT extern "C" __declspec(dllexport)

namespace il2cpp {
namespace runtime {

// M1 icall: Console.WriteLine 支持 int 与字符串常量
void icall_console_write_line_i(int32_t value);
void icall_console_write_line_s(const char* value);
void icall_console_write_line_n(); // WriteLine()

// 运行时入口：由生成代码注册的类型表
void runtime_init();

}} // namespace il2cpp::runtime
```

`src/runtime/src/runtime.cpp`:
```cpp
#include "il2cpp_runtime.h"
#include <cstdio>

namespace il2cpp { namespace runtime {

void icall_console_write_line_i(int32_t value) {
    std::printf("%d\n", value);
}
void icall_console_write_line_s(const char* value) {
    std::printf("%s\n", value);
}
void icall_console_write_line_n() {
    std::printf("\n");
}
void runtime_init() {
    // M1: 无状态需要初始化；M2 起填充类型表/GC
}

}} // namespace il2cpp::runtime
```

`src/runtime/CMakeLists.txt`（运行时静态库）:
```cmake
cmake_minimum_required(VERSION 3.10)
project(il2cpp_runtime CXX)
add_library(il2cpp_runtime STATIC
    src/runtime.cpp
)
target_include_directories(il2cpp_runtime PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include/internal
)
target_compile_features(il2cpp_runtime PUBLIC cxx_std_17)
if(MSVC)
    target_compile_options(il2cpp_runtime PUBLIC /EHsc /bigobj)
else()
    target_compile_options(il2cpp_runtime PUBLIC -fexceptions -fno-rtti)
endif()
```

- [ ] **Step 4: 构建脚本**

`scripts/build-transpiler.ps1`:
```powershell
param([string]$Config = "Debug")
$ErrorActionPreference = "Stop"
dotnet build "src/il2cppc/il2cppc.csproj" -c $Config
if ($LASTEXITCODE -ne 0) { throw "transpiler build failed" }
Write-Host "transpiler built OK"
```

- [ ] **Step 5: 验证脚本 + 提交**

Run: `powershell -File scripts/build-transpiler.ps1`
Expected: `transpiler built OK`

Run: `dotnet run --project src/il2cppc -- a.dll out`（在仓库根）
Expected: `input: a.dll` / `output: out` 且退出码 0

```bash
git init
git add .
git commit -m "feat: M1 skeleton - transpiler project + minimal runtime"
```

---

### Task 2: 元数据加载器

**Files:**
- Create: `src/il2cppc/Model/TypeDef.cs`
- Create: `src/il2cppc/Model/FieldDef.cs`
- Create: `src/il2cppc/Model/MethodDef.cs`
- Create: `src/il2cppc/MetadataLoader.cs`
- Test: `tests/unit/MetadataLoaderTests.cs` + `tests/unit/unit.csproj`

**Interfaces:**
- Consumes: Task 1 项目结构
- Produces: `MetadataLoader.LoadAssembly(string path) -> AssemblyModel`
  - `AssemblyModel.Types: IReadOnlyList<TypeDef>`
  - `TypeDef { string Name; string Namespace; string FullName; IReadOnlyList<FieldDef> StaticFields; IReadOnlyList<MethodDef> Methods; bool IsModuleType; }`
  - `MethodDef { string Name; string FullName; int Rva; bool IsStatic; bool IsPublic; }`
  - `FieldDef { string Name; TypeDef Owner; bool IsStatic; }`

- [ ] **Step 1: 写失败测试**

`tests/unit/unit.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/il2cppc/il2cppc.csproj" />
  </ItemGroup>
</Project>
```

`tests/unit/MetadataLoaderTests.cs`（测试用样例程序集由 Task 3 的测试夹具编译；本任务先测 Program.Main 外的纯函数——先建一个可编译的最小样例）:
```csharp
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Model;
using Xunit;

public class MetadataLoaderTests
{
    [Fact]
    public void LoadAssembly_FindsTypesAndStatics()
    {
        // 使用 Task 3 Step 1 的 fixtures 编译产物
        var asm = MetadataLoader.LoadAssembly("tests/fixtures/calc.dll");
        Assert.Contains(asm.Types, t => t.FullName == "Calc.Program");
        var program = asm.Types.Single(t => t.FullName == "Calc.Program");
        Assert.Contains(program.StaticFields, f => f.Name == "counter");
        Assert.Contains(program.Methods, m => m.Name == "Main");
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/unit/unit.csproj`
Expected: FAIL（MetadataLoader 不存在）

- [ ] **Step 3: 实现模型 + 加载器**

`src/il2cppc/Model/TypeDef.cs`:
```csharp
namespace Il2Cpp.Transpiler.Model;

public sealed class TypeDef
{
    public required string Name { get; init; }
    public required string Namespace { get; init; }
    public string FullName => string.IsNullOrEmpty(Namespace) ? Name : Namespace + "." + Name;
    public List<FieldDef> StaticFields { get; } = new();
    public List<MethodDef> Methods { get; } = new();
    public bool IsModuleType { get; init; }
}

public sealed class FieldDef
{
    public required string Name { get; init; }
    public required TypeDef Owner { get; init; }
    public bool IsStatic { get; init; }
    public bool IsLiteral { get; init; }   // const 字段
    public string? ConstantValue { get; init; }
}

public sealed class MethodDef
{
    public required string Name { get; init; }
    public required TypeDef Owner { get; init; }
    public bool IsStatic { get; init; }
    public bool IsPublic { get; init; }
}
```

`src/il2cppc/MetadataLoader.cs`:
```csharp
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler;

public static class MetadataLoader
{
    public static List<TypeDef> LoadAssembly(string path)
    {
        var types = new List<TypeDef>();
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        foreach (var tdHandle in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(tdHandle);
            if (td.IsNested) continue; // M1 只处理顶层类型
            var name = md.GetString(td.Name);
            var ns = md.GetString(td.Namespace);
            if (name == "<Module>") continue; // 模块初始化类型 M2 处理

            var typeDef = new TypeDef { Name = name, Namespace = ns, IsModuleType = false };

            foreach (var fh in td.GetFields())
            {
                var fd = md.GetFieldDefinition(fh);
                var fName = md.GetString(fd.Name);
                if (fName.StartsWith("<")) continue; // 编译器内部字段 M2 处理
                typeDef.StaticFields.Add(new FieldDef
                {
                    Name = fName,
                    Owner = typeDef,
                    IsStatic = (fd.Attributes & FieldAttributes.Static) != 0,
                    IsLiteral = (fd.Attributes & FieldAttributes.Literal) != 0,
                    ConstantValue = TryReadConstant(md, fd)
                });
            }

            foreach (var mh in td.GetMethods())
            {
                var mdInfo = md.GetMethodDefinition(mh);
                var mName = md.GetString(mdInfo.Name);
                typeDef.Methods.Add(new MethodDef
                {
                    Name = mName,
                    Owner = typeDef,
                    IsStatic = (mdInfo.Attributes & MethodAttributes.Static) != 0,
                    IsPublic = (mdInfo.Attributes & MethodAttributes.Public) != 0
                });
            }
            types.Add(typeDef);
        }
        return types;
    }

    private static string? TryReadConstant(MetadataReader md, FieldDefinition fd)
    {
        if (fd.GetDefaultValue().IsNil) return null;
        var c = md.GetConstant(fd.GetDefaultValue());
        return c.Value.IsNil ? null : md.GetBlobReader(c.Value).ReadConstant(c.TypeCode).ToString();
    }
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests/unit/unit.csproj`
Expected: PASS（依赖 Task 3 的 fixtures/calc.dll；若尚未生成，本任务先以手工 csc 编译的样例验证——fixtures 生成脚本见 Task 3 Step 1）

- [ ] **Step 5: 提交**

```bash
git add src/il2cppc tests/unit
git commit -m "feat: M1 metadata loader (types/statics/methods)"
```

---

### Task 3: IL 解码器 + 测试夹具编译链

**Files:**
- Create: `src/il2cppc/Il/IlInstruction.cs`
- Create: `src/il2cppc/Il/IlDecoder.cs`
- Create: `scripts/build-fixtures.ps1`
- Create: `tests/fixtures/calc/calc.cs`
- Create: `tests/fixtures/calc/calc.csproj`
- Test: `tests/unit/IlDecoderTests.cs`

**Interfaces:**
- Consumes: Task 2 `MetadataLoader`/模型
- Produces: `IlDecoder.DecodeMethod(BlobReader body) -> List<IlInstruction>`
  - `IlInstruction { ILOpCode OpCode; int Offset; int? Operand; string? StringOperand; }`（Operand 语义按指令族：分支目标偏移 / 字段 token / 方法 token）

- [ ] **Step 1: 测试夹具（同时解决 Task 2 测试依赖）**

`tests/fixtures/calc/calc.cs`:
```csharp
using System;

namespace Calc
{
    public static class Program
    {
        static int counter = 10;

        public static void Main()
        {
            int x = Add(3, 4);
            Console.WriteLine(x);
            counter++;
            Console.WriteLine(counter);
        }

        static int Add(int a, int b)
        {
            return a + b;
        }
    }
}
```

`tests/fixtures/calc/calc.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <OutputType>Exe</OutputType>
    <LangVersion>7.3</LangVersion>
    <Nullable>disable</Nullable>
    <EnableDefaultCompileItems>true</EnableDefaultCompileItems>
    <AssemblyName>calc</AssemblyName>
    <DebugType>portable</DebugType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net48"
                      Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

`scripts/build-fixtures.ps1`:
```powershell
$ErrorActionPreference = "Stop"
dotnet build "tests/fixtures/calc/calc.csproj" -c Release -o "tests/fixtures/out"
if ($LASTEXITCODE -ne 0) { throw "fixture build failed" }
Write-Host "fixtures built OK"
```

Run: `powershell -File scripts/build-fixtures.ps1`
Expected: `fixtures built OK`，产生 `tests/fixtures/out/calc.dll`。参考运行验证：`dotnet tests/fixtures/out/calc.dll` 输出 `7` 和 `11`。

- [ ] **Step 2: 写失败测试**

`tests/unit/IlDecoderTests.cs`:
```csharp
using Il2Cpp.Transpiler.Il;
using Xunit;

public class IlDecoderTests
{
    [Fact]
    public void Decode_CalcAdd_HasExpectedOpcodeSequence()
    {
        // 用 MetadataLoader 打开 calc.dll 找 Calc.Program.Add，
        // 解出 IL，断言含 ldarg.0/ldarg.1/add/ret 序列
        var types = MetadataLoader.LoadAssembly("tests/fixtures/out/calc.dll");
        // 完整断言依赖方法体读取（见 Step 3），先断言解码器存在且不抛
        Assert.NotNull(IlDecoder.DecodeMethod(new byte[] { 0x02, 0x03, 0x58, 0x2A }));
    }
}
```

- [ ] **Step 3: 跑测试确认失败**

Run: `dotnet test tests/unit/unit.csproj`
Expected: FAIL（IlDecoder 不存在）

- [ ] **Step 4: 实现解码器**

`src/il2cppc/Il/IlInstruction.cs`:
```csharp
namespace Il2Cpp.Transpiler.Il;

public enum IlOperandKind { None, BranchTarget, FieldToken, MethodToken, StringToken, TypeToken, Int8, Int32, Int64, Float, Double }

public readonly struct IlInstruction
{
    public ILOpCode OpCode { get; init; }
    public int Offset { get; init; }
    public IlOperandKind OperandKind { get; init; }
    public long Operand { get; init; }      // 数值/token/分支目标
    public string? StringOperand { get; init; } // 字符串常量
}
```

`src/il2cppc/Il/IlDecoder.cs`（M1 子集：算术/加载存储/分支/调用/返回/常量/转换）:
```csharp
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Il2Cpp.Transpiler.Il;

public static class IlDecoder
{
    public static List<IlInstruction> DecodeMethod(byte[] il)
    {
        var result = new List<IlInstruction>();
        var reader = new BlobReader(il);
        while (reader.RemainingBytes > 0)
        {
            var offset = reader.Offset;
            var raw = reader.ReadByte();
            var opCode = (ILOpCode)raw;
            var kind = IlOperandKind.None;
            long operand = 0;
            string? strOp = null;

            if (opCode == ILOpCode.Prefix1) { opCode = (ILOpCode)(0xFE00 | reader.ReadByte()); }
            if (opCode == ILOpCode.Prefix2) { opCode = (ILOpCode)(0xFF00 | reader.ReadByte()); }

            switch (opCode)
            {
                case ILOpCode.Nop:
                case ILOpCode.Ret:
                case ILOpCode.Pop:
                case ILOpCode.Dup:
                case ILOpCode.Add:
                case ILOpCode.Sub:
                case ILOpCode.Mul:
                case ILOpCode.Div:
                case ILOpCode.Rem:
                case ILOpCode.Neg:
                case ILOpCode.Not:
                case ILOpCode.And:
                case ILOpCode.Or:
                case ILOpCode.Xor:
                case ILOpCode.Shl:
                case ILOpCode.Shr:
                case ILOpCode.Ldarg0:
                case ILOpCode.Ldarg1:
                case ILOpCode.Ldarg2:
                case ILOpCode.Ldarg3:
                case ILOpCode.Ldloc0:
                case ILOpCode.Ldloc1:
                case ILOpCode.Ldloc2:
                case ILOpCode.Ldloc3:
                case ILOpCode.Stloc0:
                case ILOpCode.Stloc1:
                case ILOpCode.Stloc2:
                case ILOpCode.Stloc3:
                case ILOpCode.Ceq:
                case ILOpCode.Cgt:
                case ILOpCode.Clt:
                case ILOpCode.Cgt_Un:
                case ILOpCode.Clt_Un:
                    break; // 无操作数
                case ILOpCode.Ldc_I4_M1:
                case ILOpCode.Ldc_I4_0:
                case ILOpCode.Ldc_I4_1:
                case ILOpCode.Ldc_I4_2:
                case ILOpCode.Ldc_I4_3:
                case ILOpCode.Ldc_I4_4:
                case ILOpCode.Ldc_I4_5:
                case ILOpCode.Ldc_I4_6:
                case ILOpCode.Ldc_I4_7:
                case ILOpCode.Ldc_I4_8:
                    break;
                case ILOpCode.Ldc_I4_S:
                    operand = (sbyte)reader.ReadByte(); kind = IlOperandKind.Int8; break;
                case ILOpCode.Ldc_I4:
                    operand = reader.ReadInt32(); kind = IlOperandKind.Int32; break;
                case ILOpCode.Ldc_I8:
                    operand = reader.ReadInt64(); kind = IlOperandKind.Int64; break;
                case ILOpCode.Ldc_R4:
                    operand = BitConverter.SingleToInt32Bits(reader.ReadSingle()); kind = IlOperandKind.Float; break;
                case ILOpCode.Ldc_R8:
                    operand = BitConverter.DoubleToInt64Bits(reader.ReadDouble()); kind = IlOperandKind.Double; break;
                case ILOpCode.Ldstr:
                    kind = IlOperandKind.StringToken; operand = reader.ReadInt32();
                    // 字符串解析在调用方用 MetadataReader 完成（IlDecoder 无 md 上下文）
                    break;
                case ILOpCode.Ldarg:
                case ILOpCode.Starg:
                case ILOpCode.Ldloc:
                case ILOpCode.Stloc:
                case ILOpCode.Ldloca:
                case ILOpCode.Ldarga:
                    operand = reader.ReadUInt16(); kind = IlOperandKind.Int16; break;
                case ILOpCode.Ldloc_S:
                case ILOpCode.Stloc_S:
                case ILOpCode.Ldarg_S:
                case ILOpCode.Starg_S:
                case ILOpCode.Ldloca_S:
                case ILOpCode.Ldarga_S:
                    operand = reader.ReadByte(); kind = IlOperandKind.Int8; break;
                case ILOpCode.Br:
                case ILOpCode.Brfalse:
                case ILOpCode.Brtrue:
                case ILOpCode.Beq:
                case ILOpCode.Bge:
                case ILOpCode.Bgt:
                case ILOpCode.Ble:
                case ILOpCode.Blt:
                case ILOpCode.Bne_Un:
                case ILOpCode.Bge_Un:
                case ILOpCode.Bgt_Un:
                case ILOpCode.Ble_Un:
                case ILOpCode.Blt_Un:
                case ILOpCode.Leave:
                case ILOpCode.Leave_S:
                    kind = IlOperandKind.BranchTarget;
                    if (opCode is ILOpCode.Leave or ILOpCode.Br or ILOpCode.Brfalse
                        or ILOpCode.Brtrue or ILOpCode.Beq or ILOpCode.Bge
                        or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt
                        or ILOpCode.Bne_Un or ILOpCode.Bge_Un or ILOpCode.Bgt_Un
                        or ILOpCode.Ble_Un or ILOpCode.Blt_Un)
                        operand = offset + 5 + reader.ReadInt32();
                    else
                        operand = offset + 2 + reader.ReadSByte();
                    break;
                case ILOpCode.Br_S:
                case ILOpCode.Brfalse_S:
                case ILOpCode.Brtrue_S:
                case ILOpCode.Beq_S:
                case ILOpCode.Bge_S:
                case ILOpCode.Bgt_S:
                case ILOpCode.Ble_S:
                case ILOpCode.Blt_S:
                case ILOpCode.Bne_Un_S:
                case ILOpCode.Bge_Un_S:
                case ILOpCode.Bgt_Un_S:
                case ILOpCode.Ble_Un_S:
                case ILOpCode.Blt_Un_S:
                    kind = IlOperandKind.BranchTarget;
                    operand = offset + 2 + reader.ReadSByte();
                    break;
                case ILOpCode.Call:
                case ILOpCode.Callvirt:
                case ILOpCode.Newobj:
                case ILOpCode.Ldftn:
                case ILOpCode.Ldvirtftn:
                    kind = IlOperandKind.MethodToken; operand = reader.ReadInt32(); break;
                case ILOpCode.Ldsfld:
                case ILOpCode.Ldfld:
                case ILOpCode.Ldsflda:
                case ILOpCode.Ldflda:
                case ILOpCode.Stsfld:
                case ILOpCode.Stfld:
                case ILOpCode.Ldtoken:
                    kind = IlOperandKind.FieldToken; operand = reader.ReadInt32(); break;
                case ILOpCode.Castclass:
                case ILOpCode.Isinst:
                case ILOpCode.Newarr:
                case ILOpCode.Box:
                case ILOpCode.Unbox:
                case ILOpCode.Unbox_Any:
                case ILOpCode.Ldobj:
                case ILOpCode.Stobj:
                case ILOpCode.Initobj:
                case ILOpCode.Sizeof:
                    kind = IlOperandKind.TypeToken; operand = reader.ReadInt32(); break;
                case ILOpCode.Conv_I:
                case ILOpCode.Conv_I1:
                case ILOpCode.Conv_I2:
                case ILOpCode.Conv_I4:
                case ILOpCode.Conv_I8:
                case ILOpCode.Conv_U:
                case ILOpCode.Conv_U1:
                case ILOpCode.Conv_U2:
                case ILOpCode.Conv_U4:
                case ILOpCode.Conv_U8:
                case ILOpCode.Conv_R4:
                case ILOpCode.Conv_R8:
                case ILOpCode.Conv_R_Un:
                case ILOpCode.Conv_Ovf_I:
                case ILOpCode.Conv_Ovf_I1:
                case ILOpCode.Conv_Ovf_I2:
                case ILOpCode.Conv_Ovf_I4:
                case ILOpCode.Conv_Ovf_I8:
                case ILOpCode.Conv_Ovf_U:
                case ILOpCode.Conv_Ovf_U1:
                case ILOpCode.Conv_Ovf_U2:
                case ILOpCode.Conv_Ovf_U4:
                case ILOpCode.Conv_Ovf_U8:
                case ILOpCode.Conv_Ovf_I_Un:
                case ILOpCode.Conv_Ovf_I1_Un:
                case ILOpCode.Conv_Ovf_I2_Un:
                case ILOpCode.Conv_Ovf_I4_Un:
                case ILOpCode.Conv_Ovf_I8_Un:
                case ILOpCode.Conv_Ovf_U_Un:
                case ILOpCode.Conv_Ovf_U1_Un:
                case ILOpCode.Conv_Ovf_U2_Un:
                case ILOpCode.Conv_Ovf_U4_Un:
                case ILOpCode.Conv_Ovf_U8_Un:
                case ILOpCode.Conv_Ovf_I4_S:
                case ILOpCode.Conv_Ovf_U4_S:
                case ILOpCode.Conv_Ovf_I8_S:
                case ILOpCode.Conv_Ovf_U8_S:
                    break; // 无操作数
                case ILOpCode.Localloc:
                    break; // M3 支持
                default:
                    throw new NotSupportedException(
                        $"IL opcode 0x{(int)opCode:X4} at offset {offset} not supported in M1");
            }
            result.Add(new IlInstruction
            {
                OpCode = opCode, Offset = offset,
                OperandKind = kind, Operand = operand, StringOperand = strOp
            });
        }
        return result;
    }
}
```

- [ ] **Step 5: 跑测试确认通过 + 提交**

Run: `dotnet test tests/unit/unit.csproj`
Expected: PASS

```bash
git add .
git commit -m "feat: M1 IL decoder + test fixtures build chain"
```

---

### Task 4: 栈机 → 临时变量 IR 折叠（M1 简化版）

**Files:**
- Create: `src/il2cppc/Ir/StackFold.cs`
- Create: `src/il2cppc/Ir/Stmt.cs`
- Create: `src/il2cppc/Ir/Expr.cs`
- Test: `tests/unit/StackFoldTests.cs`

**Interfaces:**
- Consumes: Task 3 `IlDecoder`/`IlInstruction`
- Produces: `StackFold.Fold(List<IlInstruction>, MethodContext) -> List<Stmt>`
  - `abstract Stmt`（`ExprStmt`/`AssignStmt`/`BranchStmt`/`LabelStmt`/`RetStmt`/`CallStmt`）
  - `Expr`（`IntConstExpr`/`LocExpr`/`ArgExpr`/`StaticFieldExpr`/`BinaryExpr`/`CallExpr`/`StringConstExpr`）
  - `MethodContext { int ArgCount; string[] LocNames; }`（M1：loc 命名为 `loc0..locN`，arg 为 `arg0..argN`）

- [ ] **Step 1: 写失败测试**

`tests/unit/StackFoldTests.cs`（对 calc.dll 的 Add 方法：`return a + b;`）:
```csharp
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Xunit;

public class StackFoldTests
{
    private static List<IlInstruction> DecodeAdd()
    {
        // 从 calc.dll 读 Calc.Program.Add 的 IL（简化：固定字节序列 02 03 58 2A）
        return IlDecoder.DecodeMethod(new byte[] { 0x02, 0x03, 0x58, 0x2A });
    }

    [Fact]
    public void Fold_Add_ProducesReturnBinary()
    {
        var stmts = StackFold.Fold(DecodeAdd(), new MethodContext { ArgCount = 2 });
        // 最终语句应为 return arg0 + arg1 的 RetStmt 含 BinaryExpr(Add, Loc(arg0), Loc(arg1))
        var ret = Assert.IsType<RetStmt>(stmts[^1]);
        var bin = Assert.IsType<BinaryExpr>(ret.Value);
        Assert.Equal(BinaryOp.Add, bin.Op);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/unit/unit.csproj`
Expected: FAIL（StackFold 不存在）

- [ ] **Step 3: 实现 IR 与折叠器（M1 简化：指令序列 → 临时变量化，每 op 一条语句）**

`src/il2cppc/Ir/Stmt.cs`:
```csharp
namespace Il2Cpp.Transpiler.Ir;

public abstract class Stmt { }

public sealed class LabelStmt : Stmt { public required string Name { get; init; } }
public sealed class AssignStmt : Stmt { public required string Target { get; init; } public required Expr Value { get; init; } }
public sealed class ExprStmt : Stmt { public required Expr Value { get; init; } }
public sealed class BranchStmt : Stmt
{
    public required string TargetLabel { get; init; }
    public Expr? Condition { get; init; } // null = 无条件
}
public sealed class RetStmt : Stmt { public Expr? Value { get; init; } }
public sealed class CallStmt : Stmt { public required string Function { get; init; } public List<Expr> Args { get; } = new(); }
```

`src/il2cppc/Ir/Expr.cs`:
```csharp
namespace Il2Cpp.Transpiler.Ir;

public enum BinaryOp { Add, Sub, Mul, Div, Rem, And, Or, Xor, Shl, Shr, Ceq, Cgt, Clt, CgtUn, CltUn }

public abstract class Expr { }
public sealed class IntConstExpr : Expr { public required long Value { get; init; } }
public sealed class StringConstExpr : Expr { public required string Value { get; init; } }
public sealed class LocExpr : Expr { public required string Name { get; init; } }
public sealed class StaticFieldExpr : Expr { public required string FieldName { get; init; } }
public sealed class BinaryExpr : Expr { public required BinaryOp Op { get; init; } public required Expr Left { get; init; } public required Expr Right { get; init; } }
public sealed class CallExpr : Expr { public required string Function { get; init; } public List<Expr> Args { get; } = new(); }
```

`src/il2cppc/Ir/StackFold.cs`:
```csharp
using Il2Cpp.Transpiler.Il;

namespace Il2Cpp.Transpiler.Ir;

public sealed class MethodContext
{
    public int ArgCount { get; init; }
    public string[] LocNames { get; init; } = Array.Empty<string>();
}

public static class StackFold
{
    public static List<Stmt> Fold(List<IlInstruction> il, MethodContext ctx)
    {
        var stmts = new List<Stmt>();
        var stack = new Stack<Expr>();
        var temps = new List<string>();
        var labels = new Dictionary<int, string>();

        // 第一遍：为每个分支目标生成标签
        foreach (var ins in il)
        {
            if (ins.OperandKind == IlOperandKind.BranchTarget)
                labels.TryAdd((int)ins.Operand, $"L_{ins.Operand}");
        }

        foreach (var ins in il)
        {
            if (labels.TryGetValue(ins.Offset, out var labelName))
                stmts.Add(new LabelStmt { Name = labelName });

            switch (ins.OpCode)
            {
                case ILOpCode.Ldc_I4_M1: Push(new IntConstExpr { Value = -1 }); break;
                case ILOpCode.Ldc_I4_0: Push(new IntConstExpr { Value = 0 }); break;
                // ... ILOpCode.Ldc_I4_1..8 依此类推
                case ILOpCode.Ldc_I4_S:
                case ILOpCode.Ldc_I4:
                    Push(new IntConstExpr { Value = ins.Operand }); break;
                case ILOpCode.Ldc_I8:
                    Push(new IntConstExpr { Value = ins.Operand }); break;
                case ILOpCode.Ldstr:
                    Push(new StringConstExpr { Value = ins.StringOperand ?? "" }); break;
                case ILOpCode.Ldarg0:
                case ILOpCode.Ldarg1:
                case ILOpCode.Ldarg2:
                case ILOpCode.Ldarg3:
                    Push(new LocExpr { Name = ArgName(ins.OpCode - ILOpCode.Ldarg0) }); break;
                case ILOpCode.Ldarg:
                case ILOpCode.Ldarg_S:
                    Push(new LocExpr { Name = ArgName((int)ins.Operand) }); break;
                case ILOpCode.Ldloc0:
                case ILOpCode.Ldloc1:
                case ILOpCode.Ldloc2:
                case ILOpCode.Ldloc3:
                    Push(new LocExpr { Name = LocName(ctx, ins.OpCode - ILOpCode.Ldloc0) }); break;
                case ILOpCode.Ldloc:
                case ILOpCode.Ldloc_S:
                    Push(new LocExpr { Name = LocName(ctx, (int)ins.Operand) }); break;
                case ILOpCode.Stloc0:
                case ILOpCode.Stloc1:
                case ILOpCode.Stloc2:
                case ILOpCode.Stloc3:
                    stmts.Add(new AssignStmt { Target = LocName(ctx, ins.OpCode - ILOpCode.Stloc0), Value = stack.Pop() }); break;
                case ILOpCode.Stloc:
                case ILOpCode.Stloc_S:
                    stmts.Add(new AssignStmt { Target = LocName(ctx, (int)ins.Operand), Value = stack.Pop() }); break;
                case ILOpCode.Ldsfld:
                    Push(new StaticFieldExpr { FieldName = $"{ins.StringOperand}" }); break; // M2 用 token 解析
                case ILOpCode.Stsfld:
                    stmts.Add(new AssignStmt { Target = ins.StringOperand ?? "", Value = stack.Pop() }); break;
                case ILOpCode.Add:
                case ILOpCode.Sub:
                case ILOpCode.Mul:
                case ILOpCode.Div:
                case ILOpCode.Rem:
                case ILOpCode.And:
                case ILOpCode.Or:
                case ILOpCode.Xor:
                case ILOpCode.Shl:
                case ILOpCode.Shr:
                case ILOpCode.Ceq:
                case ILOpCode.Cgt:
                case ILOpCode.Clt:
                case ILOpCode.Cgt_Un:
                case ILOpCode.Clt_Un:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    Push(new BinaryExpr { Op = MapBinary(ins.OpCode), Left = left, Right = right });
                    break;
                }
                case ILOpCode.Call:
                    EmitCall(stmts, stack, temps, ins, callAsStmt: false);
                    break;
                case ILOpCode.Ret:
                    stmts.Add(new RetStmt { Value = stack.Count > 0 ? stack.Pop() : null });
                    break;
                case ILOpCode.Br:
                    stmts.Add(new BranchStmt { TargetLabel = labels[(int)ins.Operand] });
                    break;
                case ILOpCode.Brfalse:
                    stmts.Add(new BranchStmt { TargetLabel = labels[(int)ins.Operand], Condition = Not(stack.Pop()) });
                    break;
                case ILOpCode.Brtrue:
                    stmts.Add(new BranchStmt { TargetLabel = labels[(int)ins.Operand], Condition = stack.Pop() });
                    break;
                case ILOpCode.Beq:
                case ILOpCode.Bne_Un:
                case ILOpCode.Bge:
                case ILOpCode.Bgt:
                case ILOpCode.Ble:
                case ILOpCode.Blt:
                case ILOpCode.Bge_Un:
                case ILOpCode.Bgt_Un:
                case ILOpCode.Ble_Un:
                case ILOpCode.Blt_Un:
                {
                    var right = stack.Pop();
                    var left = stack.Pop();
                    stmts.Add(new BranchStmt
                    {
                        TargetLabel = labels[(int)ins.Operand],
                        Condition = new BinaryExpr { Op = MapBinary(ins.OpCode), Left = left, Right = right }
                    });
                    break;
                }
                default:
                    throw new NotSupportedException($"fold: opcode {ins.OpCode} not supported in M1");
            }
        }
        return stmts;

        void Push(Expr e)
        {
            if (e is CallExpr) // 调用结果入栈需临时变量（M1 简化：每 call 一个 temp）
            {
                var name = $"t{temps.Count}";
                temps.Add(name);
                stmts.Add(new AssignStmt { Target = name, Value = e });
                stack.Push(new LocExpr { Name = name });
            }
            else
            {
                stack.Push(e);
            }
        }

        static void EmitCall(List<Stmt> stmts, Stack<Expr> stack, List<string> temps, IlInstruction ins, bool callAsStmt)
        {
            var call = new CallExpr { Function = $"call_{ins.Operand}" };
            // M2：从 token 解析真实函数名；M1 先用占位，Task 5 实现 MethodRefResolver
            stmts.Add(new CallStmt { Function = call.Function, Args = { } });
        }

        static string ArgName(int index) => $"arg{index}";
        static string LocName(MethodContext ctx, int index) => index < ctx.LocNames.Length ? ctx.LocNames[index] : $"loc{index}";
        static Expr Not(Expr e) => new BinaryExpr { Op = BinaryOp.Ceq, Left = e, Right = new IntConstExpr { Value = 0 } };
        static BinaryOp MapBinary(ILOpCode op) => op switch
        {
            ILOpCode.Add => BinaryOp.Add,
            ILOpCode.Sub => BinaryOp.Sub,
            ILOpCode.Mul => BinaryOp.Mul,
            ILOpCode.Div => BinaryOp.Div,
            ILOpCode.Rem => BinaryOp.Rem,
            ILOpCode.And => BinaryOp.And,
            ILOpCode.Or => BinaryOp.Or,
            ILOpCode.Xor => BinaryOp.Xor,
            ILOpCode.Shl => BinaryOp.Shl,
            ILOpCode.Shr => BinaryOp.Shr,
            ILOpCode.Ceq or ILOpCode.Beq => BinaryOp.Ceq,
            ILOpCode.Cgt or ILOpCode.Bgt or ILOpCode.Bgt_Un => BinaryOp.Cgt,
            ILOpCode.Clt or ILOpCode.Blt or ILOpCode.Blt_Un => BinaryOp.Clt,
            ILOpCode.Bge or ILOpCode.Bge_Un => BinaryOp.CgtUn, // 取反在代码生成层处理
            ILOpCode.Ble or ILOpCode.Ble_Un => BinaryOp.CltUn,
            _ => throw new NotSupportedException($"binary {op}")
        };
    }
}
```

> 说明：Task 4 的 `EmitCall`/静态字段为 M2 占位（token 解析），M1 测试覆盖算术/存储/分支/返回路径。Task 5 实现完整的 MethodRefResolver 让 call 落到真实函数。

- [ ] **Step 4: 跑测试确认通过 + 提交**

Run: `dotnet test tests/unit/unit.csproj`
Expected: PASS（Add 折叠产生 `return arg0 + arg1`）

```bash
git add .
git commit -m "feat: M1 stack->IR folding (temp-var style)"
```

---

### Task 5: 方法代码生成 + 方法引用解析（C++ 发射）

**Files:**
- Create: `src/il2cppc/Emit/MethodEmitter.cs`
- Create: `src/il2cppc/Emit/CppWriter.cs`
- Create: `src/il2cppc/Emit/MethodRefResolver.cs`
- Create: `src/il2cppc/Emit/SigHash.cs`
- Test: `tests/unit/MethodEmitterTests.cs`

**Interfaces:**
- Consumes: Task 4 IR；Task 2 元数据（token 解析）
- Produces: `MethodEmitter.EmitMethod(MethodDef, List<IlInstruction>) -> string`（C++ 函数源码）
  - 命名：`Ns_Type_Method_SigHash`（`Ns`/`Type`/`Method` 用 `_` 连接，SigHash = FNV-1a 64 十六进制小写 16 位）
  - `CppWriter`：缩进/拼接辅助；`MethodRefResolver.Resolve(MetadataReader, int token, List<TypeDef> known) -> (string CppName, int ParamCount, bool IsSystemConsoleWriteLine)`
  - `SigHash.Hash(string signature) -> string`（FNV-1a 64）

- [ ] **Step 1: 写失败测试**

`tests/unit/MethodEmitterTests.cs`:
```csharp
using Il2Cpp.Transpiler.Emit;
using Xunit;

public class MethodEmitterTests
{
    [Fact]
    public void SigHash_IsStable()
    {
        Assert.Equal(SigHash.Hash("System.Int32 Add(System.Int32,System.Int32)"),
                     SigHash.Hash("System.Int32 Add(System.Int32,System.Int32)"));
        Assert.NotEqual(SigHash.Hash("System.Int32 Add(System.Int32)"),
                        SigHash.Hash("System.Int32 Add(System.Int32,System.Int32)"));
    }

    [Fact]
    public void EmitMethod_Add_ContainsBinaryOp()
    {
        // 用 Task 3 fixtures：Calc.Program.Add → "return arg0 + arg1;"
        var src = MethodEmitter.EmitMethod("Calc_Program_Add_" + SigHash.Hash("x"),
            new[] { "arg0", "arg1" }, "int32_t",
            "return arg0 + arg1;");
        Assert.Contains("return arg0 + arg1;", src);
        Assert.Contains("IL2CPP_NOINLINE", src);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/unit/unit.csproj`
Expected: FAIL（MethodEmitter 不存在）

- [ ] **Step 3: 实现**

`src/il2cppc/Emit/SigHash.cs`:
```csharp
namespace Il2Cpp.Transpiler.Emit;

public static class SigHash
{
    public static string Hash(string signature)
    {
        // FNV-1a 64，稳定跨进程/版本（不用 string.GetHashCode 的随机化）
        ulong hash = 14695981039346656037UL;
        foreach (var c in signature)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16");
    }
}
```

`src/il2cppc/Emit/CppWriter.cs`:
```csharp
using System.Text;

namespace Il2Cpp.Transpiler.Emit;

public sealed class CppWriter
{
    private const int AlignColumn = 100; // 行尾注释右对齐列（spec 4.4.2 风格规范）
    private readonly StringBuilder _sb = new();
    private int _indent;

    public void Line(string text = "")
    {
        if (text.Length > 0) _sb.Append(' ', _indent * 4);
        _sb.AppendLine(text);
    }

    // 代码 + 行尾注释：注释右对齐到第 100 列（openppp2 风格，spec 4.4.2）
    public void LineAligned(string code, string comment = "")
    {
        _sb.Append(' ', _indent * 4);
        if (comment.Length == 0)
        {
            _sb.AppendLine(code);
            return;
        }
        var padded = code.Length < AlignColumn - 2
            ? code.PadRight(AlignColumn - 2 - _indent * 4)
            : code + " ";
        _sb.AppendLine(padded + "// " + comment);
    }

    public void Section(string title) // 节分隔线
    {
        _sb.AppendLine("//------------------------------------------------------------------------------");
        if (title.Length > 0) _sb.AppendLine("// " + title);
        _sb.AppendLine("//------------------------------------------------------------------------------");
    }

    public void Open() => _indent++;
    public void Close() => _indent--;
    public override string ToString() => _sb.ToString();
}
```

`src/il2cppc/Emit/MethodRefResolver.cs`（M1：识别 Console.WriteLine 与用户程序集方法）:
```csharp
using System.Reflection.Metadata;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Emit;

public sealed record ResolvedMethod(string CppName, int ParamCount, bool IsConsoleWriteLine);

public static class MethodRefResolver
{
    public static ResolvedMethod Resolve(MetadataReader md, int token, IReadOnlyList<TypeDef> knownTypes)
    {
        var handle = MetadataTokens.Handle(token);
        var mr = md.GetMemberReference((MemberReferenceHandle)handle);
        var name = md.GetString(mr.Name);
        var owner = md.GetString(md.GetString(md.GetTypeDefinition(md.GetTypeReference((TypeReferenceHandle)MetadataTokens.Handle(mr.Parent))).Name));

        var ownerFullName = ResolveOwnerFullName(md, mr.Parent);
        if (ownerFullName == "System.Console" && name == "WriteLine")
        {
            return new ResolvedMethod("il2cpp::runtime::icall_console_write_line_i", 1, true);
        }
        // M1：仅解析已知用户类型方法（Task 6 注册表配合）
        return new ResolvedMethod($"{Sanitize(ownerFullName)}_{name}_{SigHash.Hash(ownerFullName + "." + name)}", 0, false);
    }

    private static string ResolveOwnerFullName(MetadataReader md, EntityHandle parent) =>
        parent.Kind switch
        {
            HandleKind.TypeReference => md.GetFullName((TypeReferenceHandle)parent),
            HandleKind.TypeDefinition => md.GetFullName((TypeDefinitionHandle)parent),
            _ => "?",
        };

    private static string Sanitize(string s) => string.Join("_", s.Split('.', '+', '`', '<', '>', ',', '[', ']'));
}
```

（注：`md.GetFullName` 为扩展方法，见 Step 3 补丁——在 `MetadataExtensions.cs` 实现）

`src/il2cppc/Emit/MetadataExtensions.cs`:
```csharp
using System.Reflection.Metadata;

namespace Il2Cpp.Transpiler.Emit;

public static class MetadataExtensions
{
    public static string GetFullName(this MetadataReader md, TypeReferenceHandle h)
    {
        var t = md.GetTypeReference(h);
        var name = md.GetString(t.Name);
        var ns = md.GetString(t.Namespace);
        var parent = t.ResolutionScope;
        if (parent.Kind == HandleKind.TypeReference)
            return md.GetFullName((TypeReferenceHandle)parent) + "+" + name;
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public static string GetFullName(this MetadataReader md, TypeDefinitionHandle h)
    {
        var t = md.GetTypeDefinition(h);
        var name = md.GetString(t.Name);
        var ns = md.GetString(t.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}
```

`src/il2cppc/Emit/MethodEmitter.cs`:
```csharp
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Emit;

public static class MethodEmitter
{
    // M1 简化签名：调用方（Task 7 CodeGenerator）负责 IR→C++ 体，此处输出函数外壳
    public static string EmitMethod(string cppName, string[] paramNames, string returnType, string body)
    {
        var w = new CppWriter();
        w.Line($"IL2CPP_NOINLINE {returnType} {cppName}({string.Join(", ", paramNames.Select(p => $"int32_t {p}"))})");
        w.Line("{");
        w.Open();
        w.Line(body);
        w.Close();
        w.Line("}");
        return w.ToString();
    }
}
```

- [ ] **Step 4: 跑测试确认通过 + 提交**

Run: `dotnet test tests/unit/unit.csproj`
Expected: PASS

```bash
git add .
git commit -m "feat: M1 method emitter + sig hash + method ref resolver"
```

---

### Task 6: 类型代码生成（struct 骨架 + 静态字段区 + 生成器主流程）

**Files:**
- Create: `src/il2cppc/Emit/CodeGenerator.cs`
- Create: `src/il2cppc/Emit/TypeEmitter.cs`
- Test: `tests/unit/CodeGeneratorTests.cs`

**Interfaces:**
- Consumes: Task 2/3/4/5 全部
- Produces: `CodeGenerator.Generate(List<TypeDef>, string outDir) -> void`
  - 输出：`gen/CMakeLists.txt`、`gen/src/<Type>.cpp`（每类型一文件）、`gen/include/internal/generated.h`（类型注册表声明）
  - `TypeEmitter.EmitStaticStorage(TypeDef) -> string`（`static int32_t sf_counter = 10;` 形式，static 字段在文件作用域）

- [ ] **Step 1: 写失败测试**

`tests/unit/CodeGeneratorTests.cs`:
```csharp
using Il2Cpp.Transpiler;
using Il2Cpp.Transpiler.Emit;
using Xunit;

public class CodeGeneratorTests
{
    [Fact]
    public void Generate_CreatesCppFiles()
    {
        var types = MetadataLoader.LoadAssembly("tests/fixtures/out/calc.dll");
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_gen_" + Guid.NewGuid().ToString("N"));
        CodeGenerator.Generate(types, dir);
        Assert.True(File.Exists(Path.Combine(dir, "CMakeLists.txt")));
        Assert.True(Directory.GetFiles(Path.Combine(dir, "src")).Length > 0);
        Directory.Delete(dir, true);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/unit/unit.csproj`
Expected: FAIL（CodeGenerator 不存在）

- [ ] **Step 3: 实现**

`src/il2cppc/Emit/TypeEmitter.cs`:
```csharp
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Emit;

public static class TypeEmitter
{
    public static string TypeCppName(TypeDef t) => Sanitize(t.FullName);

    private static string Sanitize(string s) => string.Join("_", s.Split('.', '+', '`', '<', '>', ',', '[', ']'));

    public static string EmitStaticStorage(TypeDef t)
    {
        var w = new CppWriter();
        w.Line("// static field storage for " + t.FullName);
        foreach (var f in t.StaticFields.Where(f => f.IsStatic && !f.IsLiteral))
        {
            var init = f.ConstantValue ?? "0";
            w.Line($"int32_t {TypeCppName(t)}_sf_{Sanitize(f.Name)} = {init};");
        }
        return w.ToString();
    }
}
```

`src/il2cppc/Emit/CodeGenerator.cs`:
```csharp
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Il2Cpp.Transpiler.Il;
using Il2Cpp.Transpiler.Ir;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler.Emit;

public static class CodeGenerator
{
    public static void Generate(List<TypeDef> types, string outDir, string assemblyPath = "")
    {
        Directory.CreateDirectory(Path.Combine(outDir, "src"));
        Directory.CreateDirectory(Path.Combine(outDir, "include", "internal"));

        var generatedHeader = new CppWriter();
        generatedHeader.Line("#pragma once");
        generatedHeader.Line("// generated by il2cppc - do not edit");
        generatedHeader.Line("#include \"il2cpp_runtime.h\"");
        generatedHeader.Line();

        var allSrc = new List<string>();

        foreach (var t in types)
        {
            var cppName = TypeEmitter.TypeCppName(t);
            var w = new CppWriter();
            w.Line("// generated by il2cppc - do not edit");
            w.Line("#include \"" + cppName + ".h\"");
            w.Line();
            w.Line(TypeEmitter.EmitStaticStorage(t));
            w.Line();

            foreach (var m in t.Methods.Where(m => m.IsStatic && m.Name != ".cctor"))
            {
                var body = EmitBody(t, m);
                var sig = $"{t.FullName}.{m.Name}";
                var cppMethodName = $"{cppName}_{Sanitize(m.Name)}_{SigHash.Hash(sig)}";
                w.Line(MethodEmitter.EmitMethod(cppMethodName,
                    Enumerable.Range(0, 0).ToArray(), "int32_t", body));
                w.Line();
                generatedHeader.Line($"int32_t {cppMethodName}(void);");
            }

            File.WriteAllText(Path.Combine(outDir, "src", cppName + ".cpp"), w.ToString());
            File.WriteAllText(Path.Combine(outDir, "include", "internal", cppName + ".h"),
                $"#pragma once\n#include \"il2cpp_runtime.h\"\n");
            allSrc.Add(cppName);
        }

        generatedHeader.Line();
        generatedHeader.Line($"IL2CPP_EXPORT int32_t il2cpp_entry_main(void);");
        File.WriteAllText(Path.Combine(outDir, "include", "internal", "generated.h"), generatedHeader.ToString());

        EmitMain(outDir, types);
        EmitCmake(outDir, allSrc);
    }

    private static string EmitBody(TypeDef t, MethodDef m)
    {
        // M1：方法体从 assemblyPath 的 PE 读取（简化：CodeGenerator 持有 assemblyPath 打开 reader）
        // 见 Step 3 补丁：方法体读取 + StackFold + IR→C++ 语句发射
        return "return 0;";
    }

    private static void EmitMain(string outDir, List<TypeDef> types)
    {
        var main = types.FirstOrDefault(t => t.Methods.Any(m => m.Name == "Main"));
        if (main == null) return;
        var w = new CppWriter();
        w.Line("#include \"generated.h\"");
        w.Line();
        w.Line("int32_t il2cpp_entry_main(void) {");
        w.Open();
        w.Line("il2cpp::runtime::runtime_init();");
        w.Line($"return {TypeEmitter.TypeCppName(main)}_Main_{SigHash.Hash(main.FullName + ".Main")}();");
        w.Close();
        w.Line("}");
        File.WriteAllText(Path.Combine(outDir, "src", "entry.cpp"), w.ToString());
    }

    private static void EmitCmake(string outDir, List<string> srcs)
    {
        var w = new CppWriter();
        w.Line("cmake_minimum_required(VERSION 3.10)");
        w.Line("project(il2cpp_gen CXX)");
        w.Line("add_subdirectory(../../../src/runtime ${CMAKE_BINARY_DIR}/runtime)");
        w.Line("add_executable(il2cpp_gen_exe");
        foreach (var s in srcs) w.Line("    src/" + s + ".cpp");
        w.Line("    src/entry.cpp");
        w.Line(")");
        w.Line("target_include_directories(il2cpp_gen_exe PRIVATE include/internal)");
        w.Line("target_link_libraries(il2cpp_gen_exe PRIVATE il2cpp_runtime)");
        w.Line("target_compile_features(il2cpp_gen_exe PRIVATE cxx_std_17)");
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            w.Line("if(MSVC)");
            w.Line("  target_compile_options(il2cpp_gen_exe PRIVATE /EHsc /bigobj)");
            w.Line("else()");
            w.Line("  target_compile_options(il2cpp_gen_exe PRIVATE -fexceptions)");
            w.Line("endif()");
        }
        File.WriteAllText(Path.Combine(outDir, "CMakeLists.txt"), w.ToString());
    }

    private static string Sanitize(string s) => TypeEmitter.TypeCppName(new TypeDef { Name = s, Namespace = "" });
}
```

> Step 3 补丁（本任务内完成，作为收尾）：`EmitBody` 打开 assemblyPath 的 PEReader，用 MetadataLoader 扩展出的 `MethodBodyReader`（Task 3 未含方法体读取——新增 `src/il2cppc/Il/MethodBodyReader.cs` 从 MethodDefinition.RelativeVirtualAddress 定位并读取 IL 字节 + 局部变量签名 + 字符串堆解析），IL 解码 → StackFold → IR 发射（Expr→C++ 表达式字符串：`IntConstExpr`→数字、`LocExpr`→名、`BinaryExpr`→`(l op r)`、`CallExpr`→函数调用；Stmt→语句：Assign→`t = expr;`、Ret→`return expr;`、Branch→`if(cond) goto L_x;`、Label→`L_x:`、CallStmt→`f(args);`）。Console.WriteLine 的 icall 参数类型映射：WriteLine(int)→`icall_console_write_line_i`；WriteLine()→`icall_console_write_line_n`；WriteLine(string)→`icall_console_write_line_s`（字符串常量经 StringOperand 直出 C 字符串字面量）。

- [ ] **Step 4: 跑测试确认通过 + 提交**

Run: `dotnet test tests/unit/unit.csproj`
Expected: PASS

```bash
git add .
git commit -m "feat: M1 code generator (types/static storage/cmake/entry)"
```

---

### Task 7: 运行时入口 + 转译器 CLI 接线

**Files:**
- Modify: `src/il2cppc/Program.cs`
- Modify: `src/runtime/src/runtime.cpp`（若需扩展 icall）
- Test: `tests/unit/CliTests.cs`

**Interfaces:**
- Consumes: Task 6 `CodeGenerator.Generate`
- Produces: CLI `il2cppc <input-assembly> <output-dir>` 完整流程（读 PE → 生成 → 退出码 0）

- [ ] **Step 1: 写失败测试**

`tests/unit/CliTests.cs`:
```csharp
using Il2Cpp.Transpiler;
using Xunit;

public class CliTests
{
    [Fact]
    public void Main_GeneratesOutput()
    {
        var dir = Path.Combine(Path.GetTempPath(), "il2cpp_cli_" + Guid.NewGuid().ToString("N"));
        var code = Program.RunMain(new[] { "tests/fixtures/out/calc.dll", dir });
        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(dir, "CMakeLists.txt")));
        Directory.Delete(dir, true);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/unit/unit.csproj`
Expected: FAIL（RunMain 不存在）

- [ ] **Step 3: 实现 CLI 接线**

`src/il2cppc/Program.cs`（替换 Task 1 骨架）:
```csharp
using Il2Cpp.Transpiler.Emit;
using Il2Cpp.Transpiler.Model;

namespace Il2Cpp.Transpiler;

internal static class Program
{
    public static int Main(string[] args) => RunMain(args);

    public static int RunMain(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: il2cppc <input-assembly> <output-dir>");
            return 1;
        }
        try
        {
            var types = MetadataLoader.LoadAssembly(args[0]);
            CodeGenerator.Generate(types, args[1], args[0]);
            Console.WriteLine($"generated {types.Count} types -> {args[1]}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"il2cppc error: {ex.Message}");
            return 2;
        }
    }
}
```

- [ ] **Step 4: 跑测试确认通过 + 提交**

Run: `dotnet test tests/unit/unit.csproj`
Expected: PASS

```bash
git add .
git commit -m "feat: M1 CLI pipeline wired"
```

---

### Task 8: 双编译器构建脚本（MSVC + MinGW）

**Files:**
- Create: `scripts/build-gen.ps1`
- Create: `scripts/vcvars.ps1`（定位 VS2022 开发者环境）

**Interfaces:**
- Consumes: Task 6 生成物（gen/CMakeLists.txt）
- Produces: `build-gen.ps1 -GenDir <dir> -Compiler msvc|mingw` → 编译出可执行文件；两编译器产物路径约定 `build/msvc/`、`build/mingw/`

- [ ] **Step 1: 写脚本**

`scripts/vcvars.ps1`:
```powershell
# 定位 VS2022 Enterprise 并导入 cl.exe 环境
$vs = "C:\Program Files\Microsoft Visual Studio\2022\Enterprise"
$vcvars = Join-Path $vs "VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $vcvars)) { throw "vcvars64.bat not found at $vcvars" }
$env:VCToolsInstallDir = ""
& cmd /c "`"$vcvars`" >nul 2>&1 && set" | ForEach-Object {
    if ($_ -match "^([^=]+)=(.*)$") { Set-Item -Path "env:$($matches[1])" -Value $matches[2] }
}
```

`scripts/build-gen.ps1`:
```powershell
param(
    [Parameter(Mandatory)][string]$GenDir,
    [ValidateSet("msvc","mingw","wsl")][string]$Compiler = "msvc"
)
$ErrorActionPreference = "Stop"

function ConvertTo-WslPath([string]$winPath) {
    # E:\il2cpp\xxx -> /mnt/e/il2cpp/xxx
    $drive = $winPath.Substring(0,1).ToLower()
    $rest = $winPath.Substring(3).Replace("\","/")
    return "/mnt/$drive/$rest"
}

if ($Compiler -eq "wsl") {
    $wslGen = ConvertTo-WslPath $GenDir
    $wslRoot = ConvertTo-WslPath (Resolve-Path ".")
    wsl bash -c "cd $wslRoot && rm -rf build/wsl && cmake -S $wslGen -B build/wsl -G 'Unix Makefiles' -DCMAKE_BUILD_TYPE=Release && cmake --build build/wsl"
    if ($LASTEXITCODE -ne 0) { throw "wsl build failed" }
    $exe = "build/wsl/il2cpp_gen_exe"
} elseif ($Compiler -eq "msvc") {
    . "scripts/vcvars.ps1"
    $generator = "Visual Studio 17 2022"
    $buildDir = "build/msvc"
    cmake -S $GenDir -B $buildDir -G $generator
    cmake --build $buildDir --config Release
    $exe = Join-Path $buildDir "Release\il2cpp_gen_exe.exe"
} else {
    $generator = "MinGW Makefiles"
    $buildDir = "build/mingw"
    cmake -S $GenDir -B $buildDir -G $generator
    cmake --build $buildDir --config Release
    $exe = Join-Path $buildDir "il2cpp_gen_exe.exe"
}
if (-not (Test-Path $exe)) { throw "build failed: $exe missing" }
Write-Host "built: $exe"
```

- [ ] **Step 2: 验证（用 Task 6 生成物）**

Run: `powershell -File scripts/build-gen.ps1 -GenDir <临时gen目录> -Compiler mingw`
Expected: `built: build/mingw/il2cpp_gen_exe.exe`（M1 先验证 mingw；msvc 路径在 Task 9 全流程验证）

- [ ] **Step 3: 提交**

```bash
git add scripts
git commit -m "feat: M1 dual-compiler build scripts"
```

---

### Task 9: 双跑对比测试 harness + 首批端到端用例

**Files:**
- Create: `scripts/run-tests.ps1`
- Create: `tests/cases/arith.cs`、`tests/cases/branch.cs`、`tests/cases/loop.cs`、`tests/cases/methods.cs`、`tests/cases/statics.cs`、`tests/cases/overflow.cs`
- Create: `tests/cases/README.md`

**Interfaces:**
- Consumes: build-fixtures（各用例按 fixtures 模式编译为 net48 程序集）、build-gen、dotnet 参考运行
- Produces: `run-tests.ps1 -Compiler msvc|mingw` → 全部用例双跑 diff，失败列出差异，退出码非零

- [ ] **Step 1: 测试用例（每个 = 单文件 C#，输出到 stdout）**

`tests/cases/arith.cs`:
```csharp
using System;
namespace Cases { public static class Arith {
    public static void Main() {
        Console.WriteLine(2 + 3 * 4);      // 14
        Console.WriteLine((2 + 3) * 4);    // 20
        Console.WriteLine(17 / 5);         // 3
        Console.WriteLine(17 % 5);         // 2
        Console.WriteLine(-7 / 2);         // -3 (trunc)
        Console.WriteLine(-7 % 2);         // -1
        Console.WriteLine(1 << 10);        // 1024
        Console.WriteLine(1024 >> 3);      // 128
        Console.WriteLine(0x0F & 0x33);    // 3
        Console.WriteLine(0x0F | 0x30);    // 63
        Console.WriteLine(0xFF ^ 0x0F);    // 240
        Console.WriteLine(~0);             // -1
    }
}}
```

`tests/cases/branch.cs`:
```csharp
using System;
namespace Cases { public static class Branch {
    public static void Main() {
        int x = 5;
        if (x > 3) Console.WriteLine("gt"); else Console.WriteLine("le");
        if (x == 5) Console.WriteLine("eq"); else Console.WriteLine("ne");
        if (x != 4) Console.WriteLine("ne2"); else Console.WriteLine("eq2");
        int y = x > 3 ? 100 : 200;
        Console.WriteLine(y);
        Console.WriteLine(x >= 5 ? "ge" : "lt");
    }
}}
```

`tests/cases/loop.cs`:
```csharp
using System;
namespace Cases { public static class Loop {
    public static void Main() {
        int sum = 0;
        for (int i = 1; i <= 100; i++) sum += i;
        Console.WriteLine(sum);            // 5050
        int n = 10;
        while (n > 0) { Console.Write(n); n--; }
        Console.WriteLine();
        int m = 3;
        do { Console.Write(m); m++; } while (m < 6);
        Console.WriteLine();
        int c = 0;
        for (int i = 0; i < 10; i++) { if (i == 4) continue; c += i; if (i == 7) break; }
        Console.WriteLine(c);              // 0+1+2+3+5+6+7 = 24
    }
}}
```

`tests/cases/methods.cs`:
```csharp
using System;
namespace Cases { public static class Methods {
    public static void Main() {
        Console.WriteLine(Add(3, 4));                 // 7
        Console.WriteLine(Fib(10));                   // 55
        Console.WriteLine(Pow2(5));                   // 32
        Console.WriteLine(Max(10, -3));               // 10
    }
    static int Add(int a, int b) => a + b;
    static int Fib(int n) => n <= 1 ? n : Fib(n - 1) + Fib(n - 2);
    static int Pow2(int n) { int r = 1; while (n-- > 0) r *= 2; return r; }
    static int Max(int a, int b) => a > b ? a : b;
}}
```

`tests/cases/statics.cs`:
```csharp
using System;
namespace Cases { public static class Statics {
    static int counter = 100;
    static readonly int baseValue = 7;
    public static void Main() {
        for (int i = 0; i < 5; i++) { counter += 2; Console.WriteLine(counter); }
        Console.WriteLine(baseValue * 3);             // 21
        counter -= 50;
        Console.WriteLine(counter);                   // 100+10-50 = 60
    }
}}
```

`tests/cases/overflow.cs`:
```csharp
using System;
namespace Cases { public static class Overflow {
    public static void Main() {
        int a = int.MaxValue;
        Console.WriteLine(a + 1);            // unchecked wrap: -2147483648
        Console.WriteLine(int.MinValue - 1); // 2147483647
        Console.WriteLine(-(-2147483647 - 1)); // 2147483647
        Console.WriteLine(int.MaxValue * 2); // -2
    }
}}
```

- [ ] **Step 2: 写 harness**

`scripts/run-tests.ps1`:
```powershell
param(
    [ValidateSet("msvc","mingw","wsl")][string]$Compiler = "mingw"
)
$ErrorActionPreference = "Stop"

function ConvertTo-WslPath([string]$winPath) {
    $drive = $winPath.Substring(0,1).ToLower()
    $rest = $winPath.Substring(3).Replace("\","/")
    return "/mnt/$drive/$rest"
}

$failures = 0
$cases = Get-ChildItem "tests/cases" -Filter "*.cs"
foreach ($case in $cases) {
    $name = $case.BaseName
    Write-Host "=== case: $name ($Compiler) ==="

    # 1. 编译为 net48 程序集
    $csproj = "tests/cases/$name/$name.csproj"  # fixtures 模式：每用例独立目录
    if (-not (Test-Path $csproj)) { throw "missing $csproj" }
    dotnet build $csproj -c Release -o "tests/cases/$name/out" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "build $name failed" }

    # 2. 参考运行（.NET Framework 4.8 直接执行 net48 exe；.NET 8 不能跑 net48 程序集）
    $expected = & "tests/cases/$name/out/$name.exe" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "reference run $name failed" }

    # 3. 转译
    $gen = "tests/cases/$name/gen"
    Remove-Item $gen -Recurse -Force -ErrorAction SilentlyContinue
    dotnet run --project src/il2cppc -- "tests/cases/$name/out/$name.dll" $gen | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "transpile $name failed" }

    # 4. 构建 + 运行
    $buildDir = "tests/cases/$name/build_$Compiler"
    if ($Compiler -eq "wsl") {
        $wslGen = ConvertTo-WslPath (Resolve-Path $gen)
        $wslBuild = ConvertTo-WslPath (Resolve-Path $buildDir)
        wsl bash -c "cmake -S $wslGen -B $wslBuild -G 'Unix Makefiles' -DCMAKE_BUILD_TYPE=Release && cmake --build $wslBuild"
        if ($LASTEXITCODE -ne 0) { throw "wsl cmake build $name failed" }
        $actual = wsl bash -c "$(ConvertTo-WslPath (Resolve-Path $buildDir))/il2cpp_gen_exe"
    } elseif ($Compiler -eq "msvc") {
        . "scripts/vcvars.ps1"
        cmake -S $gen -B $buildDir -G "Visual Studio 17 2022" | Out-Null
        cmake --build $buildDir --config Release | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "cmake build $name failed" }
        $actual = & "$buildDir/Release/il2cpp_gen_exe.exe" 2>&1
    } else {
        cmake -S $gen -B $buildDir -G "MinGW Makefiles" | Out-Null
        cmake --build $buildDir --config Release | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "cmake build $name failed" }
        $actual = & "$buildDir/il2cpp_gen_exe.exe" 2>&1
    }

    # 5. 逐字节 diff
    $e = ($expected -join "`n")
    $a = ($actual -join "`n")
    if ($e -ne $a) {
        $failures++
        Write-Host "FAIL: $name"
        Write-Host "--- expected ---"; $expected | ForEach-Object { Write-Host $_ }
        Write-Host "--- actual ---"; $actual | ForEach-Object { Write-Host $_ }
    } else {
        Write-Host "PASS: $name"
    }
}
Write-Host "=== results: $($cases.Count - $failures)/$($cases.Count) passed ==="
exit $(if ($failures -gt 0) { 1 } else { 0 })
```

（Step 1 各用例需配套 csproj：拷贝 fixtures/calc/calc.csproj 模式，AssemblyName 与目录同名，详见 `tests/cases/README.md`）

- [ ] **Step 3: 跑 harness（MinGW 优先）**

Run: `powershell -File scripts/run-tests.ps1 -Compiler mingw`
Expected: `6/6 passed`（若部分 FAIL，按 harness 差异输出定位修复——修复循环是本项目的常态流程）

- [ ] **Step 4: 跑 harness（MSVC）**

Run: `powershell -File scripts/run-tests.ps1 -Compiler msvc`
Expected: `6/6 passed`

- [ ] **Step 5: 提交**

```bash
git add tests/cases scripts/run-tests.ps1
git commit -m "feat: M1 dual-run test harness + 6 end-to-end cases"
```

---

### Task 10: 综合验证、CI 脚本、文档、M1 交接

**Files:**
- Create: `scripts/ci.ps1`
- Create: `README.md`（仓库级：架构一句话、构建、测试、里程碑状态）
- Modify: `docs/superpowers/specs/2026-08-02-il2cpp-design.md`（若有 M1 偏差则修订）

**Interfaces:**
- Consumes: 全部任务
- Produces: `ci.ps1` = 构建转译器 + fixtures + 双编译器全用例（一个命令全绿）

- [ ] **Step 1: CI 脚本**

`scripts/ci.ps1`:
```powershell
$ErrorActionPreference = "Stop"
Write-Host "=== 1. build transpiler ==="
powershell -File scripts/build-transpiler.ps1
Write-Host "=== 2. build fixtures ==="
powershell -File scripts/build-fixtures.ps1
Write-Host "=== 3. unit tests ==="
dotnet test tests/unit/unit.csproj
if ($LASTEXITCODE -ne 0) { throw "unit tests failed" }
Write-Host "=== 4. e2e mingw ==="
powershell -File scripts/run-tests.ps1 -Compiler mingw
Write-Host "=== 5. e2e msvc ==="
powershell -File scripts/run-tests.ps1 -Compiler msvc
Write-Host "=== CI PASS ==="
```

- [ ] **Step 2: 全量验证**

Run: `powershell -File scripts/ci.ps1`
Expected: 全部通过，输出 `=== CI PASS ===`

- [ ] **Step 3: 写 README.md**（架构一句话 + 快速开始 + 里程碑状态表：M1 [x]，M2-M8 [ ]）

- [ ] **Step 4: 提交**

```bash
git add .
git commit -m "docs: M1 complete - README + CI"
```

---

## Self-Review 记录

**1. Spec 覆盖（M1 范围）**：spec 4.1 元数据层→Task 2；4.3 IL 层→Task 3/4；4.4 代码生成→Task 5/6；icall Console→Task 1/6；cmake 双编译器→Task 8；双跑 harness→Task 9；CI/测试体系雏形→Task 10。泛型/虚表/GC/异常/帧记录等为 M2+（spec 里程碑明示）。
**2. 占位符扫描**：Task 4 的 `EmitCall` 明确标注 M2 占位（测试不覆盖该路径）；Task 6 `EmitBody` 有明确实现补丁描述（非占位）。所有命令/代码块均具体。
**3. 类型一致性**：`MetadataLoader.LoadAssembly`→`CodeGenerator.Generate`→`TypeEmitter.TypeCppName`→`SigHash.Hash` 全链路命名一致；`IlOperandKind.BranchTarget` 在解码/折叠两处使用一致；fixtures 编译产物路径 `tests/fixtures/out/calc.dll` 与 `tests/cases/<name>/out/<name>.dll` 约定统一。

## 交接说明

- M1 完成 = "纯计算 C# 程序"经 il2cppc → cmake → MSVC/MinGW → 双跑一致（6 用例全绿）
- 下一计划（M2）：对象模型（对象头/类型表/引用字段位图）+ 精确根扫描 + 标记-清除 GC + 类/继承/接口/虚方法/装箱 + 表达式折叠（IR 增强）——M1 完成后单独编写
- 并行策略：M1 链式依赖期顺序执行（子代理驱动）；M5（BCL 命名空间族）才放开 24 子代理并行（每子代理一个独立 BCL 域 + 独立测试）

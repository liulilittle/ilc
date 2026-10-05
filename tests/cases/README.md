# M1 端到端测试用例（tests/cases）

每个用例 = 一个独立的单文件 C# 程序（输出到 stdout），作为 M1 转译链的验收样例：
C# 源码 → `dotnet build`（net48 参考程序集）→ 参考运行（.NET Framework 4.8 直接执行 net48 exe）
→ `il2cppc` 转译 → 三编译器（MSVC / MinGW / WSL GCC 7.5）构建 → 转译产物运行
→ 与参考输出**逐字节 diff**（`scripts/run-tests.ps1`）。

## 目录结构

```
tests/cases/<name>/
    <name>.cs        单文件 C# 程序（namespace Cases，static class，public static void Main()）
    <name>.csproj    net48 Exe（拷贝 fixtures/calc/calc.csproj 模式，AssemblyName=<name>）
    out/             dotnet build -c Release -o out 产物（<name>.exe 为 net48 PE，.NET Framework 4.8 直接运行）
    gen/             il2cppc 转译输出（CMakeLists + src/*.cpp + include/internal/*.h）
    build_<compiler>/ 三编译器各自的 cmake 构建目录（mingw / msvc / wsl）
```

## 用例清单

| 用例      | 覆盖点                                              | 参考输出（预估，以实际为准） |
|-----------|-----------------------------------------------------|------------------------------|
| arith     | 算术优先级/除法截断/取模符号/移位/位运算/~0          | 14/20/3/2/-3/-1/1024/128/3/63/240/-1 |
| branch    | if-else/==/!=/三元表达式/>=                          | gt/eq/ne2/100/ge              |
| loop      | for/while/do-while/continue/break + Console.Write    | 5050/109876543210/345/24      |
| methods   | 静态方法调用/递归（Fib）/后置递减参数/while 内乘      | 7/55/32/10                    |
| statics   | 静态字段 .cctor 初值/readonly/累加累减                | 102/104/106/108/110/21/60     |
| overflow  | unchecked 环绕（int.MaxValue+1 等）                  | -2147483648/2147483647/.../-2 |
| overloads | 重载方法（Add(int,int) 与 Add(int,int,int)）签名哈希命名 + 方法体签名匹配 | 3/6 |
| crossstat | 跨类型静态字段访问（generated.h extern 声明）        | 10/21 |

## 新增用例步骤

1. `tests/cases/<name>/<name>.cs`：namespace Cases，`public static class <Name>`，`public static void Main()`，输出全走 `Console.Write/WriteLine`。
2. `tests/cases/<name>/<name>.csproj`：拷贝 `arith/arith.csproj` 模式（net48、LangVersion 7.3、OutputType Exe、AssemblyName=<name>、DebugType portable、`Microsoft.NETFramework.ReferenceAssemblies.net48` 1.0.3 PrivateAssets=all）。
3. 运行 `powershell -File scripts/run-tests.ps1 -Compiler <msvc|mingw|wsl>`：自动完成 build → 参考运行 → 转译 → 三编译器构建 → diff。新增用例需三个编译器分支全绿。

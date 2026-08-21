# 转译器审计摘要

- 日期：2026-08-03
- 范围：src/il2cppc/** + src/runtime/**（只读审计）
- 严重度统计：**Critical 4** / High 9 / Medium 11 / Low 6

## Critical / High 完整列表

| # | 严重度 | 位置 | 问题 | 为什么危险 |
|---|--------|------|------|-----------|
| C1 | Critical | CodeGenerator.cs:922-954, gc.cpp:663-692 | 引用参数（含 this）不是 GC 根 | 对象仅经参数可达时被回收 → 悬垂指针 → 静默内存损坏；合法代码极常见（方法内分配后读参数） |
| C2 | Critical | gc.cpp:742-775, 552-646; CodeGenerator.cs:708-710 | 移动式 Gen0 压缩与 C++ 栈根不兼容 | 寄存器/原生栈驻留引用、堆内字段地址（ldflda）不更新 → use-after-free；e2e 通过是编译器的运气，非保证 |
| C3 | Critical | StackFold.cs:92-93, 279-283; CodeGenerator.cs:640, 320 | int64 常量/算术静默截断为 int32（ldc.i8、conv.i4 no-op、TryFoldInt32 回绕） | `long x = 4294967296L` 得 0、int64 算术按 int32 求值——静默错误结果，long 是日常类型 |
| C4 | Critical | CodeGenerator.cs:1291-1295, 1312-1319 | 引用数组 newarr 恒 elemSize=4，stelem_ref 写 8 字节 → 越界写 | `string[]/object[]` 合法代码 → 堆损坏 + 引用元素不被扫描 → 双重损坏 |
| H1 | High | CodeGenerator.cs:1342-1348, 691 | Cgt_un/Clt_un 非折叠路径有符号发射 | uint 比较（高位为 1）静默错；uint 参数可转译故可达 |
| H2 | High | StackFold.cs:284-290; CodeGenerator.cs:326-468 | leave 折叠为 no-op，try 内 break/continue/goto 出 try 错控制流 | 静默无限循环/错误逻辑；不拒绝不报错 |
| H3 | High | CodeGenerator.cs:1275-1276 | box 非 Int32 静默装箱为 Int32 盒 | `object o = longVal` 值截断 + 类型错，无信号 |
| H4 | High | CodeGenerator.cs:1282-1289 | castclass 失败返回 null 而非抛 InvalidCastException | 静默行为偏离；null 传播造成更难查的后续错误 |
| H5 | High | pinvoke.cpp:152-182; CodeGenerator.cs:1323-1326 | P/Invoke 多 string 参数共享单静态缓冲 → 参数别名 | 两个 string 实参指向同一缓冲，内容被覆盖；C++ 实参求值序未定义 |
| H6 | High | TypeEmitter.cs:79; gc.cpp:507-518 | `1u<<slot` slot≥32 UB，>31 字段类型引用字段漏扫 | 引用字段永不扫描 → 对象被提前回收 → 悬垂/损坏 |
| H7 | High | metadata.cpp:129-153; VTablePlanner.cs:63-79 | 接口分派不走基类链（继承接口） | 派生实例经基类实现接口调用 → 运行时 abort（合法 C# 崩溃） |
| H8 | High | StackFold.cs:302-307; CodeGenerator.cs:600-605 | throw 恒抛 System.Exception（异常对象/类型丢弃） | catch(派生类型) 永不匹配 → 未处理崩溃；异常类型/消息丢失 |
| H9 | High | exception.cpp:106-115 | INT_MIN / -1 为 C++ UB | .NET 抛 OverflowException，本实现 x86 SIGFPE 崩溃 |

## 最重要的 5 个问题

1. **C1 引用参数不是 GC 根**——覆盖面最广的静默内存损坏源，任何带对象参数且内部分配的方法都触发。
2. **C2 移动压缩 vs C++ 根**——架构性错误方向：无精确栈图无法安全移动对象；当前全绿测试掩盖了该缺陷。
3. **C4 引用数组越界写**——`string[]`/`object[]` 这类最基础的引用类型用法即堆损坏。
4. **C3 int64 静默截断**——`long` 是日常类型，静默错值比崩溃更难发现。
5. **H5 P/Invoke 多字符串参数别名**——单缓冲区在单调用多 string 参数下必然错，且求值序不可控。

## 建议修复顺序

1. **C4 引用数组**（改动最小、危害最直接）：newarr 按 token 解码元素类型 → elemSize/isRefElement
   正确；非 int32/引用类型 loud 拒绝。先补 `string[]` e2e。
2. **C3 int64 截断**：Ldc_i8 超 int32 范围 loud 拒绝 + Conv_i4 真截断；补 long e2e。
3. **C1 参数根**：refLocals 并入引用参数地址；补「参数唯一持有对象 + GC 压力」e2e。
4. **H1/H5**：uint 比较改 `(uint32_t)` 发射；P/Invoke 多 string 参数拆分独立缓冲。
5. **C2 压缩**：撤销移动压缩（恢复非移动 mark-sweep），或文档化为已知危险并默认关闭压缩；
   与 M3 分代设计冲突，需架构决策。
6. **H2/H8**：leave 出 try loud 拒绝；throw 携带真实类型+基类链。
7. 其余按报告逐项处理（H6/H7/H9、M1-M11、L1-L6）。

## 审计方法与证据说明

- 所有发现均逐行核对了 `file:line` 证据（见 transpiler-audit.md）；未修改任何代码。
- 测试盲区实证：`tests/cases/*.cs` 中 grep `cgt.un`/`ldc.i8`/`uint` 零匹配 → C3/H1 无回归保护；
  `strlen_wrap` 单字符串参数 → H5 未触发；`arrays.cs` 仅 int32 数组 → C4 未触发；`exceptions.cs`
  仅单层 try → M1 未触发；`generics.cs` 仅 Box<int> → 泛型引用实参路径未触发。
- 复核说明：并行域已新增 exceptionext/boxingext 运行时助手，但转译器 throw/box 发射
  （CodeGenerator.cs:603/1275-1276）未接线——H3/H8 维持成立（详见 transpiler-audit.md 对应小节）。

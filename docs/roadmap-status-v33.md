# Roadmap Status v33 — 缺失 runtime 域战役（P13-P19，7 域全部完成）

日期：2026-08-20。HEAD：68e0186（batch 350）。

## 本战役完成（7 个缺失 runtime 域，全部 TDD + e2e byte-exact）

| 域 | commit | runtime 测试 | e2e 案例 | 说明 |
|----|--------|-------------|---------|------|
| GregorianCalendar | 1868b12 (344) | test_calendar.cpp | calendarcase (16 bool) | Hinnant civil-date 数学 + 全局描述符 |
| DateTimeOffset | 704bfd9 (345) | test_dtoffset.cpp | dtoffsetcase (13 bool) | 16 字节值类型 + 新 "dto" 值槽 kind |
| wait-handle | 3f33985 (346) | test_waithandle.cpp | waithandlecase (12 bool) | Semaphore/Mutex/EventWaitHandle + 两线程 |
| encodingconv UTF-16BE | 53329fa (347) | test_encodingconv.cpp | encodingconvcase (8 bool) | unicodebe 页 + BigEndianUnicode |
| BigInteger | 9e48430 (348) | test_biginteger.cpp | bigintcase (11 bool) | 值槽指针模型 + 50! 已知值 |
| DeflateStream | ac8a99a (349) | test_deflate.cpp | deflatecase (4 bool) | RFC 1951 存储块 + 固定 Huffman |
| Expression-tree | 68e0186 (350) | test_exprtree.cpp | exprtreecase (4 bool) | int32 常量/算术表达式树 + Compile/DynamicInvoke |

## 验证状态
- runtime：153/153 PASS（146 既有 + 7 新测试）
- unit：1807/1807 PASS
- e2e：7 新案例 + 既有代表案例（streamops/sbinsertcase/bigintcase/dtoffsetcase/
  calendarcase/waithandlecase/encodingconvcase/datetime2/threadsync2/decimalcase/
  guidcase/delegops/winforms1）全 PASS 0 diff
- 全矩阵 415/415 有效 PASS（P12，本战役前）

## 转译器新增机制（本战役）
- 新值槽 kind："dto"（16 字节 DateTimeOffset）、"bi"（8 字节 BigInteger 指针槽）
- StackFold isValueTypeNewobj：加 BigInteger/DateTimeOffset（newobj 作操作数走就地值路径）
- vctor 路径地址化：icall_biginteger_* 取 Il2CppBigInteger*（decimal 先例）
- AssignStmt 结果槽特例："bi"（结果槽前置 (&t)）
- 基类 owner 路由：Calendar/WaitHandle/Stream 基类（继承成员声明在基类）
- StackFold re-resolve：DeflateStream 接收者即使首解返回 "il2cpp::" icall 也 re-resolve
- EmitVirtualCall：BCL owner 虚拟调用传接收者类型
- Object::ToString receiver-hint 路由：DateTimeOffset/BigInteger
- Array.Empty<T>：CLASS (0x12)/OBJECT (0x1C) 元素 → icall_array_empty_object
- Delegate.DynamicInvoke：编译表达式 lambda 求值（boxed int32 结果）

## 剩余缺失 runtime 域
- **无**——taskcards-128.md 的 T2xx 缺失域（biginteger/dtoffset/waithandle/memorystream/
  textstream/deflate/calendar/exprtree/encodingconv）中，memorystream/textstream 已由既有
  streamio 域覆盖，其余 7 个本战役全部完成。

## 剩余阻塞项（外部输入）
- #114 原始崩溃用例（需用户提供原用例；分析文档 docs/plans/issue-114-analysis.md 已产出）
- M8 S2 macOS/OpenBSD 实机验证（外部硬件；run-tests-posix.ps1 已就绪）

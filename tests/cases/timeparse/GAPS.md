# GAPS — timeparse (e2e batch C66)

## D-TMP-1: 用户方法带 DateTime 值参数 → 生成 C++ 签名不匹配（int32_t vs Il2CppDateTime）

- 现象（实测）：`static string F(DateTime d)` 被生成成
  `Cases_TimeParse_F_xxx(int32_t arg0)`，而调用方传入的是 8 字节 `Il2CppDateTime`
  结构体（il2cpp_time.h:38 定义 struct { int64_t ticks; }）→ 编译器
  "no matching function for call to 'Cases_TimeParse_F_xxx'"（20 errors）。
- 根因：用户方法的值类型参数按 32 位 int 槽降格（transpiler 值类型约定），但
  DateTime 以 dt 结构体承载，调用点以完整结构体传值——签名两态不一致。
- 规避：本用例不定义接收 DateTime 参数的用户方法，改用
  `DateTime.Parse(...).CompareTo(new DateTime(...)) == 0`（CompareTo 已接线）。
- 状态：未修（本批只记录，不修产品代码）。

## 记录（oracle 实证 2026-08-12，net48 接受而 runtime 拒绝 —— 用例不探）

- `DateTime.Parse` 拒绝集合（timeext2.cpp:24-27 文档化分歧，oracle 验证）：
  3 位年 ISO "123-06-15"、时区后缀 "…Z"、小数秒 "…30.5"、纯时间 "13:45"、
  紧凑 "20240615"、内部/尾部空白以外的 5+ 位年。
- ParseExact 拒绝 'h'/'t'/'f'/'F'/'z'/'Z'/'K' token（net48 接受 12 小时制/AM-PM）。
- Parse null / provider+DateTimeStyles 重载未接线。
- culture 敏感解析（zh-CN 'yyyy/M/d'、月份名 pattern）为已记录分歧（runtime 恒
  invariant subset）——用例全部显式传 CultureInfo.InvariantCulture。

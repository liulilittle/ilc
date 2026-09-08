# L2七区TypeKindResolver三元组复核（无残留本地switch）

> 状态：复核完成，7区清洁无残留
> 父会话：ses_faec50249ffeP9Xm8rXxPL2fiP / 子会话：ses_f9ddf2724ffeiOokWoachM8R6x
> 权威：TypeKindResolver三元组 (Kind, FullName, CppKind) 单一真值表
> 范围：CodeGenerator.cs 1900-2040/2051-2128/4054-4113/6540-6560/6609-6822

## 1 7区映射复核表

| 区 | 范围 | 权威入口 | 代表类型 | (Kind, FullName, CppKind) | C++声明 | 状态 |
|---|---|---|---|---|---|---|
| Z1 | 1900-1998 | `ResolveFromFullName(primName)` + `Kind is FieldTypeKind.Int32 or ...` | System.Int32 | (Int32, "System.Int32", "int32_t") | `int32_t d;` | ✅ |
| Z2 | 1998-2040 | `ResolveFromFullName(fbName)` + `fbCpp is "dt" or ...` | System.DateTime | (Int32, "System.DateTime", "dt") | `int32_t d;` tolerant | ✅ |
| Z3 | 2051-2090 | `kind is "l" or "d" or "p" or "ptr"` 单表 | l/d/p/ptr | int64_t/double/void*/intptr_t | `int64_t d;`等 | ✅ |
| Z4 | 2090-2128 | `kind is "dt" or ... or "ip4"` 单表 | dt/ts/dto/bi/dec/guid/ip4 | 运行时结构体/数组 | `Il2CppDateTime d;`等 | ✅ |
| Z5 | 4054-4113 | `Resolve(elementByte, isStaticField:true)` | SZARRAY element | (Int32, "System.Int32", "int32_t") | FullName字符串 | ✅ |
| Z6 | 6540-6560 | `ResolveParamEncoding("V:"+fn)` + `ResolveParamEncoding("B:V:"+fn)` | field value/addr | val/l/d/dt.. / int32_t*/Il2CppDateTime*.. | field kind | ✅ |
| Z7 | 6609-6822 | `ResolveParamEncoding("B:V:"+sfn)` + `ResolveFromBlob` | static addr/ref | sKind / kind==Reference | static field | ✅ |

## 2 残留本地switch扫描

- **扫描命令**：`python -c "import re; patterns=[r'System\.Boolean.*System\.Byte.*System\.SByte','primName switch','ldName switch','ioName switch']; zones=[(1900,2040),(2051,2128),(4054,4113),(6540,6560),(6609,6822)]; ..."`
- **结果**：`7 zones clean: no residual local primitive switches in 1900-2040/2051-2128/4054-4113/6540-6560/6609-6822` [执行:python 7zone scan clean]
- 其他区域（initobj/ldelema/callvirt等）的switch不在本次7区范围，由后续批次处理

## 3 证据

- 读：`src/il2cppc/Emit/CodeGenerator.cs:1895` Z1 `primKind is FieldTypeKind...`
- 读：`src/il2cppc/Emit/CodeGenerator.cs:1924` Z2 `fbCpp is "dt" or ...`
- 读：`src/il2cppc/Emit/CodeGenerator.cs:1991` Z3 `kind is "l" or "d" or "p" or "ptr"`
- 读：`src/il2cppc/Emit/CodeGenerator.cs:2001` Z4 `kind is "dt" or ...`
- 读：`src/il2cppc/Emit/CodeGenerator.cs:4060` Z5 `Resolve(elementByte, isStaticField:true)`
- 读：`src/il2cppc/Emit/CodeGenerator.cs:6476` Z6 `vKind is "val" or ...`
- 读：`src/il2cppc/Emit/CodeGenerator.cs:6504` Z6 `bKind.EndsWith("*")`
- 读：`src/il2cppc/Emit/CodeGenerator.cs:6546` Z7 `sKind.EndsWith("*")`
- 读：`src/il2cppc/Emit/CodeGenerator.cs:6562` Z7 `ResolveFromBlob` `kind==Reference`
- 执行：`python 7 zone residual switch scan -> clean`

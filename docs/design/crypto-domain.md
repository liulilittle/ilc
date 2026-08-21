# 加密域设计文档（System.Security.Cryptography）

> 定位：本文档是 ILC 运行时（src/runtime）新增 `System.Security.Cryptography` 基础域的
> **设计先行文档**——只定义范围 / 架构 / 契约，不实现。产出的任务卡（§5）可直接拆分为
> 4 个实现任务（T1-T4）。
>
> 语义基线：**.NET Framework 4.8（net48）**——项目 BCL 对齐基线（同 versionuri.h 的
> SEMANTIC BASELINE 惯例）。所有裁剪与行为分歧在本文档**显式声明**（§1.4 汇总表）。
>
> 现状快照（2026-08-05 复核）：会话初 glob/grep 零匹配；收尾复核发现**并行实现代理已落地
> T1 核心代码**（git untracked，代理在途，sha1.cpp 最后写于 03:32）：
> `src/runtime/include/internal/md5.h` / `sha1.h` / `sha256.h` +
> `src/runtime/src/md5.cpp` / `sha1.cpp` / `sha256.cpp` +
> `tests/runtime/test_md5.cpp` / `test_sha1.cpp` / `test_sha256.cpp` +
> `tests/cases/hashcase/`（expected.txt + ref_out.txt 为 net48 参考运行 9/9 True）。
> 本文档 §2.1/§3 契约已**对齐实际实现**；实现侧三份头文件存在四处不一致
> （命名空间 / create 语义 / hash_bytes 返回形态 / Ctx 命名），作为 T4 对齐项记录（§2.1 末）。
> 实现侧已记录首个接线阻塞：`MD5.Create()` 被 StackFold.cs:1423 判为 INSTANCE
> （静态/实例误分类，同 e2e-sprint-worklist #28/#29）——T4 前置条件（§4.2）。
>
> 接线模式参考：`MethodRefResolver.BclResolveCore` 按 owner 全名分派
> （src/il2cppc/Emit/MethodRefResolver.cs:378-383；注册约定 :31-41——每个域恰好一行
> `case "X.Y": return TryMap<Domain>(...)`）。对象化先例 `System.Version`（versionuri.h：
> GLOBAL class 描述符 + `il2cpp_object_new(&System_Version_class)` newobj fallback +
> `icall_version_*` 实例 icall 族——memory.md 会话结论 6-9：BclAllocExpr 非必需）；
> `System.Random` 对象+状态模式（il2cpp_random.h）。`IDisposable.Dispose` 的 receiver 路由
> 先例：MethodMap.StreamIO.cs:81（stream 域）。
>
> 配套文档：`docs/architecture/overview.md`（ILC 总览）、`docs/REQUIREMENTS.md`（铁律）。

---

## 1. 范围裁剪

### 1.1 API 面（按阶段）

| 类型 / 成员 | 阶段 | 说明 |
|---|---|---|
| `MD5` / `SHA1` / `SHA256`：`Create()` + `ComputeHash(byte[])` / `ComputeHash(byte[], int, int)` + `Dispose()` | T1 | Hash 族第一阶段（§2.1 对象模型） |
| `SHA384` / `SHA512`：同上 | T2 | 64 位字族，共用核心（§2.3） |
| `HMACSHA1` / `HMACSHA256` / `HMACSHA384` / `HMACSHA512`：`new HMACSHAx(byte[] key)` + `ComputeHash(byte[])` | T2 | KeyedHashAlgorithm；key 存对象引用字段（§2.1） |
| `TransformBlock` / `TransformFinalBlock`（增量流式哈希） | v2 | 需流式 ctx 跨多次调用；v1 不映射（落 BclResolve 默认 LOUD throw） |
| `ComputeHash(Stream)` | v2 | streamio 域已就绪，但需流式读取循环；v1 裁剪 |
| `HashAlgorithm.Create(string)` / CryptoConfig 工厂 | ❌ | net48 依赖机器级 crypto 配置；仅直接静态 `MD5.Create()` |
| `Aes`：`Aes.Create()` + `Key`/`IV` 属性 + `CreateEncryptor/Decryptor(key, iv)` + `TransformFinalBlock(byte[], int, int)` | T3 | 对称加密（§2.4） |
| `Aes.TransformBlock`（多块流式加解密） | v2 | 需跨调用 ctx 状态机 |
| `RNGCryptoServiceProvider`：`new` + `GetBytes(byte[])` + `GetNonZeroBytes(byte[])` | T3 | CSPRNG（§2.5）；net48 仅这两个实例方法，无 (byte[],int,int) 重载 |
| `RNGCryptoServiceProvider(string)` / CspParameters 构造 | ❌ | 具名 CSP 不支持 → NotSupported（loud） |
| `MD5CryptoServiceProvider` / `SHA1CryptoServiceProvider` / `*Managed` 旧类型 | T4 | owner 映射到同一 TryMapHash；newobj 符号处理见 §4.4 |

> Hash 阶段（T1/T2）全部成员为**确定性纯函数路径**：`MD5.Create()` 的实例状态仅承载
> 内联 ctx（§2.1），`ComputeHash(byte[])` 每次调用内建临时 ctx → 无跨调用共享状态。
> 这使 net48 live diff 可作 e2e oracle（与 versioncase 同法，§5.1）。

### 1.2 明确不做（及理由）

| 特性 | 理由 |
|---|---|
| 对称加密模式超出 CBC + PKCS7 | net48 `Aes` 默认 CBC/PKCS7 即最常见用法；ECB/CFB/OFB/GCM v2 |
| AES-192 密钥 | 需求面为 128/256；192 抛 NotSupported（loud），不进白名单 |
| HMAC 的 `Key` 属性读写、`HashName` 重配 | v1 仅 ctor(key) 注入 |
| RSA/DSA/ECDsa 非对称 | 完整 PKI 域，独立工作订单 |
| 证书（X509）、签名字节格式、PEM 解析 | 依赖非对称域 |
| CryptoStream | 依赖 TransformBlock 流式语义（v2）；与 streamio 域不混合 |
| 密钥派生 `Rfc2898DeriveBytes`（PBKDF2） | v2 候选；依赖 HMAC 核心 |
| `RandomNumberGenerator.Fill` / `GetInt32` 等 .NET Core 新增 API | net48 基线无此 API |
| BoringSSL / OpenSSL 集成 | **无外部依赖铁律**：算法全部为公开标准（RFC 1321 / FIPS 180-4 / FIPS 197 / RFC 2104），自实现（§2.2 决策记录） |

### 1.3 范围汇总表

| 算法 | 摘要/输出长度 | 标准 | 阶段 |
|---|---|---|---|
| MD5 | 16 B | RFC 1321 | T1 |
| SHA-1 | 20 B | FIPS 180-4 | T1 |
| SHA-256 | 32 B | FIPS 180-4 | T1 |
| SHA-384 | 48 B | FIPS 180-4（SHA-512 变体） | T2 |
| SHA-512 | 64 B | FIPS 180-4 | T2 |
| HMAC-SHA1/256/384/512 | 与底层一致 | RFC 2104 / FIPS 198-1 | T2 |
| AES-128/256 CBC+PKCS7 | 块 16 B | FIPS 197 + SP 800-38A | T3 |
| CSPRNG | — | BCryptGenRandom / getrandom | T3 |

### 1.4 裁剪声明汇总（与 net48 的行为分歧，全部显式化）

1. **哈希实例的运行时类型**：net48 的 `MD5.Create()` 实际返回 `MD5CryptoServiceProvider`
   实例；本域返回 `System_MD5_class` 描述符对象。对只调用 `ComputeHash` 的用户代码不可见；
   若用户代码对结果做 `GetType()` 或 `isinst MD5CryptoServiceProvider`，行为与 net48
   不同——文档化分歧（`MD5CryptoServiceProvider` 类型本身的成员调用仍经 §4.4 owner
   映射到同一 icall，仅运行时 klass 不同）。
2. **无增量流式哈希**：`TransformBlock`/`TransformFinalBlock` v1 不映射（BclResolve 默认
   LOUD throw，无静默错误）。仅 `ComputeHash` 全量一次性 + `(byte[], int, int)` 子区间
   重载。
3. **AES 仅 CBC + PKCS7**（net48 `Aes.Create()` 默认即此组合）；块加密核心按 SP 800-38A
   向量单测，模式层只实现 CBC 加 PKCS7 填充/去填充。AES-192 → NotSupported（loud）。
4. **RNGCryptoServiceProvider 的 CSP 参数构造不接受**：`new RNGCryptoServiceProvider("name")`
   与 CspParameters 重载 → NotSupported（loud）。无参 ctor 为无状态 no-op（§2.5）。
5. **无 CryptoConfig**：`HashAlgorithm.Create("MD5")` 按名工厂不映射（owner
   `System.Security.Cryptography.HashAlgorithm` 落 LOUD）；只支持具体类型静态调用。
6. **RNG 无 oracle**：CSPRNG 输出不可复现，net48 live diff 不能直接比较随机字节——
   e2e 只断言结构属性（长度、非零性），哈希/AES/HMAC 才有精确 expected.txt 向量（§5.1）。
7. **线程模型**：ctx 全部内联于实例或每次调用临时分配——无跨调用共享可变状态
   （D13：无需锁）。同一实例跨线程并发 = 用户错误，与 net48 相同（文档化）。

---

## 2. 架构

### 2.1 Hash 上下文模式（create/update/final——与实现一致）

**实际实现契约**（2026-08-05 复核 md5.h:38-68 / sha1.h:33-68 / sha256.h:21-47）：

```cpp
namespace il2cpp { namespace runtime {          // 注意：sha1.h 在 il2cpp（无 runtime）——见末表
struct Il2CppMd5Context  { uint32_t h[4]; uint64_t bitCount; uint8_t buffer[64]; size_t bufferLength; };
struct Il2CppSha1Context { uint32_t state[5]; uint64_t count; uint8_t buffer[64]; size_t bufferLen; };
struct Il2CppSha256Ctx   { uint32_t state[8]; uint8_t buffer[64]; uint64_t totalBits; size_t buffered; };

// 三阶段：create -> (update)* -> final
Il2CppMd5Context il2cpp_md5_create();                          // md5：返回值构造（zeroed）
void il2cpp_md5_update(Il2CppMd5Context*, const void* data, size_t length);
void il2cpp_md5_final(Il2CppMd5Context*, uint8_t out16[16]);   // final 后 ctx 复位可复用
void il2cpp_sha1_create(Il2CppSha1Context*);                   // sha1/256：指针复位（idempotent）
void il2cpp_sha256_create(Il2CppSha256Ctx*);
// 一次性：
Il2CppArray* il2cpp_md5_hash_bytes(const void* data, int32_t length);  // 返回新托管 byte[16]（= MD5.ComputeHash）
void il2cpp_sha1_hash_bytes(const void* data, size_t len, uint8_t digest[20]);   // 纯函数写 caller buffer
void il2cpp_sha256_hash_bytes(const void* data, size_t len, uint8_t out[32]);    // 纯函数写 caller buffer
}}
```

- 三算法均为 64 位总长计数；ctx 全部**栈可分配、无堆、无动态分配**（sha1.h:35-38）；
  `update(len==0 / null data)` 为 no-op；`final` 后必须 `create` 才能复用。
- **对象模型（T4 设计决策）**：运行时层当前只暴露 ctx 结构体，**无托管对象层**。
  托管侧 `MD5.Create()` → `ComputeHash(byte[])` 的实例映射（GLOBAL class 描述符 +
  `il2cpp_object_new` fallback，Version 先例 memory.md 结论 6-9）尚未建立。两条可选路径：
  1. **ctx 内联对象**：`Il2CppMD5 { Il2CppObject base; Il2CppMd5Context ctx; }`，ctor icall
     调 `il2cpp_md5_create()` 就地构造，`ComputeHash` icall 调 `update/final`（完整实例语义，
     兼容未来 TransformBlock 增量流）；或
  2. **直接映射一次性**：`ComputeHash(byte[])` → `il2cpp_md5_hash_bytes`（KISS，零对象
     开销；代价：无实例状态，TransformBlock v2 需重建对象层）。
  推荐 1（与 versionuri.h 对象化先例同构，成本低）；hash_bytes 一次性函数保留为
  `Create()`+`ComputeHash` 组合时的快速路径（T1 实测后定）。
- **T4 对齐项（实现侧四处不一致，C8 命名/签名一致性）**：

| # | 不一致 | 位置 | T4 处理 |
|---|---|---|---|
| 1 | 命名空间 `il2cpp::runtime` vs `il2cpp` | sha1.h:33 独在 il2cpp | 统一 `il2cpp::runtime`（icall 惯例） |
| 2 | `create` 语义：返回值构造 vs 指针复位 | md5.h:52 vs sha1/256 | 统一指针复位（或 md5 增补指针版） |
| 3 | `hash_bytes` 返回形态：托管 byte[] vs caller buffer | md5.h:66 vs sha1/256 | 按 T4 对象层决策统一（见上） |
| 4 | 异常文档矛盾：sha1.h:26-27 "NEVER throw" vs md5.h:63-65 抛 ArgumentNullException | 两处 | 统一为 md5 语义（.NET ComputeHash 抛） |

- **HMAC 对象**（T2 设计，未实现）：`Il2CppHMACSHA256 { Il2CppObject base; Il2CppArray* key; ctx; }`
  ——**首个带 GC 引用字段的哈希对象**（`refFieldBitmap` 置位；先例：stringbuilder/arraylist
  已有引用字段对象）。`ComputeHash` 按 RFC 2104 内建临时 HMAC ctx（ipad/opad 派生自 key），
  零跨调用共享状态。

### 2.2 无外部依赖决策记录（AES）

问题：阶段 2 对称加密抄 .NET 实现还是 BoringSSL？

| 方案 | 判定 | 理由 |
|---|---|---|
| BoringSSL / OpenSSL | ❌ 拒绝 | **项目铁律：无外部依赖**；mingw 链引入第三方构建复杂度 |
| 抄 .NET reference source | ⚠ 受限 | sparse checkout 无 `System.Security.Cryptography`（§6.2 核查）；在线检索作语义参考（同 regex-domain.md §6 做法） |
| **自实现 AES-128/256** | ✅ 采纳 | FIPS 197 为公开标准算法：S-box/密钥扩展/轮变换 ~400 行核心，CBC+PKCS7 模式层 ~100 行；测试向量充足（SP 800-38A / RFC 3602） |

结论：**自实现**。核心与模式分层（§2.4）：核心用 SP 800-38A ECB 向量单测，模式层用
CBC 向量 + PKCS7 往返测试，两层解耦。

### 2.3 SHA-384/SHA-512 共用核心（T2）

SHA-384 = SHA-512 换初始 IV + 截断 48 字节（FIPS 180-4 §5.3.4）。实现为**同一 64 位字
核心（128 位计数、128 字节块），两个 final 出口**：`sha512_final` 输出 64 B、
`sha384_final` 输出 48 B（截断语义）。不复制两份 64 位核心代码（C6 DRY）。

### 2.4 AES 模块（T3）

```
src/runtime/src/aes.cpp + include/internal/aes.h（纯核心）
  aes_expand_key / aes_encrypt_block / aes_decrypt_block     // FIPS 197，SP 800-38A 向量单测
src/runtime/src/aescipher.cpp + include/internal/aescipher.h（模式层 + 对象层）
  cbc encrypt/decrypt + PKCS7 填充/去填充（net48 默认语义）
  struct Il2CppAesTransform { Il2CppObject base; 密钥调度内联; 加/解密标记; }
```

- **对象模型**：`Aes.Create()` 返回 `System_Aes_class` 对象（`Key`/`IV` 为引用字段 →
  `refFieldBitmap` 置位，先例同 §2.1 HMAC）；`CreateEncryptor(key, iv)` 返回
  `System_AesTransform_class` 对象，密钥调度内联进 transform（无原生指针，GC 安全）。
- **密钥校验**：`set_Key`/`set_IV` 非法长度（Key 非 16/32 字节、IV 非 16 字节）→ net48
  抛 `CryptographicException`；实现时检查 il2cpp_exception.h 的 DotNetException 载体是否
  支持该异常类型，不支持则扩展载体（T3 注意项）。
- **模式**：v1 仅 CBC + PKCS7；`TransformFinalBlock` 一次性完成加/解密（PKCS7 去填充
  校验失败 → CryptographicException）；`TransformBlock` v2（loud）。AES-192 → NotSupported
  （loud）。
- **Aes.GenerateKey()/GenerateIV()**：复用 §2.5 CSPRNG 填充（T3 内部串联点）。

### 2.5 CSPRNG（T3）

```
src/runtime/src/rng.cpp + include/internal/il2cpp_rng.h
void il2cpp_crypto_random(uint8_t* buf, size_t len);            // 平台分派
void il2cpp_crypto_random_nonzero(uint8_t* buf, size_t len);    // 重采样直到无零字节
struct Il2CppRNG { Il2CppObject base; };                        // 无状态实例
icall_rng_ctor(Il2CppObject* self)                              // no-op（net48 无参 ctor 无副作用）
icall_rng_get_bytes(Il2CppObject* self, Il2CppArray* bytes)
icall_rng_get_nonzero_bytes(Il2CppObject* self, Il2CppArray* bytes)
```

- **平台后端**（platform_win.cpp / platform_posix.cpp 分派，同现有平台文件模式）：
  - Windows：`BCryptGenRandom`（bcrypt.dll，mingw 链 `-lbcrypt`）；回退
    `SystemFunction036`/`RtlGenRandom`（advapi32，XP+ 全可用）。
  - POSIX：`getrandom(2)`；回退 `/dev/urandom` 读取。
  - **显式禁止**：`rand()`/`System.Random` LCG（il2cpp_random.h 的 64 位 LCG 只服务
    `System.Random` 语义，**绝不**用作 CSPRNG 后端——设计红线）。
- 失败处理：平台后端失败 → CryptographicException（loud），不静默降级。

---

## 3. C 契约汇总

| 函数 | 说明 | 状态 |
|---|---|---|
| `il2cpp_md5_create/update/final/hash_bytes`（`sha1_*`/`sha256_*` 同构） | 三阶段 + 一次性（§2.1 实际契约） | ✅ T1 已落地（并行代理） |
| `il2cpp_sha384_*` / `il2cpp_sha512_*` | 三阶段 + 一次性；sha384 为截断出口（§2.3） | T2 |
| `il2cpp_hmacsha*_*` | RFC 2104 通用核心（§2.1 HMAC） | T2 |
| `il2cpp_aes_*` 核心/模式 + `il2cpp_crypto_random(_nonzero)` | §2.4 / §2.5 | T3 |
| 对象层 GLOBAL class + 实例 icall（`System_MD5_class` 等 5 个） | T4 设计决策（§2.1 对象模型，路径 1/2） | T4 |

---

## 4. 接线（T4）

> **T1 接线状态（2026-08-05 11:10 实测）**：对象 gap 简化决策落地——**不做对象层**，
> `Create()` → `::null::` fold（NullExpr.TypeName 携带算法类型，Encoding getter 先例），
> `ComputeHash(byte[])` → **hash_bytes 数组形 overload**（receiver "drop" + "a" cast；
> md5.h/sha1.h/sha256.h 各加 `Il2CppArray* il2cpp_*_hash_bytes(Il2CppArray*)`，同文件并入）。
> **hashcase e2e PASS 1/1**（net48 oracle + 转译 + cmake + 逐字节 diff 全链）。
> generated.h include 已补（CodeGenerator.cs，Guid 代理释放后）。单元 8/8；runtime 3 测试全绿。
> 唯一遗留（他域施工单）：`new string('a',100)` 的 `String::.ctor(char,int)` 未接线
> （hashcase.cs 已用 100 字面量绕开，byte 相同向量不变；真实修复 = MethodMap.String.cs
> `.ctor(char,int)` → runtime string-repeat icall）。

### 4.1 BclResolveCore 分派（MethodRefResolver.cs:383 switch）

按注册约定（:31-41，每个域恰好一行）：

```csharp
case "System.Security.Cryptography.MD5":   return TryMapHash(owner, name, ps, count, ..., HashAlgo.MD5);
case "System.Security.Cryptography.SHA1":  return TryMapHash(..., HashAlgo.SHA1);
case "System.Security.Cryptography.SHA256":return TryMapHash(..., HashAlgo.SHA256);
case "System.Security.Cryptography.SHA384":return TryMapHash(..., HashAlgo.SHA384);
case "System.Security.Cryptography.SHA512":return TryMapHash(..., HashAlgo.SHA512);
// T2: HMACSHA1/HMACSHA256/HMACSHA384/HMACSHA512 → TryMapHMAC
// T3: "System.Security.Cryptography.RNGCryptoServiceProvider" → TryMapRNG
// T3: "System.Security.Cryptography.Aes" → TryMapAes
// T4: *CryptoServiceProvider / *Managed 旧类型 → 同一 TryMapHash（§4.4）
```

- **DRY（C6）**：5 个 Hash owner 共用一个 `TryMapHash`（HashAlgo 枚举 → icall 前缀查表），
  HMAC 4 个 owner 共用一个 `TryMapHMAC`——不写 8 份近乎相同的映射。
- 签名面：`Create()`（静态、0 参）→ ctor icall；`ComputeHash(byte[])` / `ComputeHash(byte[],int,int)`
  （实例、1/3 参）→ compute_hash / compute_hash_range；`Dispose()` → dispose icall。
  `IDisposable.Dispose` 若以 `System.IDisposable` 为 owner 到达，按 receiver-type 路由到
  Hash 域——MethodMap.StreamIO.cs:81 的 receiver 分支先例（memory.md 会话结论 11-16：
  callvirt Object::ToString 的 receiver 路由教训）。

### 4.2 对象生命周期

- **已知阻塞（hashcase.cs:13-23 实测记录）**：`MD5.Create()` 被 `StackFold.cs:1423` 判定为
  INSTANCE（"resolved ... as INSTANCE (pops this) but the stack is empty"）——静态/实例
  误分类，与 e2e-sprint-worklist #28/#29（StackFold.cs:1369，Marshal 域同款）同类。
  **T4 前置条件**：先修 StackFold 静态分类，否则 `Create()` 生成的 pop-this 会空栈炸。
  修复时注意 hashcase 后续依赖：`Encoding.UTF8.GetBytes(string)` 的 raw-buffer
  代码页协议缺口（worklist #2/#12，需分配式 `icall_encoding_get_bytes_alloc`）。
- `MD5.Create()` 是**静态方法调用**：映射为 icall 分配对象（GLOBAL class）并 init ctx——
  无需 BclAllocExpr（Version 先例：fallback `il2cpp_object_new(&System_MD5_class)`）。
- `new MD5CryptoServiceProvider()` 是 **newobj**：发射 `il2cpp_object_new(&System_MD5CryptoServiceProvider_class)`。
  该符号默认不存在 → 链接失败。二选一（§4.4）：
  1. CodeGenerator.cs 的 BclAllocExpr 加一条 `"System.Security.Cryptography.MD5CryptoServiceProvider"` →
     `System_MD5_class` 符号（**注意**：CodeGenerator.cs 现由并行 Guid 代理独占，memory.md
     冲突判定；条目需等其释放后加）；或
  2. runtime 侧定义同布局的别名描述符 `System_MD5CryptoServiceProvider_class`（一行注册，
     复用同一 icall），自包含、无跨文件依赖。

### 4.3 异常契约

- 范围/参数错误 → ArgumentOutOfRangeException / ArgumentNullException（il2cpp_exception.h
  现有载体）。
- 算法不支持（AES-192、CSP 构造、TransformBlock）→ NotSupportedException（loud，
  BclResolve 既有 NotSupported 模式）。
- 密钥/IV/填充错误 → CryptographicException（需检查载体，T3 注意项，§2.4）。
- 所有 hash 核心为纯函数**不抛异常**；异常只在 icall 边界按 net48 语义抛——与
  versionuri.h "TryParse 从不抛"惯例一致。

---

## 5. 任务卡

### T1 MD5 / SHA1 / SHA256（当前工作订单）

- **状态**：纯函数核心 + 运行时测试已由并行代理落地（md5.h/sha1.h/sha256.h + 三个 .cpp +
  test_md5/test_sha1/test_sha256.cpp + tests/cases/hashcase/，§2.1 复核）。**剩余工作**：
  ① §2.1 末四处一致性对齐；② 对象层 icall（§2.1 路径 1）+ `MethodRefResolver` 映射；
  ③ hashcase e2e 转绿（阻塞：StackFold.cs:1423 静态/实例误分类，§4.2）。
- **文件域**：既有 + `src/runtime/src/md5obj.cpp`（或并入 md5.cpp）+ 对象层头文件；
  `tests/runtime/test_md5.cpp` 增补对象层断言。
- **输入/输出**：§2.1 实际契约（`il2cpp_md5_create/update/final` + `hash_bytes`）。
- **测试要点**（权威向量，全部确定值）：
  - 空串与 `"abc"`：MD5 `d41d8cd98f00b204e9800998ecf8427e` / `900150983cd24fb0d6963f7d28e17f72`（RFC 1321）；
    SHA-1 `a9993e364706816aba3e25717850c26c9cd0d89d`；SHA-256
    `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`；
    `"a"*100` 双块向量（hashcase.cs:34-36 已含 net48 实测值）；
  - 分段 update 等价性：同一数据按 1/2/7/64/1000 字节分段 update 与一次性 final
    逐字节相等（ctx 缓冲/进位逻辑核心断言）；
  - 非 64 倍数边界：len ∈ {0,1,55,56,63,64,65,127,128,129}（填充两路径：无/单块补）；
  - `hash_bytes`：null data + len>0 → ArgumentNullException（md5 语义，对齐项 4）；
  - 对象层（路径 1）：`il2cpp_object_new(&System_MD5_class)` → ctor → ComputeHash →
    dispose no-op 不崩溃；描述符身份（isinst 路径）；
  - e2e：hashcase 的 9/9 断言（expected.txt 已有 net48 oracle）。

### T2 SHA384 / SHA512 + HMAC

- **文件域**：新建 `src/runtime/src/sha512.cpp`（含 sha384 final 出口）+ `sha512.h`；
  新建 `src/runtime/src/hmac.cpp` + `hmac.h`（RFC 2104 ipad/opad 通用核心，底层 hash
  函数表参数化——同一份 HMAC 代码服务 4 个算法，C6 DRY）；新建
  `tests/runtime/test_sha512.cpp` / `test_hmac.cpp`。
- **输入/输出**：`sha512_init/update/final`（64 B）+ `sha384_final`（48 B 截断）；
  `hmac_init/update/final`（key → ipad/opad 派生，ctx 由调用方提供）；实例 icall
  `icall_sha512_ctor/compute_hash/...` + `icall_hmacsha256_ctor(key)/compute_hash/dispose`；
  `Il2CppHMACSHA256 { base; key ref; ctx; }`（refFieldBitmap 置位，§2.1）。
- **测试要点**：
  - SHA-512("abc") = `ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f`；
    SHA-384("abc") = `cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7`；
    千字符 'a' 长消息（FIPS 180-4 附录向量）；
  - 分段 update 等价性（同 T1 边界集，含 128 字节块界）；
  - HMAC：RFC 4231 case 1-3（SHA-256）、case 1（SHA-384/512）；RFC 2202（SHA-1）；
    空 key / 空消息 / key 长于块长（>64B 先 hash 压缩）；
  - HMAC 对象：`new HMACSHA256(key)` → compute_hash → dispose；null key →
    ArgumentNullException；key 引用字段被 GC 正确追踪（弱引用/存活测试可选）。

### T3 AES + 随机数

- **文件域**：新建 `src/runtime/src/aes.cpp` + `aes.h`（纯核心：S-box/密钥扩展/轮变换）、
  `src/runtime/src/aescipher.cpp` + `aescipher.h`（CBC+PKCS7 模式层 + Aes/AesTransform
  对象层）、`src/runtime/src/rng.cpp` + `il2cpp_rng.h`（§2.5）；新建
  `tests/runtime/test_aes.cpp` / `test_rng.cpp`。
- **输入/输出**：`aes_expand_key/encrypt_block/decrypt_block`；`icall_aes_ctor`、
  `icall_aes_set_key/iv`、`icall_aes_create_encryptor/decryptor`、
  `icall_aes_transform_final_block`、`icall_aes_generate_key/iv`；`il2cpp_crypto_random(_nonzero)`、
  `icall_rng_*`；`il2cpp_aes_class()` / `il2cpp_aes_transform_class()` / `il2cpp_rng_class()`。
- **测试要点**：
  - 核心（SP 800-38A F.1.1 ECB-AES128）：key `2b7e151628aed2a6abf7158809cf4f3c`，
    明文块 `6bc1bee22e409f96e93d7e117393172a` → 密文 `3ad77bb40d7a3660a89ecaf32466ef97`
    （再加 2-3 个连续块向量 + AES-256 组）；解密反向；
  - CBC：SP 800-38A F.2.1（key 同上，IV `000102030405060708090a0b0c0d0e0f`）首块密文
    `7649abac8119b246cee98e9b12e9197d`；模式层边界（块对齐/非对齐）；
  - PKCS7：全块明文 → 附加整块 0x10；去填充篡改末字节 → CryptographicException；
  - 对象：`Aes.Create()` → set Key/IV → CreateEncryptor → TransformFinalBlock 往返
    （明文=解密(加密(明文))）；GenerateKey/GenerateIV 输出长度 16/32/16；
  - 密钥校验：Key 长度 ∉ {16,32} / IV ≠ 16 → CryptographicException；AES-192 → NotSupported；
  - RNG：GetBytes 填充长度正确；GetNonZeroBytes 输出无零字节；两次调用不等（高概率断言）；
    null 数组 → ArgumentNullException；
  - 平台后端：win32 下 BCryptGenRandom 路径实测（mingw 链 `-lbcrypt` 链接成功）。

### T4 转译器接线（System.Security.Cryptography 域）

- **文件域**：改 `src/il2cppc/Emit/MethodRefResolver.cs`（§4.1 case 行 + TryMapHash/
  TryMapHMAC/TryMapRNG/TryMapAes）+ 可选 `CodeGenerator.cs` BclAllocExpr 条目（§4.2，
  Guid 代理释放后）或 runtime 别名描述符；`tests/unit` 加 MethodMap 测试；
  新建 `tests/cases/cryptocase/`（net48 oracle e2e）。
- **输入/输出**：owner→icall 映射（§4.1）；`*CryptoServiceProvider`/`*Managed` 旧类型
  owner 同映射 + newobj 符号（§4.2）。
- **测试要点**：
  - 单元：TryMapHash 对 5 owner × {Create, ComputeHash(1/3 参), Dispose} 的签名矩阵；
    TryMapHMAC ctor(key) 1 参签名；TryMapRNG/Aes 签名；未识别成员（TransformBlock 等）→
    LOUD（不静默）；
  - cryptocase（net48 expected.txt oracle，确定性路径可 diff——versioncase 同法）：
    打印 MD5/SHA1/SHA256/SHA384/SHA512("abc") 与空串 hex（`BitConverter.ToString` 已接线，
    bitconvcase 先例）+ 分段 update 对照 + HMAC RFC 向量 + AES 加解密往返 base64
    （`Convert.ToBase64String` 已接线，base64case 先例）；RNG 只做结构断言（§1.4-6），
    net48 live diff 对 RNG 段跳过比较；
  - 回归：`dotnet build src/il2cppc/il2cppc.csproj` + `dotnet test tests/unit/unit.csproj`
    + mingw 重建 runtime + `tests/cases/cryptocase` 走 `pwsh scripts/run-tests.ps1
    -Compiler mingw` 全链；runtime 测试 91 exes 全过。

---

## 6. 参考

### 6.1 算法标准文本（权威语义，net48 与其一致）

| 算法 | 标准 | 测试向量来源 |
|---|---|---|
| MD5 | RFC 1321 | RFC 1321 §A.5（空串/"abc"/1M×'a'） |
| SHA-1/256/384/512 | FIPS 180-4 | FIPS 180-4 §B（"abc"/长消息） |
| HMAC | RFC 2104 / FIPS 198-1 | RFC 4231（SHA-2 族）/ RFC 2202（SHA-1） |
| AES | FIPS 197 | SP 800-38A 附录 F（ECB/CBC 向量） |
| CSPRNG | — | NIST SP 800-90A 概念层（BCryptGenRandom 为 Windows 实现） |

### 6.2 本地参考现状（2026-08-05 核查）

- `reference/netcore/src/libraries/` sparse 仅 4 目录：System.Console、System.Private.CoreLib、
  System.Private.Uri、System.Text.RegularExpressions——**`System.Security.Cryptography` 源
  不在本地 sparse 中**（`Test-Path ...Algorithms` = False）。
- `reference/coreclr/` 为 native CLR（vm/pal/gc 等），无托管 libraries；
  `reference/corert/src/System.Private.CoreLib` 无 Security/Cryptography 目录。
- 结论：本域的 .NET 语义基线以 **net48 本机实测**（跑 net48 程序验证行为，与
  regex-domain.md §6 末段"实现提示"同法）+ **公开标准文本** + 在线检索 dotnet/runtime
  `src/libraries/System.Security.Cryptography.Algorithms|Primitives`（语义参考，非抄源码）为准。
- 已接线消费域（hash 输出可直达）：`Convert.ToBase64String`（convertbase64.cpp，
  base64case）、`BitConverter.ToString`（bitconvcase）、`System.IO`（iocase）。

### 6.3 参考来源

- RFC 1321（MD5）、RFC 2104（HMAC）、RFC 2202（HMAC-SHA1 向量）、RFC 4231（HMAC-SHA2 向量）
- FIPS 180-4（SHA-1/SHA-2 族）、FIPS 197（AES）、FIPS 198-1（HMAC）、NIST SP 800-38A（AES 模式向量）
- dotnet/runtime `src/libraries/System.Security.Cryptography.*`（在线检索，语义参考）
- MSDN net48 文档：MD5/SHA1/SHA256/SHA384/SHA512、HMACSHA*、Aes、RNGCryptoServiceProvider
  （成员面与异常语义核对用）

> 实现提示：所有哈希/AES/HMAC 向量断言在实现时用本机 .NET Framework 4.8 程序实测复核
> （net48 oracle，versioncase 既有脚本模式）；摘要展示走已接线的 BitConverter/Base64 域，
> e2e diff 可精确比对。

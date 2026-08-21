# System.Net 域设计文档（Socket / HTTP 最小网络栈）

> 定位：本文档是 ILC 运行时新增 `System.Net` 基础域的**设计先行文档**——只定义范围 / 架构 /
> 契约 / 任务卡，不实现。任务卡 T1/T2/T3 可直接拆分实现（T1 进行中）。
>
> 语义基线：**.NET Framework 4.8（net48）**——项目 BCL 对齐基线（同 versionuri.h 的
> SEMANTIC BASELINE 惯例）。所有裁剪与行为分歧在本文档**显式声明**（§1.4 汇总表）。
>
> 现状快照：`grep "Socket|TcpClient|IPAddress|WebRequest|WebClient" src/ tests/` **零命中**
> ——网络域尚未接线（绿地）；`reference/netcore` sparse 仓库**不含** System.Net.Sockets
> （仅 System.Console / System.Private.CoreLib / System.Private.Uri /
> System.Text.RegularExpressions 四个库，§6）。本文档是网络域的第一份设计输入。
>
> 接线模式参考：`MethodRefResolver` 的 owner 全名分派 + `MethodMap.<域>.cs` 自注册文件
> （先例 `MethodMap.Version.cs`，RUNTIME OBJECT CLOSURE 头注释块）；BCL 对象 newobj 走
> `BclAllocExpr` fallback `il2cpp_object_new(&{TypeCppName}_class)`（CodeGenerator.cs:1269-1270），
> runtime 定义 GLOBAL 描述符后自动正确（Version 闭环先例，discover.md:22-24）。
>
> 配套文档：`docs/design/cross-platform-design.md`（平台层 §2 收敛策略——本域第 9 域）、
> `docs/plans/taskcards-128.md` T212/T213（旧卡，命名沿用）、`docs/architecture/overview.md`。

---

## 1. 范围裁剪

### 1.1 API 面

| 类型 | 成员 | 版本 | 说明 |
|---|---|---|---|
| `Socket` | `.ctor(AddressFamily, SocketType, ProtocolType)` | T1 | 三个枚举 → int32 常量直通（§2.2 等值表） |
| `Socket` | `Connect(string, int)` | T1 | 内部 DNS 解析 + 连第一个 IPv4 地址 |
| `Socket` | `Send(byte[])` / `Send(byte[], int, int)` | T1 | **循环发满**（net48 阻塞语义），返回总字节数 |
| `Socket` | `Receive(byte[])` / `Receive(byte[], int, int)` | T1 | **单次 recv**（net48 语义：有数据即返回，可能 < 缓冲区长度），返回实际字节数 |
| `Socket` | `Close()` | T1 | 幂等（fd → -1，二次 Close 无害） |
| `Socket` | `Connect(IPAddress, int)` / `Connect(IPEndPoint)` | T2 | 依赖 IPAddress/IPEndPoint（T2） |
| `Socket` | `Bind(IPEndPoint)` / `Listen(int)` / `Accept()` | T2 | 服务端三件套；Accept 返回**新 Socket 对象**（§2.3） |
| `Socket`（UDP） | `Connect(ep)` + `Send/Receive`（连接式 UDP） | T1 | §1.4-11 裁剪 |
| `IPAddress` | `Parse(string)`（静态）、`ToString()`、`GetAddressBytes()`、`get_AddressFamily` | T2 | IPv4 only（§1.4-1）；GetAddressBytes → byte[]（runtime 自建数组） |
| `IPEndPoint` | `.ctor(IPAddress, int)`、`get_Address`、`get_Port` | T2 | 供 Bind/Connect 用 |
| `Dns` | `GetHostAddresses(string)` → `IPAddress[]` | T2 | 仅此一入口（§1.4-13）；解析失败 → SocketException |
| `HttpWebRequest` | `Create(string)`（静态）、`get_Method`/`set_Method`、`GetResponse()` | T3 | v1 仅 GET 状态探测（§1.4-7）；返回 HttpWebResponse 对象 |
| `HttpWebResponse` | `get_StatusCode`、`get_StatusDescription` | T3 | 4xx/5xx → WebException（§2.5） |
| `WebClient` | `.ctor()`、`DownloadString(string)`、`DownloadData(string)`、`UploadString(string, string)`、`UploadString(string, string, string)`、`UploadData(string, byte[])`、`UploadData(string, string, byte[])` | T3 | GET/POST 主力入口（§1.4-9：响应体经 WebClient 读） |

> `TcpClient` / `TcpListener`：🟡 **可选 T4**（不在本 3 张卡内）——`GetStream()` 需要
> `NetworkStream` 对象族（§5 T4 简述），对应 worklist T213。

### 1.2 明确不做（及理由）

| 特性 | 理由 |
|---|---|
| async/await、Task、`BeginXxx/EndXxx`（BeginConnect/BeginSend/...） | 转译器**无 async 支持**（§3）——async 模式 API 一律 NotSupported（loud） |
| IPv6（AddressFamily.InterNetworkV6、IPv6 字面量） | v1 IPv4-only（§1.4-1）；IPv6 涉及 16 字节地址 + 双栈，独立工作 |
| `NetworkStream` / `Stream` 读写图（GetResponseStream/GetRequestStream） | .NET 的流对象族（含 StreamReader 依赖链）超出最小客户端范围；响应体经 WebClient 读（§1.4-9） |
| `WebHeaderCollection`、Cookie、Proxy、凭据 | 与请求/响应头解析配套的大对象族；v1 请求头固定集合（§2.4） |
| HTTPS/TLS | 无加密域；`https://` 方案 → NotSupported（loud） |
| chunked Transfer-Encoding | §2.4 body framing 规则 3（loud 拒绝，防挂死） |
| 重定向跟随 | net48 WebClient 默认跟随；v1 不跟随（§1.4-2） |
| UDP 无连接模式（SendTo/ReceiveFrom） | 端点编组（sockaddr + 回填远端端点）v1 不做（§1.4-11） |
| `Dns.GetHostEntry/GetHostName/Resolve` | 仅 GetHostAddresses 一个入口（§1.4-13） |
| Socket 属性面（LocalEndPoint/RemoteEndPoint/AddressFamily/...）与 `Dispose()` | 属性经 getsockname 编组 IPEndPoint 对象，v1 不做；Close() 足够（测试用固定端口，§5 T1） |
| 超时控制（SendTimeout/ReceiveTimeout 属性） | net48 默认 -1（无限）与 v1 一致；阻塞语义用 OS 默认（§3） |

### 1.3 硬前置（阻塞性依赖，T0）

**`new byte[n]` 当前被转译器 loud 拒绝**——`ArrayElementType`（CodeGenerator.cs:2358-2371）
只放行 `System.Int32 → (4,false)` 与引用类型 → (8,true)，"everything else (byte, double,
user struct, ...) -> LOUD rejection"（:2367, :2370）。而 Socket.Send/Receive、WebClient
UploadData/DownloadData 全部以用户构造的 byte[] 为参数。

- **T0 前置卡**：`ArrayElementType` 增 `"System.Byte" → (1, false)`（值数组、非 ref、GC
  零扫描）；`ldelem/stelem` 的 1 字节宽度分派**已就绪**（CodeGenerator.cs:2174-2198，
  Base64 icall 产出的 byte[] 已可达，u1 读写宽度已按 elemSize 分派）——T0 是一行改动 +
  测试，不是新机制。
- runtime 侧自建 byte[] 先例（无需 newarr）：`il2cpp_array_new(nullptr, 1, false, len)`
  （bitconverter.cpp:26 / convertbase64.cpp:241 / encoding.cpp:444）。
- 本域文档化的依赖：**T1 及之后所有卡均假定 T0 已合入**。

### 1.4 裁剪声明汇总（与 net48 的行为分歧，全部显式化）

1. **IPv4-only**：`AddressFamily.InterNetworkV6` → NotSupported（loud）；`Dns.GetHostAddresses`
   只返回 IPv4 地址（net48 在双栈主机返回 IPv6 优先）；IPAddress 仅点分十进制。
2. **不跟随重定向**：net48 WebClient 默认 AllowedAutoRedirect=true；v1 把 3xx 作为普通响应
   返回（StatusCode=302 可读），不做 Location 跟进（§2.4 状态分派）。
3. **DownloadString 编码固定 UTF-8**：忽略 Content-Type 的 charset 参数（net48 按 charset
   解码、缺省 UTF-8）；e2e fixture 服务器固定 UTF-8 响应。
4. **chunked 拒绝**：响应头含 `Transfer-Encoding: chunked` → NotSupported（loud），
   v1 不做分块解码（§2.4 规则 3）。
5. **SocketException 文案稳定**：抛出的消息是固定英文文案（异常工厂契约：STABLE string），
   不镜像 net48 的本地化/OS 消息；`ErrorCode` 不保真（默认 0）。**e2e 断言约定：只断言
   `e.GetType().Name` 与 caught 标志，不断言消息文本**（既有 exceptions 用例惯例）。
6. **关闭后使用**：Close() 后 Send/Receive → `InvalidOperationException`（net48 抛
   ObjectDisposedException；后者继承自 InvalidOperationException——`catch
   (InvalidOperationException)` 语义一致，`catch (ObjectDisposedException)` 不命中，文档化）。
7. **HttpWebRequest v1 = GET-only**：无 GetRequestStream/POST 请求体；`Method = "POST"`
   存储后 `GetResponse()` 抛 NotSupported（提示走 WebClient.UploadString）。POST 全量能力
   在 WebClient（同一引擎，§2.4）。
8. **URL 解析内置**：不依赖 Uri 对象/域（Uri 运行时闭环尚未完成，process.md:30）；`string`
   URL 在 nethttp.cpp 内置解析 `scheme://host[:port]/path`；畸形 URL → `FormatException`。
9. **WebResponse 无 GetResponseStream/Headers**：响应体只经 WebClient 读（§1.2）；
   HttpWebResponse 只暴露 StatusCode/StatusDescription。
10. **Accept 返回 Socket 对象**：net48 Accept() 返回 Socket——一致，无分歧；仅服务端
    Bind 需 IPEndPoint（T2 引入）。
11. **UDP v1 = 连接式**：`Connect(ep)` + Send/Receive（net48 语义：连接式 UDP 与
    unconnected 的 sendto 等价，recv 只收来自对端的数据报）；SendTo/ReceiveFrom v2。
12. **HttpStatusCode 等枚举 = int32 常量**：枚举静态字面量被常量折叠（PassFoldEnum），
    `(int)resp.StatusCode` 直接可用；无枚举类型描述符。
13. **Dns 仅 GetHostAddresses**：其余静态入口 → NotSupported（loud）。
14. **无超时/无 KeepAlive 控制**：每次请求独立连接（请求带 `Connection: close`，§2.4），
    响应读完即关；与 net48 缺省行为在可观测面一致（net48 默认 KeepAlive=true 但响应对象
    用完前连接保持不可见）。

---

## 2. 架构

### 2.1 分层

```
托管 C#（同步 .NET API，§3 决策）
   │  MethodMap.NetSocket.cs / NetIP.cs / NetHttp.cs（owner 全名分派，MethodMap.Version.cs 形态）
   ▼
icall 域（runtime，namespace il2cpp::runtime，GLOBAL 类描述符）：
   netsocket.cpp   —— System.Net.Sockets.Socket 对象 + icall_socket_*
   netip.cpp       —— System.Net.IPAddress / IPEndPoint / Dns + icall_ip_* / icall_dns_*
   nethttp.cpp     —— WebClient / WebRequest / HttpWebResponse 对象 + icall_web_* / icall_http_*
   │  第 9 域（新增）
   ▼
platform.h：plat_net_*（platform_win.cpp = WinSock2 / platform_posix.cpp = BSD sockets）
```

- **平台差异收敛在平台层**：业务文件（netsocket/netip/nethttp）零平台假设，只调
  `plat_net_*`——与 cross-platform-design.md §2.1 策略一致（"业务文件内 `#if` 数量应降至
  接近 0"）；§7.2 结论（生成代码零平台分支）不受影响，网域全部在 runtime 内。
- **生成代码零平台假设**：转译器侧只发射 `il2cpp_object_new(&System_Net_*_class)` +
  icall 直调，无任何平台调用。

### 2.2 平台层第 9 域（network，platform.h 新增）

沿用 platform.h 契约（platform.h:21-30：UTF-8 字符串、失败返回 nullptr/false/-1、
**层内不抛异常**——last-error 留给调用方）：

```cpp
// 9. Network domain (v1: TCP + connected UDP + IPv4 resolution).
bool plat_net_init();        // Win: WSAStartup(2,2)（进程内 lazy 一次）；POSIX: true
void plat_net_cleanup();     // Win: WSACleanup；POSIX: no-op

struct plat_socket;          // 不透明句柄（Win: SOCKET 包装 / POSIX: int fd 包装）
using plat_socket_t = plat_socket*;

plat_socket_t plat_net_socket(int32_t family, int32_t type, int32_t protocol); // 失败 → nullptr
void          plat_net_close(plat_socket_t s);
bool          plat_net_connect(plat_socket_t s, const uint8_t addr4[4], uint16_t port); // 阻塞
bool          plat_net_bind(plat_socket_t s, const uint8_t addr4[4], uint16_t port);    // addr4=nullptr → INADDR_ANY
bool          plat_net_listen(plat_socket_t s, int32_t backlog);
plat_socket_t plat_net_accept(plat_socket_t s, uint8_t out_addr4[4], uint16_t* out_port); // 失败 → nullptr
int32_t       plat_net_send(plat_socket_t s, const void* data, int32_t len);   // 失败 → -1
int32_t       plat_net_recv(plat_socket_t s, void* buf, int32_t len);          // 0 = EOF；失败 → -1
int32_t       plat_net_resolve(const char* host, uint8_t* out_addr4, int32_t max); // 返回 IPv4 地址数；0 = 未解析；<0 = 错误
```

- **枚举值直通**（Win 与 POSIX 同值，已验证的常量表）：`AddressFamily.InterNetwork=2=AF_INET`；
  `SocketType.Stream=1=SOCK_STREAM`、`SocketType.Dgram=2=SOCK_DGRAM`；
  `ProtocolType.Tcp=6=IPPROTO_TCP`、`ProtocolType.Udp=17=IPPROTO_UDP`——sockaddr_in 布局
  （4 字节地址 + 16 位端口，网络字节序）双平台一致，编组代码可共用。
- Win 实现：WSAStartup 用 `std::once_flag` 守卫（D13/D14：静态初始化无锁竞争）；`plat_socket`
  包装 `SOCKET`（uintptr）；错误码经 `WSAGetLastError`（线程本地，安全）。
- POSIX 实现：直接 fd；`getaddrinfo(AF_INET)` 解析（Linux/Android/Apple/OpenBSD 均可用）。
- 头文件不 include 平台头（platform.h 纪律）；WinSock2 类型只在 platform_win.cpp 内。

### 2.3 对象描述符族（Version 先例）

BCL 对象 newobj → `il2cpp_object_new(&{TypeCppName}_class)`（CodeGenerator.cs:1269-1270
fallback，**自动适用任意 BCL 类型**）；runtime 定义 GLOBAL 描述符后链接即通——Version 闭环
已证（discover.md:22-24），本域照抄该形态，**无需 BclAllocExpr 条目**。

| 描述符（GLOBAL，lazy register） | 布局（struct + instanceSize） | baseClass |
|---|---|---|
| `System_Net_Sockets_Socket_class` | `Il2CppSocketObject { obj; int32_t fd; }` = 12 | `&System_Object_class` |
| `System_Net_IPAddress_class` | `Il2CppIPAddressObject { obj; uint32_t addr; }` = 12（host 序 uint32） | `&System_Object_class` |
| `System_Net_IPEndPoint_class` | `Il2CppIPEndPointObject { obj; Il2CppObject* address; int32_t port; }` = 16 | `&System_Object_class` |
| `System_Net_WebClient_class` | `{ obj }` = 8（无状态，每次调用独立） | `&System_Object_class` |
| `System_Net_WebRequest_class` | `{ obj; Il2CppString* url; Il2CppString* method; }` = 16 | `&System_Object_class` |
| `System_Net_HttpWebRequest_class` | 同上布局（实际分配类） | `&System_Net_WebRequest_class` |
| `System_Net_WebResponse_class` | `{ obj; int32_t statusCode; Il2CppString* statusDescription; }` = 16（抽象基，不直接分配） | `&System_Object_class` |
| `System_Net_HttpWebResponse_class` | 同上布局 | `&System_Net_WebResponse_class` |

- **baseClass 链必须正确**：`castclass`/`isinst` 走 `il2cpp_is_instance(obj, &Type_class)`
  （CodeGenerator.cs:2055-2056），typecheck 沿 `baseClass` 链判定（TypeEmitter.cs:406
  注释：descriptor 的 baseClass 链被 isinst/interface 扫描使用）——用户代码的
  `(HttpWebResponse)req.GetResponse()` 依赖 WebResponse→HttpWebResponse 链，缺链 =
  InvalidCastException。
- 注册：每文件一个 lazy accessor（`il2cpp_socket_class()` / `il2cpp_ip_address_class()` /
  `il2cpp_web_request_class()` 等，il2cpp_version_class() 形态）；`fd = -1` 表示已关闭；
  `Close()` 幂等。
- ctor 映射：`.ctor` → icall（`icall_socket_ctor(self, family, type, protocol)` 等，
  MethodMap.Version.cs:41-52 形态；枚举参数 cast `"i"`）。
- WebClient 无状态：`.ctor` 映射 no-op icall（仅 self）。

### 2.4 HTTP 层（nethttp.cpp，对 socket 上层）

**请求构建**（每次调用独立连接）：

```
URL 解析（内置，§1.4-8）：scheme://host[:port]/path  →  https:// → NotSupported（loud）
请求行：GET {path} HTTP/1.1\r\n      （path 缺省 "/"；port 缺省 80）
POST：   Content-Length: {len}\r\n + 请求体（WebClient.UploadString/UploadData）
固定头：Host: {host}[:port]\r\n  Connection: close\r\n  Content-Type（POST 缺省
        application/x-www-form-urlencoded，UploadData 为 application/octet-stream）
结束：   \r\n
```

**响应解析**（三段式，简化）：

```
1. 状态行：HTTP/1.1 SP 3位状态码 SP 描述 CRLF    → statusCode + statusDescription
2. 头：逐行到空行 CRLF（Content-Length / Transfer-Encoding 用于体分帧；其余丢弃）
3. 体分帧（规则序）：
   规则1 Content-Length: N        → 精确读 N 字节
   规则2 无 Content-Length        → 读至 EOF（配合 Connection: close；服务器需关闭）
   规则3 Transfer-Encoding: chunked → NotSupported（loud，§1.4-4）
```

**状态分派**：`2xx` → 成功响应对象；`3xx` → **作为普通响应返回**（§1.4-2 不跟随）；
`4xx/5xx` → `il2cpp_throw_web_exception`（§2.5）。

**响应对象**：HttpWebResponse 对象（字段 statusCode/statusDescription 由 runtime 写，
property getter icall 读）；**响应体不经 HttpWebResponse 暴露**——WebClient.DownloadString/
DownloadData 在同一引擎内读体后整体返回（§1.4-9）。

**编码**：DownloadString 固定 UTF-8（§1.4-3）。

**测试拓扑（零外网）**：runtime 测试自建回环 fixture（原生 `plat_net_*` 直接宿主服务器，
test_nethttp.cpp 内）；e2e（tests/cases/nethc）由 harness（run-tests.ps1）起 PS7
`System.Net.Sockets.TcpListener` 固定响应服务器，net48 参考运行连同一服务器 → 逐字节一致
（§5 T3）。服务器端口选 45xxx 段固定值（运行时测试单进程串行执行，冲突风险低；文档化）。

### 2.5 错误语义（新异常工厂）

现有工厂（il2cpp_exception_ext.h:46-88）无 SocketException/WebException → 新增两个工厂
（exceptionext.cpp 既有模式，5 行级）：

| 条件 | 行为 |
|---|---|
| 连接失败 / DNS 解析失败 / send/recv 失败 | `il2cpp_throw_socket_exception(message)` |
| HTTP 4xx/5xx | `il2cpp_throw_web_exception(message)` |
| `https://`、chunked、`Method="POST"`（HttpWebRequest） | `il2cpp_throw_not_supported_exception`（loud） |
| Close 后使用 / 无效操作序 | `il2cpp_throw_invalid_operation_exception`（§1.4-6） |
| null 参数（host/buffer/url/data） | `il2cpp_throw_argument_null_exception` |
| port 越界（<0 或 >65535） | `il2cpp_throw_argument_out_of_range_exception` |
| URL 畸形 | `il2cpp_throw_format_exception`（§1.4-8） |
| Async/BeginXxx/超范围成员 | NotSupported（loud，MethodRefResolver 约定："known but unwireable members MUST throw"） |

异常链（catch 继承语义，il2cpp_exception_ext.h:15-21 链约定；net48 继承关系对齐）：

- SocketException：`System.Net.Sockets.SocketException,` + `...Win32Exception,`
  `System.Runtime.InteropServices.ExternalException,` `System.SystemException,` `System.Exception`
- WebException：`System.Net.WebException,` + `System.InvalidOperationException,`
  `System.SystemException,` `System.Exception`（net48 WebException : InvalidOperationException）

消息全部 STABLE 英文文案（§1.4-5 断言约定）。

---

## 3. 异步决策（阶段 1：同步阻塞版）

1. **阶段 1 全部同步阻塞**，.NET 同步 API 一一映射（§1.1 表）。`Connect`/`Send`/`Receive`
   阻塞到完成（OS 默认超时；net48 默认 SendTimeout/ReceiveTimeout=-1 无限，一致 §1.4-14）。
2. **不做 Task/async**：C# async 方法编译为状态机 + Task 类型族，转译器**无 async 支持**
   （Task 域独立排期，worklist T2xx task 卡）；async/await 源码在转译期自然 NotSupported
   （Task 返回类型不可映射）——无需额外动作，文档化即可。
3. **async 模式 API 显式 loud 拒绝**：`Socket.BeginConnect/BeginSend/BeginReceive/EndXxx`、
   `WebClient.DownloadStringAsync` → NotSupported（loud，同 §2.5 约定）。
4. **线程模型**：同步阻塞下多线程客户端可用既有线程域（threadpool.cpp POSIX/Win 双分支
   已就绪）；每个 Socket/IPAddress/WebClient 对象**独立无共享状态** → D13 无锁
   （单对象状态如 fd 仅被其所属线程使用；跨线程共享同一 Socket 对象不在 v1 契约内，文档化）。
5. **测试防挂死**：阻塞 recv 的测试必须带超时保护（runtime 测试内 socket 选项
   SO_RCVTIMEO，或 fixture 服务器固定响应 + 测试序列化）；worklist T212 同款约定
   （"本地回环 + 超时保护"）。
6. **明确不做**：线程池 socket 事件模型、IO 完成端口/epoll、非阻塞 + 轮询——全部 v2+。

---

## 4. C API 草案

### 4.1 命名与位置

- icall 前缀 `icall_<域>_*`（`il2cpp::runtime::` 命名空间，MethodMap.Version.cs 形态；
  非 regex 域的 flat `il2cpp_regex_*` 风格——Socket 域需要 self 对象参数，Version 的
  `icall_version_*` 是最近亲先例）。
- 头文件命名沿用 worklist T212/T213 约定：`il2cpp_netsocket.h` / `il2cpp_nethttp.h`，
  新加 `il2cpp_netip.h`（无重名冲突，worklist:884 命名表校验过）。
- 头文件带 net48 SEMANTIC BASELINE 注释块（versionuri.h 惯例）。
- 字符串参数/返回用 `Il2CppString*`，数组用 `Il2CppArray*`（既有约定，regex 域 §4.1）。

### 4.2 il2cpp_netsocket.h（T1 + T2）

```cpp
namespace il2cpp { namespace runtime {

// T1（客户端 + 连接式 UDP）
int32_t  icall_socket_ctor(Il2CppObject* self, int32_t family, int32_t type, int32_t protocol);
         // 创建 OS socket（plat_net_socket）；失败 → SocketException。fd 存入 self 槽。
void     icall_socket_connect_host(Il2CppObject* self, Il2CppString* host, int32_t port);
         // 解析 + 连第一个 IPv4；host null → ArgumentNullException；失败 → SocketException
int32_t  icall_socket_send(Il2CppObject* self, Il2CppArray* buffer);                    // 循环发满 → 总数
int32_t  icall_socket_send_offset(Il2CppObject* self, Il2CppArray* buffer, int32_t offset, int32_t count);
int32_t  icall_socket_receive(Il2CppObject* self, Il2CppArray* buffer);                 // 单次 recv → 实际数
int32_t  icall_socket_receive_offset(Il2CppObject* self, Il2CppArray* buffer, int32_t offset, int32_t count);
void     icall_socket_close(Il2CppObject* self);                                        // 幂等

// T2（服务端 + 地址重载）
void          icall_socket_connect_ip(Il2CppObject* self, Il2CppObject* ipAddress, int32_t port);
void          icall_socket_connect_endpoint(Il2CppObject* self, Il2CppObject* endPoint);
void          icall_socket_bind(Il2CppObject* self, Il2CppObject* endPoint);
void          icall_socket_listen(Il2CppObject* self, int32_t backlog);
Il2CppObject* icall_socket_accept(Il2CppObject* self);   // → 新 Socket 对象（fd 继承），失败 → SocketException

} } // namespace il2cpp::runtime
```

参数校验契约（全部 icall 共守）：`self` 的 fd == -1 → InvalidOperationException（§1.4-6）；
buffer 非数组对象 → ArgumentException（arrayext2.cpp RequireArrayExt2 同款守卫）；
offset/count 越界 → ArgumentOutOfRangeException（先读 buffer->length）。

### 4.3 il2cpp_netip.h（T2）

```cpp
Il2CppObject* icall_ip_parse(Il2CppString* text);             // 静态；畸形 → FormatException
Il2CppString* icall_ip_to_string(Il2CppObject* self);         // 点分十进制
Il2CppArray*  icall_ip_get_address_bytes(Il2CppObject* self); // byte[]（il2cpp_array_new(nullptr,1,false,4)）
int32_t       icall_ip_get_address_family(Il2CppObject* self); // 恒 2（InterNetwork）
Il2CppObject* icall_ip_endpoint_ctor(Il2CppObject* self, Il2CppObject* address, int32_t port);
Il2CppObject* icall_ip_endpoint_get_address(Il2CppObject* self);
int32_t       icall_ip_endpoint_get_port(Il2CppObject* self);
Il2CppArray*  icall_dns_get_host_addresses(Il2CppString* host); // IPAddress[]（plat_net_resolve）；失败 → SocketException
```

### 4.4 il2cpp_nethttp.h（T3）

```cpp
Il2CppObject* icall_web_request_create(Il2CppString* url);     // 静态 → HttpWebRequest 对象；畸形 URL → FormatException
Il2CppString* icall_web_request_get_method(Il2CppObject* self);
void          icall_web_request_set_method(Il2CppObject* self, Il2CppString* method);
Il2CppObject* icall_web_request_get_response(Il2CppObject* self); // → HttpWebResponse；4xx/5xx → WebException；
                                                                  // https://、chunked、Method!="GET" → NotSupported
int32_t       icall_web_response_get_status_code(Il2CppObject* self);
Il2CppString* icall_web_response_get_status_description(Il2CppObject* self);
void          icall_web_client_ctor(Il2CppObject* self);        // no-op
Il2CppString* icall_web_client_download_string(Il2CppObject* self, Il2CppString* url);     // GET → UTF-8 string
Il2CppArray*  icall_web_client_download_data(Il2CppObject* self, Il2CppString* url);       // GET → byte[]
Il2CppString* icall_web_client_upload_string(Il2CppObject* self, Il2CppString* url, Il2CppString* data); // POST
Il2CppString* icall_web_client_upload_string_method(Il2CppObject* self, Il2CppString* url, Il2CppString* method, Il2CppString* data);
Il2CppArray*  icall_web_client_upload_data(Il2CppObject* self, Il2CppString* url, Il2CppArray* data);       // POST
Il2CppArray*  icall_web_client_upload_data_method(Il2CppObject* self, Il2CppString* url, Il2CppString* method, Il2CppArray* data);
```

### 4.5 接线建议（MethodMap，owner 分派）

| owner 全名 | 文件 | 说明 |
|---|---|---|
| `System.Net.Sockets.Socket` | `MethodMap.NetSocket.cs` | ctor（3 枚举参数 cast `"i"`）、Connect/Send/Receive/Close/Bind/Listen/Accept；返回 Socket 的成员 `ReturnsRef=true` + `returnTypeName:"System.Net.Sockets.Socket"` |
| `System.Net.IPAddress` / `System.Net.IPEndPoint` / `System.Net.Dns` | `MethodMap.NetIP.cs` | Parse（静态，returnsRef）、ToString（`ReturnsRef=true`）、GetAddressBytes（`returnsRef` + `returnTypeName:"System.Byte[]"`）、get_*、ctor；Dns.GetHostAddresses（returnsRef + `"System.Net.IPAddress[]"`） |
| `System.Net.WebClient` / `System.Net.WebRequest` / `System.Net.HttpWebResponse` | `MethodMap.NetHttp.cs` | DownloadString/UploadString 等返回 string/byte[] 的 `ReturnsRef=true`；WebRequest.Create 返回 `"System.Net.HttpWebRequest"`；GetResponse 返回 `"System.Net.HttpWebResponse"` |

- 实例方法 receiver 即 self：`I("il2cpp::runtime::icall_socket_connect_host", ...)` 形态
  参照 MethodMap.Version.cs:43-76（`new[] { "", "i", "i" }` 首槽 = receiver）。
- 签名 byte 解码与 cast 码细节以实现时核对 `MethodRefResolver.cs:12-55`（I() 助手）为准。
- 未接线成员（Begin*/Async/属性面）→ NotSupported（loud，§2.5 约定）。

---

## 5. 任务卡

通用：runtime 文件 CMake GLOB 自动发现（src/*.cpp、tests/runtime/test_*.cpp 无需改
CMakeLists）；**唯一例外：Windows 需链接 ws2_32**——src/runtime/CMakeLists.txt 加
`if(WIN32) target_link_libraries(il2cpp_runtime ws2_32)`（POSIX sockets 在 libc，零改动）。
测试用 Expect + g_failures + 非零退出码约定；头文件带 doc 块。

### T0（前置使能，30 分钟）：byte[] newarr

- **文件域**：[编辑] `src/il2cppc/Emit/CodeGenerator.cs`（`ArrayElementType` :2361 增
  `"System.Byte" → (1, false)`）；[写] `tests/unit/` byte 数组发射测试（newarr/ldelem.u1/
  stelem 宽度）；[写] `tests/cases/bytearr/`（net48 双跑：new byte[n] + 填充 + 逐元素打印）。
- **注意**：CodeGenerator.cs 有并行代理在途前科（discover.md:8）——动手前 git status/mtime
  检查，冲突则等。
- **验证**：dotnet build 0 err → dotnet test 全绿 → e2e bytearr 双跑逐字节一致。
- **理由**：T1 的 Send/Receive 与 T3 的 Upload/DownloadData 全部依赖用户构造 byte[]；
  无 T0，T1/T3 的托管面不可测（§1.3）。

### T1（进行中，60 分钟）：Socket 基础（客户端 TCP + 连接式 UDP）

- **文件域**：[写] `src/runtime/include/internal/platform.h` 第 9 域（§2.2）+ [编辑]
  `platform_win.cpp`（WSAStartup lazy once_flag + WinSock2）+ [编辑] `platform_posix.cpp`
  （BSD + getaddrinfo）+ [编辑] `src/runtime/CMakeLists.txt`（ws2_32）；[写]
  `src/runtime/include/internal/il2cpp_netsocket.h` + `src/runtime/src/netsocket.cpp`
  （`System_Net_Sockets_Socket_class` GLOBAL + `il2cpp_socket_class()` lazy register +
  T1 全部 icall：ctor/connect_host/send×2/receive×2/close）；[写]
  `src/il2cppc/Emit/MethodMap.NetSocket.cs`（owner `System.Net.Sockets.Socket`）；
  [写] `tests/runtime/test_netsocket.cpp`。
- **测试要点**（原生 fixture 服务器经 `plat_net_*` 直建，客户端走托管 icall 路径）：
  - TCP 回环 echo：connect 127.0.0.1（固定端口 45xxx）→ send → recv 往返；
  - send 循环发满断言（长 payload 分多次 send 的服务器端收齐）；
  - receive 单次语义（服务器分片发，客户端首 recv 可 < 缓冲区长度）；
  - Close 幂等 + Close 后 send → InvalidOperationException（§1.4-6）；
  - 连接拒绝（未监听端口）→ SocketException 链断言（catch SocketException 命中）；
  - null host/buffer → ArgumentNullException；port 越界 → AOOoR；
  - 连接式 UDP 回环 echo（plat_net_* 服务器端 recv+send 原样返回）；
  - **超时保护**：全部阻塞调用配 SO_RCVTIMEO（测试自设，30s 上限）；
  - 平台层单测：plat_net_resolve("localhost") 含 127.0.0.1；plat_net_connect 失败返回
    false（不抛——层契约 platform.h:21-30）。
- **验证**：mingw32-make 重建 runtime → test_netsocket.exe 全绿 → 全量 runtime 回归 →
  dotnet build + dotnet test 全绿。

### T2（60 分钟）：IPAddress / IPEndPoint / Dns + 服务端接线

- **文件域**：[写] `src/runtime/include/internal/il2cpp_netip.h` + `src/runtime/src/netip.cpp`
  （IPAddress/IPEndPoint 描述符 + lazy register + §4.3 icall；`System_Net_IPAddress_class`
  baseClass=&System_Object_class）；[编辑] `netsocket.cpp`（+icall_socket_connect_ip/
  connect_endpoint/bind/listen/accept，§4.2 T2 组）；[编辑] `MethodMap.NetSocket.cs`
  （+服务端与地址重载映射）；[写] `tests/runtime/test_netip.cpp`（Parse/ToString/
  GetAddressBytes 往返矩阵 + 畸形 Parse → FormatException + Dns localhost/127.0.0.1 解析 +
  IPv6 字面量 Parse → FormatException 分歧断言）；[编辑] `tests/runtime/test_netsocket.cpp`
  （+托管 Bind/Listen/Accept 回环 echo：Accept 返回新 Socket 对象 + isinst 断言 +
  Connect(IPAddress,int) 与 Connect(IPEndPoint) 路径）；[写] `tests/cases/netsocket/` e2e
  （回环回显，输出固定；net48 双跑）。
- **harness 配套**：[编辑] `scripts/run-tests.ps1`：fixture 服务器支持——e2e 运行前起
  PS7 `System.Net.Sockets.TcpListener` 线程（固定 45xxx 端口回显 / 固定响应），结束回收。
  netsocket 与 nethc 共用；服务器输出固定（确定性，e2e 用例规范）。
- **测试要点**：`(HttpWebResponse)` 链式 castclass 正确性不在本卡（HTTP 在 T3），但
  Socket 对象的 isinst/castclass（Socket → object）断言 baseClass 链；
  IPEndPoint 往返（address+port 经 Bind/connect_endpoint 复用）；
  Dns.GetHostAddresses 返回数组的 isRefElem/elemSize 正确（object 数组）。

### T3（90 分钟）：HTTP 客户端（GET/POST）

- **文件域**：[写] `src/runtime/include/internal/il2cpp_nethttp.h` + `src/runtime/src/nethttp.cpp`
  （URL 解析 + 请求构建 + 响应三段解析 §2.4 + WebClient/WebRequest/HttpWebResponse 描述符
  族（baseClass 链 §2.3）+ §4.4 icall + WebException 工厂）；[写]
  `src/il2cppc/Emit/MethodMap.NetHttp.cs`；[写] `tests/runtime/test_nethttp.cpp`（原生
  plat_net_* 回环 HTTP fixture：固定 200 响应 / POST 回显 / 404 / chunked 拒绝 / 无
  Content-Length 读至 EOF；超时保护）；[写] `tests/cases/nethc/` e2e（harness PS7 fixture
  服务器；net48 双跑逐字节一致）+ GAPS.md。
- **测试要点**：
  - GET：DownloadString 命中固定体；DownloadData 二进制往返；状态行解析（200/404）；
  - POST：UploadString/UploadData 服务器端回显体一致（Content-Length 正确）；
  - UploadString(url, "PUT", data) 方法重载 → 请求行方法正确；
  - 404/500 → WebException 链断言（catch WebException 命中，§2.5）；
  - 3xx → 作为响应返回（StatusCode 302 可读，§1.4-2 分歧断言）；
  - chunked 响应 → NotSupported（loud）；https:// → NotSupported；
  - 畸形 URL → FormatException；null url/data → ArgumentNullException；
  - HttpWebRequest：Create → GetResponse 200；Method="POST" → NotSupported（§1.4-7）；
    `(HttpWebResponse)` castclass 沿 baseClass 链成功（§2.3）；
  - 无 Content-Length 且连接关闭 → 读至 EOF 完整体（fixture 服务器 Connection: close）；
  - 长响应体（>64KB）跨多次 recv 正确拼接。
- **验证**：runtime 全绿 + 单元全绿 + nethc e2e 双跑（含 net48 参考运行，固定服务器输出）。

### T4（可选，不在本 3 卡）：TcpClient / TcpListener（worklist T213 映射）

- 设计简述（排期时细化）：`TcpClient { obj; int32_t fd; }`（ctor 无参 + Connect(host,port)）；
  `TcpListener { obj; int32_t fd; }`（ctor(IPAddress,port) + Start() + AcceptTcpClient →
  新 TcpClient）；`NetworkStream { obj; int32_t fd; }`（Read/Write(byte[],int,int) 映射
  recv/send）。依赖：T0（byte[]）、T2（IPAddress/IPEndPoint）、T1 平台域。
- 命名沿用 worklist T213：`il2cpp_netsocket.h` 内扩展 + `tests/cases/netsocket/` 增补。

---

## 6. 参考

1. **.NET Core Socket.cs（不可用，已核查）**：`reference/netcore/src/libraries/` sparse 仅含
   `System.Console / System.Private.CoreLib / System.Private.Uri /
   System.Text.RegularExpressions`——**无 System.Net.Sockets**（[BASH] Get-ChildItem 实测）；
   `reference/coreclr/src/libraries/` 为空目录；`reference/corert/` 仅 ILCompiler 族（含
   ILCompiler.CppCodeGen，无 BCL sockets 源码）。→ 本文档以**net48 本机运行时实测**为行为
   锚点（e2e 双跑惯例），与 regex 域 §6 提示同款（"实现时可用本机 .NET Framework 4.8 实测
   复核"）。可检索的公开参考（实现时按需）：referencesource `System/net/System/Net/
   Sockets/Socket.cs`（net48 同宗源码）。
2. **平台层**：docs/design/cross-platform-design.md §2（八域收敛策略——本域第 9 域是其
   自然延伸；§2.3 迁移顺序与本域独立，互不阻塞）。
3. **对象闭环先例**：discover.md:22-24（BclAllocExpr 非必需）、MethodMap.Version.cs:11-16
   （newobj fallback + GLOBAL 描述符）、versionuri.cpp（lazy register 形态）。
4. **既有 worklist 卡**：docs/plans/taskcards-128.md T212（nethttp 命名/文件域）、T213
   （netsocket 命名/文件域）——命名沿用，范围以本文档 §1 为准（T212 的 "WinSock2" 措辞
   升级为平台层第 9 域，见 §2.2；T213 的 TcpClient/Listener 移入 T4 可选）。
5. **接线先例**：MethodRefResolver.cs（BclResolveCore owner 分派）、MethodMap.Version.cs
   （I() 助手形态）、MethodMap.Base64.cs（byte[] 参数/返回接线先例——`"a"` cast 与
   ReturnsRef）。

> 实现提示：SocketException/WebException 的 catch 语义（§2.5 链）与 Receive 单次语义
> （§1.1）在实现时用 net48 实测复核；e2e 用例一律**不断言异常消息文本**（§1.4-5）。

---

## 附：设计决策摘要

1. 分层：托管面 → icall 域（netsocket/netip/nethttp）→ 平台层**第 9 域** plat_net_*
   （WinSock2 / BSD sockets 差异收敛平台层，生成代码零平台假设保持）
2. 对象化：GLOBAL 描述符族 + newobj fallback（Version 先例，无需 BclAllocExpr）；
   baseClass 链必须正确（castclass 语义）
3. 阶段 1 同步阻塞；Task/async/BeginXxx 一律 loud NotSupported（转译器无 async 支持）
4. HTTP = 状态行/头/体三段解析简化；Content-Length 或读至 EOF；chunked/https loud 拒绝；
   不跟随重定向；编码固定 UTF-8
5. 异常：新增 SocketException/WebException 工厂（链含 Win32Exception/InvalidOperationException
   段对齐 net48 继承）；e2e 只断言类型名不断言消息
6. 硬前置 T0：newarr System.Byte → (1,false)（当前 loud 拒绝，CodeGenerator.cs:2361）
7. 命名沿用 worklist T212/T213（il2cpp_netsocket.h / il2cpp_nethttp.h / MethodMap.NetSocket.cs /
   MethodMap.NetHttp.cs / cases nethc、netsocket）；TcpClient/Listener → 可选 T4

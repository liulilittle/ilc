# .NET Framework 4.8 InternalCall 全量盘点（ILC 接线审计）

> 审计代理（只读）| 日期: 2026-08-06 | 目标: 枚举 net48 全部 InternalCall 并与 ILC 66 域 MethodMap 接线对比
> 双通道验证: PowerShell 反射（Windows PowerShell 5.1 / .NET Framework 4.8 宿主）+ System.Reflection.Metadata 元数据层（PS7）逐行一致

## 0. 审计方法学
- 反射通道: 枚举全部类型（含非公开）的全部方法+构造器，过滤 MethodImplAttributes.InternalCall (0x1000)
- 元数据通道: PEReader + MetadataReader 直接解析 MethodDef.ImplAttributes 位，独立于运行时反射
- 双通道一致性: 910/910 一致（仅 System.StubHelpers.StubHelpers 2 个内部方法可见性差异，不影响结论）
- ILC 对比键: (声明类型全名, 方法名) vs MethodRefResolver.cs BclResolveCore dispatch + 66 个 MethodMap.*.cs case 集
- 可见性口径: public API 面 = 类型 public 且方法 public（用户 IL 可直接 call 的面）；internal/private = CLR 内部调用，由 public 托管包装承接，ILC 无需直接接线

## 1. 程序集枚举结果

| 程序集 | 类型数 | InternalCall 数 | 说明 |
|---|---|---|---|
| mscorlib.dll | 3356 | 908 | net48 icall 绝对主体（含全部构造器重载） |
| System.dll | 2364 | 2 | 仅 COM 内部 ctor（InternetSecurityManager/MSAdminBase，非用户可达） |
| System.Core.dll | 1175 | 0 | 元数据层确认 |
| System.Xml.dll | 1249 | 0 | 元数据层确认 |
| System.Configuration.dll | 222 | 0 | 元数据层确认 |
| System.Data.dll | 1139 | 0 | byte[] 加载+AssemblyResolve 确认 |
| System.Numerics.dll | 26 | 0 | 元数据层确认 |
| **合计** | | **910** | |

## 2. 全量清单（910 项，按程序集分组）

状态: ✅ = ILC 域+方法均已接; 🟡 = 域存在但该 icall 名未接; ❌ = 无 dispatch 域; — = 内部 icall（由 public 包装承接，不需直接接线）

### 2.1 mscorlib.dll（908 项）

| 类型 | 方法 | 静态 | 可见性 | 返回/参数 | 状态 | 域 | 备注 |
|---|---|---|---|---|---|---|---|
| Microsoft.Win32.OAVariantLib | ChangeTypeEx | static | nonpublic/nonpublic | System.Void (System.Variant& result, System.Variant& source, System.Int32 lcid, System.IntPtr typeHandle, System.Int32 cvType, System.Int16 flags) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | GetHashFromAssemblyFile | instance | nonpublic/public | System.Int32 (System.String pszFilePath, System.Int32& piHashAlg, System.Byte[] pbHash, System.Int32 cchHash, System.Int32& pchHash) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | GetHashFromAssemblyFileW | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.Int32& piHashAlg, System.Byte[] pbHash, System.Int32 cchHash, System.Int32& pchHash) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | GetHashFromBlob | instance | nonpublic/public | System.Int32 (System.IntPtr pbBlob, System.Int32 cchBlob, System.Int32& piHashAlg, System.Byte[] pbHash, System.Int32 cchHash, System.Int32& pchHash) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | GetHashFromFile | instance | nonpublic/public | System.Int32 (System.String pszFilePath, System.Int32& piHashAlg, System.Byte[] pbHash, System.Int32 cchHash, System.Int32& pchHash) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | GetHashFromFileW | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.Int32& piHashAlg, System.Byte[] pbHash, System.Int32 cchHash, System.Int32& pchHash) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | GetHashFromHandle | instance | nonpublic/public | System.Int32 (System.IntPtr hFile, System.Int32& piHashAlg, System.Byte[] pbHash, System.Int32 cchHash, System.Int32& pchHash) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameCompareAssemblies | instance | nonpublic/public | System.Int32 (System.String pwzAssembly1, System.String pwzAssembly2, System.Int32& dwResult) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameFreeBuffer | instance | nonpublic/public | System.Int32 (System.IntPtr pbMemory) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameGetBlob | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.Byte[] pbBlob, System.Int32& pcbBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameGetBlobFromImage | instance | nonpublic/public | System.Int32 (System.IntPtr pbBase, System.Int32 dwLength, System.Byte[] pbBlob, System.Int32& pcbBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameGetPublicKey | instance | nonpublic/public | System.Int32 (System.String pwzKeyContainer, System.IntPtr pbKeyBlob, System.Int32 cbKeyBlob, System.IntPtr& ppbPublicKeyBlob, System.Int32& pcbPublicKeyBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameHashSize | instance | nonpublic/public | System.Int32 (System.Int32 ulHashAlg, System.Int32& cbSize) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameKeyDelete | instance | nonpublic/public | System.Int32 (System.String pwzKeyContainer) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameKeyGen | instance | nonpublic/public | System.Int32 (System.String pwzKeyContainer, System.Int32 dwFlags, System.IntPtr& ppbKeyBlob, System.Int32& pcbKeyBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameKeyGenEx | instance | nonpublic/public | System.Int32 (System.String pwzKeyContainer, System.Int32 dwFlags, System.Int32 dwKeySize, System.IntPtr& ppbKeyBlob, System.Int32& pcbKeyBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameKeyInstall | instance | nonpublic/public | System.Int32 (System.String pwzKeyContainer, System.IntPtr pbKeyBlob, System.Int32 cbKeyBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameSignatureGeneration | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.String pwzKeyContainer, System.IntPtr pbKeyBlob, System.Int32 cbKeyBlob, System.IntPtr ppbSignatureBlob, System.Int32& pcbSignatureBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameSignatureGenerationEx | instance | nonpublic/public | System.Int32 (System.String wszFilePath, System.String wszKeyContainer, System.IntPtr pbKeyBlob, System.Int32 cbKeyBlob, System.IntPtr ppbSignatureBlob, System.Int32& pcbSignatureBlob, System.Int32 dwFlags) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameSignatureSize | instance | nonpublic/public | System.Int32 (System.IntPtr pbPublicKeyBlob, System.Int32 cbPublicKeyBlob, System.Int32& pcbSize) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameSignatureVerification | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.Int32 dwInFlags, System.Int32& dwOutFlags) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameSignatureVerificationEx | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.Boolean fForceVerification, System.Boolean& fWasVerified) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameSignatureVerificationFromImage | instance | nonpublic/public | System.Int32 (System.IntPtr pbBase, System.Int32 dwLength, System.Int32 dwInFlags, System.Int32& dwOutFlags) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameTokenFromAssembly | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.IntPtr& ppbStrongNameToken, System.Int32& pcbStrongNameToken) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameTokenFromAssemblyEx | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.IntPtr& ppbStrongNameToken, System.Int32& pcbStrongNameToken, System.IntPtr& ppbPublicKeyBlob, System.Int32& pcbPublicKeyBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongNameUsingIntPtr | StrongNameTokenFromPublicKey | instance | nonpublic/public | System.Int32 (System.IntPtr pbPublicKeyBlob, System.Int32 cbPublicKeyBlob, System.IntPtr& ppbStrongNameToken, System.Int32& pcbStrongNameToken) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | GetHashFromAssemblyFile | instance | nonpublic/public | System.Int32 (System.String pszFilePath, System.Int32& piHashAlg, System.Byte[] pbHash, System.Int32 cchHash, System.Int32& pchHash) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | GetHashFromAssemblyFileW | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.Int32& piHashAlg, System.Byte[] pbHash, System.Int32 cchHash, System.Int32& pchHash) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | GetHashFromBlob | instance | nonpublic/public | System.Int32 (System.IntPtr pbBlob, System.Int32 cchBlob, System.Int32& piHashAlg, System.Byte[] pbHash, System.Int32 cchHash, System.Int32& pchHash) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | GetHashFromFile | instance | nonpublic/public | System.Int32 (System.String pszFilePath, System.Int32& piHashAlg, System.Byte[] pbHash, System.Int32 cchHash, System.Int32& pchHash) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | GetHashFromFileW | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.Int32& piHashAlg, System.Byte[] pbHash, System.Int32 cchHash, System.Int32& pchHash) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | GetHashFromHandle | instance | nonpublic/public | System.Int32 (System.IntPtr hFile, System.Int32& piHashAlg, System.Byte[] pbHash, System.Int32 cchHash, System.Int32& pchHash) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameCompareAssemblies | instance | nonpublic/public | System.Int32 (System.String pwzAssembly1, System.String pwzAssembly2, System.Int32& dwResult) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameFreeBuffer | instance | nonpublic/public | System.Int32 (System.IntPtr pbMemory) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameGetBlob | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.Byte[] pbBlob, System.Int32& pcbBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameGetBlobFromImage | instance | nonpublic/public | System.Int32 (System.IntPtr pbBase, System.Int32 dwLength, System.Byte[] pbBlob, System.Int32& pcbBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameGetPublicKey | instance | nonpublic/public | System.Int32 (System.String pwzKeyContainer, System.Byte[] pbKeyBlob, System.Int32 cbKeyBlob, System.IntPtr& ppbPublicKeyBlob, System.Int32& pcbPublicKeyBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameHashSize | instance | nonpublic/public | System.Int32 (System.Int32 ulHashAlg, System.Int32& cbSize) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameKeyDelete | instance | nonpublic/public | System.Int32 (System.String pwzKeyContainer) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameKeyGen | instance | nonpublic/public | System.Int32 (System.String pwzKeyContainer, System.Int32 dwFlags, System.IntPtr& ppbKeyBlob, System.Int32& pcbKeyBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameKeyGenEx | instance | nonpublic/public | System.Int32 (System.String pwzKeyContainer, System.Int32 dwFlags, System.Int32 dwKeySize, System.IntPtr& ppbKeyBlob, System.Int32& pcbKeyBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameKeyInstall | instance | nonpublic/public | System.Int32 (System.String pwzKeyContainer, System.Byte[] pbKeyBlob, System.Int32 cbKeyBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameSignatureGeneration | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.String pwzKeyContainer, System.Byte[] pbKeyBlob, System.Int32 cbKeyBlob, System.IntPtr ppbSignatureBlob, System.Int32& pcbSignatureBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameSignatureGenerationEx | instance | nonpublic/public | System.Int32 (System.String wszFilePath, System.String wszKeyContainer, System.Byte[] pbKeyBlob, System.Int32 cbKeyBlob, System.IntPtr ppbSignatureBlob, System.Int32& pcbSignatureBlob, System.Int32 dwFlags) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameSignatureSize | instance | nonpublic/public | System.Int32 (System.Byte[] pbPublicKeyBlob, System.Int32 cbPublicKeyBlob, System.Int32& pcbSize) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameSignatureVerification | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.Int32 dwInFlags, System.Int32& dwOutFlags) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameSignatureVerificationEx | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.Boolean fForceVerification, System.Boolean& fWasVerified) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameSignatureVerificationFromImage | instance | nonpublic/public | System.Int32 (System.IntPtr pbBase, System.Int32 dwLength, System.Int32 dwInFlags, System.Int32& dwOutFlags) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameTokenFromAssembly | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.IntPtr& ppbStrongNameToken, System.Int32& pcbStrongNameToken) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameTokenFromAssemblyEx | instance | nonpublic/public | System.Int32 (System.String pwzFilePath, System.IntPtr& ppbStrongNameToken, System.Int32& pcbStrongNameToken, System.IntPtr& ppbPublicKeyBlob, System.Int32& pcbPublicKeyBlob) | ❌ | - | 内部 icall（public 包装承接） |
| Microsoft.Runtime.Hosting.IClrStrongName | StrongNameTokenFromPublicKey | instance | nonpublic/public | System.Int32 (System.Byte[] pbPublicKeyBlob, System.Int32 cbPublicKeyBlob, System.IntPtr& ppbStrongNameToken, System.Int32& pcbStrongNameToken) | ❌ | - | 内部 icall（public 包装承接） |
| Windows.Foundation.Diagnostics.TracingStatusChangedEventArgs | get_Enabled | instance | nonpublic/public | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| Windows.Foundation.Diagnostics.TracingStatusChangedEventArgs | get_TraceLevel | instance | nonpublic/public | Windows.Foundation.Diagnostics.CausalityTraceLevel () | ❌ | - | 内部 icall（public 包装承接） |
| Windows.Foundation.Diagnostics.TracingStatusChangedEventArgs | .ctor | instance | nonpublic/public |  () | ❌ | - | 内部 icall（public 包装承接） |
| System.Object | GetType | instance | public/public | System.Type () | ✅ | MethodMap.Object |  |
| System.Object | MemberwiseClone | instance | public/nonpublic | System.Object () | 🟡 | MethodMap.Object | 内部 icall（public 包装承接） |
| System.Array | get_LongLength | instance | public/public | System.Int64 () | 🟡 | MethodMap.Array+MethodMap.ArraySort |  |
| System.Array | GetDataPtrOffsetInternal | instance | public/nonpublic | System.Int32 () | 🟡 | MethodMap.Array+MethodMap.ArraySort | 内部 icall（public 包装承接） |
| System.Array | TrySZLastIndexOf | static | public/nonpublic | System.Boolean (System.Array sourceArray, System.Int32 sourceIndex, System.Int32 count, System.Object value, System.Int32& retVal) | 🟡 | MethodMap.Array+MethodMap.ArraySort | 内部 icall（public 包装承接） |
| System.Array | Initialize | instance | public/public | System.Void () | 🟡 | MethodMap.Array+MethodMap.ArraySort |  |
| System.Array | InternalCreate | static | public/nonpublic | System.Array (System.Void* elementType, System.Int32 rank, System.Int32* pLengths, System.Int32* pLowerBounds) | 🟡 | MethodMap.Array+MethodMap.ArraySort | 内部 icall（public 包装承接） |
| System.Array | Copy | static | public/nonpublic | System.Void (System.Array sourceArray, System.Int32 sourceIndex, System.Array destinationArray, System.Int32 destinationIndex, System.Int32 length, System.Boolean reliable) | ✅ | MethodMap.Array | 内部 icall（public 包装承接） |
| System.Array | Clear | static | public/public | System.Void (System.Array array, System.Int32 index, System.Int32 length) | ✅ | MethodMap.Array |  |
| System.Array | InternalGetReference | instance | public/nonpublic | System.Void (System.Void* elemRef, System.Int32 rank, System.Int32* pIndices) | 🟡 | MethodMap.Array+MethodMap.ArraySort | 内部 icall（public 包装承接） |
| System.Array | InternalSetValue | static | public/nonpublic | System.Void (System.Void* target, System.Object value) | 🟡 | MethodMap.Array+MethodMap.ArraySort | 内部 icall（public 包装承接） |
| System.Array | get_Length | instance | public/public | System.Int32 () | ✅ | MethodMap.Array |  |
| System.Array | GetLength | instance | public/public | System.Int32 (System.Int32 dimension) | ✅ | MethodMap.Array |  |
| System.Array | get_Rank | instance | public/public | System.Int32 () | 🟡 | MethodMap.Array+MethodMap.ArraySort |  |
| System.Array | GetUpperBound | instance | public/public | System.Int32 (System.Int32 dimension) | ✅ | MethodMap.Array |  |
| System.Array | GetLowerBound | instance | public/public | System.Int32 (System.Int32 dimension) | 🟡 | MethodMap.Array+MethodMap.ArraySort |  |
| System.Array | TrySZBinarySearch | static | public/nonpublic | System.Boolean (System.Array sourceArray, System.Int32 sourceIndex, System.Int32 count, System.Object value, System.Int32& retVal) | 🟡 | MethodMap.Array+MethodMap.ArraySort | 内部 icall（public 包装承接） |
| System.Array | TrySZIndexOf | static | public/nonpublic | System.Boolean (System.Array sourceArray, System.Int32 sourceIndex, System.Int32 count, System.Object value, System.Int32& retVal) | 🟡 | MethodMap.Array+MethodMap.ArraySort | 内部 icall（public 包装承接） |
| System.Array | TrySZReverse | static | public/nonpublic | System.Boolean (System.Array array, System.Int32 index, System.Int32 count) | 🟡 | MethodMap.Array+MethodMap.ArraySort | 内部 icall（public 包装承接） |
| System.Array | TrySZSort | static | public/nonpublic | System.Boolean (System.Array keys, System.Array items, System.Int32 left, System.Int32 right) | 🟡 | MethodMap.Array+MethodMap.ArraySort | 内部 icall（public 包装承接） |
| System.String | get_Chars | instance | public/public | System.Char (System.Int32 index) | ✅ | MethodMap.String |  |
| System.String | InternalMarvin32HashString | static | public/nonpublic | System.Int32 (System.String s, System.Int32 strLen, System.Int64 additionalEntropy) | 🟡 | MethodMap.String+MethodMap.StringExt3 | 内部 icall（public 包装承接） |
| System.String | get_Length | instance | public/public | System.Int32 () | ✅ | MethodMap.String |  |
| System.String | FastAllocateString | static | public/nonpublic | System.String (System.Int32 length) | 🟡 | MethodMap.String+MethodMap.StringExt3 | 内部 icall（public 包装承接） |
| System.String | IsFastSort | instance | public/nonpublic | System.Boolean () | 🟡 | MethodMap.String+MethodMap.StringExt3 | 内部 icall（public 包装承接） |
| System.String | SetTrailByte | instance | public/nonpublic | System.Void (System.Byte data) | 🟡 | MethodMap.String+MethodMap.StringExt3 | 内部 icall（public 包装承接） |
| System.String | nativeCompareOrdinalEx | static | public/nonpublic | System.Int32 (System.String strA, System.Int32 indexA, System.String strB, System.Int32 indexB, System.Int32 count) | 🟡 | MethodMap.String+MethodMap.StringExt3 | 内部 icall（public 包装承接） |
| System.String | nativeCompareOrdinalIgnoreCaseWC | static | public/nonpublic | System.Int32 (System.String strA, System.SByte* strBBytes) | 🟡 | MethodMap.String+MethodMap.StringExt3 | 内部 icall（public 包装承接） |
| System.String | IndexOf | instance | public/public | System.Int32 (System.Char value, System.Int32 startIndex, System.Int32 count) | ✅ | MethodMap.String |  |
| System.String | IndexOfAny | instance | public/public | System.Int32 (System.Char[] anyOf, System.Int32 startIndex, System.Int32 count) | 🟡 | MethodMap.String+MethodMap.StringExt3 |  |
| System.String | LastIndexOf | instance | public/public | System.Int32 (System.Char value, System.Int32 startIndex, System.Int32 count) | ✅ | MethodMap.String |  |
| System.String | LastIndexOfAny | instance | public/public | System.Int32 (System.Char[] anyOf, System.Int32 startIndex, System.Int32 count) | 🟡 | MethodMap.String+MethodMap.StringExt3 |  |
| System.String | PadHelper | instance | public/nonpublic | System.String (System.Int32 totalWidth, System.Char paddingChar, System.Boolean isRightPadded) | 🟡 | MethodMap.String+MethodMap.StringExt3 | 内部 icall（public 包装承接） |
| System.String | ReplaceInternal | instance | public/nonpublic | System.String (System.Char oldChar, System.Char newChar) | 🟡 | MethodMap.String+MethodMap.StringExt3 | 内部 icall（public 包装承接） |
| System.String | ReplaceInternal | instance | public/nonpublic | System.String (System.String oldValue, System.String newValue) | 🟡 | MethodMap.String+MethodMap.StringExt3 | 内部 icall（public 包装承接） |
| System.String | IsAscii | instance | public/nonpublic | System.Boolean () | 🟡 | MethodMap.String+MethodMap.StringExt3 | 内部 icall（public 包装承接） |
| System.String | TryGetTrailByte | instance | public/nonpublic | System.Boolean (System.Byte& data) | 🟡 | MethodMap.String+MethodMap.StringExt3 | 内部 icall（public 包装承接） |
| System.String | .ctor | instance | public/public |  (System.Char* value) | 🟡 | MethodMap.String+MethodMap.StringExt3 |  |
| System.String | .ctor | instance | public/public |  (System.Char* value, System.Int32 startIndex, System.Int32 length) | 🟡 | MethodMap.String+MethodMap.StringExt3 |  |
| System.String | .ctor | instance | public/public |  (System.SByte* value) | 🟡 | MethodMap.String+MethodMap.StringExt3 |  |
| System.String | .ctor | instance | public/public |  (System.SByte* value, System.Int32 startIndex, System.Int32 length) | 🟡 | MethodMap.String+MethodMap.StringExt3 |  |
| System.String | .ctor | instance | public/public |  (System.SByte* value, System.Int32 startIndex, System.Int32 length, System.Text.Encoding enc) | 🟡 | MethodMap.String+MethodMap.StringExt3 |  |
| System.String | .ctor | instance | public/public |  (System.Char[] value, System.Int32 startIndex, System.Int32 length) | 🟡 | MethodMap.String+MethodMap.StringExt3 |  |
| System.String | .ctor | instance | public/public |  (System.Char[] value) | 🟡 | MethodMap.String+MethodMap.StringExt3 |  |
| System.String | .ctor | instance | public/public |  (System.Char c, System.Int32 count) | 🟡 | MethodMap.String+MethodMap.StringExt3 |  |
| System.Exception | GetMethodFromStackTrace | static | public/nonpublic | System.IRuntimeMethodInfo (System.Object stackTrace) | 🟡 | MethodMap.Exception | 内部 icall（public 包装承接） |
| System.Exception | CopyDynamicMethods | static | public/nonpublic | System.Object (System.Object currentDynamicMethods) | 🟡 | MethodMap.Exception | 内部 icall（public 包装承接） |
| System.Exception | StripFileInfo | instance | public/nonpublic | System.String (System.String stackTrace, System.Boolean isRemoteStackTrace) | 🟡 | MethodMap.Exception | 内部 icall（public 包装承接） |
| System.Exception | nIsTransient | static | public/nonpublic | System.Boolean (System.Int32 hr) | 🟡 | MethodMap.Exception | 内部 icall（public 包装承接） |
| System.Exception | IsImmutableAgileException | static | public/nonpublic | System.Boolean (System.Exception e) | 🟡 | MethodMap.Exception | 内部 icall（public 包装承接） |
| System.Exception | PrepareForForeignExceptionRaise | static | public/nonpublic | System.Void () | 🟡 | MethodMap.Exception | 内部 icall（public 包装承接） |
| System.Exception | GetStackTracesDeepCopy | static | public/nonpublic | System.Void (System.Exception exception, System.Object& currentStackTrace, System.Object& dynamicMethodArray) | 🟡 | MethodMap.Exception | 内部 icall（public 包装承接） |
| System.Exception | SaveStackTracesFromDeepCopy | static | public/nonpublic | System.Void (System.Exception exception, System.Object currentStackTrace, System.Object dynamicMethodArray) | 🟡 | MethodMap.Exception | 内部 icall（public 包装承接） |
| System.Exception | CopyStackTrace | static | public/nonpublic | System.Object (System.Object currentStackTrace) | 🟡 | MethodMap.Exception | 内部 icall（public 包装承接） |
| System.DateTime | ValidateSystemTime | static | public/nonpublic | System.Boolean (System.DateTime+FullSystemTime& time, System.Boolean localTime) | 🟡 | MethodMap.DateTime+MethodMap.TimeExt2+MethodMap.DateTimeExtra | 内部 icall（public 包装承接） |
| System.DateTime | SystemTimeToSystemFileTime | static | public/nonpublic | System.Boolean (System.DateTime+FullSystemTime& time, System.Int64& fileTime) | 🟡 | MethodMap.DateTime+MethodMap.TimeExt2+MethodMap.DateTimeExtra | 内部 icall（public 包装承接） |
| System.DateTime | GetSystemTimeAsFileTime | static | public/nonpublic | System.Int64 () | 🟡 | MethodMap.DateTime+MethodMap.TimeExt2+MethodMap.DateTimeExtra | 内部 icall（public 包装承接） |
| System.DateTime | GetSystemTimeWithLeapSecondsHandling | static | public/nonpublic | System.Void (System.DateTime+FullSystemTime& time) | 🟡 | MethodMap.DateTime+MethodMap.TimeExt2+MethodMap.DateTimeExtra | 内部 icall（public 包装承接） |
| System.DateTime | SystemFileTimeToSystemTime | static | public/nonpublic | System.Boolean (System.Int64 fileTime, System.DateTime+FullSystemTime& time) | 🟡 | MethodMap.DateTime+MethodMap.TimeExt2+MethodMap.DateTimeExtra | 内部 icall（public 包装承接） |
| System.Delegate | DelegateConstruct | instance | public/nonpublic | System.Void (System.Object target, System.IntPtr slot) | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.Delegate | GetCallStub | instance | public/nonpublic | System.IntPtr (System.IntPtr methodPtr) | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.Delegate | CompareUnmanagedFunctionPtrs | static | public/nonpublic | System.Boolean (System.Delegate d1, System.Delegate d2) | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.Delegate | BindToMethodName | instance | public/nonpublic | System.Boolean (System.Object target, System.RuntimeType methodType, System.String method, System.DelegateBindingFlags flags) | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.Delegate | BindToMethodInfo | instance | public/nonpublic | System.Boolean (System.Object target, System.IRuntimeMethodInfo method, System.RuntimeType methodType, System.DelegateBindingFlags flags) | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.Delegate | InternalAlloc | static | public/nonpublic | System.MulticastDelegate (System.RuntimeType type) | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.Delegate | InternalAllocLike | static | public/nonpublic | System.MulticastDelegate (System.Delegate d) | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.Delegate | InternalEqualTypes | static | public/nonpublic | System.Boolean (System.Object a, System.Object b) | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.Delegate | GetMulticastInvoke | instance | public/nonpublic | System.IntPtr () | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.Delegate | GetInvokeMethod | instance | public/nonpublic | System.IntPtr () | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.Delegate | FindMethodHandle | instance | public/nonpublic | System.IRuntimeMethodInfo () | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.Delegate | InternalEqualMethodHandles | static | public/nonpublic | System.Boolean (System.Delegate left, System.Delegate right) | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.Delegate | AdjustTarget | instance | public/nonpublic | System.IntPtr (System.Object target, System.IntPtr methodPtr) | 🟡 | MethodMap.Delegate | 内部 icall（public 包装承接） |
| System.BCLDebug | GetRegistryLoggingValues | static | nonpublic/nonpublic | System.Int32 (System.Boolean& loggingEnabled, System.Boolean& logToConsole, System.Int32& logLevel, System.Boolean& perfWarnings, System.Boolean& correctnessWarnings, System.Boolean& safeHandleStackTraces) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nApplyPolicy | instance | public/nonpublic | System.String (System.Reflection.AssemblyName an) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nGetAssemblies | instance | public/nonpublic | System.Reflection.Assembly[] (System.Boolean forIntrospection) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | IsUnloadingForcedFinalize | instance | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | IsFinalizingForUnload | instance | public/public | System.Boolean () | ❌ | - |  |
| System.AppDomain | GetDynamicDir | instance | public/nonpublic | System.String () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | _nExecuteAssembly | instance | public/nonpublic | System.Int32 (System.Reflection.RuntimeAssembly assembly, System.String[] args) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nGetFriendlyName | instance | public/nonpublic | System.String () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nIsDefaultAppDomainForEvidence | instance | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | IsDomainIdValid | static | public/nonpublic | System.Boolean (System.Int32 id) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | GetFusionContext | instance | public/nonpublic | System.IntPtr () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | GetSecurityDescriptor | instance | public/nonpublic | System.IntPtr () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nCreateInstance | static | public/nonpublic | System.Runtime.Remoting.ObjRef (System.String friendlyName, System.AppDomainSetup setup, System.Security.Policy.Evidence providedSecurityInfo, System.Security.Policy.Evidence creatorsSecurityInfo, System.IntPtr parentSecurityDescriptor) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nSetupFriendlyName | instance | public/nonpublic | System.Void (System.String friendlyName) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nSetDisableInterfaceCache | instance | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | UpdateLoaderOptimization | instance | public/nonpublic | System.Void (System.LoaderOptimization optimization) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | IsStringInterned | instance | public/nonpublic | System.String (System.String str) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | GetOrInternString | instance | public/nonpublic | System.String (System.String str) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nChangeSecurityPolicy | instance | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | GetId | instance | public/nonpublic | System.Int32 () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nEnableMonitoring | static | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nMonitoringIsEnabled | static | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nGetTotalProcessorTime | instance | public/nonpublic | System.Int64 () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nGetTotalAllocatedMemorySize | instance | public/nonpublic | System.Int64 () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nGetLastSurvivedMemorySize | instance | public/nonpublic | System.Int64 () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nGetLastSurvivedProcessMemorySize | static | public/nonpublic | System.Int64 () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nSetHostSecurityManagerFlags | static | public/nonpublic | System.Void (System.Security.HostSecurityManagerOptions flags) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | PublishAnonymouslyHostedDynamicMethodsAssembly | static | public/nonpublic | System.Void (System.Reflection.RuntimeAssembly assemblyHandle) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | GetDefaultDomain | static | public/nonpublic | System.AppDomain () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nCreateDomain | static | public/nonpublic | System.AppDomain (System.String friendlyName, System.AppDomainSetup setup, System.Security.Policy.Evidence providedSecurityInfo, System.Security.Policy.Evidence creatorsSecurityInfo, System.IntPtr parentSecurityDescriptor) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomain | nUnload | static | public/nonpublic | System.Void (System.Int32 domainInternal) | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomainManager | HasHost | static | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.AppDomainSetup | UpdateContextProperty | static | public/nonpublic | System.Void (System.IntPtr fusionContext, System.String key, System.Object value) | ❌ | - | 内部 icall（public 包装承接） |
| System.ArgIterator | FCallGetNextArg | instance | public/nonpublic | System.Void (System.Void* result) | ❌ | - | 内部 icall（public 包装承接） |
| System.ArgIterator | InternalGetNextArg | instance | public/nonpublic | System.Void (System.Void* result, System.RuntimeType rt) | ❌ | - | 内部 icall（public 包装承接） |
| System.ArgIterator | GetRemainingCount | instance | public/public | System.Int32 () | ❌ | - |  |
| System.ArgIterator | _GetNextArgType | instance | public/nonpublic | System.Void* () | ❌ | - | 内部 icall（public 包装承接） |
| System.ArgIterator | .ctor | instance | public/nonpublic |  (System.IntPtr arglist) | ❌ | - | 内部 icall（public 包装承接） |
| System.ArgIterator | .ctor | instance | public/nonpublic |  (System.IntPtr arglist, System.Void* ptr) | ❌ | - | 内部 icall（public 包装承接） |
| System.Buffer | IsPrimitiveTypeArray | static | public/nonpublic | System.Boolean (System.Array array) | 🟡 | MethodMap.Buffer | 内部 icall（public 包装承接） |
| System.Buffer | _GetByte | static | public/nonpublic | System.Byte (System.Array array, System.Int32 index) | 🟡 | MethodMap.Buffer | 内部 icall（public 包装承接） |
| System.Buffer | _SetByte | static | public/nonpublic | System.Void (System.Array array, System.Int32 index, System.Byte value) | 🟡 | MethodMap.Buffer | 内部 icall（public 包装承接） |
| System.Buffer | _ByteLength | static | public/nonpublic | System.Int32 (System.Array array) | 🟡 | MethodMap.Buffer | 内部 icall（public 包装承接） |
| System.Buffer | BlockCopy | static | public/public | System.Void (System.Array src, System.Int32 srcOffset, System.Array dst, System.Int32 dstOffset, System.Int32 count) | ✅ | MethodMap.Buffer |  |
| System.Buffer | InternalBlockCopy | static | public/nonpublic | System.Void (System.Array src, System.Int32 srcOffsetBytes, System.Array dst, System.Int32 dstOffsetBytes, System.Int32 byteCount) | 🟡 | MethodMap.Buffer | 内部 icall（public 包装承接） |
| System.BaseConfigHandler | RunParser | instance | nonpublic/nonpublic | System.Void (System.String fileName) | ❌ | - | 内部 icall（public 包装承接） |
| System.Currency | FCallToDecimal | static | nonpublic/nonpublic | System.Void (System.Decimal& result, System.Currency c) | ❌ | - | 内部 icall（public 包装承接） |
| System.CurrentSystemTimeZone | nativeGetTimeZoneMinuteOffset | static | nonpublic/nonpublic | System.Int32 () | ❌ | - | 内部 icall（public 包装承接） |
| System.CurrentSystemTimeZone | nativeGetDaylightName | static | nonpublic/nonpublic | System.String () | ❌ | - | 内部 icall（public 包装承接） |
| System.CurrentSystemTimeZone | nativeGetStandardName | static | nonpublic/nonpublic | System.String () | ❌ | - | 内部 icall（public 包装承接） |
| System.CurrentSystemTimeZone | nativeGetDaylightChanges | static | nonpublic/nonpublic | System.Int16[] (System.Int32 year) | ❌ | - | 内部 icall（public 包装承接） |
| System.Decimal | FCallAddSub | static | public/nonpublic | System.Void (System.Decimal& d1, System.Decimal& d2, System.Byte bSign) | 🟡 | MethodMap.Format+MethodMap.Decimal | 内部 icall（public 包装承接） |
| System.Decimal | FCallAddSubOverflowed | static | public/nonpublic | System.Void (System.Decimal& d1, System.Decimal& d2, System.Byte bSign, System.Boolean& overflowed) | 🟡 | MethodMap.Format+MethodMap.Decimal | 内部 icall（public 包装承接） |
| System.Decimal | FCallDivide | static | public/nonpublic | System.Void (System.Decimal& d1, System.Decimal& d2) | 🟡 | MethodMap.Format+MethodMap.Decimal | 内部 icall（public 包装承接） |
| System.Decimal | FCallDivideOverflowed | static | public/nonpublic | System.Void (System.Decimal& d1, System.Decimal& d2, System.Boolean& overflowed) | 🟡 | MethodMap.Format+MethodMap.Decimal | 内部 icall（public 包装承接） |
| System.Decimal | GetHashCode | instance | public/public | System.Int32 () | ✅ | MethodMap.Decimal |  |
| System.Decimal | FCallFloor | static | public/nonpublic | System.Void (System.Decimal& d) | 🟡 | MethodMap.Format+MethodMap.Decimal | 内部 icall（public 包装承接） |
| System.Decimal | FCallMultiply | static | public/nonpublic | System.Void (System.Decimal& d1, System.Decimal& d2) | 🟡 | MethodMap.Format+MethodMap.Decimal | 内部 icall（public 包装承接） |
| System.Decimal | FCallMultiplyOverflowed | static | public/nonpublic | System.Void (System.Decimal& d1, System.Decimal& d2, System.Boolean& overflowed) | 🟡 | MethodMap.Format+MethodMap.Decimal | 内部 icall（public 包装承接） |
| System.Decimal | FCallRound | static | public/nonpublic | System.Void (System.Decimal& d, System.Int32 decimals) | 🟡 | MethodMap.Format+MethodMap.Decimal | 内部 icall（public 包装承接） |
| System.Decimal | FCallToCurrency | static | public/nonpublic | System.Void (System.Currency& result, System.Decimal d) | 🟡 | MethodMap.Format+MethodMap.Decimal | 内部 icall（public 包装承接） |
| System.Decimal | ToDouble | static | public/public | System.Double (System.Decimal d) | ✅ | MethodMap.Decimal |  |
| System.Decimal | FCallToInt32 | static | public/nonpublic | System.Int32 (System.Decimal d) | 🟡 | MethodMap.Format+MethodMap.Decimal | 内部 icall（public 包装承接） |
| System.Decimal | ToSingle | static | public/public | System.Single (System.Decimal d) | ✅ | MethodMap.Decimal |  |
| System.Decimal | FCallCompare | static | public/nonpublic | System.Int32 (System.Decimal& d1, System.Decimal& d2) | 🟡 | MethodMap.Format+MethodMap.Decimal | 内部 icall（public 包装承接） |
| System.Decimal | FCallTruncate | static | public/nonpublic | System.Void (System.Decimal& d) | 🟡 | MethodMap.Format+MethodMap.Decimal | 内部 icall（public 包装承接） |
| System.Decimal | .ctor | instance | public/public |  (System.Single value) | ✅ | MethodMap.Decimal |  |
| System.Decimal | .ctor | instance | public/public |  (System.Double value) | ✅ | MethodMap.Decimal |  |
| System.DefaultBinder | CanConvertPrimitive | static | nonpublic/nonpublic | System.Boolean (System.RuntimeType source, System.RuntimeType target) | ❌ | - | 内部 icall（public 包装承接） |
| System.DefaultBinder | CanConvertPrimitiveObjectToType | static | nonpublic/nonpublic | System.Boolean (System.Object source, System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.Enum | Equals | instance | public/public | System.Boolean (System.Object obj) | 🟡 | MethodMap.Enum |  |
| System.Enum | InternalCompareTo | static | public/nonpublic | System.Int32 (System.Object o1, System.Object o2) | 🟡 | MethodMap.Enum | 内部 icall（public 包装承接） |
| System.Enum | InternalGetUnderlyingType | static | public/nonpublic | System.RuntimeType (System.RuntimeType enumType) | 🟡 | MethodMap.Enum | 内部 icall（public 包装承接） |
| System.Enum | InternalBoxEnum | static | public/nonpublic | System.Object (System.RuntimeType enumType, System.Int64 value) | 🟡 | MethodMap.Enum | 内部 icall（public 包装承接） |
| System.Enum | InternalHasFlag | instance | public/nonpublic | System.Boolean (System.Enum flags) | 🟡 | MethodMap.Enum | 内部 icall（public 包装承接） |
| System.Enum | InternalGetCorElementType | instance | public/nonpublic | System.Reflection.CorElementType () | 🟡 | MethodMap.Enum | 内部 icall（public 包装承接） |
| System.Environment | set_ExitCode | static | public/public | System.Void (System.Int32 value) | ✅ | MethodMap.EnvironmentExtra |  |
| System.Environment | FailFast | static | public/public | System.Void (System.String message) | ✅ | MethodMap.EnvironmentExtra |  |
| System.Environment | FailFast | static | public/public | System.Void (System.String message, System.UInt32 exitCode) | ✅ | MethodMap.EnvironmentExtra |  |
| System.Environment | FailFast | static | public/public | System.Void (System.String message, System.Exception exception) | ✅ | MethodMap.EnvironmentExtra |  |
| System.Environment | GetCompatibilityFlag | static | public/nonpublic | System.Boolean (System.CompatibilityFlag flag) | 🟡 | MethodMap.Environment+MethodMap.EnvironmentExtra | 内部 icall（public 包装承接） |
| System.Environment | get_TickCount | static | public/public | System.Int32 () | ✅ | MethodMap.Environment |  |
| System.Environment | get_ExitCode | static | public/public | System.Int32 () | ✅ | MethodMap.EnvironmentExtra |  |
| System.Environment | GetCommandLineArgsNative | static | public/nonpublic | System.String[] () | 🟡 | MethodMap.Environment+MethodMap.EnvironmentExtra | 内部 icall（public 包装承接） |
| System.Environment | nativeGetEnvironmentVariable | static | public/nonpublic | System.String (System.String variable) | 🟡 | MethodMap.Environment+MethodMap.EnvironmentExtra | 内部 icall（public 包装承接） |
| System.Environment | GetVersion | static | public/nonpublic | System.Boolean (Microsoft.Win32.Win32Native+OSVERSIONINFO osVer) | 🟡 | MethodMap.Environment+MethodMap.EnvironmentExtra | 内部 icall（public 包装承接） |
| System.Environment | GetVersionEx | static | public/nonpublic | System.Boolean (Microsoft.Win32.Win32Native+OSVERSIONINFOEX osVer) | 🟡 | MethodMap.Environment+MethodMap.EnvironmentExtra | 内部 icall（public 包装承接） |
| System.Environment | GetResourceFromDefault | static | public/nonpublic | System.String (System.String key) | 🟡 | MethodMap.Environment+MethodMap.EnvironmentExtra | 内部 icall（public 包装承接） |
| System.Environment | get_HasShutdownStarted | static | public/public | System.Boolean () | ✅ | MethodMap.EnvironmentExtra |  |
| System.Environment | get_TickCount64 | static | public/nonpublic | System.Int64 () | 🟡 | MethodMap.Environment+MethodMap.EnvironmentExtra | 内部 icall（public 包装承接） |
| System.GC | GetGCLatencyMode | static | public/nonpublic | System.Int32 () | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | SetGCLatencyMode | static | public/nonpublic | System.Int32 (System.Int32 newLatencyMode) | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | GetLOHCompactionMode | static | public/nonpublic | System.Int32 () | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | GetGenerationWR | static | public/nonpublic | System.Int32 (System.IntPtr handle) | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | IsServerGC | static | public/nonpublic | System.Boolean () | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | _GetAllocatedBytesForCurrentThread | static | public/nonpublic | System.Int64 () | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | _RegisterForFullGCNotification | static | public/nonpublic | System.Boolean (System.Int32 maxGenerationPercentage, System.Int32 largeObjectHeapPercentage) | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | _CancelFullGCNotification | static | public/nonpublic | System.Boolean () | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | _WaitForFullGCApproach | static | public/nonpublic | System.Int32 (System.Int32 millisecondsTimeout) | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | _WaitForFullGCComplete | static | public/nonpublic | System.Int32 (System.Int32 millisecondsTimeout) | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | SetLOHCompactionMode | static | public/nonpublic | System.Void (System.Int32 newLOHCompactionyMode) | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | GetMaxGeneration | static | public/nonpublic | System.Int32 () | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | _CollectionCount | static | public/nonpublic | System.Int32 (System.Int32 generation, System.Int32 getSpecialGCCount) | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | GetGeneration | static | public/public | System.Int32 (System.Object obj) | ✅ | MethodMap.GC |  |
| System.GC | _SuppressFinalize | static | public/nonpublic | System.Void (System.Object o) | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.GC | _ReRegisterForFinalize | static | public/nonpublic | System.Void (System.Object o) | 🟡 | MethodMap.GC | 内部 icall（public 包装承接） |
| System.SizedReference | CreateSizedRef | static | nonpublic/nonpublic | System.IntPtr (System.Object o) | ❌ | - | 内部 icall（public 包装承接） |
| System.SizedReference | FreeSizedRef | static | nonpublic/nonpublic | System.Void (System.IntPtr h) | ❌ | - | 内部 icall（public 包装承接） |
| System.SizedReference | GetTargetOfSizedRef | static | nonpublic/nonpublic | System.Object (System.IntPtr h) | ❌ | - | 内部 icall（public 包装承接） |
| System.SizedReference | GetApproximateSizeOfSizedRef | static | nonpublic/nonpublic | System.Int64 (System.IntPtr h) | ❌ | - | 内部 icall（public 包装承接） |
| System.MarshalByRefObject | GetComIUnknown | static | public/nonpublic | System.IntPtr (System.MarshalByRefObject o) | ❌ | - | 内部 icall（public 包装承接） |
| System.Math | Acos | static | public/public | System.Double (System.Double d) | ✅ | MethodMap.Math |  |
| System.Math | Asin | static | public/public | System.Double (System.Double d) | ✅ | MethodMap.Math |  |
| System.Math | Atan | static | public/public | System.Double (System.Double d) | ✅ | MethodMap.Math |  |
| System.Math | Atan2 | static | public/public | System.Double (System.Double y, System.Double x) | ✅ | MethodMap.Math |  |
| System.Math | Cos | static | public/public | System.Double (System.Double d) | ✅ | MethodMap.Math |  |
| System.Math | Cosh | static | public/public | System.Double (System.Double value) | ✅ | MethodMap.Math |  |
| System.Math | Sin | static | public/public | System.Double (System.Double a) | ✅ | MethodMap.Math |  |
| System.Math | Sinh | static | public/public | System.Double (System.Double value) | ✅ | MethodMap.Math |  |
| System.Math | Tanh | static | public/public | System.Double (System.Double value) | ✅ | MethodMap.Math |  |
| System.Math | Round | static | public/public | System.Double (System.Double a) | ✅ | MethodMap.Math |  |
| System.Math | SplitFractionDouble | static | public/nonpublic | System.Double (System.Double* value) | 🟡 | MethodMap.Math+MethodMap.MathExtra | 内部 icall（public 包装承接） |
| System.Math | Sqrt | static | public/public | System.Double (System.Double d) | ✅ | MethodMap.Math |  |
| System.Math | Log10 | static | public/public | System.Double (System.Double d) | ✅ | MethodMap.Math |  |
| System.Math | Abs | static | public/public | System.Single (System.Single value) | ✅ | MethodMap.Math |  |
| System.Math | Abs | static | public/public | System.Double (System.Double value) | ✅ | MethodMap.Math |  |
| System.Math | Ceiling | static | public/public | System.Double (System.Double a) | ✅ | MethodMap.Math |  |
| System.Math | Floor | static | public/public | System.Double (System.Double d) | ✅ | MethodMap.Math |  |
| System.Math | Tan | static | public/public | System.Double (System.Double a) | ✅ | MethodMap.Math |  |
| System.Math | Log | static | public/public | System.Double (System.Double d) | ✅ | MethodMap.Math |  |
| System.Math | Exp | static | public/public | System.Double (System.Double d) | ✅ | MethodMap.Math |  |
| System.Math | Pow | static | public/public | System.Double (System.Double x, System.Double y) | ✅ | MethodMap.Math |  |
| System.Mda | IsStreamWriterBufferedDataLostEnabled | static | nonpublic/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Mda | MemberInfoCacheCreation | static | nonpublic/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Mda | IsInvalidGCHandleCookieProbeEnabled | static | nonpublic/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Mda | ReportStreamWriterBufferedDataLost | static | nonpublic/nonpublic | System.Void (System.String text) | ❌ | - | 内部 icall（public 包装承接） |
| System.Mda | IsStreamWriterBufferedDataLostCaptureAllocatedCallStack | static | nonpublic/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Mda | DateTimeInvalidLocalFormat | static | nonpublic/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Mda | FireInvalidGCHandleCookieProbe | static | nonpublic/nonpublic | System.Void (System.IntPtr cookie) | ❌ | - | 内部 icall（public 包装承接） |
| System.Mda | ReportErrorSafeHandleRelease | static | nonpublic/nonpublic | System.Void (System.Exception ex) | ❌ | - | 内部 icall（public 包装承接） |
| System.MissingMemberException | FormatSignature | static | public/nonpublic | System.String (System.Byte[] signature) | ❌ | - | 内部 icall（public 包装承接） |
| System.Number | FormatNumberBuffer | static | nonpublic/nonpublic | System.String (System.Byte* number, System.String format, System.Globalization.NumberFormatInfo info, System.Char* allDigits) | ❌ | - | 内部 icall（public 包装承接） |
| System.Number | FormatDecimal | static | nonpublic/public | System.String (System.Decimal value, System.String format, System.Globalization.NumberFormatInfo info) | ❌ | - | 内部 icall（public 包装承接） |
| System.Number | FormatDouble | static | nonpublic/public | System.String (System.Double value, System.String format, System.Globalization.NumberFormatInfo info) | ❌ | - | 内部 icall（public 包装承接） |
| System.Number | FormatInt32 | static | nonpublic/public | System.String (System.Int32 value, System.String format, System.Globalization.NumberFormatInfo info) | ❌ | - | 内部 icall（public 包装承接） |
| System.Number | FormatUInt32 | static | nonpublic/public | System.String (System.UInt32 value, System.String format, System.Globalization.NumberFormatInfo info) | ❌ | - | 内部 icall（public 包装承接） |
| System.Number | FormatInt64 | static | nonpublic/public | System.String (System.Int64 value, System.String format, System.Globalization.NumberFormatInfo info) | ❌ | - | 内部 icall（public 包装承接） |
| System.Number | FormatUInt64 | static | nonpublic/public | System.String (System.UInt64 value, System.String format, System.Globalization.NumberFormatInfo info) | ❌ | - | 内部 icall（public 包装承接） |
| System.Number | FormatSingle | static | nonpublic/public | System.String (System.Single value, System.String format, System.Globalization.NumberFormatInfo info) | ❌ | - | 内部 icall（public 包装承接） |
| System.Number | NumberBufferToDecimal | static | nonpublic/public | System.Boolean (System.Byte* number, System.Decimal& value) | ❌ | - | 内部 icall（public 包装承接） |
| System.Number | NumberBufferToDouble | static | nonpublic/nonpublic | System.Boolean (System.Byte* number, System.Double& value) | ❌ | - | 内部 icall（public 包装承接） |
| System.ParseNumbers | StringToLong | static | nonpublic/public | System.Int64 (System.String s, System.Int32 radix, System.Int32 flags, System.Int32* currPos) | ❌ | - | 内部 icall（public 包装承接） |
| System.ParseNumbers | StringToInt | static | nonpublic/public | System.Int32 (System.String s, System.Int32 radix, System.Int32 flags, System.Int32* currPos) | ❌ | - | 内部 icall（public 包装承接） |
| System.ParseNumbers | IntToString | static | nonpublic/public | System.String (System.Int32 l, System.Int32 radix, System.Int32 width, System.Char paddingChar, System.Int32 flags) | ❌ | - | 内部 icall（public 包装承接） |
| System.ParseNumbers | LongToString | static | nonpublic/public | System.String (System.Int64 l, System.Int32 radix, System.Int32 width, System.Char paddingChar, System.Int32 flags) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeType | GetTypeFromCLSIDImpl | static | nonpublic/nonpublic | System.Type (System.Guid clsid, System.String server, System.Boolean throwOnError) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeType | GetGUID | instance | nonpublic/nonpublic | System.Void (System.Guid& result) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeType | IsWindowsRuntimeObjectType | static | nonpublic/nonpublic | System.Boolean (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeType | CanValueSpecialCast | static | nonpublic/nonpublic | System.Boolean (System.RuntimeType valueType, System.RuntimeType targetType) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeType | AllocateValueType | static | nonpublic/nonpublic | System.Object (System.RuntimeType type, System.Object value, System.Boolean fForceTypeChange) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeType | _CreateEnum | static | nonpublic/nonpublic | System.Object (System.RuntimeType enumType, System.Int64 value) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeType | IsTypeExportedToWindowsRuntime | static | nonpublic/nonpublic | System.Boolean (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeType | InvokeDispMethod | instance | nonpublic/nonpublic | System.Object (System.String name, System.Reflection.BindingFlags invokeAttr, System.Object target, System.Object[] args, System.Boolean[] byrefModifiers, System.Int32 culture, System.String[] namedParameters) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeType | GetTypeFromProgIDImpl | static | nonpublic/nonpublic | System.Type (System.String progID, System.String server, System.Boolean throwOnError) | ❌ | - | 内部 icall（public 包装承接） |
| System.Utf8String | EqualsCaseSensitive | static | nonpublic/nonpublic | System.Boolean (System.Void* szLhs, System.Void* szRhs, System.Int32 cSz) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetValueInternal | static | public/nonpublic | System.IntPtr (System.RuntimeTypeHandle handle) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | CompareCanonicalHandles | static | public/nonpublic | System.Boolean (System.RuntimeType left, System.RuntimeType right) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | SatisfiesConstraints | static | public/nonpublic | System.Boolean (System.RuntimeType paramType, System.IntPtr* pTypeContext, System.Int32 typeContextLength, System.IntPtr* pMethodContext, System.Int32 methodContextLength, System.RuntimeType toType) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | IsInstanceOfType | static | public/nonpublic | System.Boolean (System.RuntimeType type, System.Object o) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | CreateInstance | static | public/nonpublic | System.Object (System.RuntimeType type, System.Boolean publicOnly, System.Boolean noCheck, System.Boolean& canBeCached, System.RuntimeMethodHandleInternal& ctor, System.Boolean& bNeedSecurityCheck) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | CreateCaInstance | static | public/nonpublic | System.Object (System.RuntimeType type, System.IRuntimeMethodInfo ctor) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | Allocate | static | public/nonpublic | System.Object (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | CreateInstanceForAnotherGenericParameter | static | public/nonpublic | System.Object (System.RuntimeType type, System.RuntimeType genericParameter) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetCorElementType | static | public/nonpublic | System.Reflection.CorElementType (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetAssembly | static | public/nonpublic | System.Reflection.RuntimeAssembly (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetModule | static | public/nonpublic | System.Reflection.RuntimeModule (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetBaseType | static | public/nonpublic | System.RuntimeType (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetAttributes | static | public/nonpublic | System.Reflection.TypeAttributes (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetElementType | static | public/nonpublic | System.RuntimeType (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetArrayRank | static | public/nonpublic | System.Int32 (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetToken | static | public/nonpublic | System.Int32 (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetMethodAt | static | public/nonpublic | System.RuntimeMethodHandleInternal (System.RuntimeType type, System.Int32 slot) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetFirstIntroducedMethod | static | public/nonpublic | System.RuntimeMethodHandleInternal (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetNextIntroducedMethod | static | public/nonpublic | System.Void (System.RuntimeMethodHandleInternal& method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetFields | static | public/nonpublic | System.Boolean (System.RuntimeType type, System.IntPtr* result, System.Int32* count) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetInterfaces | static | public/nonpublic | System.Type[] (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetNumVirtuals | static | public/nonpublic | System.Int32 (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | IsZapped | static | public/nonpublic | System.Boolean (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | IsDoNotForceOrderOfConstructorsSet | static | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | IsComObject | static | public/nonpublic | System.Boolean (System.RuntimeType type, System.Boolean isGenericCOM) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | IsContextful | static | public/nonpublic | System.Boolean (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | IsInterface | static | public/nonpublic | System.Boolean (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | HasProxyAttribute | static | public/nonpublic | System.Boolean (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | IsValueType | static | public/nonpublic | System.Boolean (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | _GetUtf8Name | static | public/nonpublic | System.Void* (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | CanCastTo | static | public/nonpublic | System.Boolean (System.RuntimeType type, System.RuntimeType target) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetDeclaringType | static | public/nonpublic | System.RuntimeType (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetDeclaringMethod | static | public/nonpublic | System.IRuntimeMethodInfo (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | HasInstantiation | static | public/nonpublic | System.Boolean (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | IsGenericTypeDefinition | static | public/nonpublic | System.Boolean (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | IsGenericVariable | static | public/nonpublic | System.Boolean (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | GetGenericVariableIndex | static | public/nonpublic | System.Int32 (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | ContainsGenericVariables | static | public/nonpublic | System.Boolean (System.RuntimeType handle) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | _GetMetadataImport | static | public/nonpublic | System.IntPtr (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | IsEquivalentTo | static | public/nonpublic | System.Boolean (System.RuntimeType rtType1, System.RuntimeType rtType2) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeTypeHandle | IsEquivalentType | static | public/nonpublic | System.Boolean (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | _GetCurrentMethod | static | public/nonpublic | System.IRuntimeMethodInfo (System.Threading.StackCrawlMark& stackMark) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | _IsTokenSecurityTransparent | static | public/nonpublic | System.Boolean (System.Reflection.RuntimeModule module, System.Int32 metaDataToken) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | CheckLinktimeDemands | static | public/nonpublic | System.Void (System.IRuntimeMethodInfo method, System.Reflection.RuntimeModule module, System.Boolean isDecoratedTargetSecurityTransparent) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | GetAttributes | static | public/nonpublic | System.Reflection.MethodAttributes (System.RuntimeMethodHandleInternal method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | GetImplAttributes | static | public/nonpublic | System.Reflection.MethodImplAttributes (System.IRuntimeMethodInfo method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | GetDeclaringType | static | public/nonpublic | System.RuntimeType (System.RuntimeMethodHandleInternal method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | GetSlot | static | public/nonpublic | System.Int32 (System.RuntimeMethodHandleInternal method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | GetMethodDef | static | public/nonpublic | System.Int32 (System.IRuntimeMethodInfo method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | GetName | static | public/nonpublic | System.String (System.RuntimeMethodHandleInternal method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | _GetUtf8Name | static | public/nonpublic | System.Void* (System.RuntimeMethodHandleInternal method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | MatchesNameHash | static | public/nonpublic | System.Boolean (System.RuntimeMethodHandleInternal method, System.UInt32 hash) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | InvokeMethod | static | public/nonpublic | System.Object (System.Object target, System.Object[] arguments, System.Signature sig, System.Boolean constructor) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | GetSpecialSecurityFlags | static | public/nonpublic | System.UInt32 (System.IRuntimeMethodInfo method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | PerformSecurityCheck | static | public/nonpublic | System.Void (System.Object obj, System.RuntimeMethodHandleInternal method, System.RuntimeType parent, System.UInt32 invocationFlags) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | SerializationInvoke | static | public/nonpublic | System.Void (System.IRuntimeMethodInfo method, System.Object target, System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext& context) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | HasMethodInstantiation | static | public/nonpublic | System.Boolean (System.RuntimeMethodHandleInternal method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | GetStubIfNeeded | static | public/nonpublic | System.RuntimeMethodHandleInternal (System.RuntimeMethodHandleInternal method, System.RuntimeType declaringType, System.RuntimeType[] methodInstantiation) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | GetMethodFromCanonical | static | public/nonpublic | System.RuntimeMethodHandleInternal (System.RuntimeMethodHandleInternal method, System.RuntimeType declaringType) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | IsGenericMethodDefinition | static | public/nonpublic | System.Boolean (System.RuntimeMethodHandleInternal method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | IsTypicalMethodDefinition | static | public/nonpublic | System.Boolean (System.IRuntimeMethodInfo method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | IsDynamicMethod | static | public/nonpublic | System.Boolean (System.RuntimeMethodHandleInternal method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | GetResolver | static | public/nonpublic | System.Resolver (System.RuntimeMethodHandleInternal method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | GetMethodBody | static | public/nonpublic | System.Reflection.MethodBody (System.IRuntimeMethodInfo method, System.RuntimeType declaringType) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | IsConstructor | static | public/nonpublic | System.Boolean (System.RuntimeMethodHandleInternal method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeMethodHandle | GetLoaderAllocator | static | public/nonpublic | System.Reflection.LoaderAllocator (System.RuntimeMethodHandleInternal method) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeFieldHandle | GetName | static | public/nonpublic | System.String (System.Reflection.RtFieldInfo field) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeFieldHandle | _GetUtf8Name | static | public/nonpublic | System.Void* (System.RuntimeFieldHandleInternal field) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeFieldHandle | MatchesNameHash | static | public/nonpublic | System.Boolean (System.RuntimeFieldHandleInternal handle, System.UInt32 hash) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeFieldHandle | GetAttributes | static | public/nonpublic | System.Reflection.FieldAttributes (System.RuntimeFieldHandleInternal field) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeFieldHandle | GetApproxDeclaringType | static | public/nonpublic | System.RuntimeType (System.RuntimeFieldHandleInternal field) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeFieldHandle | GetToken | static | public/nonpublic | System.Int32 (System.Reflection.RtFieldInfo field) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeFieldHandle | GetValue | static | public/nonpublic | System.Object (System.Reflection.RtFieldInfo field, System.Object instance, System.RuntimeType fieldType, System.RuntimeType declaringType, System.Boolean& domainInitialized) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeFieldHandle | SetValue | static | public/nonpublic | System.Void (System.Reflection.RtFieldInfo field, System.Object obj, System.Object value, System.RuntimeType fieldType, System.Reflection.FieldAttributes fieldAttr, System.RuntimeType declaringType, System.Boolean& domainInitialized) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeFieldHandle | GetStaticFieldForGenericType | static | public/nonpublic | System.RuntimeFieldHandleInternal (System.RuntimeFieldHandleInternal field, System.RuntimeType declaringType) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeFieldHandle | AcquiresContextFromThis | static | public/nonpublic | System.Boolean (System.RuntimeFieldHandleInternal field) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeFieldHandle | GetValueDirect | static | public/nonpublic | System.Object (System.Reflection.RtFieldInfo field, System.RuntimeType fieldType, System.Void* pTypedRef, System.RuntimeType contextType) | ❌ | - | 内部 icall（public 包装承接） |
| System.RuntimeFieldHandle | SetValueDirect | static | public/nonpublic | System.Void (System.Reflection.RtFieldInfo field, System.RuntimeType fieldType, System.Void* pTypedRef, System.Object value, System.RuntimeType contextType) | ❌ | - | 内部 icall（public 包装承接） |
| System.ModuleHandle | GetDynamicMethod | static | public/nonpublic | System.IRuntimeMethodInfo (System.Reflection.Emit.DynamicMethod method, System.Reflection.RuntimeModule module, System.String name, System.Byte[] sig, System.Resolver resolver) | ❌ | - | 内部 icall（public 包装承接） |
| System.ModuleHandle | GetToken | static | public/nonpublic | System.Int32 (System.Reflection.RuntimeModule module) | ❌ | - | 内部 icall（public 包装承接） |
| System.ModuleHandle | GetMDStreamVersion | static | public/nonpublic | System.Int32 (System.Reflection.RuntimeModule module) | ❌ | - | 内部 icall（public 包装承接） |
| System.ModuleHandle | _GetMetadataImport | static | public/nonpublic | System.IntPtr (System.Reflection.RuntimeModule module) | ❌ | - | 内部 icall（public 包装承接） |
| System.Signature | GetSignature | instance | nonpublic/nonpublic | System.Void (System.Void* pCorSig, System.Int32 cCorSig, System.RuntimeFieldHandleInternal fieldHandle, System.IRuntimeMethodInfo methodHandle, System.RuntimeType declaringType) | ❌ | - | 内部 icall（public 包装承接） |
| System.Signature | CompareSig | static | nonpublic/nonpublic | System.Boolean (System.Signature sig1, System.Signature sig2) | ❌ | - | 内部 icall（public 包装承接） |
| System.Signature | GetCustomModifiers | instance | nonpublic/nonpublic | System.Type[] (System.Int32 position, System.Boolean required) | ❌ | - | 内部 icall（public 包装承接） |
| System.TimeSpan | LegacyFormatMode | static | public/nonpublic | System.Boolean () | 🟡 | MethodMap.TimeSpan+MethodMap.TimeExt2+MethodMap.DateTimeExtra | 内部 icall（public 包装承接） |
| System.Type | GetTypeFromHandleUnsafe | static | public/nonpublic | System.RuntimeType (System.IntPtr handle) | 🟡 | MethodMap.Reflect+MethodMap.Type | 内部 icall（public 包装承接） |
| System.Type | GetTypeFromHandle | static | public/public | System.Type (System.RuntimeTypeHandle handle) | ✅ | MethodMap.Type | b类: ldtoken fold + MethodMap.Type 已接 |
| System.Type | op_Equality | static | public/public | System.Boolean (System.Type left, System.Type right) | ✅ | MethodMap.Type |  |
| System.Type | op_Inequality | static | public/public | System.Boolean (System.Type left, System.Type right) | 🟡 | MethodMap.Reflect+MethodMap.Type |  |
| System.TypedReference | InternalToObject | static | public/nonpublic | System.Object (System.Void* value) | ❌ | - | 内部 icall（public 包装承接） |
| System.TypedReference | InternalMakeTypedReference | static | public/nonpublic | System.Void (System.Void* result, System.Object target, System.IntPtr[] flds, System.RuntimeType lastFieldType) | ❌ | - | 内部 icall（public 包装承接） |
| System.TypedReference | InternalSetTypedReference | static | public/nonpublic | System.Void (System.Void* target, System.Object value) | ❌ | - | 内部 icall（public 包装承接） |
| System.ValueType | GetHashCode | instance | public/public | System.Int32 () | ❌ | - |  |
| System.ValueType | CanCompareBits | static | public/nonpublic | System.Boolean (System.Object obj) | ❌ | - | 内部 icall（public 包装承接） |
| System.ValueType | FastEqualsCheck | static | public/nonpublic | System.Boolean (System.Object a, System.Object b) | ❌ | - | 内部 icall（public 包装承接） |
| System.ValueType | GetHashCodeOfPtr | static | public/nonpublic | System.Int32 (System.IntPtr ptr) | ❌ | - | 内部 icall（public 包装承接） |
| System.Variant | SetFieldsObject | instance | nonpublic/nonpublic | System.Void (System.Object val) | ❌ | - | 内部 icall（public 包装承接） |
| System.Variant | GetR8FromVar | instance | nonpublic/nonpublic | System.Double () | ❌ | - | 内部 icall（public 包装承接） |
| System.Variant | GetR4FromVar | instance | nonpublic/nonpublic | System.Single () | ❌ | - | 内部 icall（public 包装承接） |
| System.Variant | SetFieldsR4 | instance | nonpublic/nonpublic | System.Void (System.Single val) | ❌ | - | 内部 icall（public 包装承接） |
| System.Variant | SetFieldsR8 | instance | nonpublic/nonpublic | System.Void (System.Double val) | ❌ | - | 内部 icall（public 包装承接） |
| System.Variant | BoxEnum | instance | nonpublic/nonpublic | System.Object () | ❌ | - | 内部 icall（public 包装承接） |
| System.WeakReference | get_IsAlive | instance | public/public | System.Boolean () | ✅ | MethodMap.WeakReference |  |
| System.WeakReference | get_Target | instance | public/public | System.Object () | ✅ | MethodMap.WeakReference |  |
| System.WeakReference | set_Target | instance | public/public | System.Void (System.Object value) | ✅ | MethodMap.WeakReference |  |
| System.WeakReference | Finalize | instance | public/nonpublic | System.Void () | 🟡 | MethodMap.WeakReference | 内部 icall（public 包装承接） |
| System.WeakReference | IsTrackResurrection | instance | public/nonpublic | System.Boolean () | 🟡 | MethodMap.WeakReference | 内部 icall（public 包装承接） |
| System.WeakReference | Create | instance | public/nonpublic | System.Void (System.Object target, System.Boolean trackResurrection) | 🟡 | MethodMap.WeakReference | 内部 icall（public 包装承接） |
| System.WeakReference&#96;1 | get_Target | instance | public/nonpublic |  () | ✅ | MethodMap.WeakReference | 内部 icall（public 包装承接） |
| System.WeakReference&#96;1 | set_Target | instance | public/nonpublic | System.Void (value) | ✅ | MethodMap.WeakReference | 内部 icall（public 包装承接） |
| System.WeakReference&#96;1 | Create | instance | public/nonpublic | System.Void (target, System.Boolean trackResurrection) | 🟡 | MethodMap.WeakReference | 内部 icall（public 包装承接） |
| System.WeakReference&#96;1 | Finalize | instance | public/nonpublic | System.Void () | 🟡 | MethodMap.WeakReference | 内部 icall（public 包装承接） |
| System.WeakReference&#96;1 | IsTrackResurrection | instance | public/nonpublic | System.Boolean () | 🟡 | MethodMap.WeakReference | 内部 icall（public 包装承接） |
| System.CLRConfig | CheckLegacyManagedDeflateStream | static | nonpublic/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.CLRConfig | CheckThrowUnobservedTaskExceptions | static | nonpublic/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.IO.__ConsoleStream | WaitForAvailableConsoleInput | static | nonpublic/nonpublic | System.Void (Microsoft.Win32.SafeHandles.SafeFileHandle file, System.Boolean isPipe) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.CodeAccessSecurityEngine | SpecialDemand | static | nonpublic/nonpublic | System.Void (System.Security.PermissionType whatPermission, System.Threading.StackCrawlMark& stackMark) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.CodeAccessSecurityEngine | Check | static | nonpublic/nonpublic | System.Void (System.Object demand, System.Threading.StackCrawlMark& stackMark, System.Boolean isPermSet) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.CodeAccessSecurityEngine | QuickCheckForAllDemands | static | nonpublic/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.CodeAccessSecurityEngine | CheckNReturnSO | static | nonpublic/nonpublic | System.Security.FrameSecurityDescriptor (System.Security.PermissionToken permToken, System.Security.CodeAccessPermission demand, System.Threading.StackCrawlMark& stackMark, System.Int32 create) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.CodeAccessSecurityEngine | GetZoneAndOriginInternal | static | nonpublic/nonpublic | System.Void (System.Collections.ArrayList zoneList, System.Collections.ArrayList originList, System.Threading.StackCrawlMark& stackMark) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.CodeAccessSecurityEngine | AllDomainsHomogeneousWithNoStackModifiers | static | nonpublic/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.FrameSecurityDescriptor | IncrementAssertCount | static | nonpublic/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.FrameSecurityDescriptor | DecrementAssertCount | static | nonpublic/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.FrameSecurityDescriptor | IncrementOverridesCount | static | nonpublic/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.FrameSecurityDescriptor | DecrementOverridesCount | static | nonpublic/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.SecurityContext | GetImpersonationFlowMode | static | public/nonpublic | System.Security.WindowsImpersonationFlowMode () | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.SecurityContext | IsDefaultThreadSecurityInfo | static | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.SecurityManager | _SetThreadSecurity | static | public/nonpublic | System.Boolean (System.Boolean bThreadSecurity) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.SecurityRuntime | GetSecurityObjectForFrame | static | nonpublic/nonpublic | System.Security.FrameSecurityDescriptor (System.Threading.StackCrawlMark& stackMark, System.Boolean create) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _GetEnforceFipsPolicySetting | static | nonpublic/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _AcquireCSP | static | nonpublic/nonpublic | System.Void (System.Security.Cryptography.CspParameters param, System.Security.Cryptography.SafeProvHandle& hProv) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _CreateCSP | static | nonpublic/nonpublic | System.Void (System.Security.Cryptography.CspParameters param, System.Boolean randomKeyContainer, System.Security.Cryptography.SafeProvHandle& hProv) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _DecryptData | static | nonpublic/nonpublic | System.Int32 (System.Security.Cryptography.SafeKeyHandle hKey, System.Byte[] data, System.Int32 ib, System.Int32 cb, System.Byte[]& outputBuffer, System.Int32 outputOffset, System.Security.Cryptography.PaddingMode PaddingMode, System.Boolean fDone) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _EncryptData | static | nonpublic/nonpublic | System.Int32 (System.Security.Cryptography.SafeKeyHandle hKey, System.Byte[] data, System.Int32 ib, System.Int32 cb, System.Byte[]& outputBuffer, System.Int32 outputOffset, System.Security.Cryptography.PaddingMode PaddingMode, System.Boolean fDone) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _ExportKey | static | nonpublic/nonpublic | System.Void (System.Security.Cryptography.SafeKeyHandle hKey, System.Int32 blobType, System.Object cspObject) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _GenerateKey | static | nonpublic/nonpublic | System.Void (System.Security.Cryptography.SafeProvHandle hProv, System.Int32 algid, System.Security.Cryptography.CspProviderFlags flags, System.Int32 keySize, System.Security.Cryptography.SafeKeyHandle& hKey) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _GetKeyParameter | static | nonpublic/nonpublic | System.Byte[] (System.Security.Cryptography.SafeKeyHandle hKey, System.UInt32 paramID) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _GetKeySetSecurityInfo | static | nonpublic/nonpublic | System.Byte[] (System.Security.Cryptography.SafeProvHandle hProv, System.Security.AccessControl.SecurityInfos securityInfo, System.Int32& error) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _GetProviderParameter | static | nonpublic/nonpublic | System.Object (System.Security.Cryptography.SafeProvHandle hProv, System.Int32 keyNumber, System.UInt32 paramID) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _GetUserKey | static | nonpublic/nonpublic | System.Int32 (System.Security.Cryptography.SafeProvHandle hProv, System.Int32 keyNumber, System.Security.Cryptography.SafeKeyHandle& hKey) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _ImportBulkKey | static | nonpublic/nonpublic | System.Void (System.Security.Cryptography.SafeProvHandle hProv, System.Int32 algid, System.Boolean useSalt, System.Byte[] key, System.Security.Cryptography.SafeKeyHandle& hKey) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _ImportCspBlob | static | nonpublic/nonpublic | System.Int32 (System.Byte[] keyBlob, System.Security.Cryptography.SafeProvHandle hProv, System.Security.Cryptography.CspProviderFlags flags, System.Security.Cryptography.SafeKeyHandle& hKey) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _ImportKey | static | nonpublic/nonpublic | System.Void (System.Security.Cryptography.SafeProvHandle hCSP, System.Int32 keyNumber, System.Security.Cryptography.CspProviderFlags flags, System.Object cspObject, System.Security.Cryptography.SafeKeyHandle& hKey) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _ProduceLegacyHmacValues | static | nonpublic/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.Utils | _OpenCSP | static | nonpublic/nonpublic | System.Int32 (System.Security.Cryptography.CspParameters param, System.UInt32 flags, System.Security.Cryptography.SafeProvHandle& hProv) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.SafeCertContextHandle | _FreePCertContext | static | nonpublic/nonpublic | System.Void (System.IntPtr pCert) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.SafeCertStoreHandle | _FreeCertStoreContext | static | nonpublic/nonpublic | System.Void (System.IntPtr hCertStore) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _DuplicateCertContext | static | nonpublic/nonpublic | System.Void (System.IntPtr handle, System.Security.Cryptography.X509Certificates.SafeCertContextHandle& safeCertContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _GetCertRawData | static | nonpublic/nonpublic | System.Byte[] (System.Security.Cryptography.X509Certificates.SafeCertContextHandle safeCertContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _GetThumbprint | static | nonpublic/nonpublic | System.Byte[] (System.Security.Cryptography.X509Certificates.SafeCertContextHandle safeCertContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _LoadCertFromBlob | static | nonpublic/nonpublic | System.Void (System.Byte[] rawData, System.IntPtr password, System.UInt32 dwFlags, System.Boolean persistKeySet, System.Security.Cryptography.X509Certificates.SafeCertContextHandle& pCertCtx) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _QueryCertBlobType | static | nonpublic/nonpublic | System.UInt32 (System.Byte[] rawData) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _AddCertificateToStore | static | nonpublic/nonpublic | System.Void (System.Security.Cryptography.X509Certificates.SafeCertStoreHandle safeCertStoreHandle, System.Security.Cryptography.X509Certificates.SafeCertContextHandle safeCertContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _ExportCertificatesToBlob | static | nonpublic/nonpublic | System.Byte[] (System.Security.Cryptography.X509Certificates.SafeCertStoreHandle safeCertStoreHandle, System.Security.Cryptography.X509Certificates.X509ContentType contentType, System.IntPtr password) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _GetDateNotAfter | static | nonpublic/nonpublic | System.Void (System.Security.Cryptography.X509Certificates.SafeCertContextHandle safeCertContext, Microsoft.Win32.Win32Native+FILE_TIME& fileTime) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _GetDateNotBefore | static | nonpublic/nonpublic | System.Void (System.Security.Cryptography.X509Certificates.SafeCertContextHandle safeCertContext, Microsoft.Win32.Win32Native+FILE_TIME& fileTime) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _GetIssuerName | static | nonpublic/nonpublic | System.String (System.Security.Cryptography.X509Certificates.SafeCertContextHandle safeCertContext, System.Boolean legacyV1Mode) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _GetPublicKeyOid | static | nonpublic/nonpublic | System.String (System.Security.Cryptography.X509Certificates.SafeCertContextHandle safeCertContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _GetPublicKeyParameters | static | nonpublic/nonpublic | System.Byte[] (System.Security.Cryptography.X509Certificates.SafeCertContextHandle safeCertContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _GetPublicKeyValue | static | nonpublic/nonpublic | System.Byte[] (System.Security.Cryptography.X509Certificates.SafeCertContextHandle safeCertContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _GetSubjectInfo | static | nonpublic/nonpublic | System.String (System.Security.Cryptography.X509Certificates.SafeCertContextHandle safeCertContext, System.UInt32 displayType, System.Boolean legacyV1Mode) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _GetSerialNumber | static | nonpublic/nonpublic | System.Byte[] (System.Security.Cryptography.X509Certificates.SafeCertContextHandle safeCertContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _LoadCertFromFile | static | nonpublic/nonpublic | System.Void (System.String fileName, System.IntPtr password, System.UInt32 dwFlags, System.Boolean persistKeySet, System.Security.Cryptography.X509Certificates.SafeCertContextHandle& pCertCtx) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _OpenX509Store | static | nonpublic/nonpublic | System.Void (System.UInt32 storeType, System.UInt32 flags, System.String storeName, System.Security.Cryptography.X509Certificates.SafeCertStoreHandle& safeCertStoreHandle) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Cryptography.X509Certificates.X509Utils | _QueryCertFileType | static | nonpublic/nonpublic | System.UInt32 (System.String fileName) | ❌ | - | 内部 icall（public 包装承接） |
| System.Security.Principal.Win32 | OpenThreadToken | static | nonpublic/nonpublic | System.Int32 (System.Security.Principal.TokenAccessLevels dwDesiredAccess, System.Security.Principal.WinSecurityContext OpenAs, Microsoft.Win32.SafeHandles.SafeAccessTokenHandle& phThreadToken) | ❌ | - | 内部 icall（public 包装承接） |
| System.Globalization.CalendarData | nativeGetCalendarData | static | nonpublic/nonpublic | System.Boolean (System.Globalization.CalendarData data, System.String localeName, System.Int32 calendar) | ❌ | - | 内部 icall（public 包装承接） |
| System.Globalization.CalendarData | nativeGetCalendars | static | nonpublic/nonpublic | System.Int32 (System.String localeName, System.Boolean useUserOverride, System.Int32[] calendars) | ❌ | - | 内部 icall（public 包装承接） |
| System.Globalization.CalendarData | nativeGetTwoDigitYearMax | static | nonpublic/nonpublic | System.Int32 (System.Int32 calID) | ❌ | - | 内部 icall（public 包装承接） |
| System.Globalization.CultureInfo | InternalGetSystemDefaultUILanguage | static | public/nonpublic | System.Boolean (System.Runtime.CompilerServices.StringHandleOnStack systemDefaultUiLanguage) | 🟡 | MethodMap.CultureInfo | 内部 icall（public 包装承接） |
| System.Globalization.CultureInfo | nativeGetLocaleInfoEx | static | public/nonpublic | System.String (System.String localeName, System.UInt32 field) | 🟡 | MethodMap.CultureInfo | 内部 icall（public 包装承接） |
| System.Globalization.CultureInfo | nativeGetLocaleInfoExInt | static | public/nonpublic | System.Int32 (System.String localeName, System.UInt32 field) | 🟡 | MethodMap.CultureInfo | 内部 icall（public 包装承接） |
| System.Globalization.CultureInfo | nativeSetThreadLocale | static | public/nonpublic | System.Boolean (System.String localeName) | 🟡 | MethodMap.CultureInfo | 内部 icall（public 包装承接） |
| System.Globalization.EncodingTable | GetEncodingData | static | nonpublic/nonpublic | System.Globalization.InternalEncodingDataItem* () | ❌ | - | 内部 icall（public 包装承接） |
| System.Globalization.EncodingTable | GetNumEncodingItems | static | nonpublic/nonpublic | System.Int32 () | ❌ | - | 内部 icall（public 包装承接） |
| System.Globalization.EncodingTable | GetCodePageData | static | nonpublic/nonpublic | System.Globalization.InternalCodePageDataItem* () | ❌ | - | 内部 icall（public 包装承接） |
| System.Globalization.EncodingTable | nativeCreateOpenFileMapping | static | nonpublic/nonpublic | System.Byte* (System.String inSectionName, System.Int32 inBytesToAllocate, System.IntPtr& mappedFileHandle) | ❌ | - | 内部 icall（public 包装承接） |
| System.Globalization.TextInfo | InternalChangeCaseChar | static | public/nonpublic | System.Char (System.IntPtr handle, System.IntPtr handleOrigin, System.String localeName, System.Char ch, System.Boolean isToUpper) | 🟡 | MethodMap.NumberFormat | 内部 icall（public 包装承接） |
| System.Globalization.TextInfo | InternalChangeCaseString | static | public/nonpublic | System.String (System.IntPtr handle, System.IntPtr handleOrigin, System.String localeName, System.String str, System.Boolean isToUpper) | 🟡 | MethodMap.NumberFormat | 内部 icall（public 包装承接） |
| System.Globalization.TextInfo | InternalGetCaseInsHash | static | public/nonpublic | System.Int32 (System.IntPtr handle, System.IntPtr handleOrigin, System.String localeName, System.String str, System.Boolean forceRandomizedHashing, System.Int64 additionalEntropy) | 🟡 | MethodMap.NumberFormat | 内部 icall（public 包装承接） |
| System.Globalization.CultureData | LCIDToLocaleName | static | nonpublic/nonpublic | System.String (System.Int32 lcid) | ❌ | - | 内部 icall（public 包装承接） |
| System.Globalization.CultureData | LocaleNameToLCID | static | nonpublic/nonpublic | System.Int32 (System.String localeName) | ❌ | - | 内部 icall（public 包装承接） |
| System.Globalization.CultureData | nativeInitCultureData | static | nonpublic/nonpublic | System.Boolean (System.Globalization.CultureData cultureData) | ❌ | - | 内部 icall（public 包装承接） |
| System.Globalization.CultureData | nativeGetNumberFormatInfoValues | static | nonpublic/nonpublic | System.Boolean (System.String localeName, System.Globalization.NumberFormatInfo nfi, System.Boolean useUserOverride) | ❌ | - | 内部 icall（public 包装承接） |
| System.Globalization.CultureData | nativeEnumTimeFormats | static | nonpublic/nonpublic | System.String[] (System.String localeName, System.UInt32 dwFlags, System.Boolean useUserOverride) | ❌ | - | 内部 icall（public 包装承接） |
| System.Diagnostics.Assert | ShowDefaultAssertDialog | static | nonpublic/nonpublic | System.Int32 (System.String conditionString, System.String message, System.String stackTrace, System.String windowTitle) | ❌ | - | 内部 icall（public 包装承接） |
| System.Diagnostics.Debugger | BreakInternal | static | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Diagnostics.Debugger | LaunchInternal | static | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Diagnostics.Debugger | Log | static | public/public | System.Void (System.Int32 level, System.String category, System.String message) | ❌ | - |  |
| System.Diagnostics.Debugger | CustomNotification | static | public/nonpublic | System.Void (System.Diagnostics.ICustomDebuggerNotification data) | ❌ | - | 内部 icall（public 包装承接） |
| System.Diagnostics.Debugger | get_IsAttached | static | public/public | System.Boolean () | ❌ | - |  |
| System.Diagnostics.Debugger | IsLogging | static | public/public | System.Boolean () | ❌ | - |  |
| System.Diagnostics.Log | AddLogSwitch | static | nonpublic/nonpublic | System.Void (System.Diagnostics.LogSwitch logSwitch) | ❌ | - | 内部 icall（public 包装承接） |
| System.Diagnostics.Log | ModifyLogSwitch | static | nonpublic/nonpublic | System.Void (System.Int32 iNewLevel, System.String strSwitchName, System.String strParentName) | ❌ | - | 内部 icall（public 包装承接） |
| System.Diagnostics.StackTrace | GetStackFramesInternal | static | public/nonpublic | System.Void (System.Diagnostics.StackFrameHelper sfh, System.Int32 iSkip, System.Boolean fNeedFileInfo, System.Exception e) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.SynchronizationContext | WaitHelper | static | public/nonpublic | System.Int32 (System.IntPtr[] waitHandles, System.Boolean waitAll, System.Int32 millisecondsTimeout) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.CompressedStack | GetDelayedCompressedStack | static | public/nonpublic | System.Threading.SafeCompressedStackHandle (System.Threading.StackCrawlMark& stackMark, System.Boolean walkStack) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.CompressedStack | DestroyDCSList | static | public/nonpublic | System.Void (System.Threading.SafeCompressedStackHandle compressedStack) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.CompressedStack | IsImmediateCompletionCandidate | static | public/nonpublic | System.Boolean (System.Threading.SafeCompressedStackHandle compressedStack, System.Threading.CompressedStack& innerCS) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.CompressedStack | DestroyDelayedCompressedStack | static | public/nonpublic | System.Void (System.IntPtr unmanagedCompressedStack) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.CompressedStack | GetDCSCount | static | public/nonpublic | System.Int32 (System.Threading.SafeCompressedStackHandle compressedStack) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.CompressedStack | GetDomainCompressedStack | static | public/nonpublic | System.Threading.DomainCompressedStack (System.Threading.SafeCompressedStackHandle compressedStack, System.Int32 index) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.CompressedStack | GetHomogeneousPLS | static | public/nonpublic | System.Void (System.Security.PermissionListSet hgPLS) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.DomainCompressedStack | GetDescCount | static | nonpublic/nonpublic | System.Int32 (System.IntPtr dcs) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.DomainCompressedStack | GetDomainPermissionSets | static | nonpublic/nonpublic | System.Void (System.IntPtr dcs, System.Security.PermissionSet& granted, System.Security.PermissionSet& refused) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.DomainCompressedStack | GetDescriptorInfo | static | nonpublic/nonpublic | System.Boolean (System.IntPtr dcs, System.Int32 index, System.Security.PermissionSet& granted, System.Security.PermissionSet& refused, System.Reflection.Assembly& assembly, System.Security.FrameSecurityDescriptor& fsd) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.DomainCompressedStack | IgnoreDomain | static | nonpublic/nonpublic | System.Boolean (System.IntPtr dcs) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.Interlocked | Exchange | static | public/public | System.Single (System.Single& location1, System.Single value) | ✅ | MethodMap.Interlocked |  |
| System.Threading.Interlocked | Exchange | static | public/public | System.Double (System.Double& location1, System.Double value) | ✅ | MethodMap.Interlocked |  |
| System.Threading.Interlocked | CompareExchange | static | public/nonpublic | System.Single (System.Single& location1, System.Single value, System.Single comparand) | ✅ | MethodMap.Interlocked | 内部 icall（public 包装承接） |
| System.Threading.Interlocked | CompareExchange | static | public/nonpublic | System.Double (System.Double& location1, System.Double value, System.Double comparand) | ✅ | MethodMap.Interlocked | 内部 icall（public 包装承接） |
| System.Threading.Interlocked | SpeculationBarrier | static | public/public | System.Void () | 🟡 | MethodMap.Interlocked | b类: 空操作语义 (speculation barrier) |
| System.Threading.Interlocked | Exchange | static | public/public | System.Int32 (System.Int32& location1, System.Int32 value) | ✅ | MethodMap.Interlocked |  |
| System.Threading.Interlocked | Exchange | static | public/public | System.Int64 (System.Int64& location1, System.Int64 value) | ✅ | MethodMap.Interlocked |  |
| System.Threading.Interlocked | Exchange | static | public/public | System.Object (System.Object& location1, System.Object value) | ✅ | MethodMap.Interlocked |  |
| System.Threading.Interlocked | Exchange | static | public/public | System.IntPtr (System.IntPtr& location1, System.IntPtr value) | ✅ | MethodMap.Interlocked |  |
| System.Threading.Interlocked | _Exchange | static | public/nonpublic | System.Void (System.TypedReference location1, System.TypedReference value) | 🟡 | MethodMap.Interlocked | 内部 icall（public 包装承接） |
| System.Threading.Interlocked | CompareExchange | static | public/nonpublic | System.Int32 (System.Int32& location1, System.Int32 value, System.Int32 comparand) | ✅ | MethodMap.Interlocked | 内部 icall（public 包装承接） |
| System.Threading.Interlocked | CompareExchange | static | public/nonpublic | System.Int64 (System.Int64& location1, System.Int64 value, System.Int64 comparand) | ✅ | MethodMap.Interlocked | 内部 icall（public 包装承接） |
| System.Threading.Interlocked | CompareExchange | static | public/nonpublic | System.Object (System.Object& location1, System.Object value, System.Object comparand) | ✅ | MethodMap.Interlocked | 内部 icall（public 包装承接） |
| System.Threading.Interlocked | CompareExchange | static | public/nonpublic | System.IntPtr (System.IntPtr& location1, System.IntPtr value, System.IntPtr comparand) | ✅ | MethodMap.Interlocked | 内部 icall（public 包装承接） |
| System.Threading.Interlocked | _CompareExchange | static | public/nonpublic | System.Void (System.TypedReference location1, System.TypedReference value, System.Object comparand) | 🟡 | MethodMap.Interlocked | 内部 icall（public 包装承接） |
| System.Threading.Interlocked | CompareExchange | static | public/nonpublic | System.Int32 (System.Int32& location1, System.Int32 value, System.Int32 comparand, System.Boolean& succeeded) | ✅ | MethodMap.Interlocked | 内部 icall（public 包装承接） |
| System.Threading.Interlocked | ExchangeAdd | static | public/nonpublic | System.Int32 (System.Int32& location1, System.Int32 value) | 🟡 | MethodMap.Interlocked | 内部 icall（public 包装承接） |
| System.Threading.Interlocked | ExchangeAdd | static | public/nonpublic | System.Int64 (System.Int64& location1, System.Int64 value) | 🟡 | MethodMap.Interlocked | 内部 icall（public 包装承接） |
| System.Threading.HostExecutionContextManager | HostSecurityManagerPresent | static | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.HostExecutionContextManager | ReleaseHostSecurityContext | static | public/nonpublic | System.Int32 (System.IntPtr context) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.HostExecutionContextManager | CloneHostSecurityContext | static | public/nonpublic | System.Int32 (System.Runtime.InteropServices.SafeHandle context, System.Runtime.InteropServices.SafeHandle clonedContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.HostExecutionContextManager | CaptureHostSecurityContext | static | public/nonpublic | System.Int32 (System.Runtime.InteropServices.SafeHandle capturedContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.HostExecutionContextManager | SetHostSecurityContext | static | public/nonpublic | System.Int32 (System.Runtime.InteropServices.SafeHandle context, System.Boolean fReturnPrevious, System.Runtime.InteropServices.SafeHandle prevContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.Monitor | Enter | static | public/public | System.Void (System.Object obj) | ✅ | MethodMap.Monitor |  |
| System.Threading.Monitor | ReliableEnter | static | public/nonpublic | System.Void (System.Object obj, System.Boolean& lockTaken) | 🟡 | MethodMap.Monitor | 内部 icall（public 包装承接） |
| System.Threading.Monitor | Exit | static | public/public | System.Void (System.Object obj) | ✅ | MethodMap.Monitor |  |
| System.Threading.Monitor | ReliableEnterTimeout | static | public/nonpublic | System.Void (System.Object obj, System.Int32 timeout, System.Boolean& lockTaken) | 🟡 | MethodMap.Monitor | 内部 icall（public 包装承接） |
| System.Threading.Monitor | IsEnteredNative | static | public/nonpublic | System.Boolean (System.Object obj) | 🟡 | MethodMap.Monitor | 内部 icall（public 包装承接） |
| System.Threading.Monitor | ObjWait | static | public/nonpublic | System.Boolean (System.Boolean exitContext, System.Int32 millisecondsTimeout, System.Object obj) | 🟡 | MethodMap.Monitor | 内部 icall（public 包装承接） |
| System.Threading.Monitor | ObjPulse | static | public/nonpublic | System.Void (System.Object obj) | 🟡 | MethodMap.Monitor | 内部 icall（public 包装承接） |
| System.Threading.Monitor | ObjPulseAll | static | public/nonpublic | System.Void (System.Object obj) | 🟡 | MethodMap.Monitor | 内部 icall（public 包装承接） |
| System.Threading.OverlappedData | AllocateNativeOverlapped | instance | nonpublic/nonpublic | System.Threading.NativeOverlapped* () | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.OverlappedData | FreeNativeOverlapped | static | nonpublic/nonpublic | System.Void (System.Threading.NativeOverlapped* nativeOverlappedPtr) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.OverlappedData | GetOverlappedFromNative | static | nonpublic/nonpublic | System.Threading.OverlappedData (System.Threading.NativeOverlapped* nativeOverlappedPtr) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.OverlappedData | CheckVMForIOPacket | static | nonpublic/nonpublic | System.Void (System.Threading.NativeOverlapped*& pOVERLAP, System.UInt32& errorCode, System.UInt32& numBytes) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | FCallReleaseLock | instance | public/nonpublic | System.Void (System.Threading.LockCookie& result) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | RestoreLockInternal | instance | public/nonpublic | System.Void (System.Threading.LockCookie& lockCookie) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | PrivateGetWriterSeqNum | instance | public/nonpublic | System.Int32 () | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | AnyWritersSince | instance | public/public | System.Boolean (System.Int32 seqNum) | ❌ | - |  |
| System.Threading.ReaderWriterLock | AcquireReaderLockInternal | instance | public/nonpublic | System.Void (System.Int32 millisecondsTimeout) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | AcquireWriterLockInternal | instance | public/nonpublic | System.Void (System.Int32 millisecondsTimeout) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | ReleaseReaderLockInternal | instance | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | ReleaseWriterLockInternal | instance | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | FCallUpgradeToWriterLock | instance | public/nonpublic | System.Void (System.Threading.LockCookie& result, System.Int32 millisecondsTimeout) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | DowngradeFromWriterLockInternal | instance | public/nonpublic | System.Void (System.Threading.LockCookie& lockCookie) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | PrivateGetIsReaderLockHeld | instance | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | PrivateGetIsWriterLockHeld | instance | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | PrivateInitialize | instance | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ReaderWriterLock | PrivateDestruct | instance | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.Thread | InternalGetCurrentThread | static | public/nonpublic | System.IntPtr () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | AbortInternal | instance | public/nonpublic | System.Void () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | ResetAbortNative | instance | public/nonpublic | System.Void () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | SuspendInternal | instance | public/nonpublic | System.Void () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | ResumeInternal | instance | public/nonpublic | System.Void () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | InterruptInternal | instance | public/nonpublic | System.Void () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | GetPriorityNative | instance | public/nonpublic | System.Int32 () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | JoinInternal | instance | public/nonpublic | System.Boolean (System.Int32 millisecondsTimeout) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | GetCurrentProcessorNumber | static | public/nonpublic | System.Int32 () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | IsBackgroundNative | instance | public/nonpublic | System.Boolean () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | GetContextInternal | static | public/nonpublic | System.Runtime.Remoting.Contexts.Context (System.IntPtr id) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | MemoryBarrier | static | public/public | System.Void () | 🟡 | MethodMap.Thread | b类: std::atomic_thread_fence 可映射 |
| System.Threading.Thread | SetAbortReason | instance | public/nonpublic | System.Void (System.Object o) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | GetAbortReason | instance | public/nonpublic | System.Object () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | ClearAbortReason | instance | public/nonpublic | System.Void () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | get_ManagedThreadId | instance | public/public | System.Int32 () | ✅ | MethodMap.Thread |  |
| System.Threading.Thread | StartInternal | instance | public/nonpublic | System.Void (System.Security.Principal.IPrincipal principal, System.Threading.StackCrawlMark& stackMark) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | SetAppDomainStack | instance | public/nonpublic | System.IntPtr (System.Threading.SafeCompressedStackHandle csHandle) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | RestoreAppDomainStack | instance | public/nonpublic | System.Void (System.IntPtr appDomainStack) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | SetPriorityNative | instance | public/nonpublic | System.Void (System.Int32 priority) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | get_IsAlive | instance | public/public | System.Boolean () | ✅ | MethodMap.Thread |  |
| System.Threading.Thread | get_IsThreadPoolThread | instance | public/public | System.Boolean () | 🟡 | MethodMap.Thread |  |
| System.Threading.Thread | SleepInternal | static | public/nonpublic | System.Void (System.Int32 millisecondsTimeout) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | SpinWaitInternal | static | public/nonpublic | System.Void (System.Int32 iterations) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | GetCurrentThreadNative | static | public/nonpublic | System.Threading.Thread () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | SetStart | instance | public/nonpublic | System.Void (System.Delegate start, System.Int32 maxStackSize) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | InternalFinalize | instance | public/nonpublic | System.Void () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | DisableComObjectEagerCleanup | instance | public/public | System.Void () | 🟡 | MethodMap.Thread |  |
| System.Threading.Thread | SetBackgroundNative | instance | public/nonpublic | System.Void (System.Boolean isBackground) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | GetThreadStateNative | instance | public/nonpublic | System.Int32 () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | GetApartmentStateNative | instance | public/nonpublic | System.Int32 () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | SetApartmentStateNative | instance | public/nonpublic | System.Int32 (System.Int32 state, System.Boolean fireMDAOnMismatch) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | StartupSetApartmentStateInternal | instance | public/nonpublic | System.Void () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | nativeGetSafeCulture | static | public/nonpublic | System.Boolean (System.Threading.Thread t, System.Int32 appDomainId, System.Boolean isUI, System.Globalization.CultureInfo& safeCulture) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | nativeSetThreadUILocale | static | public/nonpublic | System.Boolean (System.String locale) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | InternalCrossContextCallback | instance | public/nonpublic | System.Object (System.Runtime.Remoting.Contexts.Context ctx, System.IntPtr ctxID, System.Int32 appDomainID, System.Threading.InternalCrossContextDelegate ftnToCall, System.Object[] args) | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | GetDomainInternal | static | public/nonpublic | System.AppDomain () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | GetFastDomainInternal | static | public/nonpublic | System.AppDomain () | 🟡 | MethodMap.Thread | 内部 icall（public 包装承接） |
| System.Threading.Thread | BeginCriticalRegion | static | public/public | System.Void () | ✅ | MethodMap.Thread |  |
| System.Threading.Thread | EndCriticalRegion | static | public/public | System.Void () | ✅ | MethodMap.Thread |  |
| System.Threading.Thread | BeginThreadAffinity | static | public/public | System.Void () | 🟡 | MethodMap.Thread |  |
| System.Threading.Thread | EndThreadAffinity | static | public/public | System.Void () | 🟡 | MethodMap.Thread |  |
| System.Threading.RegisteredWaitHandleSafe | WaitHandleCleanupNative | static | nonpublic/nonpublic | System.Void (System.IntPtr handle) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.RegisteredWaitHandleSafe | UnregisterWaitNative | static | nonpublic/nonpublic | System.Boolean (System.IntPtr handle, System.Runtime.InteropServices.SafeHandle waitObject) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.ThreadPool | SetMinThreadsNative | static | public/nonpublic | System.Boolean (System.Int32 workerThreads, System.Int32 completionPortThreads) | 🟡 | MethodMap.ThreadPool | 内部 icall（public 包装承接） |
| System.Threading.ThreadPool | SetMaxThreadsNative | static | public/nonpublic | System.Boolean (System.Int32 workerThreads, System.Int32 completionPortThreads) | 🟡 | MethodMap.ThreadPool | 内部 icall（public 包装承接） |
| System.Threading.ThreadPool | GetMinThreadsNative | static | public/nonpublic | System.Void (System.Int32& workerThreads, System.Int32& completionPortThreads) | 🟡 | MethodMap.ThreadPool | 内部 icall（public 包装承接） |
| System.Threading.ThreadPool | ReportThreadStatus | static | public/nonpublic | System.Void (System.Boolean isWorking) | 🟡 | MethodMap.ThreadPool | 内部 icall（public 包装承接） |
| System.Threading.ThreadPool | PostQueuedCompletionStatus | static | public/nonpublic | System.Boolean (System.Threading.NativeOverlapped* overlapped) | 🟡 | MethodMap.ThreadPool | 内部 icall（public 包装承接） |
| System.Threading.ThreadPool | GetMaxThreadsNative | static | public/nonpublic | System.Void (System.Int32& workerThreads, System.Int32& completionPortThreads) | 🟡 | MethodMap.ThreadPool | 内部 icall（public 包装承接） |
| System.Threading.ThreadPool | GetAvailableThreadsNative | static | public/nonpublic | System.Void (System.Int32& workerThreads, System.Int32& completionPortThreads) | 🟡 | MethodMap.ThreadPool | 内部 icall（public 包装承接） |
| System.Threading.ThreadPool | NotifyWorkItemComplete | static | public/nonpublic | System.Boolean () | 🟡 | MethodMap.ThreadPool | 内部 icall（public 包装承接） |
| System.Threading.ThreadPool | NotifyWorkItemProgressNative | static | public/nonpublic | System.Void () | 🟡 | MethodMap.ThreadPool | 内部 icall（public 包装承接） |
| System.Threading.ThreadPool | IsThreadPoolHosted | static | public/nonpublic | System.Boolean () | 🟡 | MethodMap.ThreadPool | 内部 icall（public 包装承接） |
| System.Threading.ThreadPool | RegisterWaitForSingleObjectNative | static | public/nonpublic | System.IntPtr (System.Threading.WaitHandle waitHandle, System.Object state, System.UInt32 timeOutInterval, System.Boolean executeOnlyOnce, System.Threading.RegisteredWaitHandle registeredWaitHandle, System.Threading.StackCrawlMark& stackMark, System.Boolean compressStack) | 🟡 | MethodMap.ThreadPool | 内部 icall（public 包装承接） |
| System.Threading.ThreadPool | BindIOCompletionCallbackNative | static | public/nonpublic | System.Boolean (System.IntPtr fileHandle) | 🟡 | MethodMap.ThreadPool | 内部 icall（public 包装承接） |
| System.Threading.WaitHandle | SignalAndWaitOne | static | public/nonpublic | System.Int32 (Microsoft.Win32.SafeHandles.SafeWaitHandle waitHandleToSignal, Microsoft.Win32.SafeHandles.SafeWaitHandle waitHandleToWaitOn, System.Int32 millisecondsTimeout, System.Boolean hasThreadAffinity, System.Boolean exitContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.WaitHandle | WaitOneNative | static | public/nonpublic | System.Int32 (System.Runtime.InteropServices.SafeHandle waitableSafeHandle, System.UInt32 millisecondsTimeout, System.Boolean hasThreadAffinity, System.Boolean exitContext) | ❌ | - | 内部 icall（public 包装承接） |
| System.Threading.WaitHandle | WaitMultiple | static | public/nonpublic | System.Int32 (System.Threading.WaitHandle[] waitHandles, System.Int32 millisecondsTimeout, System.Boolean exitContext, System.Boolean WaitAll) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.ObjectMarshaler | ConvertToNative | static | nonpublic/nonpublic | System.Void (System.Object objSrc, System.IntPtr pDstVariant) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.ObjectMarshaler | ConvertToManaged | static | nonpublic/nonpublic | System.Object (System.IntPtr pSrcVariant) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.ObjectMarshaler | ClearNative | static | nonpublic/nonpublic | System.Void (System.IntPtr pVariant) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.ValueClassMarshaler | ConvertToNative | static | nonpublic/nonpublic | System.Void (System.IntPtr dst, System.IntPtr src, System.IntPtr pMT, System.StubHelpers.CleanupWorkList& pCleanupWorkList) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.ValueClassMarshaler | ConvertToManaged | static | nonpublic/nonpublic | System.Void (System.IntPtr dst, System.IntPtr src, System.IntPtr pMT) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.ValueClassMarshaler | ClearNative | static | nonpublic/nonpublic | System.Void (System.IntPtr dst, System.IntPtr pMT) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.DateMarshaler | ConvertToNative | static | nonpublic/nonpublic | System.Double (System.DateTime managedDate) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.DateMarshaler | ConvertToManaged | static | nonpublic/nonpublic | System.Int64 (System.Double nativeDate) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.InterfaceMarshaler | ConvertToNative | static | nonpublic/nonpublic | System.IntPtr (System.Object objSrc, System.IntPtr itfMT, System.IntPtr classMT, System.Int32 flags) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.InterfaceMarshaler | ConvertToManaged | static | nonpublic/nonpublic | System.Object (System.IntPtr pUnk, System.IntPtr itfMT, System.IntPtr classMT, System.Int32 flags) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.InterfaceMarshaler | ConvertToManagedWithoutUnboxing | static | nonpublic/nonpublic | System.Object (System.IntPtr pNative) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.UriMarshaler | GetRawUriFromNative | static | nonpublic/nonpublic | System.String (System.IntPtr pUri) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.UriMarshaler | CreateNativeUriInstanceHelper | static | nonpublic/nonpublic | System.IntPtr (System.Char* rawUri, System.Int32 strLen) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdNativeArrayMarshaler | ClearNativeContents | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.IntPtr pNativeHome, System.Int32 cElements) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdNativeArrayMarshaler | CreateMarshaler | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.IntPtr pMT, System.Int32 dwFlags) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdNativeArrayMarshaler | ConvertSpaceToNative | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdNativeArrayMarshaler | ConvertContentsToNative | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdNativeArrayMarshaler | ConvertSpaceToManaged | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome, System.Int32 cElements) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdNativeArrayMarshaler | ConvertContentsToManaged | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdNativeArrayMarshaler | ClearNative | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.IntPtr pNativeHome, System.Int32 cElements) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdSafeArrayMarshaler | CreateMarshaler | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.IntPtr pMT, System.Int32 iRank, System.Int32 dwFlags) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdSafeArrayMarshaler | ConvertSpaceToNative | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdSafeArrayMarshaler | ConvertContentsToNative | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome, System.Object pOriginalManaged) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdSafeArrayMarshaler | ConvertSpaceToManaged | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdSafeArrayMarshaler | ConvertContentsToManaged | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdSafeArrayMarshaler | ClearNative | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdHiddenLengthArrayMarshaler | CreateMarshaler | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.IntPtr pMT, System.IntPtr cbElementSize, System.UInt16 vt) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdHiddenLengthArrayMarshaler | ConvertSpaceToNative | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdHiddenLengthArrayMarshaler | ConvertContentsToNative | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdHiddenLengthArrayMarshaler | ConvertSpaceToManaged | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome, System.Int32 elementCount) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdHiddenLengthArrayMarshaler | ConvertContentsToManaged | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdHiddenLengthArrayMarshaler | ClearNativeContents | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.IntPtr pNativeHome, System.Int32 cElements) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdRefCustomMarshaler | CreateMarshaler | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.IntPtr pCMHelper) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdRefCustomMarshaler | ConvertContentsToNative | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdRefCustomMarshaler | ConvertContentsToManaged | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdRefCustomMarshaler | ClearNative | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.MngdRefCustomMarshaler | ClearManaged | static | nonpublic/nonpublic | System.Void (System.IntPtr pMarshalState, System.Object& pManagedHome, System.IntPtr pNativeHome) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.WinRTTypeNameConverter | ConvertToWinRTTypeName | static | nonpublic/nonpublic | System.String (System.Type managedType, System.Boolean& isPrimitive) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.WinRTTypeNameConverter | GetTypeFromWinRTTypeName | static | nonpublic/nonpublic | System.Type (System.String typeName, System.Boolean& isPrimitive) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | InternalGetHRExceptionObject | static | nonpublic/nonpublic | System.Exception (System.Int32 hr) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | CreateCustomMarshalerHelper | static | nonpublic/nonpublic | System.IntPtr (System.IntPtr pMD, System.Int32 paramToken, System.IntPtr hndManagedType) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetStubContext | static | nonpublic/nonpublic | System.IntPtr () | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | InitDeclaringType | static | nonpublic/nonpublic | System.Void (System.IntPtr pMD) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetDelegateTarget | static | nonpublic/nonpublic | System.IntPtr (System.Delegate pThis, System.IntPtr& pStubArg) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | DemandPermission | static | nonpublic/nonpublic | System.Void (System.IntPtr pNMD) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | SetLastError | static | nonpublic/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | InternalGetCOMHRExceptionObject | static | nonpublic/nonpublic | System.Exception (System.Int32 hr, System.IntPtr pCPCMD, System.Object pThis, System.Boolean fForWinRT) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetCOMIPFromRCW | static | nonpublic/nonpublic | System.IntPtr (System.Object objSrc, System.IntPtr pCPCMD, System.IntPtr& ppTarget, System.Boolean& pfNeedsRelease) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetCOMIPFromRCW_WinRT | static | nonpublic/nonpublic | System.IntPtr (System.Object objSrc, System.IntPtr pCPCMD, System.IntPtr& ppTarget) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | CheckCollectedDelegateMDA | static | nonpublic/nonpublic | System.Void (System.IntPtr pEntryThunk) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | strlen | static | nonpublic/nonpublic | System.Int32 (System.SByte* ptr) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | FmtClassUpdateNativeInternal | static | nonpublic/nonpublic | System.Void (System.Object obj, System.Byte* pNative, System.StubHelpers.CleanupWorkList& pCleanupWorkList) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | FmtClassUpdateCLRInternal | static | nonpublic/nonpublic | System.Void (System.Object obj, System.Byte* pNative) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | LayoutDestroyNativeInternal | static | nonpublic/nonpublic | System.Void (System.Byte* pNative, System.IntPtr pMT) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | AllocateInternal | static | nonpublic/nonpublic | System.Object (System.IntPtr typeHandle) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | IsQCall | static | nonpublic/nonpublic | System.Boolean (System.IntPtr pMD) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetNDirectTarget | static | nonpublic/nonpublic | System.IntPtr (System.IntPtr pMD) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetFinalStubTarget | static | ?/? | System.IntPtr (System.IntPtr pStubArg, System.IntPtr pUnmngThis, System.Int32 dwStubFlags) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | ThrowInteropParamException | static | nonpublic/nonpublic | System.Void (System.Int32 resID, System.Int32 paramIdx) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetCOMIPFromRCW_WinRTSharedGeneric | static | nonpublic/nonpublic | System.IntPtr (System.Object objSrc, System.IntPtr pCPCMD, System.IntPtr& ppTarget) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetCOMIPFromRCW_WinRTDelegate | static | nonpublic/nonpublic | System.IntPtr (System.Object objSrc, System.IntPtr pCPCMD, System.IntPtr& ppTarget) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | ShouldCallWinRTInterface | static | nonpublic/nonpublic | System.Boolean (System.Object objSrc, System.IntPtr pCPCMD) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetTargetForAmbiguousVariantCall | static | nonpublic/nonpublic | System.Delegate (System.Object objSrc, System.IntPtr pMT, System.Boolean& fUseString) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | StubRegisterRCW | static | nonpublic/nonpublic | System.Void (System.Object pThis) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | StubUnregisterRCW | static | nonpublic/nonpublic | System.Void (System.Object pThis) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetDelegateInvokeMethod | static | nonpublic/nonpublic | System.IntPtr (System.Delegate pThis) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetWinRTFactoryObject | static | nonpublic/nonpublic | System.Object (System.IntPtr pCPCMD) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetWinRTFactoryReturnValue | static | nonpublic/nonpublic | System.IntPtr (System.Object pThis, System.IntPtr pCtorEntry) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | GetOuterInspectable | static | nonpublic/nonpublic | System.IntPtr (System.Object pThis, System.IntPtr pCtorMD) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | TriggerExceptionSwallowedMDA | static | nonpublic/nonpublic | System.Exception (System.Exception ex, System.IntPtr pManagedTarget) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | ProfilerBeginTransitionCallback | static | nonpublic/nonpublic | System.IntPtr (System.IntPtr pSecretParam, System.IntPtr pThread, System.Object pThis) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | ProfilerEndTransitionCallback | static | nonpublic/nonpublic | System.Void (System.IntPtr pMD, System.IntPtr pThread) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | DecimalCanonicalizeInternal | static | nonpublic/nonpublic | System.Void (System.Decimal& dec) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | MarshalToUnmanagedVaListInternal | static | nonpublic/nonpublic | System.Void (System.IntPtr va_list, System.UInt32 vaListSize, System.IntPtr pArgIterator) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | MarshalToManagedVaListInternal | static | nonpublic/nonpublic | System.Void (System.IntPtr va_list, System.IntPtr pArgIterator) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | CalcVaListSize | static | nonpublic/nonpublic | System.UInt32 (System.IntPtr va_list) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | ValidateObject | static | nonpublic/nonpublic | System.Void (System.Object obj, System.IntPtr pMD, System.Object pThis) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | LogPinnedArgument | static | nonpublic/nonpublic | System.Void (System.IntPtr localDesc, System.IntPtr nativeArg) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | ValidateByref | static | nonpublic/nonpublic | System.Void (System.IntPtr byref, System.IntPtr pMD, System.Object pThis) | ❌ | - | 内部 icall（public 包装承接） |
| System.StubHelpers.StubHelpers | TriggerGCForMDA | static | nonpublic/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | _nLoad | static | nonpublic/nonpublic | System.Reflection.RuntimeAssembly (System.Reflection.AssemblyName fileName, System.String codeBase, System.Security.Policy.Evidence assemblySecurity, System.Reflection.RuntimeAssembly locationHint, System.Threading.StackCrawlMark& stackMark, System.IntPtr pPrivHostBinder, System.Boolean throwOnFileNotFound, System.Boolean forIntrospection, System.Boolean suppressSecurityChecks) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | IsFrameworkAssembly | static | nonpublic/nonpublic | System.Boolean (System.Reflection.AssemblyName assemblyName) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | IsReflectionOnly | static | nonpublic/nonpublic | System.Boolean (System.Reflection.RuntimeAssembly assembly) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | GetManifestResourceNames | static | nonpublic/nonpublic | System.String[] (System.Reflection.RuntimeAssembly assembly) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | IsGlobalAssemblyCache | static | nonpublic/nonpublic | System.Boolean (System.Reflection.RuntimeAssembly assembly) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | FCallIsDynamic | static | nonpublic/nonpublic | System.Boolean (System.Reflection.RuntimeAssembly assembly) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | GetManifestModule | static | nonpublic/nonpublic | System.Reflection.RuntimeModule (System.Reflection.RuntimeAssembly assembly) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | AptcaCheck | static | nonpublic/nonpublic | System.Boolean (System.Reflection.RuntimeAssembly targetAssembly, System.Reflection.RuntimeAssembly sourceAssembly) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | GetToken | static | nonpublic/nonpublic | System.Int32 (System.Reflection.RuntimeAssembly assembly) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | IsNewPortableAssembly | static | nonpublic/nonpublic | System.Boolean (System.Reflection.AssemblyName assemblyName) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | GetReferencedAssemblies | static | nonpublic/nonpublic | System.Reflection.AssemblyName[] (System.Reflection.RuntimeAssembly assembly) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | nLoadFile | static | nonpublic/nonpublic | System.Reflection.RuntimeAssembly (System.String path, System.Security.Policy.Evidence evidence) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeAssembly | nLoadImage | static | nonpublic/nonpublic | System.Reflection.RuntimeAssembly (System.Byte[] rawAssembly, System.Byte[] rawSymbolStore, System.Security.Policy.Evidence evidence, System.Threading.StackCrawlMark& stackMark, System.Boolean fIntrospection, System.Boolean fSkipIntegrityCheck, System.Security.SecurityContextSource securityContextSource) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.AssemblyName | EscapeCodeBase | static | public/nonpublic | System.String (System.String codeBase) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.AssemblyName | ReferenceMatchesDefinitionInternal | static | public/nonpublic | System.Boolean (System.Reflection.AssemblyName reference, System.Reflection.AssemblyName definition, System.Boolean parse) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.AssemblyName | nInit | instance | public/nonpublic | System.Void (System.Reflection.RuntimeAssembly& assembly, System.Boolean forIntrospection, System.Boolean raiseResolveEvent) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.AssemblyName | nGetFileInformation | static | public/nonpublic | System.Reflection.AssemblyName (System.String s) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.AssemblyName | nToString | instance | public/nonpublic | System.String () | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.AssemblyName | nGetPublicKeyToken | instance | public/nonpublic | System.Byte[] () | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.CustomAttributeEncodedArgument | ParseAttributeArguments | static | nonpublic/nonpublic | System.Void (System.IntPtr pCa, System.Int32 cCa, System.Reflection.CustomAttributeCtorParameter[]& CustomAttributeCtorParameters, System.Reflection.CustomAttributeNamedParameter[]& CustomAttributeTypedArgument, System.Reflection.RuntimeAssembly assembly) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.SecurityContextFrame | Push | instance | nonpublic/public | System.Void (System.Reflection.RuntimeAssembly assembly) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.SecurityContextFrame | Pop | instance | nonpublic/public | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.CustomAttribute | _ParseAttributeUsageAttribute | static | nonpublic/nonpublic | System.Void (System.IntPtr pCa, System.Int32 cCa, System.Int32& targets, System.Boolean& inherited, System.Boolean& allowMultiple) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.CustomAttribute | _CreateCaObject | static | nonpublic/nonpublic | System.Object (System.Reflection.RuntimeModule pModule, System.IRuntimeMethodInfo pCtor, System.Byte** ppBlob, System.Byte* pEndBlob, System.Int32* pcNamedArgs) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.CustomAttribute | _GetPropertyOrFieldData | static | nonpublic/nonpublic | System.Void (System.Reflection.RuntimeModule pModule, System.Byte** ppBlobStart, System.Byte* pBlobEnd, System.String& name, System.Boolean& bIsProperty, System.RuntimeType& type, System.Object& value) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.PseudoCustomAttribute | _GetSecurityAttributes | static | nonpublic/nonpublic | System.Void (System.Reflection.RuntimeModule module, System.Int32 token, System.Boolean assembly, System.Object[]& securityAttributes) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RtFieldInfo | PerformVisibilityCheckOnField | static | nonpublic/nonpublic | System.Void (System.IntPtr field, System.Object target, System.RuntimeType declaringType, System.Reflection.FieldAttributes attr, System.UInt32 invocationFlags) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _Enum | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 type, System.Int32 parent, System.Reflection.MetadataEnumResult& result) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetDefaultValue | static | nonpublic/nonpublic | System.String (System.IntPtr scope, System.Int32 mdToken, System.Int64& value, System.Int32& length, System.Int32& corElementType) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetName | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 mdToken, System.Void** name) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetNamespace | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 mdToken, System.Void** namesp) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetEventProps | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 mdToken, System.Void** name, System.Int32& eventAttributes) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetFieldDefProps | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 mdToken, System.Int32& fieldAttributes) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetPropertyProps | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 mdToken, System.Void** name, System.Int32& propertyAttributes, System.Reflection.ConstArray& signature) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetParentToken | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 mdToken, System.Int32& tkParent) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetParamDefProps | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 parameterToken, System.Int32& sequence, System.Int32& attributes) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetScopeProps | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Guid& mvid) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetSigOfMethodDef | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 methodToken, System.Reflection.ConstArray& signature) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetMemberRefProps | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 memberTokenRef, System.Reflection.ConstArray& signature) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetCustomAttributeProps | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 customAttributeToken, System.Int32& constructorToken, System.Reflection.ConstArray& signature) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetFieldOffset | static | nonpublic/nonpublic | System.Boolean (System.IntPtr scope, System.Int32 typeTokenDef, System.Int32 fieldTokenDef, System.Int32& offset) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetSigOfFieldDef | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 fieldToken, System.Reflection.ConstArray& fieldMarshal) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetFieldMarshal | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 fieldToken, System.Reflection.ConstArray& fieldMarshal) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetPInvokeMap | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 token, System.Int32& attributes, System.Void** importName, System.Void** importDll) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _IsValidToken | static | nonpublic/nonpublic | System.Boolean (System.IntPtr scope, System.Int32 token) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetMarshalAs | static | nonpublic/nonpublic | System.Void (System.IntPtr pNativeType, System.Int32 cNativeType, System.Int32& unmanagedType, System.Int32& safeArraySubType, System.String& safeArrayUserDefinedSubType, System.Int32& arraySubType, System.Int32& sizeParamIndex, System.Int32& sizeConst, System.String& marshalType, System.String& marshalCookie, System.Int32& iidParamIndex) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetUserString | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 mdToken, System.Void** name, System.Int32& length) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetGenericParamProps | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 genericParameter, System.Int32& flags) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetSignatureFromToken | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 methodToken, System.Reflection.ConstArray& signature) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.MetadataImport | _GetClassLayout | static | nonpublic/nonpublic | System.Void (System.IntPtr scope, System.Int32 typeTokenDef, System.Int32& packSize, System.Int32& classSize) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeModule | GetTypes | static | nonpublic/nonpublic | System.RuntimeType[] (System.Reflection.RuntimeModule module) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.RuntimeModule | IsResource | static | nonpublic/nonpublic | System.Boolean (System.Reflection.RuntimeModule module) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.Emit.AssemblyBuilder | GetOnDiskAssemblyModule | static | public/nonpublic | System.Reflection.RuntimeModule (System.Reflection.RuntimeAssembly assembly) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.Emit.AssemblyBuilder | GetInMemoryAssemblyModule | static | public/nonpublic | System.Reflection.RuntimeModule (System.Reflection.RuntimeAssembly assembly) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.Emit.AssemblyBuilder | nCreateDynamicAssembly | static | public/nonpublic | System.Reflection.Assembly (System.AppDomain domain, System.Reflection.AssemblyName name, System.Security.Policy.Evidence identity, System.Threading.StackCrawlMark& stackMark, System.Security.PermissionSet requiredPermissions, System.Security.PermissionSet optionalPermissions, System.Security.PermissionSet refusedPermissions, System.Byte[] securityRulesBlob, System.Byte[] aptcaBlob, System.Reflection.Emit.AssemblyBuilderAccess access, System.Reflection.Emit.DynamicAssemblyFlags flags, System.Security.SecurityContextSource securityContextSource) | ❌ | - | 内部 icall（public 包装承接） |
| System.Reflection.Emit.ModuleBuilder | nCreateISymWriterForDynamicModule | static | public/nonpublic | System.IntPtr (System.Reflection.Module module, System.String filename) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.MemoryFailPoint | GetMemorySettings | static | public/nonpublic | System.Void (System.UInt64& maxGCSegmentSize, System.UInt64& topOfMemory) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Versioning.VersioningHelper | GetRuntimeId | static | public/nonpublic | System.Int32 () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Versioning.CompatibilitySwitch | GetAppContextOverridesInternalCall | static | public/nonpublic | System.String () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Versioning.CompatibilitySwitch | GetValueInternalCall | static | public/nonpublic | System.String (System.String compatibilitySwitchName, System.Boolean onlyDB) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Versioning.CompatibilitySwitch | IsEnabledInternalCall | static | public/nonpublic | System.Boolean (System.String compatibilitySwitchName, System.Boolean onlyDB) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Serialization.FormatterServices | nativeGetSafeUninitializedObject | static | public/nonpublic | System.Object (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Serialization.FormatterServices | nativeGetUninitializedObject | static | public/nonpublic | System.Object (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Serialization.FormatterServices | GetEnableUnsafeTypeForwarders | static | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | CallDefaultCtor | static | public/nonpublic | System.Void (System.Object o) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | AllocateInitializedObject | static | public/nonpublic | System.MarshalByRefObject (System.RuntimeType objectType) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | nSetRemoteActivationConfigured | static | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | Unwrap | static | public/nonpublic | System.Object (System.ContextBoundObject obj) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | AlwaysUnwrap | static | public/nonpublic | System.Object (System.ContextBoundObject obj) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | CheckCast | static | public/nonpublic | System.Object (System.Object objToExpand, System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | CORProfilerTrackRemotingCookie | static | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | CORProfilerTrackRemotingAsync | static | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | CORProfilerRemotingClientSendingMessage | static | public/nonpublic | System.Void (System.Guid& id, System.Boolean fIsAsync) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | CORProfilerRemotingClientReceivingReply | static | public/nonpublic | System.Void (System.Guid id, System.Boolean fIsAsync) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | CORProfilerRemotingServerReceivingMessage | static | public/nonpublic | System.Void (System.Guid id, System.Boolean fIsAsync) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | CORProfilerRemotingServerSendingReply | static | public/nonpublic | System.Void (System.Guid& id, System.Boolean fIsAsync) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | ResetInterfaceCache | static | public/nonpublic | System.Void (System.Object proxy) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | IsTransparentProxy | static | public/public | System.Boolean (System.Object proxy) | ❌ | - |  |
| System.Runtime.Remoting.RemotingServices | GetRealProxy | static | public/public | System.Runtime.Remoting.Proxies.RealProxy (System.Object proxy) | ❌ | - |  |
| System.Runtime.Remoting.RemotingServices | CreateTransparentProxy | static | public/nonpublic | System.Object (System.Runtime.Remoting.Proxies.RealProxy rp, System.RuntimeType typeToProxy, System.IntPtr stub, System.Object stubData) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | AllocateUninitializedObject | static | public/nonpublic | System.MarshalByRefObject (System.RuntimeType objectType) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.RemotingServices | CORProfilerTrackRemoting | static | public/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Proxies.RealProxy | GetDefaultStub | static | public/nonpublic | System.IntPtr () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Proxies.RealProxy | GetStub | instance | public/nonpublic | System.IntPtr () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Proxies.RealProxy | SetStubData | static | public/public | System.Void (System.Runtime.Remoting.Proxies.RealProxy rp, System.Object stubData) | ❌ | - |  |
| System.Runtime.Remoting.Proxies.RealProxy | GetStubData | static | public/public | System.Object (System.Runtime.Remoting.Proxies.RealProxy rp) | ❌ | - |  |
| System.Runtime.Remoting.Proxies.RealProxy | GetProxiedType | instance | public/public | System.Type () | ❌ | - |  |
| System.Runtime.Remoting.Contexts.Context | SetupInternalContext | instance | public/nonpublic | System.Void (System.Boolean bDefault) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Contexts.Context | CleanupInternalContext | instance | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Contexts.Context | ExecuteCallBackInEE | static | public/nonpublic | System.Void (System.IntPtr privateData) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Channels.ChannelServices | GetPrivateContextsPerfCounters | static | public/nonpublic | System.Runtime.Remoting.Channels.Perf_Contexts* () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Messaging.Message | InternalHasVarArgs | instance | nonpublic/nonpublic | System.Boolean () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Messaging.Message | InternalGetArg | instance | nonpublic/nonpublic | System.Object (System.Int32 argNum) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Messaging.Message | InternalGetArgs | instance | nonpublic/nonpublic | System.Object[] () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Messaging.Message | PropagateOutParameters | instance | nonpublic/public | System.Void (System.Object[] OutArgs, System.Object retVal) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Messaging.Message | GetAsyncBeginInfo | instance | nonpublic/public | System.Void (System.AsyncCallback& acbd, System.Object& state) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Messaging.Message | GetThisPtr | instance | nonpublic/public | System.Object () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Messaging.Message | GetAsyncResult | instance | nonpublic/public | System.IAsyncResult () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Messaging.Message | GetReturnValue | instance | nonpublic/public | System.Object () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Messaging.Message | InternalGetArgCount | instance | nonpublic/nonpublic | System.Int32 () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Messaging.Message | Dispatch | instance | nonpublic/public | System.Boolean (System.Object target) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Messaging.Message | OutToUnmanagedDebugger | static | nonpublic/nonpublic | System.Void (System.String s) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.Remoting.Messaging.StackBuilderSink | _PrivateProcessMessage | instance | nonpublic/nonpublic | System.Object (System.IntPtr md, System.Object[] args, System.Object server, System.Object[]& outArgs) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.CompilerServices.RuntimeHelpers | _RunModuleConstructor | static | public/nonpublic | System.Void (System.Reflection.RuntimeModule module) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.CompilerServices.RuntimeHelpers | _PrepareMethod | static | public/nonpublic | System.Void (System.IRuntimeMethodInfo method, System.IntPtr* pInstantiation, System.Int32 cInstantiation) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.CompilerServices.RuntimeHelpers | InitializeArray | static | public/public | System.Void (System.Array array, System.RuntimeFieldHandle fldHandle) | ❌ | - | b类: StackFold InitializeArray fold (ldtoken+RVA) |
| System.Runtime.CompilerServices.RuntimeHelpers | GetObjectValue | static | public/public | System.Object (System.Object obj) | ❌ | - | b类: 装箱语义, 转译器 box 已有 |
| System.Runtime.CompilerServices.RuntimeHelpers | _RunClassConstructor | static | public/nonpublic | System.Void (System.RuntimeType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.CompilerServices.RuntimeHelpers | PrepareDelegate | static | public/public | System.Void (System.Delegate d) | ❌ | - |  |
| System.Runtime.CompilerServices.RuntimeHelpers | PrepareContractedDelegate | static | public/public | System.Void (System.Delegate d) | ❌ | - |  |
| System.Runtime.CompilerServices.RuntimeHelpers | GetHashCode | static | public/public | System.Int32 (System.Object o) | ❌ | - | b类: 引用哈希, 可映射 il2cpp_object_get_hash |
| System.Runtime.CompilerServices.RuntimeHelpers | Equals | static | public/public | System.Boolean (System.Object o1, System.Object o2) | ❌ | - | b类: 引用相等, 可映射指针 == |
| System.Runtime.CompilerServices.RuntimeHelpers | EnsureSufficientExecutionStack | static | public/public | System.Void () | ❌ | - |  |
| System.Runtime.CompilerServices.RuntimeHelpers | ProbeForSufficientStack | static | public/public | System.Void () | ❌ | - |  |
| System.Runtime.CompilerServices.RuntimeHelpers | ExecuteCodeWithGuaranteedCleanup | static | public/public | System.Void (System.Runtime.CompilerServices.RuntimeHelpers+TryCode code, System.Runtime.CompilerServices.RuntimeHelpers+CleanupCode backoutCode, System.Object userData) | ❌ | - |  |
| System.Runtime.CompilerServices.JitHelpers | UnsafeSetArrayElement | static | nonpublic/nonpublic | System.Void (System.Object[] target, System.Int32 index, System.Object element) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.CompilerServices.DependentHandle | nInitialize | static | nonpublic/nonpublic | System.Void (System.Object primary, System.Object secondary, System.IntPtr& dependentHandle) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.CompilerServices.DependentHandle | nGetPrimary | static | nonpublic/nonpublic | System.Void (System.IntPtr dependentHandle, System.Object& primary) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.CompilerServices.DependentHandle | nGetPrimaryAndSecondary | static | nonpublic/nonpublic | System.Void (System.IntPtr dependentHandle, System.Object& primary, System.Object& secondary) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.CompilerServices.DependentHandle | nFree | static | nonpublic/nonpublic | System.Void (System.IntPtr dependentHandle) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.ArrayWithOffset | CalculateCount | instance | public/nonpublic | System.Int32 () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.CriticalHandle | FireCustomerDebugProbe | instance | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.GCHandle | InternalGetHandleType | static | public/nonpublic | System.Runtime.InteropServices.GCHandleType (System.IntPtr handle) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.GCHandle | InternalAlloc | static | public/nonpublic | System.IntPtr (System.Object value, System.Runtime.InteropServices.GCHandleType type) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.GCHandle | InternalFree | static | public/nonpublic | System.Void (System.IntPtr handle) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.GCHandle | InternalGet | static | public/nonpublic | System.Object (System.IntPtr handle) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.GCHandle | InternalSet | static | public/nonpublic | System.Void (System.IntPtr handle, System.Object value, System.Boolean isPinned) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.GCHandle | InternalCompareExchange | static | public/nonpublic | System.Object (System.IntPtr handle, System.Object value, System.Object oldValue, System.Boolean isPinned) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.GCHandle | InternalAddrOfPinnedObject | static | public/nonpublic | System.IntPtr (System.IntPtr handle) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.GCHandle | InternalCheckDomain | static | public/nonpublic | System.Void (System.IntPtr handle) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | ThrowExceptionForHRInternal | static | public/nonpublic | System.Void (System.Int32 errorCode, System.IntPtr errorInfo) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetExceptionForHRInternal | static | public/nonpublic | System.Exception (System.Int32 errorCode, System.IntPtr errorInfo) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetHRForException | static | public/public | System.Int32 (System.Exception e) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetHRForException_WinRT | static | public/nonpublic | System.Int32 (System.Exception e) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetUnmanagedThunkForManagedMethodPtr | static | public/public | System.IntPtr (System.IntPtr pfnMethodToWrap, System.IntPtr pbSignature, System.Int32 cbSignature) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetManagedThunkForUnmanagedMethodPtr | static | public/public | System.IntPtr (System.IntPtr pfnMethodToWrap, System.IntPtr pbSignature, System.Int32 cbSignature) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | InternalGetThreadFromFiberCookie | static | public/nonpublic | System.Threading.Thread (System.Int32 cookie) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | FCallGetTypeLibGuid | static | public/nonpublic | System.Void (System.Guid& result, System.Runtime.InteropServices.ComTypes.ITypeLib pTLB) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetTypeLibLcid | static | public/public | System.Int32 (System.Runtime.InteropServices.ComTypes.ITypeLib typelib) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetTypeLibVersion | static | public/nonpublic | System.Void (System.Runtime.InteropServices.ComTypes.ITypeLib typeLibrary, System.Int32& major, System.Int32& minor) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | FCallGetTypeInfoGuid | static | public/nonpublic | System.Void (System.Guid& result, System.Runtime.InteropServices.ComTypes.ITypeInfo typeInfo) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | FCallGetTypeLibGuidForAssembly | static | public/nonpublic | System.Void (System.Guid& result, System.Reflection.RuntimeAssembly asm) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | _GetTypeLibVersionForAssembly | static | public/nonpublic | System.Void (System.Reflection.RuntimeAssembly inputAssembly, System.Int32& majorVersion, System.Int32& minorVersion) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetLoadedTypeForGUID | static | public/nonpublic | System.Type (System.Guid& guid) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetITypeInfoForType | static | public/public | System.IntPtr (System.Type t) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetRawIUnknownForComObjectNoAddRef | static | public/nonpublic | System.IntPtr (System.Object o) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetIDispatchForObjectNative | static | public/nonpublic | System.IntPtr (System.Object o, System.Boolean onlyInContext) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetTypedObjectForIUnknown | static | public/public | System.Object (System.IntPtr pUnk, System.Type t) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | CreateAggregatedObject | static | public/public | System.IntPtr (System.IntPtr pOuter, System.Object o) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | InternalFinalReleaseComObject | static | public/nonpublic | System.Void (System.Object o) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | InternalCreateWrapperOfType | static | public/nonpublic | System.Object (System.Object o, System.Type t) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | IsTypeVisibleFromCom | static | public/public | System.Boolean (System.Type t) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | AddRef | static | public/public | System.Int32 (System.IntPtr pUnk) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetNativeVariantForObject | static | public/public | System.Void (System.Object obj, System.IntPtr pDstNativeVariant) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetObjectForNativeVariant | static | public/public | System.Object (System.IntPtr pSrcNativeVariant) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetObjectsForNativeVariants | static | public/public | System.Object[] (System.IntPtr aSrcNativeVariant, System.Int32 cVars) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetStartComSlot | static | public/public | System.Int32 (System.Type t) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetEndComSlot | static | public/public | System.Int32 (System.Type t) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetMethodInfoForComSlot | static | public/public | System.Reflection.MemberInfo (System.Type t, System.Int32 slot, System.Runtime.InteropServices.ComMemberType& memberType) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | InternalGetComSlotForMethodInfo | static | public/nonpublic | System.Int32 (System.IRuntimeMethodInfo m) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | FCallGenerateGuidForType | static | public/nonpublic | System.Void (System.Guid& result, System.Type type) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | InternalSwitchCCW | static | public/nonpublic | System.Boolean (System.Object oldtp, System.Object newtp) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | InternalWrapIUnknownWithComObject | static | public/nonpublic | System.Object (System.IntPtr i) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | ChangeWrapperHandleStrength | static | public/public | System.Void (System.Object otp, System.Boolean fIsWeak) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | InitializeWrapperForWinRT | static | public/nonpublic | System.Void (System.Object o, System.IntPtr& pUnk) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | InitializeManagedWinRTFactoryObject | static | public/nonpublic | System.Void (System.Object o, System.RuntimeType runtimeClassType) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetNativeActivationFactory | static | public/nonpublic | System.Object (System.Type type) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetSystemMaxDBCSCharSize | static | public/nonpublic | System.Int32 () | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | SizeOfType | static | public/nonpublic | System.UInt32 (System.Type type) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | AlignedSizeOfType | static | public/nonpublic | System.UInt32 (System.Type type) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | SizeOfHelper | static | public/nonpublic | System.Int32 (System.Type t, System.Boolean throwIfNotMarshalable) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | OffsetOfHelper | static | public/nonpublic | System.IntPtr (System.IRuntimeFieldInfo f) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | UnsafeAddrOfPinnedArrayElement | static | public/public | System.IntPtr (System.Array arr, System.Int32 index) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | CopyToNative | static | public/nonpublic | System.Void (System.Object source, System.Int32 startIndex, System.IntPtr destination, System.Int32 length) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | CopyToManaged | static | public/nonpublic | System.Void (System.IntPtr source, System.Object destination, System.Int32 startIndex, System.Int32 length) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetLastWin32Error | static | public/public | System.Int32 () | ✅ | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | SetLastWin32Error | static | public/nonpublic | System.Void (System.Int32 error) | ✅ | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetExceptionPointers | static | public/public | System.IntPtr () | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetExceptionCode | static | public/public | System.Int32 () | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | StructureToPtr | static | public/public | System.Void (System.Object structure, System.IntPtr ptr, System.Boolean fDeleteOld) | ✅ | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | PtrToStructureHelper | static | public/nonpublic | System.Void (System.IntPtr ptr, System.Object structure, System.Boolean allowValueClasses) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | DestroyStructure | static | public/public | System.Void (System.IntPtr ptr, System.Type structuretype) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetIUnknownForObjectNative | static | public/nonpublic | System.IntPtr (System.Object o, System.Boolean onlyInContext) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetComInterfaceForObjectNative | static | public/nonpublic | System.IntPtr (System.Object o, System.Type t, System.Boolean onlyInContext, System.Boolean fEnalbeCustomizedQueryInterface) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetObjectForIUnknown | static | public/public | System.Object (System.IntPtr pUnk) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetUniqueObjectForIUnknown | static | public/public | System.Object (System.IntPtr unknown) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | CleanupUnusedObjectsInCurrentContext | static | public/public | System.Void () | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | AreComObjectsAvailableForCleanup | static | public/public | System.Boolean () | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | IsComObject | static | public/public | System.Boolean (System.Object o) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | InternalReleaseComObject | static | public/nonpublic | System.Int32 (System.Object o) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | QueryInterface | static | public/public | System.Int32 (System.IntPtr pUnk, System.Guid& iid, System.IntPtr& ppv) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | Release | static | public/public | System.Int32 (System.IntPtr pUnk) | 🟡 | MethodMap.Marshal |  |
| System.Runtime.InteropServices.Marshal | GetDelegateForFunctionPointerInternal | static | public/nonpublic | System.Delegate (System.IntPtr ptr, System.Type t) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.Marshal | GetFunctionPointerForDelegateInternal | static | public/nonpublic | System.IntPtr (System.Delegate d) | 🟡 | MethodMap.Marshal | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.RuntimeEnvironment | GetModuleFileName | static | public/nonpublic | System.String () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.RuntimeEnvironment | GetDeveloperPath | static | public/nonpublic | System.String () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.RuntimeEnvironment | GetHostBindingFile | static | public/nonpublic | System.String () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.RuntimeEnvironment | GetRuntimeDirectoryImpl | static | public/nonpublic | System.String () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.SafeBuffer | StructureToPtrNative | static | public/nonpublic | System.Void (System.TypedReference structure, System.Byte* ptr, System.UInt32 sizeofT) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.SafeBuffer | PtrToStructureNative | static | public/nonpublic | System.Void (System.Byte* ptr, System.TypedReference structure, System.UInt32 sizeofT) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.SafeHandle | InternalFinalize | instance | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.SafeHandle | InternalDispose | instance | public/nonpublic | System.Void () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.SafeHandle | SetHandleAsInvalid | instance | public/public | System.Void () | ❌ | - |  |
| System.Runtime.InteropServices.SafeHandle | DangerousAddRef | instance | public/public | System.Void (System.Boolean& success) | ❌ | - |  |
| System.Runtime.InteropServices.SafeHandle | DangerousRelease | instance | public/public | System.Void () | ❌ | - |  |
| System.Runtime.InteropServices.ExtensibleClassFactory | RegisterObjectCreationCallback | static | public/public | System.Void (System.Runtime.InteropServices.ObjectCreationDelegate callback) | ❌ | - |  |
| System.Runtime.InteropServices.RegistrationServices | RegisterTypeForComClientsNative | static | public/nonpublic | System.Void (System.Type type, System.Guid& g) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.RegistrationServices | RegisterTypeForComClientsExNative | static | public/nonpublic | System.Int32 (System.Type t, System.Runtime.InteropServices.RegistrationClassContext clsContext, System.Runtime.InteropServices.RegistrationConnectionType flags) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.TypeLibConverter | nConvertTypeLibToMetadata | static | public/nonpublic | System.Void (System.Object typeLib, System.Reflection.RuntimeAssembly asmBldr, System.Reflection.RuntimeModule modBldr, System.String nameSpace, System.Runtime.InteropServices.TypeLibImporterFlags flags, System.Runtime.InteropServices.ITypeLibImporterNotifySink notifySink, System.Collections.ArrayList& eventItfInfoList) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.TypeLibConverter | nConvertAssemblyToTypeLib | static | public/nonpublic | System.Object (System.Reflection.RuntimeAssembly assembly, System.String strTypeLibName, System.Runtime.InteropServices.TypeLibExporterFlags flags, System.Runtime.InteropServices.ITypeLibExporterNotifySink notifySink) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.WindowsRuntime.RuntimeClass | GetRedirectedGetHashCodeMD | instance | nonpublic/nonpublic | System.IntPtr () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.WindowsRuntime.RuntimeClass | RedirectGetHashCode | instance | nonpublic/nonpublic | System.Int32 (System.IntPtr pMD) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.WindowsRuntime.RuntimeClass | GetRedirectedToStringMD | instance | nonpublic/nonpublic | System.IntPtr () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.WindowsRuntime.RuntimeClass | RedirectToString | instance | nonpublic/nonpublic | System.String (System.IntPtr pMD) | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.WindowsRuntime.RuntimeClass | GetRedirectedEqualsMD | instance | nonpublic/nonpublic | System.IntPtr () | ❌ | - | 内部 icall（public 包装承接） |
| System.Runtime.InteropServices.WindowsRuntime.RuntimeClass | RedirectEquals | instance | nonpublic/nonpublic | System.Boolean (System.Object obj, System.IntPtr pMD) | ❌ | - | 内部 icall（public 包装承接） |
| System.Text.StringBuilder | ReplaceBufferInternal | instance | public/nonpublic | System.Void (System.Char* newBuffer, System.Int32 newLength) | 🟡 | MethodMap.StringBuilder | 内部 icall（public 包装承接） |
| System.Text.StringBuilder | ReplaceBufferAnsiInternal | instance | public/nonpublic | System.Void (System.SByte* newBuffer, System.Int32 newLength) | 🟡 | MethodMap.StringBuilder | 内部 icall（public 包装承接） |
| System.Text.Normalization | nativeNormalizationNormalizeString | static | nonpublic/nonpublic | System.Int32 (System.Text.NormalizationForm normForm, System.Int32& iError, System.String lpSrcString, System.Int32 cwSrcLength, System.Char[] lpDstString, System.Int32 cwDstLength) | ❌ | - | 内部 icall（public 包装承接） |
| System.Text.Normalization | nativeNormalizationIsNormalizedString | static | nonpublic/nonpublic | System.Boolean (System.Text.NormalizationForm normForm, System.Int32& iError, System.String lpString, System.Int32 cwLength) | ❌ | - | 内部 icall（public 包装承接） |

### 2.2 System.dll（2 项）

| 类型 | 方法 | 静态 | 可见性 | 返回/参数 | 状态 | 域 | 备注 |
|---|---|---|---|---|---|---|---|
| Microsoft.Win32.InternetSecurityManager | .ctor | instance | nonpublic/nonpublic | System.Void () | ❌ | - | COM 内部, 用户 IL 不可达 |
| System.Net.Mail.MSAdminBase | .ctor | instance | nonpublic/nonpublic | System.Void () | ❌ | - | COM 内部, 用户 IL 不可达 |

## 3. ILC 接线对比汇总

> 口径: 键级 = (类型全名, 方法名) 去重后 885 个键（与 ILC dispatch 同粒度）; §2 全量清单为行级（910 行, 含重载逐行）

| 口径 | 数量 | 说明 |
|---|---|---|
| net48 InternalCall 总数 | 910 | 7 个目标程序集, 双通道验证 |
| public API 面（用户 IL 可达, 必须接线） | 121 | 类型 public + 方法 public（键级） |
| ✅ 已接线（public） | 54 | dispatch 域 + case 方法名均命中（键级） |
| 🟡 部分（public, 域在方法缺） | 42 | 见 §4 缺口表（键级） |
| ❌ 未接线（public, 无域） | 25 | 见 §4 缺口表（键级） |
| 内部 icall（public 包装承接） | 764 | 不需直接接线; ILC 接 public 包装名（键级） |
| 其余（internal 但巧合匹配） | 4 | ✅ 内部命中（键级） |

## 4. 分域缺口表（public 面）

| 域 | 🟡 域在方法缺 | ❌ 无域 | 最高优先缺口 |
|---|---|---|---|
| String | 3 | 0 | `new string(ch, n)`、`IndexOfAny`、`LastIndexOfAny`（**高优先**） |
| Array | 4 | 0 | `get_Rank`（多维数组判断, **高优先**）; `get_LongLength`/`GetLowerBound`/`Initialize` 中低 |
| ValueType | 0 | 1 | `ValueType.GetHashCode`（值类型哈希, **高优先**） |
| Marshal | 28 | 0 | `UnsafeAddrOfPinnedArrayElement`（**高优先**）; `GetExceptionPointers`/`GetExceptionCode`/`DestroyStructure`/`GetHRForException` 中; 23 个 COM 相关建议 LOUD 拒绝 |
| Thread | 5 | 0 | `get_IsThreadPoolThread` 中; `MemoryBarrier` 可映射 fence; 其余低 |
| Debugger | 0 | 3 | `Debugger.Break` 路径缺口（internal BreakInternal）, `get_IsAttached`/`Log`/`IsLogging`（**中优先**） |
| Enum | 1 | 0 | `Enum.Equals`（装箱比较, 可值语义实现）中 |
| Type | 1 | 0 | `op_Inequality`（可折叠为 `!(==)`）低 |
| Interlocked | 1 | 0 | `SpeculationBarrier` 空实现即可 |
| RuntimeHelpers | 0 | 8 | 7 个可折叠/空实现（b类）; `PrepareDelegate` 低 |
| SafeHandle | 0 | 3 | P/Invoke 场景, 中优先 |
| Remoting | 0 | 6 | 旧技术, 建议 LOUD 拒绝（低） |
| AppDomain / ArgIterator / ReaderWriterLock / ExtensibleClassFactory | 0 | 4 | 低频/legacy |

## 5. 最高优先级缺口（10 个）

| # | 缺口 | 域 | 用户场景 |
|---|---|---|---|
| 1 | `System.String..ctor(char, int)` | String | `new string('*', n)` 字符串填充, 常用 |
| 2 | `System.String.IndexOfAny(char[], int, int)` | String | `s.IndexOfAny(stopChars)` 常用 |
| 3 | `System.String.LastIndexOfAny(char[], int, int)` | String | 反向扫描常用 |
| 4 | `System.Array.get_Rank` | Array | 多维数组判断 `a.Rank` |
| 5 | `System.ValueType.GetHashCode` | ValueType | 任意 struct 的 GetHashCode（constrained callvirt） |
| 6 | `System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement` | Marshal | P/Invoke pin 数组取址 |
| 7 | `System.Diagnostics.Debugger.get_IsAttached` / `Break` 路径 | Debugger | 调试断言/断点（Debugger.Break 高频） |
| 8 | `System.Runtime.InteropServices.Marshal.GetHRForException` | Marshal | 异常 HRESULT 互转 |
| 9 | `System.Runtime.InteropServices.Marshal.GetExceptionPointers`/`GetExceptionCode` | Marshal | 结构化异常查询 |
| 10 | `System.Runtime.InteropServices.SafeHandle.DangerousAddRef/DangerousRelease/SetHandleAsInvalid` | SafeHandle | P/Invoke 句柄生命周期 |

## 6. a/b 分类（需要接线 vs JIT/转译器已有语义）

### a) 需要 runtime icall 接线的（public 面 121 个中的缺口, 见 §4/§5）
- String ctor/IndexOfAny/LastIndexOfAny; Array get_Rank 等; ValueType.GetHashCode; Marshal 实用集; Debugger; SafeHandle

### b) JIT 内部直接实现 / 转译器已有语义（**不算缺口**）
- `RuntimeHelpers.InitializeArray` → StackFold.cs FoldInitializeArray（ldtoken&lt;RVA field&gt; + InitializeArray fold, IlDecoder/StackFold 已有）
- `Type.GetTypeFromHandle` → ldtoken&lt;type&gt; fold + MethodMap.Type.GetTypeFromHandle 已接
- `RuntimeHelpers.GetHashCode/Equals` → 引用语义, 可折叠为指针比较/对象哈希
- `RuntimeHelpers.GetObjectValue` → 装箱语义（转译器 box 已有）
- `Thread.MemoryBarrier` / `Interlocked.SpeculationBarrier` → 空实现/原子栅栏
- ldtoken&lt;method&gt; → M3 明确 NotSupported（LOUD, 已有守卫）

## 7. 附注
- 用户提到「已接 393/459」为上一轮口径; 本轮以「public API 面 121 个（键级）」为必须接线口径: **54 已接 / 67 缺口（42 域缺 + 25 无域, 其中约 30 个 COM/legacy 可豁免, 实际高+中优先约 20 个）**
- 内部 icall（764 个键）由 mscorlib public 包装承接; ILC 接线目标是包装名（如 Thread.SleepInternal→已接 Sleep、GC._SuppressFinalize→已接 SuppressFinalize、ThreadPool.SetMaxThreadsNative→已接 SetMaxThreads）
- 元数据佐证文件: %TEMP%\\opencode\\icalls-net48-metadata.csv / icalls-net48-refl-ctor.csv / icalls-net48-vs-ilc.csv

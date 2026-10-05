# GAPS — xmlwrite (e2e batch C65)

## D-XMLW-1: System.Xml.XmlWriter 完全未接（generic generated-name 路径，非 LOUD）

- 现象（实测）：`pwsh -File scripts/run-tests.ps1 -Compiler mingw -Case xmlwrite`
  在 cmake_build 阶段失败（20 个 C++ 编译错误），转译成功但生成未定义 extern。
- 生成码证据（tests/cases/xmlwrite/gen/src/Cases_XmlWrite.cpp）：
  - `t1 = reinterpret_cast<Il2CppObject*>(System_Xml_XmlWriter_Create_297866fa56d1a7e6(t0));`
  - `System_Xml_XmlWriter_WriteStartElement_807996b4bdb2f011(t1, il2cpp::g_strPool[0]);`
  - `System_Xml_XmlWriter_WriteAttributeString_a3dc8499fab6e29e(t1, ...);`
  - `t1 = il2cpp_object_new(&System_Xml_XmlWriterSettings_class);`
- 根因：MethodRefResolver.cs:832 只把 `System.Xml.XmlDocument` /
  `System.Xml.XmlNode` / `System.Xml.XmlElement` 路由到 TryMapXml；
  `System.Xml.XmlWriter`（以及 XmlWriterSettings）落入 generic 生成名路径，
  产出未声明 extern，而非 TryMapXml 的 NotSupportedException LOUD 拒绝。
  无任何 XmlWriter 运行时 icall（src/runtime 无 xml_writer 源）。
- net48 oracle（expected.txt，逐字节钉住，供接入后自验证）：
  `<?xml version="1.0" encoding="utf-16"?><root id="1"><a>text &amp; more</a></root>`
  + indent/xml_decl/attr_esc/text_esc 断言（CRLF + 2 空格缩进、实体转义规则）。
- 状态：未修（本批只记录，不修产品代码）。

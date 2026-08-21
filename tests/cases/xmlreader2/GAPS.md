# GAPS — xmlreader2 (e2e batch C66)

## D-XMLR-1: System.Xml.XmlReader 完全未接线（generic 生成名路径，非 LOUD）

- 事实（代码审查）：`grep XmlReader E:\il2cpp\src` → 0 命中。MethodMap.Xml.cs
  只路由 XmlDocument/XmlWriter（TryMapXml/TryMapXmlWriter）；src/runtime 无
  xml_reader 源，xml_doc.h 只有 XmlDocument 读面（load/outer/inner_text/
  inner_xml/select_nodes/select_single_node/get_attribute_s）。XmlReader /
  XmlTextReader / XmlNodeReader / XmlReaderSettings / Create / Read /
  NodeType / MoveToNextAttribute 均无 icall 无映射。
- 后果：用例使用 XmlReader 会落入 generic generated-name 路径 → cmake 构建期
  未定义 extern（非 NotSupportedException LOUD 拒绝）。
- 本用例因此只用可达读面：LoadXml→OuterXml 定点往返（遍历/属性/命名空间逐字节
  钉住）+ InnerText（解码文本拼接）+ 实体/数字字符引用解码 + CDATA 原样保留。
- 状态：未修（本批只记录，不修产品代码）。

## 记录（既有，沿用 xmldeep2）

- SelectNodes/SelectSingleNode 结果不可消费（rv=false → DiscardedValueExpr）。
- get_InnerXml-on-document 为 children-only（net48 含根标签）——只探 OuterXml/InnerText。
- 空元素序列化 "<a/>" vs net48 "<a />"、注释/声明被丢弃——用例不输入空元素。

# Word / Excel 词表导入插件

面向 DictationAssistant 插件接口第 1 版，运行目标为 .NET 10。插件在本地读取 `.docx`，不需要安装 Microsoft Word，不启动外部程序，也不访问文档中的链接或嵌入文件。

**[1.1.0 已独立公开](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-docx-vocabulary-v1.1.0)。** 新增 `.xlsx`、`.xls`、`.xlsb`、`.xlsm` 读取，继续使用插件 ID `docx-vocabulary`、程序集 `DictationAssistant.Importers.Docx` 及原有入口类型；安装目录仍为 `Plugins/DocxVocabulary/`。历史版本 1.0.1 仅支持 DOCX，资产保留。

当前源码为 **1.2.0，尚未发布，要求宿主功能级别 5**。新增背诵练习使用的可选 `IDocumentNotationImporter.ReadWithNotationAsync`：保留 Word 直接设置的上下标，输出 `Na^+`、`NH_4^+`、`SO_4^2-` 等记号。普通 `ReadAsync` 仍返回原来的纯文本；Excel 两种入口均保留原始 Unicode，不自行推断格式。

## DOCX 导入内容

- 每张正文表格生成独立的 `Table` 分区，保留单元格内容、行顺序和重复项。
- 正文中不属于表格的非空段落放入单独的 `Paragraphs` 分区，位于所有表格之后。
- 可识别表格前 10 行内的常见中英文表头，或 Word 显式标记的表头；表头行保留并标记 `IsHeader`，方便宿主排除。
- 1.2.0 增加 English Name、中文名称、Symbol 等表头识别，并标记这些表头前的整宽合并标题，避免将其作为练习条目。
- 首选有“单词”“英文”“Word”“Phrase”“Vocabulary”等表头的第一张表格及相应列。其他情况使用第一分区、第一列，由用户在预览中选择。
- 保留英文大小写、短语、单元格内的空白、制表符和换行。多个单元格段落用换行连接。
- 横向合并单元格的内容只放在起始列，后续跨度用空单元格补齐；嵌套表格文本放入所属外层单元格，不重复生成分区。
- 保留白色和显式隐藏文字，并在检测到时显示提示。删除修订、域指令、图片、文本框及嵌入对象不参与词条提取。

插件不会自行去重、打乱或修改词表。检测到文本相同的表格时只返回提示，实际导入范围由宿主预览决定。

记号读取合并相邻的同类上下标 run，再生成一个脚本组。数字下标和电荷上标使用简写；其他脚本用 `_{...}` / `^{...}` 明确分组。仅解析 run 直接声明的 `w:vertAlign`，不根据字号或位置猜测，不解析继承样式或嵌入公式；没有脚本格式的 `Br-` 保留原文，判定规则由练习插件负责。

## Excel 导入内容（1.1.0）

- 每张工作表作为独立分区，用户在宿主预览中选择工作表和列；不把不同工作表拼成一张表。
- 文本保留原始 Unicode、大小写、空白及列位置，不自行去重、规范化或填补空单元格。
- 合并区域只保留左上角锚点内容，其他位置留空，不复制成多条词条。
- 公式只读取工作簿已有的缓存结果，不重新计算；宏与外部对象不执行。没有缓存结果时不代为计算补值。

读取使用 [ExcelDataReader 3.9.0](https://github.com/ExcelDataReader/ExcelDataReader/tree/f3343f5524518923bdeb4801e5003c0417fd35f3) 的底层接口，无需安装 Excel。该库覆盖旧 `.xls` 与 `.xlsb`，适合本次只读多格式导入；相比之下，[ClosedXML](https://github.com/ClosedXML/ClosedXML/tree/09f2f21a60e821ce7fb18ca3576b272e5dd6d921) 偏向 `.xlsx` / `.xlsm` 的读写编辑，[Open XML SDK](https://github.com/dotnet/Open-XML-SDK/tree/431ab05cf160248cc3885a4a766026d4f8243792) 面向 Open XML 容器，不能覆盖本次全部格式。当前使用的 net8.0 资产没有传递依赖。

用户 `.xlsx` 的 19 张工作表、2,886 个位置和 2,683 份非空文本已在 Linux 独立逐字符比对通过，92 处合并与来源行号等读取规则也已核对；默认 112 处标题 / 表头识别、全部列预览及默认 95 条替换导入均通过。Windows 已通过 R6 装载、双工作表 / 四列 Unicode 合成样本 CLI 及程序窗口检查；其他 Excel 格式、公式缓存与 Windows 真实 Excel GUI 导入尚未实测。私人工作簿只用于本地验证，记录仅保留工作表名称、计数、文件哈希及验证范围，见 [Excel 导入验证](../../docs/Excel导入验证.md)。

## DOCX 格式与限制

读取 DOCX 包内标准位置 `word/document.xml` 的正文，支持 Transitional 和 Strict 两种 WordprocessingML 命名空间。不会执行宏、读取 OLE 对象或识别图片；旧版 `.doc`、扫描件和其他格式需要先转换。

为限制异常文件的内存消耗，既有 DOCX 读取设有以下上限：

| 项目 | 上限 |
|---|---:|
| 输入 DOCX 实际读取大小 | 64 MiB |
| ZIP 包内项目数 | 4096 |
| 解压后正文 XML 实际字节数 | 32 MiB |
| XML 字符数 | 33,554,432 |
| XML 节点深度，根节点深度为 0 | 128 |
| 正文外层表格数 | 256 |
| 每张表格的逻辑列数 | 256 |
| 输出行数，含表头、表格空行和非空正文段落 | 50,000 |
| 补齐列之后的输出单元格数 | 500,000 |

DTD 被禁止，外部 XML 资源解析被禁用。宿主传入的文件流保持打开，所有返回数据均已物化；读取和行遍历支持取消。

## 本次示例文档的预期结果

“第二周听力词汇.docx”中有两张内容相同的 40 词表格，每张含一行表头；第二张的英文答案使用白色字体。默认选择第一张表格的“单词”列，排除表头后得到 40 条。选择所有表格并保留重复项得到 80 条。两张表之间的姓名、得分栏位于正文段落分区，不会混入“所有表格”。

## 构建

源码按读取流程划分：`DocxImporter` 负责格式分派和 Word 正文解析，`DocxNotationReader` 负责可选的 run 脚本记号，`ExcelImporter` 负责工作表及单元格转换，`ExcelPackage` 负责包内 XML、解压预算与取消读取保护，`XlsbRecordValidator` 负责关系定位和二进制记录边界；两种格式共用 `ImportStreams` 的有界异步输入复制。

在包含本项目和 `src/DictationAssistant.Plugin.Abstractions` 的完整源码目录中使用 .NET 10 SDK：

```sh
dotnet publish plugins/DictationAssistant.Importers.Docx/DictationAssistant.Importers.Docx.csproj -c Release -o artifacts/docx-vocabulary
```

将输出中的插件 DLL、依赖描述文件和 `plugin.json` 一起放入插件目录。插件契约程序集由宿主提供，请勿在插件目录额外放置另一份 `DictationAssistant.Plugin.Abstractions.dll`。

许可证：AGPL-3.0-or-later，与主项目一致。

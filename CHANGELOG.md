# 更新记录

版本号采用 [SemVer](https://semver.org/lang/zh-CN/)。带 `typing.N` 后缀的版本为同步打字定制版预发布，详细交付规则见 [发布规范](docs/RELEASING.md)。

## macOS 整合更新 [1.4.3-macos.1] - 2026-10-22

**准备中，尚未发布。** 两个完整应用包分别用于 Intel 和 M 系列，内置背诵练习 1.4.3 与 Word / Excel 导入 1.2.0。

- 将中文同义答案、候选确认及本地判分功能带到 Mac。模型与翻译凭据仅用于当前程序会话，已确认答案保存在用户数据目录。
- 固定复用已公开 R8 本体，只重建练习插件并重新签名；不替换宿主、导入插件或原生库。保留旧 Mac 包和 Windows 1.4.3 的标签与附件。
- 使用独立双架构预检与同提交产物发布流程，新增实际 Mac 的模型配置会话观察；签名、语音、SDL 和窗口检查按实际构建结果记录。

## 背诵练习插件 [1.4.3] - 2026-10-21

**[仅 Windows，已预发布](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.4.3)。** 沿用已公开 R7，只更新练习插件，Mac 仍使用 1.4.2。

- 练习设置新增“可接受答案”：手动补充，或通过独立配置的 OpenAI 兼容模型接口生成本页候选；候选默认不选中，确认保存后才参与判分。
- 中文格式归一化与已确认别名在本地处理；可关闭“使用已确认的中文答案”恢复严格匹配。每题按提示与标准答案隔离，开始练习后冻结本轮答案。
- 模型配置与翻译引擎分开管理，Windows 使用当前账户和设备加密保存。配置保存、练习及结算均不调用模型；化学式仍按原严格规则。
- 关闭配置或候选答案窗口时，先取消任务并等待结束，避免留下无法退出的窗口。
- [Windows 构建 39524503791](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39524503791)通过固定 R7 加载、15 项既有检查、主窗口正常开闭和 16 项原生模型配置检查；正式 DLL 另在 Linux Avalonia.Headless 通过原功能回归、答案隔离与快照、候选确认及多层窗口关闭观察。
- [发布 39525110231](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39525110231)复用原成功产物，五份公开附件重新下载后逐字节一致；191 个源码文件、127 份原始许可及私有依赖源码核对通过，源码包在独立目录还原和构建成功。源码提交 `fd8ac3c841270e4d20ddd64186663631feaa801d`，历史发行与标签不变。真实模型服务、完整 Windows 练习交互、输入法和异机迁移尚未实测；没有新增 NuGet 依赖。

## 背诵练习插件 [1.4.2] - 2026-10-20

**[三平台随机词序补丁已预发布](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.4.2)。**

- 修复静音背诵没有打乱入口的问题，准备页新增“打乱顺序”；播报后作答也可使用。默认保持原顺序，勾选后开始一轮新练习时重排，本轮中途不再变化。
- 提示、答案和原行来源整组移动，重复题保留；播报、译文提示与成绩使用同一题序，中途结算继续可用。
- 提供 Windows R7 独立插件补丁，以及基于 Mac R8 的 Intel、M 系列整合更新包；只重新构建练习插件，宿主与 Word / Excel 导入插件不重新编译。Mac 包更换插件后重新签名，不要求用户手工修改 `.app`。
- Linux 开发构建及本轮 48 项 headless 观察通过，含 31 项既有路径、17 项随机词序检查、Common Ions 25 题静音整轮，以及 1,000 次含重复题的重排。[三平台构建 39437145026](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39437145026)通过固定宿主的双插件加载、既有逻辑检查和主窗口开闭；Mac 两包另通过严格签名校验、系统语音合成与 SDL dummy 播放。[发布 39437870903](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39437870903)复用这批验证产物，7 份公开附件重新下载核验通过。真实练习界面、输入法、扬声器和在线翻译仍未实测。

- 本次源码提交 `3e6730ddf9239ee92f41ec408ea5b9421af6cf11`。补丁源码与固定的 R8 完整源码共同提供 Mac 整合更新的对应源码，宿主及导入插件不重编，原始许可与已有组合授权披露继续保留。

## [4.0.0-typing.8] - 2026-10-20

**[R8 macOS 整合版已预发布](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.8)。** Windows 继续使用 R7 和独立的背诵练习 1.4.1 更新。

- 面向 macOS 15+，分别提供 Intel x64 和 Apple Silicon arm64 完整包，均包含背诵练习 1.4.1、Word / Excel 导入 1.2.0 及对应架构的 .NET 运行环境。新增标准应用包布局、本机启动器、原生构建及配套源码交付流程。
- 修正系统语音取消：取消后终止进程树并等待退出；先读取输出再写入输入，避免管道阻塞，已取消的请求不再启动进程。语音列表保留带空格或括号的完整名称，按语言列识别区域。
- 上述进程与语音名解析通过 15 项临时验证，覆盖实际子进程取消、大量管道输入输出及错误退出；这组检查在 Linux 完成。
- macOS 翻译凭据仅用于当前程序会话，退出后需重新输入。安装包采用 ad-hoc 签名，尚未使用 Developer ID 签名或经过 Apple 公证；安装方式和待验证范围见 [macOS 说明](docs/MACOS.md)。
- [macOS 无 DNG 原生依赖已独立公开](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/native-skia-2.88.9-no-dng-macos.1)：两个架构的实际构建、源码闭包和 7 份附件重下载核验均通过，正式来源与哈希已固定。Avalonia/MS-PL 组合授权事项继续披露，原始许可和配套源码保留。
- [macOS 构建 39425977259](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39425977259)在两个架构均通过严格签名校验、双插件加载、15 项既有逻辑检查、系统语音合成、SDL dummy 播放和主窗口正常开闭。[发布 39426632441](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39426632441)复用同提交产物，6 份公开附件重新下载核验通过。扬声器声音、中文输入法、完整练习交互和真实在线翻译仍未实测。

## 背诵练习插件 [1.4.1] - 2026-10-20

- 可以中途结算：点击“结算成绩”，已提交的答案和当前已输入的内容一并计分，未作答的题目不计分。播放中也能结算，先停止播报，再显示已有答案的成绩。
- “显示翻译提示”改为“显示提示”，提示编辑统一使用“编辑提示”“补全提示”和“保存提示”。选列、计时、成绩和翻译配置的说明改为简短直接的表达。
- 独立插件更新，继续配合 R7（API 1 / 宿主功能级别 5）使用。R7 完整包仍内置背诵练习 1.4.0 和 Word / Excel 导入 1.2.0，本次只更新背诵练习插件。
- [1.4.1 已公开为预发布版](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.4.1)，源码提交 `551174f64e15929c648b178affc3a2ec4f2f5cf3`。[Windows 构建与发布](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39413856150)通过固定 R7 的双插件加载、15 项既有检查和主窗口开闭；Linux Headless 的 48 项观察覆盖中途结算、超时、重复操作和退出。Windows 真实练习界面、原生音频及输入法仍未实测。

- 五份公开附件的下载摘要与校验和通过；对应源码逐字节核对、原始声明及私有依赖源码闭包完整，下载源码独立还原与 Release 发布构建成功。旧发行资产和标签保持不变。

## [4.0.0-typing.7] - 2026-10-20

**[R7 已公开为预发布版](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.7)。** 配套背诵练习 1.4.0 和 Word / Excel 导入 1.2.0，新增文档配对与无音频计时背诵。既有 Avalonia/MS-PL 组合授权事项继续如实披露，尚未解决。

- 同步打字插件更名为“背诵练习插件”，更新至 **1.4.0**；保留既有插件 ID、程序集和本地配置目录。新增前置文档准备界面，可从 Word / Excel 选择表格、提示列与答案列，按原行顺序配对，默认“离子式提示 → 英文名称答案”，支持中文列及反向交换。
- 新增无音频的计时背诵模式，逐题显示提示和剩余秒数，Enter 提交或到时记录，整轮完成后评分；保留播报后作答、译文提示与翻译配置。化学式答案使用区分元素大小写、电荷和计量的判定，接受 `Na^+`、`SO4^2-` 与 Unicode 上下标；成绩与 CSV 增加提示和来源。
- Word / Excel 导入插件更新至 **1.2.0**，增加可选记号读取接口以保留 Word 直接上下标 run；普通文档导入仍使用原纯文本路径，并补充英文名称、中文名称、Symbol 表头及整宽标题识别。
- 宿主功能级别升至 **5**，API 仍为 1：练习可在不改变编辑器词表的情况下选择文档，具备选源能力的插件可从空词表启动。以上两个新插件须与 R7 配套；旧 R6 的功能级别 4 不支持。
- Common Ions 的 25 对记号及原 Excel 19 页读取回归通过；31 项 headless 交互核对覆盖默认/反向练习、Enter、超时、取消和旧播报流程模拟，静音全程零播报调用。[R7 标签构建](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39398895391)的 Windows 任务已通过构建、包及 DLL 校验、15 项检查、主窗口开闭、双插件功能级别 5 加载和 SDK 示例构建。Headless 不代表 Windows 真实练习界面或字体渲染验证。

- R7 完整包已包含两个插件；[背诵练习 1.4.0](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-synchronous-typing-v1.4.0) 和 [Word / Excel 导入 1.2.0](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/plugin-docx-vocabulary-v1.2.0) 也各自提供独立更新包、配套源码与校验文件。三个标签均固定到 `20251457f881d5efca52eb39dc832f8ae2e9f135`；[附件上传恢复流程](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39401716293)只上传原标签构建的已验证附件，未重建或改动文件。
- 公开下载的 15 个附件与原 CI 文件逐字节一致，15 个服务器摘要及 12 条 `SHA256SUMS` 均通过；同批配套源码此前分别通过独立还原和 Release 构建，原始许可及源码闭包已核对。
- 先重置失败工作流，再修复生成说明 JSON 的 CRLF/LF 比较；通过指定草稿上传和完整校验后公开。历史 Release 的标题与说明改写为用途、安装方式和版本要求，原有标签与附件保留。

- 整理主程序与两个插件的职责：词表打开/导入移入独立主窗口 partial，统一 GUI/CLI 的区域选择与非空行统计；普通文本在后台读取，并对非定位流补齐 64 MiB 上限和关闭取消。
- 同步打字插件将翻译绑定草稿、统一参数校验、成绩评分/CSV 和虚拟化结果行从窗口逻辑中分离，保持既有配置格式、插件身份和接口。
- Word / Excel 插件共用有界异步输入复制，将包预检与单元格读取分离，减少等宽行与预检缓冲区的重复分配。
- Windows 便携包通过 Python 标准库明确生成 ZIP，替代依赖 `PATH` 中 `tar.exe` 行为的格式推断；保留原有包完整性校验。
- 更新文档索引和插件下载入口，新增代码职责与数据流说明。
- 验证：Linux 整体构建、双插件加载和现有 15 项逻辑检查通过；Excel 原始 19 页输出逐字节一致，56 次逐列、19 次默认列及 3 次全表导出通过，DOCX 回归通过；翻译草稿/评分/CSV 的 19 项运行核对、文本编码/取消/大小边界及 511 组计数对照通过。提交 `d7288be` 的 [Windows CI](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39395057404) 通过完整构建、包与 DLL 校验、15 项检查、主窗口开闭和 SDK 示例构建。既有 NuGet 安全告警仍在；翻译/导入配置的 Windows 交互、DPAPI、真实在线服务、音频与输入法未在本轮实测，不据此声称性能降幅。

## Word / Excel 导入插件 [1.1.0] - 2026-10-20

- 在既有 DOCX 导入插件中增加 `.xlsx`、`.xls`、`.xlsb`、`.xlsm` 读取；每张工作表作为独立分区，沿用宿主预览和选列流程。插件 ID `docx-vocabulary`、程序集及入口类型保持不变，显示名称更新为 Word / Excel。
- 文本按原样保留 Unicode、大小写、空白和列位置；合并单元格只保留左上角锚点内容，公式只读取已有缓存结果，不执行计算或宏。
- 采用固定版本 ExcelDataReader 3.9.0；面向当前 .NET 的 net8.0 资产无传递依赖。Linux 上对用户 `.xlsx` 的 19 工作表、2,886 个位置及 2,683 份非空文本独立逐字符比对零差异；92 处合并、2 处空行、来源行号及特殊文字保留。
- 现有 CLI 的 56 次逐工作表 / 列导出覆盖 2,683 个非空单元格，按宿主空白规则全部一致；19 页默认规则独立比对通过，共识别 112 处标题 / 表头，自动推荐第 2 页并默认选出 95 条，原始单元格不变。
- Linux X11 重启最终 DLL 后，实际文件选择、19 页 / 56 列预览及默认 95 条替换写入宿主均经剪贴板复核通过；DOCX Unicode 两列与正文排除回归、伪装成 `.xlsx` 的异常文件拒绝也通过。Release 构建 0 警告、0 错误，15 项既有检查通过。其他 Excel 格式、公式缓存及 Windows 真实 Excel GUI 导入未验证，范围见 [Excel 导入验证](docs/Excel导入验证.md)。
- 提交 `dbb2a04a4ee9b6d623f0c783ccd3edba6a26ea5a` 的 [Windows CI 39390197721](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39390197721) 已通过完整构建、插件与程序包校验、既有检查和生产主窗口开闭；未上传用户工作簿，也未创建新 Release。
- 标签 `plugin-docx-vocabulary-v1.1.0` 已公开，源码提交 `93631f4e7330a0ee8a89e903c127bfbd929ddf3f`；[独立 Windows CI 39392552810](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39392552810) 构建与发布成功。固定公开 R6 加载新导入插件及原同步打字 1.2.0，双工作表 / 四列 Unicode 合成样本 CLI、15 项既有检查及窗口正常开闭通过。
- 五份公开附件的摘要与校验、插件 / DLL / R6 兼容记录及配套源码闭包复核通过；下载源码包已独立还原并通过 Release 发布构建，详情见 [发布规范](docs/RELEASING.md)。
- 本次只交付导入插件、对应源码与校验文件；R6 本体、同步打字 1.3.1、原生依赖及历史 Word 1.0.1 资产保持不变。私人工作簿和整份提取内容不纳入仓库或 CI，19 页全量核对仍仅在 Linux 执行。

## 同步打字插件 [1.3.1] - 2026-10-20

- 独立插件更新，继续要求 API 1 / 宿主功能级别 4，兼容已发布的 R6 本体；宿主版本和 Word 导入插件 1.0.1 保持不变，既有 1.2.0 发行保留。
- 支持 8 类官方翻译服务、9 个可选类型：DeepL Free / Pro、必应 / Microsoft（Azure）、Google Cloud Translation Basic v2、百度翻译开放平台、有道智云、腾讯云 TMT、阿里云机器翻译及火山引擎翻译。多个命名绑定、首选项和准备页临时切换沿用原流程。
- 配置窗口按服务显示主凭据、附加密钥及区域要求；Windows 的本机 / 当前账户加密绑定和其他平台的会话模式保持不变，练习过程中不联网翻译。各服务真实账户的鉴权翻译仍待验证。
- 新增仅插件发布路径：从已公开且固定校验的 R6 二进制暂存宿主，替换并验证新插件，再准备插件对应源码及五份独立发行附件；不构建或发布新本体，不覆盖历史标签和资产。未新增 NuGet 依赖，沿用已披露的宿主 Avalonia 事项。
- 修正兼容宿主暂存时的 Windows 非法说明文件名，只更名暂存文件并保留字节，既有 R6 ZIP 不变；首次 1.3.0 候选未发布附件，保留其标签后改用 1.3.1。
- 已通过 Linux 构建、15 项既有逻辑检查及界面核对：9 个服务入口、双凭据字段、区域默认值与重开保留，以及 Google / Microsoft 双绑定、首选项和准备页切换。
- 标签提交 `418501c1a7be64bd1e81dd4dd9816e4ce822c4e2` 的 [独立 Windows 工作流 39381852561](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39381852561) 构建与发布均成功，插件 1.3.1 已公开。新插件通过暂存 R6 宿主的兼容加载、15 项既有检查和主窗口开闭验证；R6 与 Word 既有资产不变。各服务真实账户翻译、Windows DPAPI 及旧配置迁移仍未验证。
- 下载的配套源码中，166 个选定文件与标签导出逐字节一致，124 份原始许可证和一份私有依赖源码归档校验通过；已从该源码包独立还原并发布构建成功。

## [4.0.0-typing.6] - 2026-10-20

**R6 预发布版本。** 本体与同步打字插件分别交付，Word 导入插件继续使用已发布的 **1.0.1**。Avalonia 维持 11.3.12；已记录的 MS-PL/AGPL 组合授权事项仍未解决，延续既有的披露发布方式，保留原始许可、对应源码与完整性检查。

- 宿主新增逐插件「配置…」入口和可选 `IConfigurablePlugin.ConfigureAsync`，支持 `configuration` 清单类型；功能级别更新为 4，`apiVersion` 仍为 1。
- 同步打字插件更新为 **1.2.0**，要求功能级别 4，须配套 R6：可管理多个命名的 DeepL Free / Pro 绑定、设置首选项，并在翻译准备页为本次任务切换；两个入口共用配置，切换不触发翻译，打字过程中不调用翻译服务。
- Windows 对整份绑定配置使用当前账户及本机绑定的 DPAPI 加密；其他平台仅供当前程序会话使用。R5 单绑定配置读取时转换，仅在明确保存时写入新格式。
- 修正配置、练习及插件 UI 调度的异步等待：宿主等待内部任务和模态窗口实际结束后再释放运行状态，避免提前标记调用完成。
- 已通过本轮 Linux Release 构建、15 项既有检查及多绑定/首选项/准备页切换/会话重启清理的图形核对；代码提交 `0e7ecae` 的 [Windows CI 39375979975](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39375979975) 通过程序与插件构建、包及 DLL 校验、15 项既有检查、生产主窗口正常开闭和 SDK 示例构建。该主分支结果不替代最终发行提交验证；Windows 加密与旧配置迁移、真实在线翻译和完整边界校验仍待验证。
- R5 本体与同步打字 1.1.0 作为历史版本保留；Word 导入 **1.0.1** 复用既有独立 Release，本次不覆盖其标签和资产。
- R6 本体和同步打字 **1.2.0** 已公开为预发布，来自提交 `0f008c72983b9d567612855498800886201572e6`；[发行工作流 39377359747](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39377359747) 成功完成。Windows DPAPI、配置迁移及真实在线翻译的未验证范围保持不变。

## [4.0.0-typing.5] - 2026-10-20

**R5 预发布版本。** 本体与插件分别提供二进制、对应源码及校验文件。Avalonia 维持 11.3.12；MS-PL/AGPL 组合授权路径仍未解决，本次按发布决定明确披露，保留原始许可与核查记录，不声明已完成授权修复。独立无 DNG 原生资产已公开为 `native-skia-2.88.9-no-dng.1`。

- 同步打字插件更新为 **1.1.0**：提前录入成对译文并本地保存，支持打字阶段显示或隐藏翻译提示；原词播报、正确答案及评分规则保持不变。
- 可选 DeepL Free / Pro 在练习前补全缺失译文，支持英译中、中译英，结果校对后保存；独立 API 设置窗口支持 Windows 当前账户与本机绑定的加密保存、仅本次使用及删除绑定，练习过程中不调用翻译服务。
- 删除成绩摘要中的“已在整轮结束后统一评分”提示；新增翻译界面与真实 DeepL 调用的验证范围以本次实际记录为准，尚未完成 Windows 实机核对。
- Word 导入插件以 **1.0.1** 独立交付，导入功能未变；同步打字插件以 **1.1.0** 独立更新。插件接口仍为 API 1，版本不随宿主版本统一递增。
- 已通过 Linux 构建、15 项既有逻辑检查、翻译及 API 设置窗口交互和插件源码包独立构建；R5 [Windows CI](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39363409555) 另通过完整构建、包校验、15 项既有检查、窗口正常开闭及 SDK 示例构建。Windows DPAPI 保存、跨机器/账户拒绝读取及真实 DeepL 鉴权翻译仍待实机验证。

## [4.0.0-typing.4] - 2026-10-20

**历史源码预览，App 当时未公开发布。** Avalonia 保持 11.3.12；no-RAW/no-DNG 原生替代已通过真实 Windows CI 和实物核验，App 当时因 Avalonia/MS-PL 组合授权路径未解决暂停公开发行。此处保留该版本的审核状态，后续 R5 发布决定不追溯改变历史结论。

- 恢复上游 `Copyright (C) 2013-2026 alampy.com` 和完整 AGPL 简短声明，保留原许可证全文，补齐修改记录、AI 辅助开发说明及源码来源。
- “关于”界面显示原作者、完整 AGPL、第三方许可摘要和与程序提交对应的源码地址。
- 完整许可正文按行虚拟化，避免长文一次性排版造成高内存占用；实际修复 WAV 扩展头的 PCM/浮点编码识别，防止可解码的本地录音错误回退网络语音。
- 删除 BASS 原生库及其绑定，改用 MIT 许可的 NAudio/NLayer 流式解码；通用支持 WAV、MP3/MPEG、AIFF，Windows 其他格式依赖系统解码器。非定位输入使用有上限且自动清理的临时文件。
- 根据实际构建依赖准备许可证原文、组件与文件哈希清单、固定来源及源码归档；缺失声明、未知二进制或遗留 BASS 会阻止打包。HarfBuzz/ANGLE 的 Windows 编译与修改源码覆盖已补齐，保留具体范围和复现边界。
- 旧 `typing.1`、`typing.2`、`typing.3` Release 因已确认的授权和声明缺口暂撤为草稿，保留原标签及资产。
- 原 NuGet Skia DLL 含 DNG/PIEX；同版本替代已在 [CI 39355910024](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39355910024) 编译成功，1,124 个输入与编译/实现检查排除 DNG/PIEX，941 个导出槽逐字段一致、版本 88.1，上游 PNG 与 production App Windows 窗口开闭均通过。保留原包历史通知，新 DLL 与二进制/源码 ZIP 的实物哈希见 [NATIVE_BUILD.md](docs/NATIVE_BUILD.md)；未单独执行 JPEG/WebP/GIF 解码检查。
- Avalonia 的 19 个 C# 文件有 Microsoft/MS-PL 头，实际 DLL 含 22 个相关类型及真实 IL，App 直接引用。特定部分未找到双授权/链接例外，[App 与其组合授权路径仍未解决](third-party/evidence/avalonia-mspl-11.3.12/REVIEW.md)；不是只等 DNG 替换即可恢复发行。独立原生资产不含 Avalonia，可按自身范围单独审核。
- 此前替换 BASS 后的审核构建通过完整编译、15 项既有检查、双插件加载和 Windows 包工程校验；Linux 虚拟会话完成 24 位 WAV、MP3、AIFF 三词练习及关于窗口版权/AGPL 浏览。当前 no-DNG CI 另完成 Windows 图形启动检查；这些结果不证明真实扬声器、Windows 系统解码器、中文输入法或性能降幅。

## [4.0.0-typing.3] - 2026-10-20

- 进入打字练习时后台核对词表，成功显示实际读取/理论读取条数，保留重复词条并忽略空行；取消“准备就绪 · 0 / N”。
- 宿主在同一份编辑器快照上独立统计非空行，练习插件逐条校验后才允许开始；提供功能级别 3 的可选数量元数据接口，共享 API 版本保持 1。
- 读取失败、数量不符或播放练习异常时后台保存 crash log，记录阶段、数量、失败索引、版本与异常；最多保留 20 份，写入失败会明确提示。
- 删除作答倒计时上方的下划线占位；准备页隐藏空倒计时和练习进度条，介绍文字放大居中，保留正常作答、计时和成绩虚拟化。
- 同步打字插件版本更新为 1.0.1，新插件要求宿主功能级别 3；升级请完整解压新的程序包。
- 已运行 .NET 构建、15 项既有检查及 Linux 虚拟图形会话验证：读取数量、空行与重复计数、文字布局、无占位条作答、Enter 提交、结算，以及播放失败后的日志写入。读取数量不符分支尚未通过故障注入验证；真实 Windows 音频与输入法仍需实机核对。

## [4.0.0-typing.2] - 2026-10-19

基于上游 `ArcticLampyrid/DictationAssistant` 的 `v4.0.0`（`324cd043`）及本仓库 `typing.1` 继续迭代。

### 插件扩展

- 新增命令、词表观察、语音提供者、PCM 音效和生命周期接口；一个插件实例可实现多个功能。
- 提供统一上下文：词表快照、带修订校验的更新、界面调度、消息、语音、PCM 播放与 WAV 导出，以及独立数据目录和有界日志。
- 命令和所有练习插件自动进入菜单，语音插件进入引擎列表；支持保存启用状态，重启后生效。
- 保持插件程序集及 `apiVersion: 1`，新增宿主功能级别 2；新接口插件声明 `minimumHostFeatureLevel: 2`。
- 新增命令行命令列表、词表处理、语音列表与 WAV 合成，复用相同插件运行时。
- 提供可编译词表工具、离线提示音及增益效果示例，示例不预装到程序包。

### 稳定性与性能

- 加载和调用在后台执行，逐插件初始化、串行调用、记录异常，并在退出时等待实际工作完成后释放实例。
- 拒绝递归和可能互相等待的嵌套调用；音频插件忙时明确报错，避免模态窗口持有插件时无限等待。
- 合并词表观察通知，限制待执行调用和日志数量；无音效时保留原流式音频路径。
- 修复同步缓存加载失败后的重试、普通播报 PCM 资源释放和预加载异常观察。
- 音频回调内异常转换为可观察的播放故障，重复播放串行，混音缓冲复用；PCM 转换保证释放固定缓冲。
- 简化导出重复循环，在成功、失败和取消时均释放音频流；窗口关闭会取消练习、停止播放并完成插件清理。

### 验证范围

- 保留既有打字逻辑检查、DOCX 提取和完整 Windows 包校验，并验证三个 SDK 示例的构建与命令行调用。
- Linux 虚拟图形会话用于菜单、练习取消和退出检查；真实 Windows 音频、中文输入法及资源占用仍需实机核对。

## [4.0.0-typing.1] - 2026-10-19

本次为基于 DictationAssistant 4.x 的同步打字定制版首次规范发行，包含此前累计的功能和优化。

### 功能

- 通过独立 DLL 插件提供同步打字练习：按设置完成全部播报后才显示输入框并计时，支持回车提交和超时记录。
- 全轮结束后统一展示正确率和逐词成绩，支持忽略大小写及导出 UTF-8 BOM CSV。
- 保留 Word `.docx` 词表导入、表格区域与列选择，以及词表随机重排。
- 保留插件管理和加载错误信息；共享插件接口继续使用 `apiVersion: 1`。

### 稳定性与性能

- 倒计时文字按秒刷新，保留细粒度超时检查，减少频繁界面更新。
- 将语音初始化与长词表评分移到后台，减少占用界面线程的工作。
- 成绩列表使用虚拟化和控件复用，按可见范围显示记录。
- 列表展示和 CSV 导出共用成绩记录，移除额外评分数组并简化导出代码。

### 界面

- 增加练习进度条，固定倒计时显示区域，减少布局变化。
- 成绩分行展示；摘要及导出、关闭按钮在滚动成绩时保持可见。

### 程序包与交付

- 移除打字插件多余的桌面依赖与中间复制步骤，复用宿主的图形库和原生运行文件。
- Windows x64 便携包继续自带 .NET 10，无需另装运行时；升级时应完整解压到新的空文件夹。
- 修复未声明 `kind` 的既有 Word 导入插件清单在构建校验中的兼容问题。
- 统一发行标签、应用版本、版本化资产名称、对应源码和 SHA-256 校验文件。

### 验证范围

- 构建流程包括 .NET 编译、15 项既有打字逻辑检查和 Windows ZIP 文件完整性校验；插件加载与 DOCX 提取另有云环境验证。
- Windows 桌面窗口、实际音频、中文输入法及实机 CPU/内存变化仍需验证。交叉构建成功不能替代这些实机检查，本版按预发布交付。
- 保留原项目 AGPL-3.0-or-later 许可证和第三方组件声明；本版为自定义扩展发行。

[4.0.0-typing.1]: https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.1
[4.0.0-typing.2]: https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.2
[4.0.0-typing.3]: https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.3
[4.0.0-typing.4]: https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.4
[4.0.0-typing.5]: https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.5
[4.0.0-typing.6]: https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/releases/tag/v4.0.0-typing.6

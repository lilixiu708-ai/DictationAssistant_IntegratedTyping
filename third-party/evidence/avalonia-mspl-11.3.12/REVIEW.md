# Avalonia11.3.12：Silverlight/MS-PL定向核查

这批实现确实进入当前Windows-x64 App分发，不能归为只保留历史NOTICE。当前材料未建立将这些部分与AGPL App组合分发的另选授权/例外路径；应作为独立App发布待解决项，不能随DNG移除或原生对应源码补齐自动解除。独立的、不含Avalonia/App程序集的noDNG原生产物不包含本次确认的托管部分，可按自身许可单独分发。

## 真实分发证据

当前发布目录`Avalonia.Controls.dll`，程序集版本11.3.12.0，1,083,392字节：

`SHA256 748dcbdf46bf38ad956bf9592a450cc70642e8c79c8456522d63b61a5cb31023`

它与官方NuGet11.3.12的net8.0 DLL逐字节一致。直接读取PE/.NET元数据确认22个相关TypeDefinitions；AutoCompleteBox有133个、Calendar129个、CalendarDatePicker81个带实际IL方法体的方法，其他集合/按钮/事件类型亦存在实现。枚举和接口按其性质不含IL方法体。依据是TypeDef/MethodDef及方法RVA，不是仅匹配字符串。

实际`DictationAssistant.App.dll`的AssemblyRef直接列`Avalonia.Controls, Version11.3.12.0`，并引用Avalonia.Base、Markup、Themes.Fluent等；源代码`MainWindow : Window`、`App : Application`也表现为同进程框架依赖。项目`PublishTrimmed=false`，对应代码未被裁剪。页面日常是否实例化Calendar不改变这些实现随DLL分发的事实。

完整逐程序集SHA、类型/IL计数及App引用见[实际PE元数据](actual-managed-payload-metadata.json)。未把该链接关系误写成只是无关文件聚合，也未声称执行过这些控件。

## 特定源许可

官方NuGet来源固定为Avalonia commit`37fbd9655cc581ff5b1c6b1fb1be4e3118c889d0`。其根LICENSE为MIT，NOTICE同时完整列SilverlightToolkit/MS-PL：

- [固定原NOTICE](https://github.com/AvaloniaUI/Avalonia/blob/37fbd9655cc581ff5b1c6b1fb1be4e3118c889d0/NOTICE.md#L53)
- [固定原LICENSE](https://github.com/AvaloniaUI/Avalonia/blob/37fbd9655cc581ff5b1c6b1fb1be4e3118c889d0/LICENSE.md)

固定完整源树有19份C#文件逐份标注Microsoft版权和“This source is subject to the Microsoft Public License (Ms-PL).”，包括AutoCompleteBox、Calendar、CalendarDatePicker、SelectionAdapter等。原始头部、各完整源SHA与全部19份不可变源码链接见[源头索引](source-header-index.json)。若干Fluent/Simple的Calendar/DatePicker/TimePicker XAML亦有相同头部；这里已凭实际C#类型实现完成确认，不依赖对编译XAML逐条归因。

微软原SilverlightToolkit许可仍是MS-PL，本次取得原license Git blob`6d2577a9288a0b146ca230dcecb3f0e108acd577`：[不可变原文](https://api.github.com/repos/microsoftarchive/SilverlightToolkit/git/blobs/6d2577a9288a0b146ca230dcecb3f0e108acd577)。有限检查固定源及官方MS-PL/Silverlight/license/GPL相关issue/PR，没有找到这19份特定源的双授权或AGPL链接例外；不将根MIT/NuGet MIT标签本身当作Microsoft另授权证明。

## 源形式、二进制与AGPL组合须分开

MS-PL §3(C)要求保留原通知；§3(D)规定：

> If you distribute any portion of the software in source code form, you may do so only under this license by including a complete copy of this license with your distribution. If you distribute any portion of the software in compiled or object code form, you may only do so under a license that complies with this license.

因此，源形式必须保留原MS-PL；编译/目标码可选另一**符合MS-PL条件**的许可，不能仅凭MS-PL名称认定任何二进制分发都禁止。但“license that complies”不是把原Microsoft源自动重许可为AGPL，也不是AGPL整体/对应源码条件的例外。当前App与该实现直接链接，当前证据没有建立兼容的组合授权路径，应单独记录未解决；并非宣布全部Avalonia源码都属MS-PL或其顶层MIT无效。

GNU官方参考链接：[MS-PL条目](https://www.gnu.org/licenses/license-list.html#ms-pl)。本次GNU多个路径返回HTTP503；官方Savannah`www.git`对应页显示仓库为空，未成功取得该条目原文，未伪称本次抓取成功或据此新增逐字引文。上述判断依据已取得的具体条款、固定源和实际二进制证据。

本次只读核查未修改生产源码、依赖、catalog或发布规则，未要求本轮升级/重写Avalonia；原版权和MS-PL原文应继续保留。本目录只存报告、头部索引和元数据，不复制全Avalonia源码或DLL。

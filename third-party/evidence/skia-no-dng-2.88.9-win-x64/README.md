# SkiaSharp 2.88.9 无 DNG 原生构建实物

日期：2026-10-20；范围仅 Windows x64。Avalonia 11.3.12 与托管 SkiaSharp 2.88.9 保持不变。

固定生产脚本提交：`06ebe119f27cad05af71c14a69c51f5baca586b6`。真实 [Windows CI 39355910024](https://github.com/lilixiu708-ai/DictationAssistant_IntegratedTyping/actions/runs/39355910024) 全部成功。

- [原始构建记录](native-build.json)：941 个导出槽（940 个命名导出）与官方 DLL 逐字段一致，原生版本均为 88.1；DNG/PIEX 从依赖图、编译命令、输入与 DLL 已知实现标记中排除。
- [独立源码闭包复核](source-review.json)：8 份原始归档及 5 个 Gitiles 内容树匹配，源码树的 12,531 个普通文件和 1 个链接可追溯至固定来源。
- [实际源码输入](native-source-inputs.json)：1,124 个文件及各自 SHA-256，包含六项保留依赖；源码包还提供完整相关源码树、原始归档、补丁、配置与重建材料。
- 上游现有工具源文件及项目文件保持原字节，仅通过构建属性使用 .NET 10；生成有效 100×100 PNG。首轮因旧目标框架还原不一致失败，修复后本轮通过。
- [生产程序窗口检查](production-app-smoke.json)：使用新库构建程序，实际打开并正常关闭 Windows 窗口，退出码 0。
- [最终归档哈希与大小](artifact-hashes.json) 对应下载所得字节；DLL SHA-256 为 `e328c8b0a58cd66dace98c0597b404ceb0b585daadf9f8451089a8c09258c95e`，8,860,160 字节。原始记录按字节保留，换行亦参与 manifest 哈希。

没有单独执行 JPEG/WebP/GIF 解码检查，没有测试真实音频设备、中文输入法或测量性能变化。该原生资产不含 Avalonia/App 程序集；它的独立审核不解除 App 的 MS-PL/AGPL 组合授权待解决项。正式下载地址和固定校验值以 [组件目录](../../components.json) 的 `native_overrides` 为准。

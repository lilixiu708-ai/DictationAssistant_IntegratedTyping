# Windows x64 原生图形库

本项目保留 Avalonia 11.3.12 和 SkiaSharp 2.88.9，通过重建同版本原生库关闭 RAW/DNG 与 PIEX；不升级图形 API。真实结果以对应 Actions 及 `native-build.json` 为准。

## 构建入口

运行 Actions 中的 **Build pinned SkiaSharp without DNG (Windows x64)**。构建固定 wrapper、Skia、六项外部源码及工具哈希；对应资料见 [`skia-no-dng-build.json`](../third-party/skia-no-dng-build.json) 和 [`build_skia_without_dng.py`](../scripts/build_skia_without_dng.py)。

依赖 Windows x64、Python 3.12、.NET 10 SDK、MSVC v142、Windows SDK 和 LLVM 12。独立工作流安装/定位工具链并执行：

```powershell
python scripts/build_skia_without_dng.py --msvc-path '<Visual Studio>/VC' --llvm-dir '<LLVM 12>' --build-commit '<完整提交 SHA>'
```

脚本核验下载字节，应用两份固定补丁，生成 GN/Ninja 图并编译。DNG/PIEX 必须从图、编译命令及 DLL 实现中排除；新 DLL 须与官方 2.88.9 的完整导出和原生版本一致。复用上游现有工具创建位图并导出 PNG，不新增测试项目。之后工作流用新库构建本程序，确认 Windows 窗口可以启动和正常关闭。

## 产物与复建

- `SkiaSharp-2.88.9-no-dng-win-x64.zip`：DLL、构建记录、适用的原始许可证及通知，不含 PDB。
- `SkiaSharp-2.88.9-no-dng-win-x64-sources.zip`：实际修改后源码、六项依赖、原始归档、补丁、配置、构建脚本和许可材料。
- `native-build.json`：版本、完整提交、来源/工具/补丁/文件哈希、生成参数和实际验证结果。

源码包中 `sources/SkiaSharp` 是构建使用的源码树；`upstream-archives` 提供原始源归档及校验缓存。重新构建时进入 `rebuild/`，沿用上述命令，并加 `--cache-dir ../upstream-archives`、独立的 `--work-dir` 和 `--output-dir`。`--build-commit` 使用源码包根 `BUILD_PROVENANCE.json` 的原提交。通用编译工具仍须按配置安装；源归档不是 Windows SDK 或编译器的再分发包。

构建输出不等于自动发布。独立原生分发须先通过来源/许可材料审查及工作流的实际技术检查；应用整体仍单独核对其他依赖。正式应用构建只使用 [`components.json`](../third-party/components.json) 中固定地址和哈希的已核验原生资产，不静默使用旧 DNG DLL。

`DICTATION_NATIVE_REVIEW_ARCHIVE` 仅供独立工作流注入本地审查 ZIP；其应用产物保持审核状态。正式配置尚未有有效资产时，普通 Windows 打包会停止并说明原因。

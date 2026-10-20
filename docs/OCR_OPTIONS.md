# 离线 OCR 方案（尚未集成）

研究日期：2026-10-20。仅核对官方源码、许可证与模型卡；未安装 OCR 依赖或模型，未进行准确率、耗时和内存测试，也未改变本项目依赖。

| 方案 | 适用与取舍 | 许可分层要点 |
| --- | --- | --- |
| Tesseract + .NET wrapper + tessdata_fast | 清晰、摆正的印刷词表优先候选，可按需提供简体中文和英文；复杂拍照、斜字需实际验证。 | 引擎、charlesw 封装和官方 fast 模型均为 Apache-2.0；封装中的 InteropDotNet 为 MIT，Leptonica 为 BSD-2-Clause，图片 codec、VC++ runtime 另核对。封装随附的原生版本不等于最新引擎。 |
| PaddleOCR + Sdcb.PaddleOCR | 检测、方向处理、识别完整，适合比较中文照片和复杂版面；mobile 模型可减少模型体积，运行时仍有独立开销。 | PaddleOCR、PaddlePaddle、Sdcb 封装为 Apache-2.0，官方 PP-OCRv5 mobile det/rec 模型卡也分别声明 Apache-2.0；实际推理包中的 OpenCV、MKL/OpenBLAS、oneDNN 等仍按各自许可核对。 |
| RapidOCR .NET + ONNX Runtime | 不依赖 Paddle/Python 安装，可选 CPU 推理；中文模型、字典必须匹配，不能把默认 Latin 模型当中文包。 | RapidOCR 与相关 .NET 封装为 Apache-2.0，ONNX Runtime 为 MIT，转换模型及字典另核来源。BobLd/RapidOcrNet 当前依赖 SkiaSharp 3.119.1，需重新核对；RapidAI C# 路线含 Emgu.CV 4.12，其许可为 GPLv3/商业双授权，整条依赖链不能统称 Apache。 |

## 建议

先考虑**可选、按需加载的 Tesseract 词表导入插件**：仅在打开 OCR 时创建引擎，后台单任务处理，限制图片尺寸/批量，识别后由用户校对再导入词表。模型与插件独立下载，不在打字输入或倒计时中触发识别。

此建议尚未实施。若真实中文样图不满足需求，再比较 Sdcb/Paddle 或 Rapid mobile 路线；模型大小、压缩包大小、发布目录大小和运行内存不是同一指标，不承诺具体降幅。实际集成前需重新核对选定版本、原生库及模型的完整材料；OCR 研究不解除现有发布阻断。

桌面 App 与可选插件通过 [GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases) 分发即可，无需 GitHub Packages；将来需要向插件作者分发 SDK/API 库时，再考虑 NuGet。

## 官方来源（研究快照）

- Tesseract：[引擎](https://github.com/tesseract-ocr/tesseract/tree/99b8aa6cbc4dcff88abf8837dd7a3b785fdddffe)、[.NET 封装](https://github.com/charlesw/tesseract/tree/b5329d5be92fa670031d94c3875f879651a01f55)、[fast 模型及数据许可](https://github.com/tesseract-ocr/tessdata_fast/tree/87416418657359cb625c412a48b6e1d6d41c29bd)。
- Paddle：[PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR/tree/dab3fe35379033fdcb2d0e9572fac0b36c9a9ebf)、[Sdcb/PaddleSharp](https://github.com/sdcb/PaddleSharp/tree/1a9da9c237d0b019b1486219778c247101fc4319)、官方模型卡 [mobile det](https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_det/blob/0d63e78e2b680928f6b1747d76a08db6e645efb7/README.md) / [mobile rec](https://huggingface.co/PaddlePaddle/PP-OCRv5_mobile_rec/blob/682f20538d8c086cb2128e5cfac775e6c4904e85/README.md)。
- Rapid：[BobLd/RapidOcrNet](https://github.com/BobLd/RapidOcrNet/tree/9d1f9f1f6c3ec6f0da123a420842390de9b97268)、[RapidAI C#](https://github.com/RapidAI/RapidOCRCSharp/tree/f03a4a2f9a7402cf8871a01516df84019df4271a)、[ONNX Runtime](https://github.com/microsoft/onnxruntime/tree/28225b8583474cf6a6e7f97222227ae668f80e8b)、[Emgu CV 4.12 许可](https://github.com/emgucv/emgucv/blob/15da93d57b84a3e1371e8b16ded300a87dfd5a61/LICENSE)。

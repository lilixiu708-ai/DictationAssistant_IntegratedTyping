# AGENTS.md - DictationAssistant 开发指南

## 开发原则

### 架构
- **不要拆分 Core 和 App** — 当前单体架构足够简单，Desktop 目标不需要分离
- **保持简单，同时不失可扩展性** — 先简单，后优化
- **偏好 AoS (Array of Structures)** — 按功能模块组织代码，而非按数据类型
- **关注数据的流动与结构性** — 数据流清晰比代码复用更重要

### 代码风格
- **善用 OOP 和 FP** — 接口抽象 + 函数式编程技巧
- **多使用异步、流式处理** — 非阻塞方案优先
- **用库进行 P/Invoke** — 避免直接调用外部可执行文件，优先使用 .NET 绑定库
- **偏爱 API 而非外部进程** — 能用库解决的问题不用命令行

### 不需要做的事
- **不要写测试代码** — 快速行动，功能优先
- **除非被明确要求，否则不要编写兼容性代码** — Move fast and break things

## P/Invoke 原则

- 使用 NuGet 库（如 ManagedBass, Hexa.NET.SDL2）而非直接 DllImport
- 封装平台差异，不在业务代码中直接调用 native 方法
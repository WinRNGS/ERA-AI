# Emuera-SKIA

> **画蛇又添足改造版** — Emuera 1824+v24+EMv18+EEv56+Skiav9

[![Base](https://img.shields.io/badge/base-Emuera%201.824%20%2B%20EEv56-blue)](#)
[![Lang](https://img.shields.io/badge/lang-C%23%20%2F%20ERABASIC-green)](#)
[![Render](https://img.shields.io/badge/render-SkiaSharp%20%2B%20OpenGL-orange)](#)
[![Status](https://img.shields.io/badge/status-active%20development-brightgreen)](#)

---

## 新增功能

> 完整函数与规格变更清单请参阅 [Emuera Skia 文档站](https://emuera-sk-doc-705c3d.gitgud.site/zh/Skia/Skia_Summary.html)。

| 类别 | 功能 |
|:---|:---|
| **渲染引擎** | SkiaSharp 替代 GDI+，OpenGL 硬件加速 + 自动降级，SRGB 色彩空间修复；F11 全屏比例缩放；GDI 字体回退（MS Gothic 等光栅字体） |
| **HTML 渲染** | 统一 depth 绘制管线，`<div>` 容器/定位/层级/ARGB 透明/`height` auto/自闭合、`<font size/valign>` 垂直对齐、`<img>` 颜色矩阵 + `display` 绝对定位 |
| **图像图层** | `SETIMAGELAYER` 独立图层系统（多精灵同层）、`SETIMAGELAYERL` 行相对定位、`GETLINEY` 物理 Y 坐标；`SPRITECREATE`/`SPRITECREATEFROMFILE` 偏移与目标尺寸；`CBGSETSPRITE` 缩放/透明度/颜色矩阵；视口裁剪、动图离窗暂停 |
| **浮点类型** | `#DIMF` / `#FUNCTIONF` / `LOCALF` / `ARGF` / `RESULTF`；`TOSTRF` / `TOFLOAT` / `TOINT` 转换；POWER/SQRT/ABS 等函数根据参数动态返回 Integer/Float；存档双精度 |
| **数学扩展** | 三角函数（`SIN`/`COS`/`TAN`/`ASIN`/`ACOS`/`ATAN`）、取整（`FLOOR`/`CEIL`/`ROUND`）、`UNCHECKED_*` 回环溢出 |
| **变量系统** | ExecutionContext 栈式函数上下文（修复 ARG 递归覆写）、SparseArray 稀疏存储、SafeArithmetic 溢出保护、ERD 用户定义变量 `.als` 别名、ALS 多对一映射 |
| **语法扩展** | `VARIADIC` 可变参数 + `ARGLEN()`、`#REF`/`#REFS` 元素引用、`OUT` 输出参数、`@FUNCNAME:LOCAL` 调试窗口函数名访问 |
| **动态调用** | `EVAL`/`EVALS` 表达式求值、`CALLSTR`/`JUMPSTR`/`TRYCALLSTR` 动态函数名调用、函数调用三层安全性修复 |
| **像素制表** | `HTML_PRINTC`/`HTML_PRINTLC` 右/左对齐输出；`PRINTC`/`PRINTFORMC` 像素制表重构（跨平台 CJK 列对齐） |
| **SQL 系统** | 参数化查询（`SQL_P_EXECUTE_*`）、XML 导入导出（`SQL_IMPORT_XML_CUSTOM`）、连接便利函数（`SQL_CONNECTION_OPEN`） |
| **输入系统** | `TINPUTNF`/`TINPUTSNF`/`TONEINPUTNF`/`TONEINPUTSNF` 不强制回底、自由滚动、`HOVER_PAUSE` 悬停暂停、用户上滚意图保留 |
| **显示控制** | F11 全屏比例缩放、`GETDISPLAYLINE` 负数倒数索引（-1=最后一行）、`STRICT_FONT_FALLBACK` 严格字体回退、`TEXT_BGC_ON/OFF` 文本背景色 |
| **错误处理** | `BEFORE_THROW`/`BEFORE_ERROR` 事件函数、`DisableBeforeErrorThrow` 配置项、8 条 Float 错误消息修正、调试窗口调用栈保留 |
| **诊断工具** | 调试窗口 `LOCAL@FUNCNAME` 监视、当前函数栈保留、`ProcessState.ContextStackCount` 计数 |
| **性能优化** | `SELECTCASE` 编译期跳转表（O(1) 查找）、SQL 图片缓存、SharedBitmapCache 池（max 200）、AnimSpriteCache LRU（max 6）、DIV 渲染 O(1) 命中测试 |
| **跨平台** | SkiaX — LazyLoading 仓库内的 Xamarin.Android 移植端（`feature/xamarin` 分支），直接编译内核，详见 [Emuera.Xamarin/](Emuera.Xamarin/) |
| **平台检测** | `GETPLATFORM()` 返回平台编码（0=Windows / 1=Android / 2=iOS / 3=macOS / 4=Linux / 5=Unknown） |

---

## AI 对话通道（本仓库新增，功能基本已实现）

给引擎接上大语言模型：**主 API 负责叙事，副 API 负责数值结算**，双通道分工。角色人格、说话风格、可写数值范围全部写在 exe 同目录的 `ai_traits.json` 里，改完在菜单点一次重载即生效，**不需要重新编译**。

配置入口：菜单 `AI → AI 设置...`。日常维护词条与 prompt 请看 [docs/AI-词条与Prompt维护指南.md](docs/AI-词条与Prompt维护指南.md)。

> **默认全部关闭。** 启用后你的游戏文本会被发送到你自己配置的 API 端点，请自行确认该端点的隐私政策。密钥加密后存在 exe 同目录的 `ai_config.json`，不进仓库。

### 实现状态

| 阶段 | 内容 |
|:---|:---|
| **P0 地基** | 异步回注 + 硬锁定：AI 请求走后台线程，**后台线程完全不触碰变量层**，读写变量一律回到界面线程（跨线程写变量会损坏引擎数据结构）。同时只允许一个请求在飞，靠票号识别过期回调。变量写入有白名单，变量名写错与下标越界**显式报错而非静默失败**（这是原引擎的一大坑）。44 项自动自检 |
| **P1 最小链路** | AI 面板（输入/发送/终止/清空/设置）、主 API 配置与调用、密钥加密存储。顺带把默认帧率从 5 提到 30（原值在现代机器上观感明显卡顿） |
| **P2 词条系统** | 统一的 JSON 词条库替代逐角色 prompt：一条词条 = 人格描写 + 说话风格 + 行为约束 + 命中条件。命中**由角色实际数值的表达式条件决定**（例如好感度 < 30 才挂「傲娇」），不靠 tag 猜。三级冲突消解（硬冲突禁止共存 / 软冲突按优先级分主次 / 条件冲突按数值区间抑制），全部本地判定不耗 token。单角色命中上限 5 条，system prompt 按角色当前状态动态装配。菜单可预览实际发给模型的 prompt。103 项自动自检 |
| **P3 副 API 与数值回写** | 独立的计算通道把叙事结果落成数值：`function calling` 强制结构化输出，**每轮完整传入当前权威数值状态**（绝不让模型从对话历史推算现值，那是数值漂移的根源）。回写前过三层校验，任一层不过**整批拒绝**：字段白名单 → 单轮变化幅度上限 → 取值区间。幅度上限是挡幻觉的主力——「好感度 +3」和「好感度 +3000」在语义上都成立，只有幅度能识别出后者是胡说。副 API 失败会降级成主 API 单通道，整轮仍然可用。266 项自动自检 |
| **P3 玩家侧出口** | **允许玩家手动改数值**，且不受单轮幅度与区间限制——这套系统的目的是让人玩得开心，不是让人被经济/战斗系统的难度或一次不合理的 AI 结算卡住。可改范围与副 API 完全相同，引擎级校验（白名单、类型、下标）一视同仁：允许作弊不等于允许写坏存档。另有「撤销上轮数值结算」（只撤数值，正文与对话历史一个字不动，面向「叙事我喜欢但结算不合理」）。数值已写但正文生成失败时不会自动回滚，而是留一笔待处置事务交给玩家选：重生成正文 / 回滚数值 / 保留数值——处置之前发不出新请求，避免那批已落盘数值失去可撤回性 |
| **P4 交互控制完备** | AI 接上 ERA 的交互原语（选项、`INPUT` 系、命令触发），AI 不只产出文字而能推动流程；引用历史消息走文本快照副本，上下文压缩后引用不会悬空；手动修改 AI 回复只影响文本上下文，不回滚已写数值 |
| **P5 上下文压缩** | 对话历史接近上下文窗口上限时由副 API 生成摘要，而非按固定轮数硬截断；附 token 用量统计 |
| **P6 加固** | 提示注入检测与输入净化、错误路径全覆盖上报、配置页完善、多步撤销 |

---

## 快速开始

1. 将 `Emuera.exe` 放置于游戏目录
2. 配置 `lazyloading.cfg`（可选）
3. 启动引擎 — ERB 脚本无需任何修改

---

## 文档导航

| 文档 | 说明 |
|:---|:---|
| [Emuera Skia 文档站](https://emuera-sk-doc-705c3d.gitgud.site/zh/index.html) | 在线帮助手册（教程 + 指令参考 + 规格变更） |
| [docs/AI-词条与Prompt维护指南.md](docs/AI-词条与Prompt维护指南.md) | AI 词条库与 prompt 的维护手册（改人格/说话风格/可写数值，不需重新编译） |
| [CHANGELOG.md](CHANGELOG.md) | 版本更新日志（Release Notes） |
| [Readme/画蛇又添足版自改emuera相关说明.txt](Readme/画蛇又添足版自改emuera相关说明.txt) | 原始开发日志（历史参考） |

---

## 参考

| 项目 | 来源 | 说明 |
|:---|:---|:---|
| **Emuera EE** | [Emuera EE](https://gitlab.com/EvilMask/emuera.em) | 基础代码框架 |
| **Lazyloading** | [CRER/emuera.em](https://gitlab.com/CRER/emuera.em) | 懒加载功能实现（lazyloading分支） |
| **SkiaSharp** | [VVIIlet/emuera](https://gitlab.com/VVIIlet/emuera/-/commit/423fb6eb19f5f33af653a780e084bdd40b6efef1) | 渲染引擎替换 |
| **SoundTouch** | [markheath/naudio](https://github.com/naudio/varispeed-sample) | 音频变速处理 |
| **XEmuera** | [Future-R/XEmuera](https://github.com/Future-R/XEmuera) | SkiaX UI 框架参考来源 |

**SkiaSharp 移植说明**：
- 初始实现来自 VVII 的 "SkiaSharpへの置き換え" 提交
- 本项目引用 ee+em 的 b2fd164 版本作为起点
- 在 1.0.0 版本完成完整的 SkiaSharp 功能集成，并进行了大量重构优化

**SkiaX (Android) 移植说明**：
- SkiaX 是 LazyLoading 仓库内的 Xamarin.Android 移植端（`feature/xamarin` 分支），直接编译 LazyLoading Runtime/ 内核
- UI 层框架移植自 [Future-R/XEmuera](https://github.com/Future-R/XEmuera)，页面导航和触屏交互逻辑与 XEmuera 一致
- 渲染实现因 Skia 内核架构差异而完全重写
- 致谢 XEmuera 三代开发者：
  - **Fegelein21** — XEmuera 初代创建者，奠定 Android 端框架基础
  - **CKRainbow** — 适配 EM+EE 内核渲染逻辑
  - **Future-R** — XEmuera 现维护者，持续适配 EM+EE 渲染更新

---

> 版本 V9.1.0

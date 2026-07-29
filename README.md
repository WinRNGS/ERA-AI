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

## 快速开始

1. 将 `Emuera.exe` 放置于游戏目录
2. 配置 `lazyloading.cfg`（可选）
3. 启动引擎 — ERB 脚本无需任何修改

---

## 文档导航

| 文档 | 说明 |
|:---|:---|
| [Emuera Skia 文档站](https://emuera-sk-doc-705c3d.gitgud.site/zh/index.html) | 在线帮助手册（教程 + 指令参考 + 规格变更） |
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

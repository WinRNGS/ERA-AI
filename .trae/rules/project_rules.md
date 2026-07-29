---
alwaysApply: true
---

# Project Rules — LazyLoading

## 项目类型

.NET C# 解决方案（Emuera 原版派生的懒加载修改版）

- 解决方案：`Emuera.sln`
- 构建：`dotnet build "D:\emuera\emuera_lazyloading_selfmodified_version\Emuera.sln" 2>&1`
- 测试：`dotnet test 2>&1`

## Git 拓扑（强制认知）

> **Agent 激活本工作区时必须理解以下拓扑，禁止将 SkiaX 与 m-emuera 混淆。**

```
本仓库: emuera_lazyloading_selfmodified_version (D:\emuera\emuera_lazyloading_selfmodified_version)
├── .git/                                    ← 主仓库
├── [主工作区] checkout: develop-skiasharp   ← LazyLoading Desktop 开发（你当前所在）
├── .git/worktrees/SkiaX/ → D:\emuera\SkiaX ← checkout: feature/xamarin（SkiaX 移植端）
├── 分支: main-skiasharp                     ← 稳定发布
├── 分支: develop-legency                    ← 旧版 GDI+
└── 分支: legency-textrender                 ← 旧版文本渲染
```

**关键事实**：
- **SkiaX（D:\emuera\SkiaX）是本仓库的 Git worktree**，不是独立仓库，不是 m-emuera
- **m-emuera（D:\emuera\m-emuera）是完全独立的 Git 仓库**，与本仓库无 Git 共享关系
- **XEmuera** 是上游原型仓库，仅参考不修改
- 两个工作区共享 `.git`，任何分支上的提交对另一个工作区立即可见

## 技能

通用语言：[CONTEXT.md](file:///d:/emuera/shared-trae/CONTEXT.md)

| 技能 | 激活条件 |
|------|---------|
| **lazyloading** | 内核开发、分支管理、回流 |
| **erabasic** | ERABASIC 解析/运行时、.erb/.erh/.csv |
| **powershell-git** | 终端命令、Git |
| **knowledge-builder** | 发现新洞见 |

## 版本签名（强制认知）

> 更新版本号时**只改 `Emuera/Emuera.csproj`**，运行时通过 `PlatformInterop.GetProductVersion()` 自动读取。
> 详见 [branch-strategy.md §版本签名](file:///d:/emuera/shared-trae/knowledge/lazyloading/branch-strategy.md)。

| 场景 | 改什么 | 示例 |
|------|--------|------|
| 功能版本发布 | `Skiav` 段 | `Skiav9` → `Skiav9.1` |
| SkiaSharp NuGet 升级 | `Skiav` 段 | `Skiav5.1` → `Skiav6.0` |
| 上游 EM+EE 同步 | `EMv`/`EEv` 段 | `EEv56` → `EEv57` |
| Android 版本 | `AndroidManifest.xml` 的 `versionCode`/`versionName` | 独立维护 |

**历史段说明**：`1824`（Emuera 原版 1.824）与 `v24`（私家改编版）是历史版本号，不再递增；LazyLoading/Skia 变体的实际功能版本通过 `Skiav` 段表达。

**禁止**：不要改 `Sys.cs` 的 `EmueraVersionText`、不要改 `Program.cs` 的 `GetProductVersion` 委托——它们自动读取 csproj 的 `<InformationalVersion>`。

## ERB 脚本编写规则

> 编写任何 ERB/ERH 脚本前**必须激活 erabasic 技能**并查阅 [syntax-quickref.md](file:///d:/emuera/shared-trae/knowledge/erabasic/syntax-quickref.md)。
> 以下为最低限度规则，完整检查清单见 erabasic SKILL.md 的 Pre-Write Checklist。

- `#DIM` 是预处理指令，`#` 不可省略
- 字符串字面量需用 `""` 包裹（如 `"pet_1"`），否则被当变量名
- A-Z 单字母变量是引擎保留变量，不可用于 `#DIM`
- `#FUNCTION`/`#FUNCTIONS` 必须紧跟函数标签行
- HTML 标签（`<img>`, `<div>`, `<font>` 等）必须通过 `HTML_PRINT` 输出，`PRINT` 系列不解析 HTML
- FORM 字符串中：`%变量%` 用于字符串插值，`{表达式}` 用于整数插值，不可混用

## 子规则

- [内核修改分类](kernel/modification-classification.md) — A/B/C 类分类与回流策略

## 会话结束规则

1. 按 knowledge-builder 方法论同步知识库

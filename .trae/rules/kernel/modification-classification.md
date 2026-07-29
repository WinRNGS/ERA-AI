---
description: 修改 Emuera/Runtime 或 Emuera/Program.cs 内核代码时生效
---

# 内核修改分类规则

修改 `Emuera/` 下的代码时，必须判断修改属于哪类：

| 分类 | 判断标准 | 回流策略 |
|------|---------|---------|
| **A 类** | 修复 WinForms 版也会触发的 Bug（NRE、路径错误、null guard） | cherry-pick 回 develop |
| **B 类** | 移除 WinForms 依赖（`#if WINDOWS`、PlatformInterop 替代） | 需先建 PlatformInterop |
| **C 类** | Xamarin 专属（诊断日志、Android 配置项、新增方法） | 不回流 |

## 分支拓扑

```
main-skiasharp ← 发布（线性）
  └─ develop-skiasharp ← 内核开发（线性）
       └─ feature/xamarin ← Android 移植（单向接收 develop 更新）
```

## .gitignore 规则

- 仅根目录 `.gitignore`，禁止子项目 .gitignore
- 根 `.gitignore` 不含 `Emuera.Xamarin/` 忽略行

## Survey-First 协议

修改内核 Runtime/ 代码前必须先勘测：
1. 接口理解：LazyLoading 源码中该功能的完整接口是什么？
2. 目标平台限制：目标平台有哪些硬限制？
3. 移植策略：直接移植 vs 重新设计？
4. 风险 Top 3：最可能出问题的 3 个点
5. 知识库覆盖：`xemuera/` 或 `lazyloading/` 域是否有相关条目？

详见 [branch-strategy.md](file:///d:/emuera/shared-trae/knowledge/lazyloading/branch-strategy.md) 和 [kernel-audit.md](file:///d:/emuera/shared-trae/knowledge/lazyloading/kernel-audit.md)

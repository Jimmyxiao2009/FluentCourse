# 蛋白质油脂课件 · 视觉设计系统 v1

> 本文档是 `samples/ProteinOils.fcourse` 的视觉契约。所有页面必须遵循此规范，
> 避免上版「每页自己写一遍色值」导致的不统一问题。

## 1. 设计原则

1. **暗色科学风**：化学课件天然适合深色背景（凸出荧光实验照片）。
2. **两章双色**：蛋白质 = 暖红调，油脂 = 冷青调。其它（开场/检测）用中性灰白。
3. **同色板一次定义**：`palette.xaml` 与本文档 `## 2` 同源，编辑器和运行时都引用同一组值。
4. **页眉页脚 chrome 一致**：每页顶上一条「分节徽章 + 页码 + 标题」信息条，底部可选步骤/操作条。
5. **卡片结构统一**：所有信息卡都是 `Card.Default` 或 `Card.Highlight`（前者灰边白字，后者左边一条主题色）。

## 2. 色板（Color Tokens）

所有颜色以 `#AARRGGBB` 八位十六进制写在页面 XAML 中（`XamlReader` 不解析 `Color` 资源）。

| Token               | 值              | 用途                                          |
|---------------------|-----------------|-----------------------------------------------|
| `--bg-stage`        | `#FF0E1424`     | 全局幕布背景（在 manifest.stage.background） |
| `--bg-page`         | `#FF0E1424`     | 页面 `Grid Background`（与 stage 同）         |
| `--bg-elevated`     | `#FF161D33`     | 卡片底色                                      |
| `--bg-elevated-2`   | `#FF1E2747`     | 次级卡片 / 高亮卡片底色                      |
| `--border-subtle`   | `#FF252D48`     | 卡片描边                                      |
| `--border-strong`   | `#FF3B4566`     | 分割线 / 表头描边                             |
| `--text-primary`    | `#FFF5F7FB`     | 标题、正文                                    |
| `--text-secondary`  `#FFC7CEDD`     | 次级正文                                      |
| `--text-muted`     | `#FF8893AC`     | 提示、说明、英文注音                          |
| `--accent-protein` | `#FFE94560`     | 蛋白质主题色（暖红）                          |
| `--accent-protein-soft` | `#33E94560` | 蛋白质弱化（卡片左条/分隔条）                |
| `--accent-lipid`   | `#FF00B4D8`     | 油脂主题色（冷青）                            |
| `--accent-lipid-soft` | `#3300B4D8` | 油脂弱化                                      |
| `--accent-neutral` | `#FF8B5CF6`     | 中性强调（紫，用于开场/学习目标/检测）        |
| `--accent-success`  `#FF22C55E`     | 正确答案 / 成功态                             |
| `--accent-warning`  `#FFF59E0B`     | 重点 / 警告                                   |

**配色策略**：
- 蛋白质章节 3 页（组成/性质/应用）所有强调色用 `--accent-protein`。
- 油脂章节 3 页（组成/性质/应用）所有强调色用 `--accent-lipid`。
- 开场、学习目标、检测用 `--accent-neutral` 或纯白。

## 3. 字号（Type Scale）

| Token              | Size | Weight      | 用例                         |
|--------------------|------|-------------|------------------------------|
| `display.hero`     | 96   | SemiBold    | 封面主标题（蛋白质 油脂）   |
| `display.chapter`  | 56   | SemiBold    | 分节页大标题                 |
| `heading.section`  | 44   | SemiBold    | 章节标题（一、组成）         |
| `heading.card`     | 28   | SemiBold    | 卡片内一级标题               |
| `heading.subcard`  | 22   | SemiBold    | 卡片内二级标题               |
| `body.large`       | 22   | Normal      | 正文（卡片正文默认）         |
| `body.normal`      | 20   | Normal      | 次级正文                     |
| `body.small`       | 18   | Normal      | 说明、提示                   |
| `caption`          | 16   | Normal      | 角标、英文注音               |
| `code.chem`        | 24   | Normal      | 化学结构式（Consolas/Cambria Math） |
| `badge`            | 14   | SemiBold    | 编号徽章 (1/2/3)             |

## 4. 间距（Spacing Scale）

| Token        | Value | 用例                       |
|--------------|-------|----------------------------|
| `space.xs`   | 4     | 紧邻图标与文字             |
| `space.s`    | 8     | 卡片内小间距               |
| `space.m`    | 16    | 卡片基础内边距 / 卡片间距  |
| `space.l`    | 24    | 卡片大内边距               |
| `space.xl`   | 32    | 标题与正文之间             |
| `space.xxl`  | 48    | 章节大间距                 |

**页面内边距**：所有内容页统一 `Padding="100,80,100,80"`（封面除外）。
**卡片内边距**：`Padding="28,24"`（突出）或 `Padding="24,20"`（次级）。

## 5. 卡片系统

### 5.1 `Card.Default`
```
BorderThickness="1" CornerRadius="10"
BorderBrush="--border-subtle"
Background="--bg-elevated"
Padding="28,24"
```
- 卡片内一级标题：`heading.card` 字号 + `text-primary` 颜色。
- 卡片内二级标题：`heading.subcard` 字号 + `text-secondary` 颜色。
- 卡片内正文：`body.large` 字号 + `text-secondary` 颜色，行高 32。

### 5.2 `Card.Highlight`
在 `Card.Default` 基础上加：
```
BorderBrush="--accent-{section}"
BorderThickness="3,1,1,1"
```
或
```
Background="--bg-elevated-2"
```
- 用作「关键概念」「重点反应」等需要突出的卡片。

### 5.3 `FormulaChip`
化学结构式的展示容器：
```
BorderThickness="1" CornerRadius="6"
BorderBrush="--border-strong"
Background="--bg-elevated-2"
Padding="20,14"
```
- 内部用 `code.chem` 字号、化学符号的天然字体。
- `TextAlignment="Center"`，化学式居中。

## 6. 页眉页脚 Chrome

> 这是视觉统一的关键 —— 每页都长一样。

### 6.1 顶部信息条（`HeaderStrip`）
高度 ~64px，水平方向：
- 左侧：分节徽章（`Badge`）= 小色块 + 分节名（如「蛋白质 / Chapter 1」）。
- 中间：页面主标题（`heading.section` 字号）。
- 右侧：页码（`caption` 字号 + `text-muted`，如「3 / 11」）。

### 6.2 底部操作条（`FooterStrip`，可选）
- 用于「逐步显示」类页面：左下 `caption` 提示，右侧「下一步 ▶」按钮。
- 其它页面不渲染此条。

## 7. 章节色映射

| 章节       | section 值    | 主强调色          |
|------------|---------------|-------------------|
| 开场       | `开场`        | `--accent-neutral` |
| 学习目标   | `开场`        | `--accent-neutral` |
| 蛋白质     | `蛋白质`      | `--accent-protein` |
| 油脂       | `油脂`        | `--accent-lipid`   |
| 随堂检测   | `练习`        | `--accent-neutral` |

## 8. 字体

- **正文字体**：不显式设 `FontFamily`，跟随主题（Segoe UI Variable / Segoe UI）。
- **化学结构式**：`FontFamily="Consolas"` 或 `"Cambria Math"`，等宽或数学体。
- **封面大标题**：跟随主题，加 `FontWeight="SemiBold"`。

## 9. 圆角与阴影

- **卡片圆角**：`CornerRadius="10"`。
- **按钮圆角**：`CornerRadius="8"`（小）/ `CornerRadius="10"`（大）。
- **阴影**：本版不引入 `DropShadowPanel`（`XamlReader` 对其支持不稳），用 `Border` 描边 + 不同底色差做层级。

## 10. 不允许的做法

- ❌ 在不同页面随手写新的 hex 颜色值。
- ❌ 混用不同 `Padding` 值（统一 100,80 / 28,24 / 24,20 / 20,14）。
- ❌ 用 emoji 当图标（用 `Badge` 数字 + 真实图片替代）。
- ❌ 用 ASCII 画化学结构式（用 `--accent-*` + `FormulaChip` + 真实 PNG 资源）。
- ❌ 章节编号「一、二、三」与分节徽章冲突。规则：**每个 section 内部独立用「一、二、三」编号，分节徽章**（`开场` / `蛋白质` / `油脂` / `练习`）**负责标识当前属于哪一章**。

## 11. 资源文件命名约定

`assets/image{N}.{ext}` 是从原 PPT 抽出来的原始名。本设计不强制重命名，但：
- `manifest.assets[].key` 必须等于 `path`（运行时按 key 匹配描述符）。
- 资源按内容分组即可，编号不必连续。

## 12. `theme/palette.xaml` 与运行时同步

`theme/palette.xaml` 是设计令牌（`## 2` / `## 3`）的 XAML 真源，便于编辑器一键替换主题。

**注意**：v1.1 运行时暂未实现 `theme/` 自动合并，每个页面 XAML 仍以字面量 `Color` / 数值写出
令牌（因为 `XamlReader.Load` 不解析颜色资源引用）。改主题时必须**同时**更新：
1. `docs/design-system.md`（人类阅读的真源）
2. `theme/palette.xaml`（编辑器 / 未来运行时使用）
3. 所有 `pages/*.xaml`（运行时直接消费）

**未来增强**：在 `PagePlayer` 加一段 `ResourceDictionary.MergedDictionaries` 合并代码，主题就能
一次性生效。当前阶段这种「三处同步」是已知 trade-off。

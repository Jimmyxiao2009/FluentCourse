# .fcourse 文件格式规范 v1.1

## 1. 总览

`.fcourse` 是一个普通 ZIP 容器,扩展名重命名而已。设计目标:

- **可读** —— 解压即可看到 XAML/JSON,便于版本控制 (gitignore 二进制 asset 即可)。
- **可分发** —— 单文件,便于通过聊天工具/网盘传递。
- **运行时低开销** —— manifest 一次性反序列化,页面按需 `XamlReader.Load`。
- **向后兼容** —— v1.1 所有新增字段均为可选（nullable / 有默认值），旧 manifest 无需修改即可运行。

## 2. 包结构

```
MyCourse.fcourse              (ZIP)
├── manifest.json             必选,UTF-8 无 BOM
├── pages/
│   ├── 01-cover.xaml         页面 UI 树,纯 XAML,不含 x:Class
│   ├── 02-content.xaml
│   └── ...
├── assets/                   资源,manifest.assets 中登记
│   ├── images/cover.png
│   ├── audio/intro.m4a
│   └── video/demo.mp4
├── animations/               (可选) 复用动画片段,JSON
│   └── pulse.json
└── theme/                    (可选) 自定义资源字典
    └── palette.xaml
```

> **注意**:页面 XAML 内的资源引用统一使用 `ms-fcourse:///assets/...` 这类自定义 URI,运行时由 `AssetBinder` 解析为内存流。

## 3. manifest.json schema

```jsonc
{
  "$schema": "https://fluentcourse.dev/schemas/manifest.v1.json",
  "formatVersion": 1,
  "id": "9f1b9c0a-0001-4c5a-9b3a-1e6f3a2c0bca",
  "title": "WinUI 3 入门第一课",
  "author": "Ouyang Quan",
  "createdAt": "2026-06-04T00:00:00Z",
  "updatedAt": "2026-06-04T00:00:00Z",
  "language": "zh-CN",

  "stage": {
    "designWidth": 1920,
    "designHeight": 1080,
    "scaleMode": "Uniform",        // Uniform | UniformToFill | Stretch
    "background": "#FF202020",
    "theme": "System"              // Light | Dark | System
  },

  "pages": [
    {
      "id": "cover",
      "file": "pages/01-cover.xaml",
      "title": "封面",
      "section": "开场",           // v1.1: 章节/分组名
      "notes": "演讲者备注,纯文本",
      "duration": null,            // 自动播放秒数,null 表示手动
      "transition": "Fade",
      "transitionDurationMs": 600, // v1.1: 覆盖过渡时长(ms),默认320
      "hidden": false,             // v1.1: 演示流程是否跳过
      "background": "#FF2A2A2A",   // v1.1: 单页背景颜色覆盖
      "states": ["Default"]
    }
  ],

  "triggers": [
    {
      "id": "t-cover-load",
      "page": "cover",
      "on": {
        "type": "PageLoaded",      // 见下方"事件类型"表
        "target": "BtnStart",
        "key": "Enter",            // Key 事件的 VirtualKey 名称
        "tapCount": 1,             // v1.1: 区分单/双击,null=不区分
        "modifiers": ["Ctrl"],     // v1.1: 组合键,仅 Key 事件
        "debounceMs": 200,         // v1.1: 去抖(ms)
        "once": true,              // v1.1: 一次性触发后自动解绑
        "comparedTo": "equals",   // v1.1: ValueChanged 的比较方式
        "threshold": 80            // v1.1: Swipe/Drag 距离阈值(px)
      },
      "do": [
        { "type": "PlayAnimation", "target": "Title", "animation": "FadeInUp" }
      ],
      "steps": [],                 // 多步骤时 do 忽略,改为 steps
      "stopAfterLastStep": true,
      "condition": null
    }
  ],

  "animations": {
    "FadeInUp": {
      "duration": 600,
      "delay": 0,
      "easing": "EaseOutCubic",    // 见下方"缓动函数"表
      "repeat": 1,                 // 0=永久循环
      "autoReverse": false,
      "tracks": [
        { "property": "Opacity", "from": 0, "to": 1 },
        { "property": "TranslateY", "from": 24, "to": 0 }
      ]
    }
  },

  "assets": [
    {
      "key": "cover-bg",
      "path": "assets/images/cover.png",
      "type": "image",             // binary|image|video|audio|font|lottie
      "fit": "cover",              // v1.1: none|fill|contain|cover|stretch
      "placeholderColor": "#FF333333", // v1.1: 加载前背景色
      "preload": "eager",          // v1.1: eager|lazy|metadata
      "sha256": "..."
    }
  ]
}
```

## 4. 字段速查表

### 4.1 PageDescriptor（页面）

| 字段 | 类型 | 默认值 | 版本 | 说明 |
|------|------|--------|------|------|
| `id` | string | — (必填) | v1 | 页面唯一标识 |
| `file` | string | — (必填) | v1 | 页面 XAML 文件路径 |
| `title` | string? | null | v1 | 页面标题 |
| `notes` | string? | null | v1 | 演讲者备注 |
| `duration` | double? | null | v1 | 自动翻页秒数,null=手动 |
| `transition` | string | `"Fade"` | v1 | 过渡动画 |
| `states` | string[] | `[]` | v1 | VisualState 名称列表 |
| `transitionDurationMs` | int? | null | v1.1 | 覆盖过渡时长(ms) |
| `hidden` | bool | false | v1.1 | 演示流程跳过(仍可见于编辑器) |
| `section` | string? | null | v1.1 | 章节/分组名称 |
| `background` | string? | null | v1.1 | 单页背景(#AARRGGBB),覆盖 stage |

### 4.2 Transition（页面过渡）— v1.1 扩展

| 值 | 说明 | 版本 |
|----|------|------|
| `None` | 直接切换 | v1 |
| `Fade` | 淡入淡出 | v1 |
| `SlidePush` | 左推(新页从右推入) | v1 |
| `SlideUp` | 上推(新页从下推入) | v1.1 |
| `SlideDown` | 下推(新页从上推入) | v1.1 |
| `SlideLeft` | 左入(新页从右滑入) | v1.1 |
| `SlideRight` | 右入(新页从左滑入) | v1.1 |
| `Zoom` | 缩放(新页放大进入) | v1 |

### 4.3 TriggerEvent（触发器事件）— v1.1 扩展

| 字段 | 类型 | 默认值 | 版本 | 说明 |
|------|------|--------|------|------|
| `type` | string | —(必填) | v1 | 见下方事件类型表 |
| `target` | string? | null | v1 | 目标元素 `x:Name` |
| `key` | string? | null | v1 | VirtualKey 名称 |
| `at` | double? | null | v1 | 时间点(s),Time 事件专用 |
| `tapCount` | int? | null | v1.1 | 1/2/3,null=不区分 |
| `modifiers` | string[]? | null | v1.1 | `Ctrl`/`Shift`/`Alt`,Key 事件专用 |
| `debounceMs` | int? | null | v1.1 | 去抖窗口(ms) |
| `once` | bool | false | v1.1 | 触发一次后自动解绑 |
| `comparedTo` | string? | null | v1.1 | `equals`/`greater`/`less`,ValueChanged 专用 |
| `threshold` | double? | null | v1.1 | Swipe 距离/Drag 起始阈值(px) |

#### 事件类型（`on.type`）— v1.1 扩展

| 值 | 说明 | 版本 |
|----|------|------|
| `PageLoaded` | 页面加载后触发 | v1 |
| `Click` | 点击(Tapped) | v1 |
| `DoubleClick` | 双击 | v1.1 |
| `ValueChanged` | 值变化(TextBox/ToggleButton/Slider) | v1 |
| `Key` | 键盘按键 | v1 |
| `Time` | 媒体时间点 | v1 |
| `PointerEnter` | 指针进入元素 | v1.1 |
| `PointerExit` | 指针离开元素 | v1.1 |
| `PointerPressed` | 指针按下 | v1.1 |
| `PointerReleased` | 指针释放 | v1.1 |
| `Hold` | 长按(Holding Started) | v1.1 |
| `SwipeLeft` | 左滑(>80px) | v1.1 |
| `SwipeRight` | 右滑(>80px) | v1.1 |
| `SwipeUp` | 上滑(>80px) | v1.1 |
| `SwipeDown` | 下滑(>80px) | v1.1 |
| `Drag` | 拖拽(>10px 即触发) | v1.1 |
| `Focus` | 获得焦点 | v1.1 |
| `LostFocus` | 失去焦点 | v1.1 |
| `Scroll` | 滚动(ScrollViewer) | v1.1 |
| `Drop` | 拖放释放 | v1.1 |
| `MediaEnded` | 媒体播放结束 | v1.1 |

### 4.4 ActionDescriptor（动作）— v1.1 扩展

所有动作共用 `type` 和可选 `target`。额外参数通过 extension data 传入（`GetString`/`GetDouble`/`GetBool`/`GetInt` 读取）。

#### 导航类

| type | 参数 | 说明 | 版本 |
|------|------|------|------|
| `GoToPage` | `page`(string) | 跳转到指定页面 | v1 |
| `NextPage` | — | 下一页 | v1 |
| `PreviousPage` | — | 上一页 | v1 |
| `FirstPage` | — | 第一页(跳过 hidden) | v1.1 |
| `LastPage` | — | 最后一页(跳过 hidden) | v1.1 |
| `GoBack` | — | 回退到上一页(历史栈) | v1.1 |

#### 动画类

| type | 参数 | 说明 | 版本 |
|------|------|------|------|
| `PlayAnimation` | `animation`(string) | 播放动画 | v1 |
| `StopAnimation` | `animation`(string) | 停止动画(需首次 Play 缓存) | v1.1 |
| `PauseAnimation` | `animation`(string) | 暂停 | v1.1 |
| `ResumeAnimation` | `animation`(string) | 恢复 | v1.1 |

#### 状态类

| type | 参数 | 说明 | 版本 |
|------|------|------|------|
| `GoToState` | `state`(string) | 切换到指定 VisualState | v1 |
| `ToggleState` | `state`(string) | 切换 VisualState | v1.1 |

#### 媒体类（v1.1 新增）

| type | 参数 | 说明 |
|------|------|------|
| `PlayMedia` | `target` | 播放 MediaPlayerElement |
| `PauseMedia` | `target` | 暂停 |
| `StopMedia` | `target` | 停止(Pause + Position=0) |
| `SeekMedia` | `target`, `to`(double, 秒) | 跳转到指定时间 |
| `SetVolume` | `target`, `value`(double, 0-1) | 设置音量 |
| `Mute` | `target`, `value`(bool) | 静音开关 |

#### StepRevealControl（v1.1 新增）

| type | 参数 | 说明 |
|------|------|------|
| `NextStep` | `target` | 展开下一步 |
| `PreviousStep` | `target` | 收起上一步 |
| `ResetSteps` | `target` | 重置所有步骤 |
| `GoToStep` | `target`, `step`(int) | 直接跳转到第 N 步 |

#### QuizControl（v1.1 新增）

| type | 参数 | 说明 |
|------|------|------|
| `SubmitQuiz` | `target` | 提交答题 |
| `ResetQuiz` | `target` | 重做答题 |
| `ShowFeedback` | `target` | 显示反馈 |
| `HideFeedback` | `target` | 隐藏反馈 |

#### 变量（v1.1 新增）

| type | 参数 | 说明 |
|------|------|------|
| `SetVariable` | `name`(string), `value` | 设置变量(string/double/bool) |
| `IncrementVariable` | `name`(string), `by`(double, 默认1) | 数值自增 |
| `ToggleVariable` | `name`(string) | bool 取反 |

#### 窗口类（v1.1 新增）

| type | 参数 | 说明 |
|------|------|------|
| `OpenHyperlink` | `url`(string) | 打开系统浏览器 |
| `OpenFile` | `path`(string) | 打开文件 |
| `ExitPresentation` | — | 退出程序 |
| `ToggleFullscreen` | — | 切换全屏 |

#### 原有动作

| type | 参数 | 说明 | 版本 |
|------|------|------|------|
| `ShowToast` | `title`, `message` | 弹出提示 | v1 |
| `SetProperty` | `property`(`Visibility`/`Opacity`/`IsEnabled`), `value` | 设置属性 | v1 |

### 4.5 AnimationDescriptor（动画）— v1.1 扩展

| 字段 | 类型 | 默认值 | 版本 | 说明 |
|------|------|--------|------|------|
| `duration` | double | 400 | v1 | 时长(ms) |
| `delay` | double | 0 | v1 | 延迟(ms) |
| `easing` | string | `"EaseOutCubic"` | v1 | 缓动函数名 |
| `repeat` | int | 1 | v1 | 重复次数,0=永久 |
| `autoReverse` | bool | false | v1 | 自动反向 |
| `tracks` | AnimationTrack[] | `[]` | v1 | 动画轨道列表 |

#### AnimationTrack（动画轨道）

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `property` | string | `"Opacity"` | 动画属性(见下表) |
| `from` | double? | null | 起始值 |
| `to` | double? | null | 终止值 |
| `fromColor` | string? | null | 起始颜色(仅 Color 属性) |
| `toColor` | string? | null | 终止颜色(仅 Color 属性) |

#### 可动画属性（`property`）— v1.1 扩展

| 值 | Target 路径 | 版本 |
|----|-------------|------|
| `Opacity` | `(UIElement.Opacity)` | v1 |
| `TranslateX` | `(UIElement.RenderTransform).(CompositeTransform.TranslateX)` | v1 |
| `TranslateY` | `(UIElement.RenderTransform).(CompositeTransform.TranslateY)` | v1 |
| `ScaleX` | `(UIElement.RenderTransform).(CompositeTransform.ScaleX)` | v1 |
| `ScaleY` | `(UIElement.RenderTransform).(CompositeTransform.ScaleY)` | v1 |
| `Rotation` | `(UIElement.RenderTransform).(CompositeTransform.Rotation)` | v1 |
| `Color` | 按目标类型选择：`TextBlock.Foreground` / `Shape.Fill` / `Control.Foreground` `.(SolidColorBrush.Color)` | v1 |
| `Width` | `(FrameworkElement.Width)` | v1.1 |
| `Height` | `(FrameworkElement.Height)` | v1.1 |
| `FontSize` | `(Control.FontSize)` | v1.1 |
| `CornerRadius` | **不支持**（CornerRadius 是结构体，WinUI 无对应动画类型；运行时跳过该轨道并输出 Debug 警告） | v1.1（已废弃） |
| `BackgroundColor` | 按目标类型选择：`Panel.Background` / `Border.Background` / `Shape.Fill` / `Control.Background` `.(SolidColorBrush.Color)` | v1.1 |
| `BorderColor` | 按目标类型选择：`Border.BorderBrush` / `Shape.Stroke` / `Control.BorderBrush` `.(SolidColorBrush.Color)` | v1.1 |

#### 缓动函数（`easing`）— v1.1 扩展

| 值 | 类型 | 版本 |
|----|------|------|
| `Linear` | 匀速 | v1 |
| `EaseInQuad` / `EaseOutQuad` / `EaseInOutQuad` | 二次方 | v1.1 |
| `EaseInCubic` / `EaseOutCubic` / `EaseInOutCubic` | 三次方 | v1 |
| `EaseInCirc` / `EaseOutCirc` | 圆形 | v1.1 |
| `EaseInBack` / `EaseOutBack` | 回退 | v1.1 |
| `EaseInElastic` / `EaseOutElastic` | 弹性 | v1 |
| `EaseInBounce` / `EaseOutBounce` | 弹跳 | v1 |

#### 预设动画（可通过同名 `animations` 条目覆盖）

| 名称 | 效果 | 版本 |
|------|------|------|
| `FadeIn` | 淡入(Opacity 0→1) | v1 |
| `FadeOut` | 淡出(Opacity 1→0) | v1 |
| `FadeInUp` | 淡入+上移(24→0) | v1 |
| `FadeInDown` | 淡入+下移(-24→0) | v1 |
| `FadeInLeft` | 淡入+右移(-24→0) | v1.1 |
| `FadeInRight` | 淡入+左移(24→0) | v1.1 |
| `ZoomIn` | 缩放淡入(0.85→1) | v1 |
| `PopIn` | 弹入(0.5→1, EaseOutBack) | v1.1 |
| `Pulse` | 脉冲(Scale 1→1.06, 自动反向) | v1 |
| `Shake` | 抖动(TranslateX -8↔8, 4次) | v1 |
| `Bounce` | 弹跳(EaseOutBounce, 0.3→1) | v1.1 |

### 4.6 AssetDescriptor（资源）— v1.1 扩展

| 字段 | 类型 | 默认值 | 版本 | 说明 |
|------|------|--------|------|------|
| `key` | string | —(必填) | v1 | 资源标识符 |
| `path` | string | —(必填) | v1 | 包内相对路径 |
| `type` | string | `"binary"` | v1 | 资源类型 |
| `sha256` | string? | null | v1 | SHA-256 校验和 |
| `fit` | string | `"stretch"` | v1.1 | 图片填充模式 |
| `placeholderColor` | string? | null | v1.1 | 加载前背景色(#AARRGGBB) |
| `preload` | string | `"eager"` | v1.1 | 预加载策略 |

#### 资源类型（`type`）

| 值 | 说明 |
|----|------|
| `binary` | 通用二进制 |
| `image` | 图片(jpg/png/gif/webp) |
| `video` | 视频(mp4等) |
| `audio` | 音频(mp3/m4a等) |
| `font` | 字体(ttf/otf) |
| `lottie` | Lottie 动画(json) |

#### 填充模式（`fit`）

| 值 | Stretch |
|----|---------|
| `none` | None |
| `fill` | Fill |
| `contain` | Uniform |
| `cover` | UniformToFill |
| `stretch` | Uniform(默认) |

#### 预加载策略（`preload`）

| 值 | 说明 |
|----|------|
| `eager` | 加载页时立即下载(默认) |
| `lazy` | 元素进入视图 ≥500ms 后下载 |
| `metadata` | 仅解析元数据 |

### 4.7 QuizControl（答题）— v1.1 扩展

#### QuizChoice 属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Text` | string | `""` | 选项文本 |
| `IsAnswer` | bool | false | 是否为正确答案 |
| `IsSelected` | bool | false | 是否被选中(TwoWay) |
| `Feedback` | string | `""` | 答对反馈语 |
| `Explanation` | string | `""` | **v1.1**: 解析说明,提交后始终显示 |

#### QuizControl 属性

| 属性 | 类型 | 默认值 | 版本 | 说明 |
|------|------|--------|------|------|
| `Question` | string | `""` | v1 | 题目文本 |
| `Mode` | QuizMode | Single | v1 | 题型枚举 |
| `Choices` | ObservableCollection\<QuizChoice\> | — | v1 | 选项集合 |
| `ShowFeedback` | bool | true | v1 | 是否显示反馈区 |
| `AllowRetry` | bool | true | v1 | 是否允许重做 |
| `ScoreStrategy` | ScoreStrategy | AllOrNothing | v1.1 | 计分策略 |
| `Blanks` | ObservableCollection\<QuizBlank\> | — | v1.1 | 填空题空位集合 |

#### QuizMode（题型）

| 值 | 说明 | 版本 |
|----|------|------|
| `Single` | 单选 | v1 |
| `Multiple` | 多选 | v1 |
| `TrueFalse` | 判断 | v1 |
| `FillInBlank` | 填空 | v1.1 |

#### ScoreStrategy（计分策略）— v1.1

| 值 | 说明 |
|----|------|
| `AllOrNothing` | 全对才得分(默认,向后兼容) |
| `PartialCredit` | 部分得分: `(选对的 - 选错的) / 总正确数` |
| `Proportional` | 比例得分: `选对的 / 总正确数` |

#### QuizBlank（填空位）— v1.1

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Pattern` | string | `"*"` | 匹配模式(正则或`*`通配) |
| `CaseSensitive` | bool | false | 是否区分大小写 |
| `Explanation` | string | `""` | 解析说明 |
| `UserAnswer` | string | `""` | 用户填入的答案(runtime) |

## 5. 页面 XAML 约束

- **不允许** `x:Class`、`x:Name` 之外的代码后置标记 —— `XamlReader.Load` 会抛 `Cannot create unknown type`。
- **不允许** 直接写 `Click="Foo"` 这类事件 —— XamlReader 不解析事件绑定。事件统一在 manifest 的 `triggers` 节点声明,运行时再挂载。
- **不允许** 在 `Text="..."` 属性值中使用 ASCII 双引号 `"` (U+0022) —— XamlReader 将其视为属性终止符。使用 `&quot;` 转义或中文直角引号 `「」`。
- **不允许** 使用 XML 字符实体如 `&#x0a;` —— 改用 `<LineBreak/>` 元素。
- **允许** 任意 WinUI 3 控件、`ResourceDictionary`、`VisualStateManager`、`Storyboard`(作为静态资源)。
- 根元素必须是 `Grid` 或 `Canvas`,运行时把它塞进 `ContentPresenter`。
- 通过 `xmlns:fc="using:FluentCourse.Runtime.Controls"` 引入预制组件(`QuizControl`、`StepRevealControl` 等)。

## 6. 自定义 URI scheme

| Scheme                          | 作用                                     |
| ------------------------------- | ---------------------------------------- |
| `ms-appx:///`                   | 应用安装目录,WinUI 内置                |
| `ms-fcourse:///assets/img.png`  | 当前打开的 .fcourse 包内资源(本规范)  |
| `ms-fcdata:///pages/...`        | 内部页面引用(跳页动作用)              |

`ms-fcourse://` 在 `PageLoader` 中先替换为 `https://fcourse.local/`（绕开 XamlReader 的 URI 解析限制），解析后由 `AssetBinder` 遍历元素树，把 placeholder URI 替换为真实的 `BitmapImage.SetSourceAsync` 流。

## 7. 版本演进

- `formatVersion` 单调递增,**只允许向后兼容**地添加可选字段。
- 不兼容变更升 `formatVersion`,运行时遇到更高版本应给出明确提示而非崩溃。
- v1.0 → v1.1: 全部新增字段均带默认值/nullable,`PagePlayer`/`TriggerEngine`/`ActionExecutor` 遇到未知值仅记录警告,不抛异常。

## 8. 变更记录

| 日期 | 版本 | 变更 |
|------|------|------|
| 2026-06 | 1.1 | 新增 PageDescriptor: hidden/section/transitionDurationMs/background<br>新增 Trigger: 15 种事件类型, tapCount/modifiers/debounceMs/once/comparedTo/threshold<br>新增 Action: 媒体/StepReveal/Quiz/变量/窗口/动画控制/导航扩展<br>新增 Animation: 6 属性路径, 9 缓动函数, 4 预设动画, Slide 系列过渡<br>新增 Asset: fit/placeholderColor/preload<br>新增 Quiz: FillInBlank, QuizBlank, ScoreStrategy, Explanation |
| 2026-06 | 1.0 | 初始版本 |

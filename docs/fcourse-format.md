# .fcourse 文件格式规范 v1

## 1. 总览

`.fcourse` 是一个普通 ZIP 容器,扩展名重命名而已。设计目标:

- **可读** —— 解压即可看到 XAML/JSON,便于版本控制 (gitignore 二进制 asset 即可)。
- **可分发** —— 单文件,便于通过聊天工具/网盘传递。
- **运行时低开销** —— manifest 一次性反序列化,页面按需 `XamlReader.Load`。

## 2. 包结构

```
MyCourse.fcourse              (ZIP)
├── manifest.json             必选,UTF-8 无 BOM
├── pages/
│   ├── 01-cover.xaml         页面 UI 树,纯 XAML,不含 x:Class
│   ├── 02-quiz.xaml
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

> **注意**:页面 XAML 内的资源引用统一使用 `ms-appx-fcourse:///assets/...` 这类自定义 URI,运行时由 `FCourseUriResolver` 解析为内存流或临时文件。

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
      "notes": "演讲者备注,纯文本",
      "duration": null,            // 自动播放秒数,null 表示手动
      "transition": "Fade",        // Fade | SlidePush | Zoom | None
      "states": ["Default", "Highlight"]
    }
  ],

  "triggers": [
    {
      "id": "t-cover-start",
      "page": "cover",
      "on": { "type": "PageLoaded" },
      "do": [
        { "type": "PlayAnimation", "target": "TitleText", "animation": "FadeInUp" }
      ]
    }
  ],

  "animations": {
    "FadeInUp": {
      "duration": 600,
      "easing": "EaseOutCubic",
      "tracks": [
        { "property": "Opacity", "from": 0, "to": 1 },
        { "property": "TranslateY", "from": 24, "to": 0 }
      ]
    }
  },

  "assets": [
    { "key": "cover-bg", "path": "assets/images/cover.png", "type": "image", "sha256": "..." },
    { "key": "intro-audio", "path": "assets/audio/intro.m4a", "type": "audio" }
  ]
}
```

字段类型在 `FluentCourse.Core.Models` 一一对应,以 `[JsonPropertyName]` 显式标注,保证 AOT/源生成兼容。

## 4. 页面 XAML 约束

- **不允许** `x:Class`、`x:Name` 之外的代码后置标记 —— `XamlReader.Load` 会抛 `Cannot create unknown type`。
- **不允许** 直接写 `Click="Foo"` 这类事件 —— XamlReader 不解析事件绑定。事件统一在 manifest 的 `triggers` 节点声明,运行时再挂载。
- **允许** 任意 WinUI 3 控件、`ResourceDictionary`、`VisualStateManager`、`Storyboard`(作为静态资源)。
- 根元素必须是 `Grid` 或 `Canvas`,运行时把它塞进 `PageHost.ContentPresenter`。
- 通过 `xmlns:fc="using:FluentCourse.Runtime.Controls"` 引入预制组件。

## 5. 自定义 URI scheme

| Scheme                          | 作用                                     |
| ------------------------------- | ---------------------------------------- |
| `ms-appx:///`                   | 应用安装目录,WinUI 内置                |
| `ms-fcourse:///assets/img.png`  | 当前打开的 .fcourse 包内资源(本规范)  |
| `ms-fcdata:///pages/...`        | 内部页面引用(跳页动作用)              |

`ms-fcourse://` 由 `FCourseUriResolver` 在运行时通过 `BitmapImage.SetSourceAsync` / `MediaSource.CreateFromStream` 等流式 API 注入,避免把整个包解压到磁盘。

## 6. 版本演进

- `formatVersion` 单调递增,**只允许向后兼容**地添加可选字段。
- 不兼容变更升 `formatVersion`,运行时遇到更高版本应给出明确提示而非崩溃。

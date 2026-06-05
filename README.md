# FluentCourse

> 像 PPT 一样简单使用，像 HTML 一样灵活互动 —— 基于 WinUI 3 的原生互动教学课件系统。

## 项目状态

骨架已搭好，能跑通"打开 .fcourse → 渲染页面 → 触发器响应 → 切页"的端到端链路。下一步是补编辑器和导出器的剩余路径。

## 解决方案布局

```
FluentCourse/
├── FluentCourse.sln
├── docs/
│   ├── fcourse-format.md        .fcourse 文件格式规范
│   └── export-exe.md            打包独立 EXE 的方案
├── src/
│   ├── FluentCourse.Core        模型 + 包装 + JSON (无 UI 依赖)
│   ├── FluentCourse.Runtime     播放器、触发器、动画、控件
│   ├── FluentCourse.App         WinUI 3 桌面播放器
│   └── FluentCourse.Exporter    CLI 工具：bundle / info
└── samples/
    └── HelloWorld.fcourse/      示例课件 (解压形式，便于编辑)
```

## 核心理念

| 关注点       | 设计选择                                                                 |
| ------------ | ------------------------------------------------------------------------ |
| **页面格式** | 纯 XAML，无 `x:Class`、无 inline 事件 —— 全交给 `XamlReader.Load`         |
| **互动**     | 触发器+动作 JSON 配置，运行时遍历 `FindName` 挂载                        |
| **动画**     | 配置 → `StoryboardFactory` → 原生 Storyboard，60fps                       |
| **资源**     | `ms-fcourse:///` 自定义 URI，运行时从 ZIP 流式加载                        |
| **分发**     | 单文件 `.fcourse` (ZIP 改后缀)；可 bundle 成自抽取式 EXE                  |

## XamlReader 的几个坑（已规避）

1. **不能加载 `x:Class`** —— 模板里出现就抛 `XamlParseException`。所有页面 XAML 必须是纯 UI 树。
2. **不能加载事件处理器** —— `Click="Foo"` 这种属性会失败。我们用触发器系统把所有事件挪到 manifest。
3. **资源 URI 解析在 parse 时发生** —— 必须把 `ms-fcourse://` 在传入 XamlReader 前替换成 `https://fcourse.local/` 这类合法 URI；解析后 `AssetBinder` 再回填真实流。
4. **不能用自定义 `MarkupExtension`** —— AOT 路径下扩展找不到。所有动态值都通过 `DependencyProperty` 默认值或运行时 setter 注入。
5. **每次 `Load` 都返回新树** —— 别试图缓存元素本身，可以缓存 XAML 字符串。

## 快速上手

1. **打开 sln**：用 VS 2022 17.10+ 打开 `FluentCourse.sln`，确保装了 *Windows App SDK C# 模板* 和 *Single-project MSIX Packaging Tools*。
2. **运行**：F5 启动 `FluentCourse.App`，点工具栏 *打开*，选 `samples/HelloWorld.fcourse/manifest.json` 所在目录的父目录（先打 ZIP）或直接选打好的 `.fcourse` 文件。
   - 简易打包命令：`Compress-Archive -Path samples/HelloWorld.fcourse/* -DestinationPath samples/HelloWorld.fcourse.zip` 然后改后缀。
3. **键盘**：方向键 / PageUp/Down / Space 翻页；F5 启动；ESC 退出全屏。

## 7 大需求对应表

| #   | 需求                | 落地位置                                                                            |
| --- | ------------------- | ----------------------------------------------------------------------------------- |
| 1   | 文件格式规范         | `docs/fcourse-format.md` + `Core/Models/*` + `Core/Packaging/*`                     |
| 2   | 播放器核心           | `Runtime/Player/{PageLoader,AssetBinder,PagePlayer,PageTransitionFactory}.cs`       |
| 3   | 触发器-动作系统      | `Core/Models/Triggers.cs` + `Runtime/Triggers/{TriggerEngine,ActionExecutor}.cs`    |
| 4   | QuizControl          | `Runtime/Controls/QuizControl.cs` + `Runtime/Themes/Generic.xaml`                   |
| 5   | 动画系统             | `Core/Models/Animations.cs` + `Runtime/Animations/{StoryboardFactory,Presets}.cs`   |
| 6   | 演讲者视图           | `Runtime/Presenter/{DualScreenLayout,PresenterWindow}.cs`                          |
| 7   | 导出独立 EXE         | `docs/export-exe.md` + `Exporter/Program.cs`                                        |

## 已知 TODO（不影响主链路）

- [ ] `App.OnLaunched` 读 `--course=` 参数 + 读自身尾部 ZIP（bundle 模式入口）
- [ ] HTML5 / WASM 导出 —— 需要把 XAML 翻译成 HTML+CSS+JS，是整个项目里最重的 milestone
- [ ] 视频导出 —— `Microsoft.Graphics.Canvas` 逐帧捕获 + `Windows.Media.Transcoding`
- [ ] 屏幕注释、Spotlight —— 演讲者视图叠加层未实现
- [ ] 编辑器 (FluentCourse.Designer) —— 当前完全靠手写 manifest.json

## 性能注意事项

- **`XamlReader.Load` 是 main-thread-only**。大型页面 (>200 元素) 在切页时会有可感知卡顿。建议把页面切小，或者在切页前用 `DispatcherQueue.TryEnqueue` 让出一帧。
- **`RenderTargetBitmap` 不能渲染处于 `Visibility.Collapsed` 的元素**。演讲者视图的下一页缩略图依赖元素在 visual tree 里至少存在一帧。
- **Storyboard 用属性路径绑定，类型不匹配只会静默失败**。`StoryboardFactory.ResolvePropertyPath` 显式枚举，添加新属性时要同步更新。
- **触发器引擎在 `RebindToPage` 时 detach 上一页的所有 handler**，避免内存泄漏；如果未来加跨页持续触发器，要单独管理生命周期。

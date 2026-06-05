# 导出独立 EXE 播放器

## TL;DR

```bash
# 1. 用 self-contained 配置发布播放器
dotnet publish src/FluentCourse.App/FluentCourse.App.csproj `
    -c Release -r win-x64 `
    -p:WindowsAppSDKSelfContained=true `
    -p:WindowsPackageType=None

# 2. 把课件 + 播放器打成一个 .exe
dotnet run --project src/FluentCourse.Exporter -- bundle `
    samples/HelloWorld.fcourse `
    src/FluentCourse.App/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish `
    HelloWorld-Player.exe
```

得到 `HelloWorld-Player.exe`，用户双击直接看课件。

## 为什么不是 PublishAot

WindowsAppSDK 1.6 仍然依赖一些反射 / 动态 codegen 路径（比如 XAML 编译产物里的 `XamlMetadataProvider`），完整 Native AOT 会断链。`<IsAotCompatible>true</IsAotCompatible>` 已经写到 Core 项目里，作为以后切到 AOT 的准备。

当前选择 **ReadyToRun + SelfContained + WindowsAppSDKSelfContained** 是最实际的折中：

| 维度       | ReadyToRun            | Native AOT            |
| ---------- | --------------------- | --------------------- |
| 启动速度   | 快（已 JIT 预热）     | 最快                  |
| 包体积     | 110~150 MB（含 SDK） | 30~40 MB（理论值）   |
| 兼容 WinUI | ✅ 完整               | ⚠️ 当前 SDK 不支持   |
| 反射限制   | 无                    | 严，需要全链路源生成 |

XamlReader 本身严重依赖反射来解析未知类型，这是另一个把 AOT 拦在门外的硬约束。等 WindowsAppSDK 正式支持 AOT 之后，我们已经具备切换条件（`Microsoft.UI.Xaml.Markup.IXamlMetadataProvider` 已经走源生成）。

## Bundle EXE 的工作原理

`FluentCourse.Exporter bundle` 的产物是一个**自抽取式 EXE**：

```
+----------------------------+
| FluentCourse.App.exe 原文  |  ← 普通 PE 头 + 代码段，正常可执行
+----------------------------+
| ZIP archive                |  ← player/ 目录 + embedded.fcourse
+----------------------------+
| int64 zipSize              |
| uint64 magic 'FCOURSEX'    |
+----------------------------+
```

启动逻辑（在 `App.OnLaunched` 启动前需要插入一段引导代码）：

1. 打开自身可执行文件，读尾部 16 字节，校验 magic。
2. 取 zipSize，定位到 ZIP 起点，解压到 `%TEMP%/FluentCourse_<sha256前12位>/`。
3. 把 `embedded.fcourse` 路径作为命令行参数传给 `MainWindow`，自动 Open。
4. 退出时不删除临时目录——下次启动可以快速热启动（哈希一致就跳过解压）。

> **注意**：当前仓库里 `MainWindow.OnOpenClicked` 是手动选文件的。要支持 bundle 模式，在 `App.OnLaunched` 里加一段：
>
> ```csharp
> if (args.Arguments.Contains("--course="))
>     await _window.OpenAsync(args.Arguments.Split('=')[1]);
> else if (TryReadEmbeddedCourse(out var path))
>     await _window.OpenAsync(path);
> ```

`TryReadEmbeddedCourse` 是导出后才注入的引导代码，开发时不影响正常调试。

## 性能/体积小贴士

- **打包前压缩**：`samples/*.fcourse` 内部资源应该选 `CompressionLevel.SmallestSize`，但课件本身在 bundle 里用 `NoCompression`（已经压缩过的 ZIP 再压一次纯亏）。
- **并发解压**：`%TEMP%` 目录用哈希命名，多版本可共存，旧版本由用户手动清理。
- **首次启动慢**：解压 + JIT 预热大概 500ms~2s，之后命中临时目录会降到 ~200ms。

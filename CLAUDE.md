# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

FluentCourse is a native WinUI 3 (.NET 8, Windows-only) interactive courseware system — "as easy as PowerPoint, as flexible as HTML." Authors build `.fcourse` packages (a renamed ZIP) of pure-XAML pages plus a JSON manifest; the player renders pages with `XamlReader.Load` and wires up interactivity from the manifest at runtime. The designer is a separate PowerPoint-style editor. Most UI text, comments, and docs are in Chinese.

## Build / run / publish

All projects target `net8.0-windows10.0.19041.0` and define **only `x64` and `arm64` platforms** (no AnyCPU) — you must pass `-p:Platform=x64`, or builds fail to resolve a configuration.

```powershell
# Build everything
dotnet build FluentCourse.sln -c Debug -p:Platform=x64

# Run the player (opens a course via file picker, or pass --course=)
dotnet run --project src/FluentCourse.App -c Debug -p:Platform=x64
dotnet run --project src/FluentCourse.App -c Debug -p:Platform=x64 -- --course=samples/HelloWorld.fcourse

# Run the editor (the active development target)
dotnet run --project src/FluentCourse.Designer -c Debug -p:Platform=x64

# Exporter CLI: inspect a course / bundle into a self-extracting EXE
dotnet run --project src/FluentCourse.Exporter -- info samples/HelloWorld.fcourse
dotnet run --project src/FluentCourse.Exporter -- bundle <course> <player-publish-dir> <out.exe>

# Publish a self-contained player (see docs/export-exe.md)
dotnet publish src/FluentCourse.App/FluentCourse.App.csproj -c Release -r win-x64 `
    -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None
```

There is **no automated test suite** — verification is manual: run `FluentCourse.App` against a sample, or run `FluentCourse.Designer` and exercise the canvas. The designer writes crash logs to `%TEMP%\fluentcourse-crash.log` (see `App.UnhandledException` / `App.LogException`).

Packaging a sample for testing: the unpacked `samples/*.fcourse/` directory *is* the source of truth (the apps open a directory or a `manifest.json` directly). To make a single-file `.fcourse`, `Compress-Archive` the directory contents and rename to `.fcourse`.

## Project layout & dependency direction

```
FluentCourse.Core      models + JSON + ZIP packaging, NO UI deps      (foundation)
FluentCourse.Runtime   playback engine: XamlReader loading, triggers, ← Core
                       animations, asset binding, custom controls
FluentCourse.App       WinUI 3 desktop player                          ← Core + Runtime
FluentCourse.Designer  WinUI 3 editor (active dev)                     ← Core + Runtime + CommunityToolkit.Mvvm
FluentCourse.Exporter  CLI: `info` / `bundle`                          ← Core only
```

## The central constraint: `XamlReader.Load`

The entire format is shaped by the limits of `XamlReader.Load` (`Runtime/Player/PageLoader.cs` is the chokepoint). Page XAML must be a **pure UI tree**. When editing pages or the serializer, these rules are non-negotiable:

- **No `x:Class`** and **no event handlers** (`Click="Foo"`) — both throw `XamlParseException`. All events live in the manifest's `triggers` and are attached at runtime via `FindName`.
- **No custom `MarkupExtension`** — dynamic values come from `DependencyProperty` defaults or runtime setters, never markup extensions.
- **No ASCII double-quote `"` inside attribute values** (e.g. `Text="..."`) — use `&quot;` or Chinese corner quotes `「」`. No `&#x0a;`-style entities — use `<LineBreak/>`.
- **No color/resource references** that need merged dictionaries — `XamlReader` doesn't resolve them, so pages write literal `#AARRGGBB` hex. (`theme/palette.xaml` exists as the design-token source but is **not** auto-merged yet; see "three-place sync" in `docs/design-system.md`.)
- **Asset URIs**: pages reference `ms-fcourse:///assets/...`. `AssetUriRewriter` (in `PageLoader.cs`) rewrites these to `https://fcourse.local/` *before* parsing (XamlReader can't use a custom scheme); after parse, `Runtime/Player/AssetBinder.cs` walks the tree and swaps placeholders for real streams from the package. `MediaPlayerElement.Source` can't be set from a XAML string at all — the source path is stashed in `Tag` and bound by `AssetBinder`.
- Page root must be `Grid` or `Canvas`. Custom controls come in via `xmlns:fc="using:FluentCourse.Runtime.Controls"`.

## `.fcourse` format & Core

Authoritative spec: `docs/fcourse-format.md` (currently v1.1). A package is `manifest.json` + `pages/*.xaml` + optional `assets/`, `animations/`, `theme/`.

- `Core/Models/{CourseManifest,Triggers,Animations}.cs` are the manifest schema. All v1.1 additions are nullable/defaulted — **backward compatibility is enforced**: `formatVersion` only increments for breaking changes, unknown enum values are warnings not exceptions.
- `Core/Json/FluentCourseJsonContext.cs` is a **source-generated** `JsonSerializerContext` (camelCase, string enums, AOT-friendly). Always serialize/deserialize through it — adding a new manifest type means adding a `[JsonSerializable]` entry here.
- `Core/Packaging/CoursePackage.cs` transparently opens either a `.fcourse`/`.zip` **or** an unpacked directory **or** a `manifest.json` path — keep both code paths working when touching it.

## Runtime playback model

- `PageLoader` → fresh `FrameworkElement` tree per load (never cache the elements, only the XAML string).
- `Triggers/TriggerEngine.cs` + `ActionExecutor.cs`: manifest triggers (events) → actions, attached by walking the tree for `x:Name`. The engine detaches all handlers on `RebindToPage` to avoid leaks.
- `Animations/StoryboardFactory.cs` builds native `Storyboard`s from `AnimationDescriptor`s. Property animation uses explicit property-path resolution (`ResolvePropertyPath`) — **type mismatches fail silently**, so adding a new animatable property requires updating both the path table here and the format doc.
- Custom controls live in `Runtime/Controls/` (`QuizControl`, `StepRevealControl`, `CountdownControl`, `FormulaControl`) with styles in `Runtime/Themes/Generic.xaml`. `FormulaControl` renders LaTeX → image offline via `CSharpMath.SkiaSharp` (no WebView).
- **Step-based playback (PPT-style builds)**: entrance animations use `On.Type="Step"` + `On.At=<order>`. `PagePlayer` collects step triggers on page load, hides their targets (`Opacity=0`), and reveals them one at a time via `StepForwardAsync`/`StepBackward`. The player shows floating step controls only when `StepCount > 0`. Exit/emphasis animations remain click-triggered.

## Designer architecture (the active dev target)

The designer never edits XAML text as the source of truth — it parses XAML into an in-memory `ElementModel` tree and serializes back. **Round-trip fidelity between `XamlParser` and `XamlSerializer` is the core invariant** — any property one reads, the other must write, or elements collapse on re-save.

Data flow:
```
.xaml file → XamlParser (recursive) → ElementModel tree (ObservableObject, in-memory truth)
ElementModel → XamlSerializer → XAML string → XamlRoundTrip.TryLoad → StageView canvas render
```

- `Models/ElementModel.cs`: `X/Y/Width/Height/Rotation/Scale`, `ZIndex`, `Type` (identity-mapped to the real WinUI type — *not* collapsed to base types), optional `Name` (`x:Name`), `Props` dictionary (raw string attributes incl. `Grid.Row/Column`), recursive `Children`.
- `Services/XamlSerializer.cs`: runtime controls (`QuizControl`, `StepRevealControl`, `CountdownControl`, `FormulaControl`) get the `fc:` prefix and `xmlns:fc=...` (see `RuntimeControlTypes`). `QuizControl` choices/blanks serialize into `<fc:QuizControl.Choices/Blanks>` property-elements via `WriteQuizCollection`. Transforms emit `CompositeTransform` (scale+rotation+center).
- `Views/StageView.xaml.cs`: 3-layer canvas (content / grid-lines / adorner). It builds a **parallel map** between the `ElementModel` tree and the `XamlReader` visual tree for hit-testing & selection (does **not** depend on `x:Name`, and does **not** recurse into `QuizControl`/`StepRevealControl` whose "children" are choice/blank collections, not visual children). `FlattenRootToCanvas` converts a Grid/StackPanel root page to an absolute-positioned Canvas after `LayoutUpdated`. **All pages are free-layout (Canvas)** by design decision.
- `ViewModels/ShellViewModel.cs`: main VM — `SelectedElement`, toolbox insertion (`CreateElementFromToolbox` dispatches on `item.Label`; `BuildQuiz`/`MakeChoice`/`MakeBlank` build quiz subtrees), button click-action helpers (`GetButtonAction`/`SetButtonAction` generate trigger descriptors so teachers never touch the raw trigger editor), `ImportAsset`.
- `Services/ToolboxCatalog.cs`: insert-bar entries keyed by internal `Label` (e.g. `QuizSingle`, `Heading`, `Triangle`). Target audience is non-technical teachers — the strip is deliberately minimal and one-click.
- `Services/UndoRedoService.cs`: Move/Resize/PropertyChange/Insert/Delete/Rotate.

Re-render currently rebuilds the whole page tree on each property change (known perf limitation). After a change, render then reselect so the properties pane reopens on the same element.

## Conventions

- Nullable + ImplicitUsings are on solution-wide (`Directory.Build.props`); `RootNamespace` is `FluentCourse`. `TreatWarningsAsErrors` is **off**.
- Native AOT is *not* enabled (WinUI/XamlReader rely on reflection); `IsAotCompatible` on Core is forward-prep only. Don't introduce reflection-dependent patterns into Core beyond what the source-gen JSON context covers.
- When changing the manifest schema, update **all** of: the relevant `Core/Models/*.cs`, `FluentCourseJsonContext`, `docs/fcourse-format.md`, and the runtime consumer (`PagePlayer`/`TriggerEngine`/`ActionExecutor`).
- The `samples/ProteinOils.fcourse` course follows a strict visual contract in `docs/design-system.md` — colors/spacing/type are tokens duplicated literally into every page (no resource refs), and changing a token means updating the doc, `theme/palette.xaml`, and every page.
```

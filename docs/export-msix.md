# 打包 MSIX

FluentCourse 默认以**非打包(unpackaged)+ 自解压 EXE**(见 `export-exe.md`)分发。本文档说明另一条路:把播放器 `FluentCourse.App` 和编辑器 `FluentCourse.Designer` 打成**单项目 MSIX**(自包含,内置 Windows App SDK 运行时,约 80 MB/个)。

## 关键前提

- **必须用 Visual Studio 的 MSBuild.exe**,不能用 `dotnet build`。MSIX 需要 PRI 资源打包任务(`Microsoft.Build.Packaging.Pri.Tasks.dll`),该 DLL 只随 VS 分发,.NET SDK 目录里没有,否则报 `MSB4062`。
- 仓库的 `Directory.Build.props` 默认 `EnablePriGen=false / AppxPackage=false`(为让本机 `dotnet build` 跑非打包构建)。这两项以 `Condition="'$(BuildForMsix)' != 'true'"` 守卫——打 MSIX 时传 `-p:BuildForMsix=true` 即自动放开。

## 一键构建(推荐)

```powershell
# 两个应用都打(x64)
pwsh build\build-msix.ps1

# 只打播放器 / 指定平台
pwsh build\build-msix.ps1 -Target App -Platform x64
pwsh build\build-msix.ps1 -Target Designer
```

脚本会自动:定位 VS MSBuild → 确保自签名证书 `CN=FluentCourse Dev` 存在(没有就创建并导出 `build\FluentCourse_Dev.pfx/.cer`)→ 用 `-p:BuildForMsix=true` 构建并就地签名。

产物:`build\msix\<项目>_<版本>_<平台>_Test\*.msix`

## 在目标机器安装(侧载)

自签名包必须先信任证书才能装。**以管理员身份**:

```powershell
pwsh build\install-msix.ps1
```

它把 `build\FluentCourse_Dev.cer` 导入 `LocalMachine\TrustedPeople`,再 `Add-AppxPackage` 安装。装好后在开始菜单搜「FluentCourse」。

卸载:`Get-AppxPackage *FluentCourse* | Remove-AppxPackage`

> 没装证书前 `Get-AuthenticodeSignature` 会显示 `UnknownError`——这是「证书未受信任」的正常表现,不是包损坏;导入证书后变 `Valid`。

## 手动构建(脚本背后的命令)

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild src\FluentCourse.App\FluentCourse.App.csproj `
  -restore `
  -p:Configuration=Release -p:Platform=x64 `
  -p:BuildForMsix=true -p:WindowsPackageType=MSIX `
  -p:AppxPackageSigningEnabled=true `
  -p:PackageCertificateThumbprint=<证书指纹> `
  -p:AppxPackageDir="<仓库>\build\msix\" `
  -p:UapAppxPackageBuildMode=SideloadOnly
```

注意两个**必须用全局命令行属性传**的点(项目文件里设了会被 SDK targets 回退):
- `WindowsPackageType=MSIX`——否则被重置为 `None`,报 `WindowsPackageType is set to None, but GenerateAppxPackageOnBuild is set to true`。
- 签名建议用 `PackageCertificateThumbprint`(证书在 `CurrentUser\My` 里),而非 `PackageCertificateKeyFile`+密码——后者在此 MSBuild 路径下会报 `APPX0105 无法导入密钥文件`。

## 正式分发证书

测试证书只能侧载。若要广泛分发,用受信任 CA 颁发的代码签名证书替换:把 `Package.appxmanifest` 的 `Publisher` 改成证书 subject,构建时用该证书签名即可。

## 涉及的文件

- `src/FluentCourse.{App,Designer}/Package.appxmanifest` —— 包清单(Identity / Publisher / 图标 / 能力)
- `src/FluentCourse.{App,Designer}/Images/*.png` —— 磁贴/启动图/商店 logo(当前为占位图,可替换)
- `src/FluentCourse.{App,Designer}/Properties/launchSettings.json` —— 单项目 MSIX 必需的 `MsixPackage` profile
- `Directory.Build.props` —— `BuildForMsix` 守卫的 PRI/Appx 开关
- 各 `.csproj` —— `BuildForMsix` 条件下的 `WindowsPackageType=MSIX` 等

# MyCert.DevToys — 开发文档

> 本文件只存在于源码仓库，**不会**随 nupkg 发布到 nuget.org（打包只带 `README.md`）。

## 环境

- DevToys **2.0.9-preview**（`DevToys.Api 2.0.9-preview`）
- **.NET 10 SDK**（仓库根目录 `../global.json` 固定 `10.0.403`）
- 插件目标框架：**net8.0**（与宿主运行时一致，详见仓库根 README 坑 1）

> 早期版本用 `net10.0` 编译：只要不碰 `System.Runtime` 之外的真实框架程序集就能侥幸加载。
> 现在统一改成 `net8.0`，一是版本引用与宿主严格一致，二是编译器直接拦住 .NET 9/10 新增 API
> （本工程里 `X509CertificateLoader` 就属于 .NET 9+，保留在 `#if NET10_0_OR_GREATER` 分支里）。

## 一键打包

```powershell
cd MyCert\MyCert.DevToys
powershell -ExecutionPolicy Bypass -File .\pack.ps1
```

常用参数：

```powershell
# 指定版本
.\pack.ps1 -Version 1.0.1

# 打包并安装到本机 DevToys Preview（会先关闭 DevToys）
.\pack.ps1 -Install

# 不清理 bin/obj/artifacts
.\pack.ps1 -NoClean
```

脚本会：清理 → `dotnet pack` → 校验 `lib/<tfm>` 结构、BouncyCastle 依赖、
`devtoys-app` 标签、README、排除宿主程序集 → 输出 nupkg 路径。

产物：`artifacts/MyCert.DevToys.<version>.nupkg`（已包含 BouncyCastle，不包含 DevToys.Api / MEF）。

> DevToys 扩展是 **NuGet 包**，不是 Visual Studio 的 VSIX 插件。

## 本地安装

DevToys Preview 的扩展目录是 `%LOCALAPPDATA%\DevToys-preview\Plugins`。将 nupkg 解压到以包 ID 命名的子目录即可：

```powershell
$dest = "$env:LOCALAPPDATA\DevToys-preview\Plugins\MyCert.DevToys"
Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dest | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory("artifacts\MyCert.DevToys.1.0.0.nupkg", $dest)
```

重启 DevToys 后，在左侧「生成器」分组中打开「证书生成器」。

> 也可以把 nupkg 放到本地 NuGet 源，通过 DevToys 的「管理扩展」安装；包已带 `devtoys-app` 标签。

## 发布到 nuget.org（DevToys 官方扩展库）

DevToys 没有独立的“应用商店审核”，官方推荐把扩展发布到 **nuget.org**，
并带上 `devtoys-app` 标签；DevToys 的「管理扩展」会从 NuGet 源读取和安装。

1. 在 https://www.nuget.org/ 注册账号，创建 API Key（Scope 选 `Push`，
   可限定包 ID `MyCert.DevToys`）。
2. 打包并推送：

```powershell
cd MyCert\MyCert.DevToys
powershell -ExecutionPolicy Bypass -File .\pack.ps1 -Version 1.0.0
powershell -ExecutionPolicy Bypass -File .\publish-nuget.ps1
```

也可以手动推送：

```powershell
dotnet nuget push ..\artifacts\MyCert.DevToys.1.0.0.nupkg `
  --api-key $env:NUGET_API_KEY `
  --source https://api.nuget.org/v3/index.json `
  --skip-duplicate
```

3. 等待 nuget.org 完成校验/索引（通常几分钟），包页面：
   https://www.nuget.org/packages/MyCert.DevToys
4. 发布新版本时递增 `<Version>`（或 `pack.ps1 -Version`）后重新推送；
   不要重复使用已发布的版本号。

注意：

- `PackageId` 为 `MyCert.DevToys`，当前 nuget.org 上未被占用。
- NuGet 包要求许可证声明；当前工程**尚未配置** `PackageLicenseExpression`/`PackageLicenseFile`，
  打包脚本会给出警告。发布前请先确定许可证（如 MIT/Apache-2.0）并补到 csproj。
- 不要在 Release 构建启用 trimming/AOT，否则 MEF 反射发现不了工具。

## 说明

- 生成逻辑位于 `TestCertGenerator/CertGen`，本工程通过 `Compile Include` 复用，不复制实现。
- DevToys 扩展基于 MEF（`System.ComponentModel.Composition`），不要开启 trimming/AOT。

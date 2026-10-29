# DevToysPlugins

自用的四个 [DevToys 2.0（Preview）](https://devtoys.app/) 扩展，统一在一个解决方案里。

| 项目 | 功能 | 工具名（DevToys 左侧） |
| --- | --- | --- |
| [`src/GoogleAuthenticator.DevToys`](src/GoogleAuthenticator.DevToys/README.md) | Google Authenticator 风格的动态口令（TOTP / HOTP）管理器，扫码添加、加密备份 | 动态口令 |
| [`src/MyCert.DevToys`](src/MyCert.DevToys/README.md) | 生成测试用 X.509 证书（RSA / SM2 国密），导出 PFX/CER/PEM | 证书生成器 |
| [`src/ChineseIpsum.DevToys`](src/ChineseIpsum.DevToys/README.md) | 中文假文生成器，语料为四大名著公版节选 | 中文假文 |
| [`src/GmCrypto.DevToys`](src/GmCrypto.DevToys/README.md) | 国密算法四件套：SM3 摘要、SM4 加解密、SM2 签名验签、SM2 加解密 | SM3 摘要 / SM4 加解密 / SM2 签名验签 / SM2 加解密 |

前三个插件在「生成器（Generators）」分组，国密四个工具在「编码/解码（Encoders & Decoders）」分组。

## 目录结构

```
DevToysPlugins/
├─ global.json                       # SDK 10.0.403
├─ DevToysPlugins.slnx               # 唯一解决方案
├─ artifacts/                        # 插件的 nupkg 都输出到这里
├─ src/
│  ├─ GoogleAuthenticator.DevToys/   # 动态口令
│  ├─ MyCert.DevToys/                # 证书生成器（CertGen\ 为内联的证书生成实现）
│  ├─ ChineseIpsum.DevToys/          # 中文假文
│  └─ GmCrypto.DevToys/              # 国密四件套（Core\ 为 GM 算法层的内联副本 + Shared\ 共享层）
└─ tools/
   ├─ GoogleAuthenticator.Smoke/     # 动态口令回归（102 项，net8.0）
   ├─ ChineseIpsum.Smoke/            # 假文生成回归（25 项，net8.0）
   └─ GmCrypto.Smoke/                # 国密算法 + 共享层回归（29 项，net8.0）
```

每个插件目录都是自包含的：`csproj` + `pack.ps1` + `README.md` + 源码 + 资源。

## 常用命令

```powershell
cd e:\NewProjects\DevToysPlugins

# 一键：结束 DevToys -> 重新编译 -> 打包 -> 强制重装全部插件（同版本也重装）
powershell -ExecutionPolicy Bypass -File .\deploy-all.ps1

# 同上，并在装完后自动启动 DevToys（可选先跑冒烟测试）
powershell -ExecutionPolicy Bypass -File .\deploy-all.ps1 -Start
powershell -ExecutionPolicy Bypass -File .\deploy-all.ps1 -Smoke -Start

# 其他参数：-Configuration Debug、-SkipBuild（跳过解决方案编译）
powershell -ExecutionPolicy Bypass -File .\deploy-all.ps1 -Configuration Debug

# 编译全部
dotnet build DevToysPlugins.slnx

# 三个回归测试
powershell -ExecutionPolicy Bypass -File .\tools\GmCrypto.Smoke\run.ps1
powershell -ExecutionPolicy Bypass -File .\tools\GoogleAuthenticator.Smoke\run.ps1
powershell -ExecutionPolicy Bypass -File .\tools\ChineseIpsum.Smoke\run.ps1

# 只装某一个插件（产物统一落在 artifacts\）
powershell -ExecutionPolicy Bypass -File .\src\MyCert.DevToys\pack.ps1 -Install
```

安装目录：`%LOCALAPPDATA%\DevToys-preview\Plugins\<PackageId>`，装完重启 DevToys 生效。

`deploy-all.ps1` 的行为要点：

- 先检查 `%LOCALAPPDATA%\Programs\DevToys Preview\DevToys.exe` 对应进程，**在运行就强制结束**并等待句柄释放（否则插件 DLL 被占用，覆盖失败）。
- 每个插件都会**先删掉** `Plugins\<PackageId>` 再解包，**不做版本比较**，版本相同也照样重装。
- 依赖各插件自己的 `pack.ps1`，因此包结构校验（`lib/<tfm>/<id>.dll`、不得含宿主程序集、`devtoys-app` 标签）仍然生效。
- 脚本为纯 ASCII：PowerShell 5.1 会把无 BOM 的脚本按 ANSI 解析，中文会导致乱码甚至语法错误。

## 发布到 nuget.org

四个包 ID（首次推送时占用，之后只能推更高版本）：

| PackageId | 工具 |
| --- | --- |
| `GmCrypto.DevToys` | 国密四件套（SM2/SM3/SM4） |
| `MyCert.DevToys` | 证书生成器 |
| `GoogleAuthenticator.DevToys` | 动态口令 |
| `ChineseIpsum.DevToys` | 中文假文 |

### 方式一：GitHub Actions + 受信任发布（推荐，无长期密钥）

1. 建 GitHub 仓库并推送（见下方「推送到 GitHub」）。
2. nuget.org → Trusted Publishing → Create policy：
   - Package Owner：`icysky`
   - CI/CD Provider：`GitHub Actions`
   - Repository Owner / Repository：与真实 GitHub 仓库**完全一致**
   - Workflow File：`publish.yml`（即 `.github/workflows/publish.yml`）
   - Scopes：`Push new packages and package versions`（首次发布必须选这项）
3. GitHub 仓库 → Settings → Secrets and variables → Actions → 新建 secret `NUGET_USER`（填 nuget.org 用户名）。
4. 升版本号（各 csproj 的 `<Version>`；同 ID 同版本会被 nuget.org 拒绝），然后：

```powershell
git tag v1.0.2
git push origin v1.0.2
```

workflow 会依次编译 → 跑三个冒烟 → 打包 → 用 OIDC 换临时 key 推送（`--skip-duplicate`，重复版本自动跳过）。

### 方式二：本地经典 API Key

```powershell
# nuget.org -> API keys -> Create，Scope=Push，Glob=* 或具体包名
$env:NUGET_API_KEY = 'oy2...'
powershell -ExecutionPolicy Bypass -File .\src\MyCert.DevToys\publish-nuget.ps1
```

> 发布前建议在 csproj 里补 `<PackageLicenseExpression>MIT</PackageLicenseExpression>` 与 `<RepositoryUrl>`，
> 否则 nuget.org 页面会显示「无许可证」；`pack.ps1` 会就此给出警告。
> DevToys 扩展管理器靠 nuspec 的 `devtoys-app` 标签发现插件（已在各 csproj 配好）。

## 仓库与推送

- 源码仓库：<https://github.com/icysky/DevToysPlugins>
- 已配置的远程：`origin`（分支 `main`）

```powershell
git push            # 日常提交后推送
git push --tags     # 发布用（配合 .github/workflows/publish.yml）
```

包的元数据（license / 项目主页 / 仓库地址 / 版权）统一定义在仓库根的
`Directory.Build.props`，各插件 csproj 只需声明 `PackageId`、`Version`、`Description` 等差异项。

## 环境

- DevToys **2.0.9-preview**（`DevToys.Api 2.0.9-preview`，宿主是**自包含 .NET 8** 应用，`includedFrameworks` 8.0.x）
- **.NET 10 SDK**（`global.json` 锁定 `10.0.403`）
- 插件 `TargetFramework`：**四个插件统一 `net8.0`**（与宿主运行时严格一致，见坑 1）

## 五个必须知道的坑（都踩过）

1. **插件要按 `net8.0` 编译，否则工具根本不出现。**
   宿主是 .NET 8。按 `net10.0` 编译会引用 `System.Runtime.Numerics 10.0.0.0` 这类**真实框架程序集**
   （`BigInteger`、`Matrix` 等），加载时抛 `ReflectionTypeLoadException` →
   扩展列表里能看到包，但左侧导航**没有该工具**（日志：`Unable to load library ...`）。
   `System.Runtime` 是特例（统一绑定），所以老插件碰巧能跑：只要不碰其它 net9/10 才升版本的程序集。
   诊断：把插件 dll 丢进 .NET 8 进程 `Assembly.LoadFrom` + `GetTypes()`，看 `LoaderExceptions`。
   **门禁**：冒烟测试统一 target `net8.0`。

2. **签名 / 特性里不要出现 `System.Text.Json` 类型**（如返回 `JsonSerializerOptions` 的成员、`[JsonIgnore]` 特性）。
   DevToys 扫描插件元数据时会去解析它，宿主只有 `System.Text.Json 8.0.0.0`，
   插件引用 `10.0.0.0` → 整个插件加载失败（日志：`Unable to load plugin`）。

3. **不要替换已经渲染过的控件实例。**
   `UIStackPresenter` 用固定序号渲染子元素（无 `SetKey`），`UIElementPresenter` **只在 `OnInitialized`
   订阅一次** `PropertyChanged`。把新元素放到同一位置后，Blazor 复用旧组件、只更新参数，
   订阅仍挂在旧元素上 —— 新控件此后怎么改都不重绘。
   现象：界面冻结（倒计时停在某个数字），**无异常、CPU 为 0、窗口仍可操作**。
   做法：控件只创建一次；新增追加到末尾；删除只 `Hide` 不从子元素数组移除。

4. **图标码位必须真实存在于 `FluentSystemIcons` 字体里**，否则显示成占位圆圈。
   已验证可用的码位：`\uE78F` 挂锁、`\uF27E` 证书、`\uF8FD` 书本、`\uE8C9` 密码、
   `\uF4B6` 钥匙、`\uF6BE` 盾牌、`\uE611` 指纹、`\uE9EC` 二维码、`\uED88` 计时器。

## 许可证

本仓库代码采用 **0BSD（BSD Zero Clause）** —— OSI 认证里最宽松的许可：可任意使用、修改、分发，
甚至**不需要署名、不要求保留版权声明**。全文见 [LICENSE](LICENSE)。

随包分发的第三方组件仍遵循各自许可证（BouncyCastle = MIT、ZXing.Net = Apache-2.0 等），
详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)；相应许可证全文会随 nupkg 一起发布到 `licenses/` 目录。

> 如果你想保留「必须署名」这一条，把 `Directory.Build.props` 里的
> `PackageLicenseExpression` 改成 `MIT`、并把 `LICENSE` 换成 MIT 文本即可。

## 说明

- 这三个插件均为个人自用；扩展本质是 NuGet 包，随包带上 `devtoys-app` 标签。
- `src/MyCert.DevToys/CertGen` 是证书生成实现的本地副本，原 WPF 版 MyCert
  （`E:\NewProjects\OFD\MyCert`）保留自己那份；两处再修改时需手动同步。

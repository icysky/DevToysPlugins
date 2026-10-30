# 国密工具箱 — 开发文档

> 本文件只存在于源码仓库，**不会**随 nupkg 发布到 nuget.org（打包只带 `README.md`）。

## 目录结构

```
src/GmCrypto.DevToys/
├─ Core/
│  ├─ SM2/ SM3/ SM4/     # GM.Core 算法实现（内联副本）
│  └─ Shared/            # Hex 解析、文本编码、密文顺序、文件摘要（纯逻辑，可被冒烟测试链接）
├─ UiKit.cs              # 四个工具共用的小型 UI 构件
├─ Sm3HashTool.cs
├─ Sm4CipherTool.cs
├─ Sm2SignatureTool.cs
├─ Sm2CipherTool.cs
├─ GmCryptoResourceAssemblyIdentifier.cs
└─ Strings.resx          # 四个工具各自的标题 / 描述 / 搜索关键词
```

算法实现内联自桌面程序 `E:\NewProjects\GM`（`GM.Core`，纯托管、零第三方依赖），
命名空间保持 `GM.Core.*`，方便与 GM 仓库对比同步；自研共享层放在 `Core/Shared`（`GmCrypto.DevToys.Core`）。

## 三个实现要点（踩过的坑）

1. **必须按 net8.0 编译**（最关键的坑）。DevToys 2.0.9-preview 是自包含 .NET 8 应用
   （runtimeconfig 里 `includedFrameworks` 为 8.0.x）。按 net10.0 编译时，程序集会引用
   `System.Runtime.Numerics 10.0.0.0` 等**真实框架程序集**（SM2 的 `BigInteger` 就在这里），
   .NET 8 宿主加载抛 `ReflectionTypeLoadException` → 扩展列表里能看到包，但**工具不出现**。
   `System.Runtime` 是特例（会被统一绑定），所以只用到它的 net10 插件能侥幸跑起来。
   诊断方法：把插件 dll 放进 .NET 8 进程 `Assembly.LoadFrom` + `GetTypes()`，看 `LoaderExceptions`。
2. **SM4-CTR 修复**：GM 原实现按 `encrypt` 参数选择轮密钥，CTR 解密误用了逆向轮密钥，解出来是乱码。
   本仓库副本已修正为「CTR 密钥流一律使用加密方向轮密钥」，并回修到 GM 仓库，冒烟里用
   「CTR 输出 = 明文 XOR 计数器块的 ECB 加密」独立校验语义。
3. **动态显隐只改可见性**：操作切换时用 `GUI.Hide/Show` 控制参数行，绝不重建或替换控件实例
   （DevToys 的 Blazor 组件只在 `OnInitialized` 订阅一次，换实例会导致界面"冻结"）。

> GB18030 需要 `CodePagesEncodingProvider`：它在 .NET 8 共享框架里，net8.0 目标下可直接用，
> 不需要 NuGet 包（net10 目标下 .NET 10 SDK 会以 NU1510 把它裁掉）。

## 文件选择的过滤器坑

`IFileStorage.PickOpenFileAsync` 传 `"*"` 会被拼成非法过滤器 `*|*.|`，对话框里**选不中任何文件**；
表示"全部文件"必须传 `"*.*"`。

## 开发与验证

```powershell
cd e:\NewProjects\DevToysPlugins

# 编译
dotnet build .\src\GmCrypto.DevToys\GmCrypto.DevToys.csproj

# 算法 + 共享层冒烟（net8.0，同时是宿主 API 可用性门禁：29 项）
powershell -ExecutionPolicy Bypass -File .\tools\GmCrypto.Smoke\run.ps1

# 打包并安装到本机 DevToys
powershell -ExecutionPolicy Bypass -File .\src\GmCrypto.DevToys\pack.ps1 -Install
```

安装目录：`%LOCALAPPDATA%\DevToys-preview\Plugins\GmCrypto.DevToys`，装完重启 DevToys。

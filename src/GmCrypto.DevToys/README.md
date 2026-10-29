# 国密工具箱（GmCrypto.DevToys）

DevToys 2.0 扩展：SM2 / SM3 / SM4 国密算法，按用途拆成 **4 个工具**，都位于「编码/解码（Encoders & Decoders）」分组：

NuGet 包：<https://www.nuget.org/packages/GmCrypto.DevToys>　·　安装：DevToys → 设置 → 扩展 → 搜索「国密」或 `SM3`。

| 工具 | 图标 | 能力 |
| --- | --- | --- |
| SM3 摘要 | 指纹 | 文本 Hash + 文件 Hash（流式、带进度），输出 64 位小写 Hex |
| SM4 加解密 | 挂锁 | 加密/解密切换；ECB / CBC / CTR；PKCS7 / None / Zeros；Key/IV 一键随机 |
| SM2 签名验签 | 盾牌 | 签名/验签切换；UserId（Z 值）；DER 或 64 字节 raw(r‖s)；一键生成密钥对 |
| SM2 加解密 | 钥匙 | 加密/解密切换；C1C3C2（国标）与 C1C2C3（老旧系统）互转 |

算法实现内联自桌面程序 [GM](../../../GM)（`GM.Core`，纯托管、零第三方依赖），
命名空间保持 `GM.Core.*`，方便与 GM 仓库对比同步；自研的共享层放在 `Core/Shared`（`GmCrypto.DevToys.Core`）。

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

## 格式约定（与 GM 桌面程序一致）

- **Hex**：输入大小写不敏感，允许空格 / `-` / `:` / `,` 分隔与 `0x` 前缀；输出一律小写。
- **SM2 私钥** 32 字节，**公钥** 65 字节未压缩格式（`04` 开头）。
- **SM2 签名** 默认 DER；验签时 64 字节输入自动按 raw(r‖s) 转 DER 再验（Core 的 Verify 只吃 DER）。
- **SM2 密文** C1C3C2 = `C1(65) ‖ C3(32) ‖ C2(明文等长)`；选 C1C2C3 时由 `Sm2CipherFormat` 重排字节。
- **UserId（Z 值）**：标准默认 `1234567812345678`，留空即用默认。
- **编码**：UTF-8 / GB18030 / ASCII；勾选「输入按 Hex 解析」时改用 Hex（解密时结果也以 Hex 输出）。

## 三个实现要点（踩过的坑）

1. **必须按 net8.0 编译**（最关键的坑）。DevToys 2.0.9-preview 是自包含 .NET 8 应用
   （runtimeconfig 里 `includedFrameworks` 为 8.0.x）。按 net10.0 编译时，程序集会引用
   `System.Runtime.Numerics 10.0.0.0` 等**真实框架程序集**（SM2 的 `BigInteger` 就在这里），
   .NET 8 宿主加载抛 `ReflectionTypeLoadException` → 扩展列表里能看到包，但**工具不出现**。
   `System.Runtime` 是特例（会被统一绑定），所以只用到它的 net10 插件能侥幸跑起来 —— 这也是
   为什么 `MyCert.DevToys` 用 net10 能工作，而这个插件不行。诊断方法：把插件 dll 放进
   .NET 8 进程 `Assembly.LoadFrom` + `GetTypes()`，看 `LoaderExceptions`。
2. **SM4-CTR 修复**：GM 原实现按 `encrypt` 参数选择轮密钥，CTR 解密误用了逆向轮密钥，解出来是乱码。
   本仓库副本已修正为「CTR 密钥流一律使用加密方向轮密钥」，并回修到 GM 仓库，冒烟里用
   「CTR 输出 = 明文 XOR 计数器块的 ECB 加密」独立校验语义。
3. **动态显隐只改可见性**：操作切换时用 `GUI.Hide/Show` 控制参数行，绝不重建或替换控件实例
   （DevToys 的 Blazor 组件只在 `OnInitialized` 订阅一次，换实例会导致界面"冻结"）。

> GB18030 需要 `CodePagesEncodingProvider`：它在 .NET 8 共享框架里，net8.0 目标下可直接用，
> 不需要 NuGet 包（net10 目标下 .NET 10 SDK 会以 NU1510 把它裁掉）。

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

## 说明

- 没有第三方运行时依赖，`lib/<tfm>` 里只有 `GmCrypto.DevToys.dll`。
- **参数全量自动持久化**：四个工具会把操作模式、编码、Hex/raw 开关、SM4 模式/填充/Key/IV、
  SM2 私钥/公钥/UserId/签名、以及输入文本与上次输出一起写进 DevToys 设置
  （`%LOCALAPPDATA%\DevToys-preview\settings.ini`，键名形如
  `GmCrypto.DevToys.GmCrypto.Sm3.State`），重启后原样恢复。私钥只落在本机设置里，介意请清空私钥框。
- **SM2 密钥对**：两个 SM2 工具顶部都有常显的「密钥对」卡片，加密/解密、签名/验签任何模式下都能一键生成。
- **文件 Hash**：只做 SM3（不含 SM4 文件加密）。文件选择必须用 `"*.*"` 表示全部文件——
  DevToys 的 `IFileStorage.PickOpenFileAsync` 对 `"*"` 会拼出非法过滤器 `*|*.|`，导致对话框里选不中任何文件。
  大文件按 8KB 块流式计算，流可 seek 时按总长度显示百分比，否则进度条保持 0。

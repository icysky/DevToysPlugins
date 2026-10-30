# GoogleAuthenticator.DevToys — 开发文档  > 本文件只存在于源码仓库，**不会**随 nupkg 发布到 nuget.org（打包只带 `README.md`）。 
## 图标

工具出现在 DevToys 左侧「生成器（Generators）」分组，名称为「动态口令」，图标是 **挂锁**
（`IconFontName = "FluentSystemIcons"`、`IconGlyph = '\uE78F'`）。

### 怎么换图标

图标由 `ToolDisplayInformation` 上的两个参数决定：

```csharp
[ToolDisplayInformation(
    IconFontName = "FluentSystemIcons",   // 字体名
    IconGlyph = '\uE78F',                 // 码位（char）
    ...)]
```

**方式 A：沿用 DevToys 自带的 FluentSystemIcons（最简单）**
只改 `IconGlyph`。唯一要求是码位在这个字体里真实存在——写一个不存在的码位不会报错，
侧边栏只会显示成占位圆圈（本项目最初抄了 `\uE8A5`，它恰好是 `ic_fluent_circle`，两个插件都成了空心圆）。

下面这些码位已用字体 `cmap` 校验存在（字体文件：
`DevToys.Blazor.Assets.fonts.FluentSystemIcons-Regular.ttf`）：

| 码位 | 字形名 | 适用 |
| --- | --- | --- |
| `\uE78F` | `ic_fluent_lock_closed_20_regular` | 挂锁（本插件默认） |
| `\uE8C9` | `ic_fluent_password_20_regular` | 密码（DevToys「密码生成器」在用） |
| `\uF4B6` | `ic_fluent_key_20_regular` | 钥匙 |
| `\uF6BE` | `ic_fluent_shield_20_regular` | 盾牌 |
| `\uE611` | `ic_fluent_fingerprint_20_regular` | 指纹 |
| `\uE9EC` | `ic_fluent_qr_code_20_regular` | 二维码（DevToys「二维码」工具在用） |
| `\uED88` | `ic_fluent_timer_20_regular` | 计时器 |
| `\uF27E` | `ic_fluent_certificate_20_regular` | 证书（适合 MyCert 那类工具） |

**方式 B：随插件自带图标字体**
用 `IResourceAssemblyIdentifier.GetFontDefinitionsAsync()` 返回自己的字体，再在
`IconFontName` 里写该字体名：

```csharp
public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
{
    Stream stream = Assembly.GetExecutingAssembly()
        .GetManifestResourceStream("MyPlugin.Assets.my-icons.ttf")!;
    return ValueTask.FromResult(new[] { new FontDefinition("MyIcons", stream) });
}
// 然后 IconFontName = "MyIcons";
```

注意字体只支持 **TTF / OTF**，WOFF / WOFF2 不支持。

改完图标要重新 `pack.ps1 -Install` 并**重启 DevToys**（图标在启动时加载）。

## 构建与安装

```powershell
cd src\GoogleAuthenticator.DevToys
powershell -ExecutionPolicy Bypass -File .\pack.ps1 -Install
```

脚本会：清理 → `dotnet pack` → 校验 `lib/<tfm>` 结构（必须含本插件 DLL 与 `zxing.dll`，不得含 `DevToys.Api.dll` 等宿主程序集）→ 输出 nupkg。
带 `-Install` 时会先关闭 DevToys，再把包解压到：

```
%LOCALAPPDATA%\DevToys-preview\Plugins\GoogleAuthenticator.DevToys
```

重启 DevToys 即可使用。

常用参数：

```powershell
.\pack.ps1                       # 只打包
.\pack.ps1 -Version 1.0.1        # 指定版本
.\pack.ps1 -Install -NoClean     # 不清理中间产物，直接打包并安装
```

## 回归测试

仓库里带了不依赖 DevToys 进程的冒烟测试（直接链接插件源码编译）：

```powershell
powershell -ExecutionPolicy Bypass -File ..\..\tools\DevToysPluginSmoke\run.ps1
```

覆盖 99 项，全部通过时退出码为 0：

- **算法**：RFC 4226 附录 D 的 10 组 HOTP 向量、RFC 6238 附录 B 的 18 组 TOTP 向量（SHA1/SHA256/SHA512）、6~12 位长度与分组格式、Base32 往返与容错
- **解析**：`otpauth://` 字段与往返、HOTP counter、异常输入、Google `otpauth-migration://` 转移载荷
- **二维码**：ZXing 生成 → ImageSharp 存 PNG → `QrScanner` 解出，内容必须完全一致；非二维码图片返回 null
- **备份**：明文/加密写入读回、无密码提示、错误密码、密文篡改被 HMAC 发现、非本插件文件被拒
- **持久化**：环境变量隔离目录、落盘非明文、重新加载无损
- **界面**：控件树构建、按 id 取控件、添加/去重、验证码渲染位数正确、`UpdateCodes` 无异常
- **对话框**：手动录入 / 编辑 / 删除确认 / 扫码 / 导出 / 密码输入 六个对话框的构建与关闭

测试通过环境变量 `GOOGLEAUTHENTICATOR_DEVTOYS_DATA_DIR` 把数据目录指向临时文件夹，**不会碰你真实的账号文件**。
这个变量在正式使用时也可以用来做便携部署。

## 实时刷新的三个前提（改刷新逻辑前必读）

1. **定时循环必须跑在线程池，不能跑在 UI 线程上。**
   DevToys 的每个控件都由 `UIElementPresenter` 订阅 `PropertyChanged` 并 `InvokeAsync(StateHasChanged)`，
   所以**从后台线程改控件是官方支持的**（会被正确 marshall 回渲染器）。
   反过来，如果在 UI 线程（`DispatcherSynchronizationContext`）上 `await Task.Delay`，
   续体要等 Blazor 渲染器泵到才执行；没有浏览器事件驱动时会成批迟到，
   表现为「倒计时卡顿、几秒后才跳一下、要点刷新才动」。
   本插件因此用 `Task.Run(... )` + `ConfigureAwait(false)` 驱动循环。

2. **行集合必须是「整体替换的快照」，不能原地增删。**
   刷新循环迭代的行快照是 `AccountRow[]`，`RebuildRows()` 只会整体换引用；
   若用 `List<T>.Clear()/Add()`，就会和循环撞出「集合已修改」异常。
   另外循环**永不自杀**：一次异常只记诊断并继续重试，早期版本「连续失败 3 次就停」会让界面永久冻住。

排查现场问题时看 `%LOCALAPPDATA%\GoogleAuthenticator.DevToys\diag.log`：
启动时会记一行刷新循环所用线程与 `SynchronizationContext`；
运行中出现 `refresh timer looks dead`（自愈重建）、`tick delayed: Nms`（调度被拖住）、
`UpdateCodes took Nms`（写控件过慢）、`row snapshot mismatch`（行快照与账号数不一致）都会被记下来；
每 30 秒还会写一行 `heartbeat: ticks=... rows=... accounts=...`，用于区分「计时器死了」和「计时器在跑但界面没重绘」。

3. **绝不能替换已经渲染过的控件实例（最坑的一条）。**
   `UIStackPresenter` 用**固定序号**渲染子元素（没有 `SetKey`），而 `UIElementPresenter`
   **只在 `OnInitialized` 里订阅一次** `PropertyChanged`。
   所以只要把「新的元素实例」放到同一个位置，Blazor 就会复用旧组件实例、仅更新参数，
   订阅仍然挂在**旧元素**上 —— 新控件此后怎么改都不会重绘：

   > 现象：界面彻底冻住（倒计时停在某个数字不动），**没有异常、CPU 为 0、窗口依然可正常响应**，
   > 点别的按钮会顺带刷新一次，然后又冻住。

   正确做法（本插件的实现方式）：
   - 账号行控件**只创建一次**，编辑时只改文本/显隐，不新建控件；
   - 新账号**追加到末尾**；
   - 删除的账号**只 `Hide`、不从子元素数组里移除**（移除会让后面的元素位置前移，同样害死订阅）；
   - 由此子元素数组永远 append-only，索引和实例都不变。

## 开发注意（改代码前务必看）

DevToys 加载插件时会反射扫描插件里**所有类型的成员签名和特性**。一旦签名或特性里出现
`System.Text.Json` 的类型（例如返回 `JsonSerializerOptions` 的属性、或 `[JsonIgnore]` 特性），
宿主就会尝试解析 `System.Text.Json, Version=10.0.0.0`；而 DevToys 2.0.9 自带的是 8.0.0.0，
于是插件直接加载失败，日志里只能看到：

```
Unable to load plugin in '...\Plugins\GoogleAuthenticator.DevToys'...
System.IO.FileNotFoundException: Could not load file or assembly 'System.Text.Json, Version=10.0.0.0'
```

所以本插件约定：

- JSON 相关类型只能在**方法体内**使用，对外只暴露泛型帮助方法（见 `Core/JsonConfig.cs`）
- 展示用的派生字段一律做成 `GetXxx()` 方法，不用属性 + `[JsonIgnore]`（见 `Core/OtpAccount.cs`）
- 改完后跑一次冒烟测试还不够（它是独立进程，不会触发这个问题），**必须真机重启 DevToys 看日志**有没有 `Instance of 'Authenticator' tool created`

## 环境与兼容性

- DevToys **2.0.9-preview**（`DevToys.Api 2.0.9-preview`）
- **.NET 10 SDK**（`global.json` 固定 `10.0.403`），插件目标框架 `net8.0`（与宿主运行时一致）
- 插件依赖：`ZXing.Net 0.16.11`（二维码识别）。`SixLabors.ImageSharp 3.1.12` 与宿主同版本，**不随包分发**。

> 宿主是自包含 .NET 8。插件统一按 `net8.0` 编译：`net10.0` 会引用 `10.0.0.0` 的真实框架程序集，
> 在 .NET 8 上加载失败（扩展列表可见但工具不出现），详见仓库根 README 坑 1。


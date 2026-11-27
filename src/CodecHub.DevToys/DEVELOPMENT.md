# 开发说明（CodecHub.DevToys）

## 结构

```
src/CodecHub.DevToys/
├─ CodecHubGuiTool.cs                  # 唯一的 IGuiTool：模式切换 + 输入 + 多视图输出
├─ CodecHubResourceAssemblyIdentifier.cs
├─ Strings.resx                        # 左侧导航显示名 / 描述 / 搜索关键词
├─ Core/
│  ├─ CodecModels.cs                   # 模式、方向、选项、结果
│  ├─ CodecPrimitives.cs               # 字符集、Hex、码点、进制解析
│  ├─ TextCodecs.cs                    # URL / HTML / Unicode / JSON
│  ├─ BinaryCodecs.cs                  # Base64 / Base32 / Base58 / Hex
│  ├─ NumberCodecs.cs                  # 进制 / 补码 / IP / 时间戳 / GUID
│  └─ CodecEngine.cs                   # 模式分发 + 辅助视图
└─ pack.ps1
```

`Core` 不引用 `DevToys.Api`，`tools/CodecHub.Smoke` 直接链接同一份源码做回归（105 项，net8.0）。

## 常用命令

```bash
# 编译插件
dotnet build src/CodecHub.DevToys/CodecHub.DevToys.csproj -c Release

# 回归测试
dotnet run --project tools/CodecHub.Smoke/CodecHub.Smoke.csproj -c Release

# 打包 / 安装（仓库现有的 PowerShell 脚本）
powershell -ExecutionPolicy Bypass -File src/CodecHub.DevToys/pack.ps1 -Install
```

## 关键实现约定

1. **必须 `net8.0`**：宿主是 .NET 8，Self-contained。用 net10 编译会导致 `System.Runtime.Numerics` 等程序集版本不匹配，扩展列表里能看到但工具不出现（见仓库根 README 坑 1）。
2. **不要在特性 / 公开签名里出现 `System.Text.Json` 类型**：`PersistedState` 只是私有嵌套类，`JsonSerializer` 只在方法内部使用。
3. **控件只创建一次**：所有选项行在构造时建好，模式切换只用 `GUI.Show` / `GUI.Hide` 改可见性，绝不从子元素数组移除再重建（见仓库根 README 坑 3）。
4. **BOM 要显式写**：严格解码用的 `Encoding` 实例 `GetPreamble()` 返回空数组，BOM 字节由 `CodecPrimitives.Bom()` 显式提供。
5. **任意字节不一定是合法文本**：二进制模式解码后先按选定字符集解文本，失败则回退 Latin-1 并在视图标题标注，避免整个转换报错。
6. **图标码位**：`\uE943`（code）已在本仓库 JsFormat 中使用，替换前必须用 FluentSystemIcons 字体 cmap 验证。

## 范围

只做「编码 + 进制」：不做 JSON/YAML/CSV 等格式互转、不做命名法/文本整理、不做哈希/GZip/JWT/国密（这些由 DevToys 内置或本仓库其它插件覆盖）。

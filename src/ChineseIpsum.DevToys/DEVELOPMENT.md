# ChineseIpsum.DevToys — 开发文档  > 本文件只存在于源码仓库，**不会**随 nupkg 发布到 nuget.org（打包只带 `README.md`）。 
## 构建与安装

```powershell
cd src\ChineseIpsum.DevToys
powershell -ExecutionPolicy Bypass -File .\pack.ps1 -Install
```

安装到 `%LOCALAPPDATA%\DevToys-preview\Plugins\ChineseIpsum.DevToys`，重启 DevToys 后在
「生成器（Generators）」分组里打开「中文假文」。

## 环境

- DevToys **2.0.9-preview**（`DevToys.Api 2.0.9-preview`）
- **.NET 10 SDK**（`global.json` 固定 `10.0.403`），插件目标框架 `net8.0`（与宿主运行时一致）

## 开发注意

1. **图标码位必须真实存在于 `FluentSystemIcons` 字体里**。本插件用 `U+F8FD`
   （`ic_fluent_book_20_regular`）。写一个不存在的码位不会报错，只会显示成占位圆圈。
2. **不要替换已经渲染过的控件实例**。DevToys 的 `UIElementPresenter` 只在 `OnInitialized`
   订阅一次 `PropertyChanged`，而 `UIStackPresenter` 用固定序号渲染子元素（无 `SetKey`）。
   一旦把新元素实例放到同一位置，Blazor 复用旧组件、只更新参数，订阅仍挂在旧元素上，
   此后控件怎么改都不会重绘（界面冻住、无异常、CPU 为 0）。
   本插件所有控件都在构造时创建一次，之后只改属性，不重建。
3. **签名/特性里不要出现 `System.Text.Json` 类型**（如返回 `JsonSerializerOptions` 的成员、
   `[JsonIgnore]` 特性）。DevToys 加载插件时会解析这些类型，而宿主只有
   `System.Text.Json 8.0.0.0`，插件引用的是 10.0.0.0，会导致整个插件加载失败。
4. **只能用 .NET 8 及更早的 BCL API。** 插件现已 target `net8.0`（编译器即可拦截），早期 target `net10.0` 时 DevToys 2.0.9 自带的是
   **.NET 8 运行时**，编译器会放行 .NET 9/10 新增的 API，运行时才炸。踩过的实例：

   ```
   System.Threading.Lock            // .NET 9+，lock (lockObj) 会去加载 Lock.Scope
   → 生成失败：Could not load type 'Scope' from assembly 'System.Runtime, Version=8.0.0.0'
   ```

   同类还有 `Task.WhenEach`、`TimeProvider` 的部分成员、集合表达式以外的
   `System.Collections.Frozen` 新方法等。**约束手段**：`tools/IpsumSmoke` 刻意
   target `net8.0`，生成逻辑一旦用了高版本 API 会直接编译失败——改 Core 后务必跑它。

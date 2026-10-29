# ChineseIpsum.DevToys

DevToys 扩展：**中文假文生成器**。交互对齐 DevToys 自带的「乱数假文（Lorem Ipsum）」，
但语料换成中国四大名著的公版原文节选，输出全中文。

NuGet 包：<https://www.nuget.org/packages/ChineseIpsum.DevToys>　·　安装：DevToys → 设置 → 扩展 → 搜索「假文」或 `lorem`。

## 功能

- **语料**：红楼梦 / 西游记 / 水浒传 / 三国演义 / 四大名著混合
- **类型**：段落 / 句子 / 字数
- **数量**：1 ~ 5000（段落=段落数，句子=句子数，字数=目标汉字数）
- **以原著开篇起句**：首句固定为该书的著名开头，相当于 `Lorem ipsum dolor sit amet`
- 改任意选项立刻重新生成，输出框右上角「换一批」可重新抽
- 生成结果只读，可直接用输出框自带的复制按钮取走
- 所有选项记忆在 DevToys 设置里，下次打开保持

## 语料说明

`Assets/Corpus/*.txt` 每行一句，共约 120 句，取自：

| 文件 | 出处 | 首句（开篇） |
| --- | --- | --- |
| `sanguoyanyi.txt` | 三国演义 | 话说天下大势，分久必合，合久必分。 |
| `xiyouji.txt` | 西游记 | 诗曰：混沌未分天地乱，茫茫渺渺无人见。 |
| `shuihuzhuan.txt` | 水浒传 | 试看书林隐处，几多俊逸儒流。 |
| `hongloumeng.txt` | 红楼梦 | 此开卷第一回也。 |

这些都是明代 / 清代作品，早已进入公有领域。文本是**节选整理**，用于占位文本，
可能有个别字句与通行本不同，不追求校勘级准确。想换/加语料直接改 txt（一行一句）后重新打包即可。

## 生成规则

- **段落**：N 段，每段随机 3~6 句，段间空行
- **句子**：N 句连续成文
- **字数**：按句子累加到不少于 N 个汉字（会略微超出，因为不截断句子）
- 中文句子之间不加空格直接拼接；相邻两句保证不重复

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

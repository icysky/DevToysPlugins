# Cron.DevToys — 开发文档

> 本文件只存在于源码仓库，**不会**随 nupkg 发布（打包只带 `README.md`）。

## 为什么自己写解析器

DevToys 2.0 自带一个「Cron 表达式解析器」（内部名 `CronParser`），但它只能看：没有生成器，
也不给「下次执行时间」。本插件补的是**生成**这一半，同时把预览做得更完整。

- 不引第三方 cron 库：Quartz / NCrontab 的方言各不相同，行为和文案都不可控；
  需要的语法（`L` / `W` / `#` / `?` / 名称 / 宏）自己实现不到 500 行。
- 纯逻辑放在 `Core/`，不依赖 `DevToys.Api`，因此冒烟工程可以直接链接源码跑（net8.0 门禁）。

## 结构

| 文件 | 职责 |
| --- | --- |
| `Core/CronField.cs` | 单字段解析（`*`、列表、范围、步长、`JAN~DEC`、`SUN~SAT`、`L`/`LW`/`nW`/`nL`/`n#k`、`?`） |
| `Core/CronSchedule.cs` | 整条表达式的解析、宏展开、日期匹配、下次执行时间推算、时间格式化 |
| `Core/CronDescriber.cs` | 中文描述（尽量说人话，识别 `*/n`、区间、工作日、最后一天等常见形态） |
| `Core/CronBuilder.cs` | 生成器状态（每个字段的「每个值 / 间隔 / 指定值」）→ 表达式 |
| `Core/CronTemplates.cs` | 9 个快捷模板；放在 Core 里是为了能进冒烟测试 |
| `CronGuiTool.cs` | DevToys UI：左侧字段生成器 + 右侧解析预览，设置持久化 |

## 语义约定（重要）

1. **5 段 = 分 时 日 月 周**，秒固定为 0；**6 段 = 秒 分 时 日 月 周**。
   不支持 7 段（Quartz 的年份字段），遇到直接报错。
2. 「日」与「周」同时受限时按**标准 cron 的「或」**语义（满足任一即触发）；
   `?` 表示该项不参与判断，`?` 与 `?` 不能同时出现。
3. 周字段 `0` 与 `7` 都是周日；描述统一按 `0` 输出。
4. 6 段表达式里「秒 = 0」在**描述**中等价于 5 段的整分触发，否则每条都是
   「每分钟第 0 秒」；只有`时:分`也是单值时才会输出 `HH:mm:00`。
5. 下次执行时间最多向后看 **8 年**（`CronSchedule.SearchYears`）。`0 0 30 2 *`
   这种永远不存在的日期会快速返回空结果并置 `searchLimitReached`，不会死循环。

## 下次执行时间的算法

不做「逐秒/逐分钟推进」，而是：

1. 按天遍历（最多 8 年 ≈ 2922 次），先用「月 + 日/周」筛掉不匹配的天；
2. 命中后再用三个字段的**有序取值集合**三层循环（时 → 分 → 秒）找当天剩余时间；
3. 直接跳过「比起点更早」的候选。

最坏情况（不可能命中的表达式）只跑 2922 次日期判断，实测 0 ms 级；
正常表达式通常第一次循环就出结果。

## UI 约定与踩坑

- **不要用 `GUI.SettingGroup` 包配置项**：DevToys 2.0.9 的 `UISettingGroupPresenter` 会把它渲染成
  `Expander`，而 `IUISettingGroup` 没有 `IsExpanded` 之类的 API，默认是**折叠**的——用户每次打开
  工具都得手动展开一次。改用 `GUI.Text(GUI.Label(id), "标题")` + `GUI.Stack().Vertical()` 平铺
  `IUISetting`（内置的 Cron 解析器也是这么写的），配置就始终可见。
- **控件只创建一次**：字段模式切换时只 `GUI.Hide` / `GUI.Show`，绝不替换实例，
  否则 DevToys 的 `UIElementPresenter` 订阅会丢，界面看起来「冻结」（见仓库根 README 坑 3）。
- **程序化写控件要挡住回调**：`SyncControlsFromState` / 套模板 / 生成表达式时用
  `_suppress` 包住 `GUI.Text` / `GUI.Value` / `GUI.Select`，否则 `OnTextChanged`
  会把中间值写进状态，又反过来再生成一次表达式（实测会出现字段被改成 `*`）。
- **生成器拥有表达式**：改动左侧任何字段都会重写右侧「Cron 表达式」输入框；
  手动改表达式只影响预览，不会再回写生成器字段（左侧「生成的表达式」保持上次生成结果）。
- **下拉框按项实例选中**，不用 `Select(index)`（索引在首次渲染时可能落到最后一项，
  见 ChineseIpsum 的实现注释）。
- 开关状态从控件 `IsOn` 读取，不采信 `OnToggle` 的事件参数（首次渲染会抛一次反向值）。
- 设置整条存成一条 JSON（`Cron.State`），里面直接序列化 `CronBuilderState`，
  字段枚举按数字存；改枚举顺序会导致旧设置错位，改之前先想清楚。

## 构建 / 测试 / 打包

```powershell
# 编译
dotnet build .\src\Cron.DevToys\Cron.DevToys.csproj -c Debug

# 核心逻辑回归（88 项：描述 / 下次时间 / 非法输入 / 格式 / 生成器 / 模板）
powershell -ExecutionPolicy Bypass -File .\tools\Cron.Smoke\run.ps1

# 打包并安装到本地插件目录（会先结束 DevToys）
powershell -ExecutionPolicy Bypass -File .\src\Cron.DevToys\pack.ps1 -Install
```

## 验证记录（2026-11-12）

- 冒烟 88/88 通过（描述 31 项、下次时间 20 项、非法输入 14 项、格式 4 项、
  生成器 10 项、模板 10 项）。
- 真机 DevToys 2.0.9-preview（UIA 自动化操作）：
  - 模板「工作日 9:00」→ `0 0 9 * * 1-5`，描述「工作日 09:00:00」，5 条时间都是周一~周五；
  - 关闭「包含秒数」→ `0 9 * * 1-5`，描述「工作日 09:00」，秒字段整行隐藏；
  - 手动输入 `0 12 L * *` → 描述「每月最后一天 12:00」；
  - 手动输入 `60 * * * *` → 描述「表达式无效。」并弹出错误提示。

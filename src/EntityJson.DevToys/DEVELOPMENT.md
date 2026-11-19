# EntityJson.DevToys — 开发文档

> 本文件只在源码仓库，不随 nupkg 发布。

## 分层

| 文件 | 职责 |
| --- | --- |
| `Core/CSharpEntityParser.cs` | 词法 + 语法：从 C# 源码里抽 `class/record/struct/enum` 的成员 |
| `Core/TypeScriptEntityParser.cs` | 词法 + 语法：从 TS 的 `interface/type/class/enum` 抽成员 |
| `Core/JavaEntityParser.cs` | 词法 + 语法：从 Java 的 `class/record/interface/enum` 抽字段与 getter |
| `Core/EntityParser.cs` | 按 `SourceLanguage` 分发到具体解析器 |
| `Core/ModelResolver.cs` | 语言无关的收尾：继承字段合并、对象引用改标成枚举 |
| `Core/JsonEntityGenerator.cs` | 反向：JSON 结构推断 → C# / TS / Java 代码 |
| `Core/EntityModel.cs` | 中间模型：`EntityType` / `EntityField` / `EntityParseResult` |
| `Core/TypeShape.cs` | 字段类型描述：基础类型、枚举、对象、数组、字典、未知 |
| `Core/NamingPolicy.cs` | camel / snake / pascal / kebab / 保持原名 |
| `Core/JsonSampleGenerator.cs` | 中间模型 → 带占位值的 JSON 文本 |
| `EntityJsonGuiTool.cs` | DevToys 界面与设置持久化 |

解析器和生成器都不依赖 `DevToys.Api`，所以 `tools/EntityJson.Smoke` 直接链接源码在 net8.0 上跑。

## 为什么不引 Roslyn

只需要读「实体类声明」这一子集，手写词法/语法（约 700 行）就能覆盖，而且：

- 不需要把 Roslyn 及其依赖塞进插件包
- 报错位置、跳过的成员、未知类型都能自己控制
- TypeScript 已有独立解析器，共用同一套中间模型和 JSON 生成器；Java / Python / Go 按同样方式扩展

## 解析范围（重要）

支持：

- `class` / `record` / `struct` / `interface`（接口不参与根类型）
- 属性（`{ get; set; }`、`{ get; init; }`、`=> expr`）与字段，含多个声明符 `int A, B;`
- `record Foo(string A, int B);` 位置参数
- `List<T>` / `IList<T>` / `IEnumerable<T>` / `HashSet<T>` / `T[]` / `T[,]`（多维按一维）
- `Dictionary<K,V>` / `IDictionary<K,V>` / `IReadOnlyDictionary<K,V>` / `ConcurrentDictionary<K,V>`
- `Nullable<T>` / `T?`、`Task<T>` / `ValueTask<T>`（解包）
- 同一份源码内的继承（按类名合并基类字段，own 覆盖同名）
- 注解：`[JsonPropertyName]`、`[JsonProperty]`、`[JsonIgnore]`、`[EnumMember(Value=...)]`，支持完全限定名

刻意不覆盖：

- 局部类 / 局部函数 / 顶级语句 / 条件编译分支
- `partial` 分散在多个文件（这里只看到粘贴进来的这一份）
- 泛型参数的具体化（`T` 一律当未知类型，输出 `{}` 并给警告）
- 复杂初始值（`new()`、`nameof`、方法调用等只保留原文，生成时忽略）
- 自定义 `JsonConverter` 改写后的实际形状

跳过规则：`private/protected/internal`、`static`、`const`、`[JsonIgnore]` 的成员不输出，原因记录在
`EntityField.IgnoreReason` 里，供以后做「被跳过字段清单」。

## TypeScript 解析器

支持的写法：

- `interface` / `class` / `type X = {...}`：都当作对象形状；**TS 的 interface 不标记成 `IsInterface`**，
  否则会从根类型下拉里消失（它和 C# 的 interface 语义不同）
- `enum`：字符串初始值作为 JSON 值（`Active = 'active'` → `"active"`），数字枚举用成员名
- `type Status = 'a' | 'b'`：字符串字面量联合 → 枚举（取第一个值做占位）
- `type UserId = string` / 其他类型别名：解析后展开（`ApplyAliases` 深拷贝，避免共享节点）
- 属性：`?` 可选 → 可空；`| null` / `| undefined` → 可空；`readonly` 保留；`private/protected/static` 忽略
- 类型：`T[]`、`Array<T>`、`ReadonlyArray<T>`、`Set<T>`、`Record<K,V>`、`Map<K,V>`、`Partial/Pick/Omit/Readonly`、
  `Promise<T>`、元组（`[A, B]` 当数组）、内联 `{ ... }`（生成 `__anonymousN` 类型）
- 引号属性名（`'user-name': string`）原样作为 JSON 键

刻意不覆盖：

- `namespace` / `module` 内部的声明（整块跳过）
- 构造函数参数属性（`constructor(public name: string)`）
- 装饰器只跳过，不解析其语义
- 函数类型（`(x: string) => void`）当未知类型
- 交叉类型 `A & B` 只取第一个名字（不合并字段）

## Java 解析器

- `class` / `record` / `interface` / `enum`；record 的组件当字段
- 字段：包含 `private`（Jackson 通过字段/getter 序列化），忽略 `static` / `transient` / `@JsonIgnore`
- getter：`getXxx()` / `isXxx()` 在字段缺失时补一个属性；getter 上的 `@JsonProperty` 会覆盖字段名
- 注解：`@JsonProperty`、`@SerializedName`、`@JSONField`，支持全限定名（取最后一段）
- 类型：`List/Set/Collection`、`Map`、`Optional<T>`（解包并标可空）、`byte[]`、`BigDecimal`、`LocalDateTime`、`UUID` 等
- 枚举常量的 `@JsonProperty("x")` 作为 JSON 值

不覆盖：构造函数参数属性（`record` 除外）、Lombok 生成的方法（字段本身已能覆盖）、注解处理器产物。

## 反向（JSON → 实体）

- `JsonEntityGenerator` 用 `JsonDocument` 读样例，推断出 `EntityType`/`TypeShape`，再按目标语言写代码
- 对象 → 递归建类（类名取属性名单数化 + PascalCase，重名自动加序号）
- 数组 → 合并所有元素：对象取字段并集；数字取最宽（int → long → double）；混合类型退化为字符串并警告
- `null` → 可空；空数组 → `unknown` 元素
- C#：`class` + 属性；`List<T>`、`Dictionary<string,T>`
- TypeScript：`export interface`，**键名原样保留**（TS 的键就是 JSON 键，命名策略在这里不生效）
- Java：`class` + 私有字段 + getter/setter；可空数值用包装类型
- **注解风格**（`AnnotationStyle`，属性名与键不同才生成）：
  - C#：`SystemTextJson`（默认）`[JsonPropertyName("x")]`、`NewtonsoftJson` `[JsonProperty("x")]`、
    `SqlSugar` `[SugarColumn(ColumnName = "x")]`
  - Java：`Jackson`（默认）`@JsonProperty("x")`、`Gson` `@SerializedName("x")`、
    `FastJson` `@JSONField(name = "x")`、`MyBatisPlus` `@TableField("x")`
  - `None`：不生成注解，字段名直接使用 JSON 键（仅做非法字符替换），保证映射仍然成立
  - 注解对应的 using/import 只在真正用到时输出（不生成注解时不会出现无关命名空间）
- 已知限制：日期/GUID 不自动识别（当字符串）；根节点非对象/对象数组时报错

## 默认根类型

`FindClass(null)` 的优先级：

1. 排除嵌套类型（`IsNested`）
2. 在剩下的顶层类里，优先选**没有被子类继承**的那个（最派生叶子），避免默认选中 `BaseEntity`
3. 都不满足就取源码里第一个

## JSON 生成

- `IndentUnit = ""` 表示压缩单行；否则每层按该字符串缩进（`\t` 也支持）
- 键/值引号分开配置：`KeyQuote` / `ValueQuote`。选单引号时产物**不是合法 JSON**，界面会提示
- 同名字段（序列化后）后者覆盖：按最终名字保留最后一个
- 可空字段：`IncludeNulls` 开 → `null`，关 → 整条不输出
- 数组元素数 `0~5`，默认 1；对象/字典数组在多行模式下展开
- 对象深度超过 `MaxDepth`（默认 5）或检测到循环引用 → `{}` + 一条警告（按消息去重）
- `UseInitialValues` 只认字符串 / 字符 / 数字（去掉 `m/d/f/l` 后缀）/ 布尔 / `null`，其余忽略

## UI 约定与踩坑

- **不要用 `GUI.SettingGroup`**：它渲染成默认折叠的 `Expander`，且没有展开 API；这里用
  `GUI.Text(GUI.Label(id), "转换设置")` + `GUI.Stack().Vertical()` 平铺设置项。
- 解析在每次源码变化时同步执行（Typical 实体类 < 1 ms），选项变化只重渲染，不重新解析。
- 根类型下拉的 `GUI.WithItems` 会触发 `OnItemSelected`，必须用 `_suppress` 挡住；
  选中项按**项实例**而不是索引（索引在首次渲染时可能落到别的项上）。
- **方向与语言联动**：正向时语言是「源语言」（C#/TS/Java），反向时是「目标语言」；
  切换方向/语言会按方向套默认命名（正向 C# camel、TS keep；反向 C# Pascal、Java camel），
  但用户手动改过命名后不再覆盖（`_namingTouched`）。
- 反向只需要「类名 / 命名 / 缩进」，其余选项行用 `GUI.Hide` 收起；标题随方向切换。
- 开关状态读控件 `IsOn`，不采信 `OnToggle` 的事件参数。
- 设置整条存成一条 JSON（`EntityJson.State`），枚举按数字存，改枚举顺序会让旧设置错位。

## 构建 / 测试 / 打包

```powershell
dotnet build .\src\EntityJson.DevToys\EntityJson.DevToys.csproj -c Debug

powershell -ExecutionPolicy Bypass -File .\tools\EntityJson.Smoke\run.ps1

powershell -ExecutionPolicy Bypass -File .\src\EntityJson.DevToys\pack.ps1 -Install
```

离屏 UI 冒烟（可选）：临时工程引用插件 DLL + `DevToys.Api`，反射构造 `EntityJsonGuiTool`，
读取 `View.GetChildElementById("entity-output").Text` 验证默认输出，不依赖窗口交互。

## 后续

1. Python / Go 解析器（共用 `EntityType` 模型与生成器）
2. 反向：日期/GUID 识别、可选「用 Newtonsoft 特性」开关
3. 可选的「被跳过字段清单」面板
4. TypeScript：构造函数参数属性、`namespace` 内部声明、交叉类型字段合并
5. Java：Lombok/构造函数参数属性的显式识别

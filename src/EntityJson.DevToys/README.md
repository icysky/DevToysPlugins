# 实体类 ⇄ JSON（DevToys 扩展）

在两个方向上转换，用于接口联调：

- **实体类 → JSON**：只有后端/前端实体，需要构造 JSON 请求体
- **JSON → 实体类**：只有一段返回 JSON，需要生成 C# / TypeScript / Java 实体

定位是**偶尔测试 API 时快速造 JSON / 临时实体**，不用于生产代码生成；复杂映射（日期/GUID 识别、双向注解、多文件 partial）仍需人工确认。

## 支持的语言

| 语言 | 实体 → JSON | JSON → 实体 |
| --- | --- | --- |
| C# | `class` / `record` / `struct` / `enum`，属性、字段、record 位置参数、序列化注解 | 生成 `class` + 属性 + `[JsonPropertyName]` |
| TypeScript | `interface` / `type` / `class` / `enum`，可选属性、联合类型、`Record`、`Partial`、字符串字面量联合 | 生成 `export interface`，键名原样保留 |
| Java | `class` / `record` / `interface` / `enum`，字段、getter、Jackson/Gson/fastjson 注解 | 生成 `class` + 字段 + getter/setter + `@JsonProperty` |

## 实体 → JSON

- **多类型**：一个文件里多个类时用「根类型」下拉切换；默认选最派生的顶层类型
- **继承展开**：同一份源码内的基类字段会合并进来
- **类型**：字符串 / 数字 / 布尔 / 日期 / Guid / 枚举 / 数组 / `List` / `Set` / `Map` / `Dictionary` / `Optional` / `byte[]`
- **序列化注解**：`[JsonPropertyName]`、`[JsonProperty]`、`@JsonProperty`、`@SerializedName`、`@JSONField`、`[JsonIgnore]`、`[EnumMember]`
- **命名策略**：保持原名、camelCase（C# 默认）、snake_case、PascalCase、kebab-case（TS 默认保持原名）
- **引号**：键、值各自可选双引号或单引号；选单引号时界面会提示「不是严格 JSON」
- **其它**：缩进（2/4/Tab/压缩）、数组元素个数、嵌套深度、是否含空值、按字段名排序、采用字段初始值、根节点包裹名、根节点输出为数组

示例（C#）：

```csharp
public class CreateOrderRequest
{
    [JsonPropertyName("order_no")]
    public string OrderNo { get; set; }

    public decimal Amount { get; set; }
    public List<OrderItem> Items { get; set; }
    public string? Remark { get; set; }
}
```

输出：

```json
{
  "order_no": "string",
  "amount": 0,
  "items": [
    { "sku": "string", "quantity": 0 }
  ],
  "remark": null
}
```

## JSON → 实体

- 粘贴 JSON（对象或对象数组），选择目标语言与类名
- 类型推断：整数 `int`/`long`、小数 `double`、字符串、布尔、`null`（可空）、对象（递归生成嵌套类）、数组（合并所有元素）
- 命名：C# 默认 PascalCase、Java 默认 camelCase、TS 保持键名原样
- **字段注解可选**（属性名与 JSON 键不一致时才生成）：
  - C#：System.Text.Json（默认）`[JsonPropertyName]`、Newtonsoft.Json `[JsonProperty]`、SqlSugar `[SugarColumn(ColumnName = ...)]`、不生成
  - Java：Jackson（默认）`@JsonProperty`、Gson `@SerializedName`、fastjson `@JSONField`、MyBatis-Plus `@TableField`、不生成
  - TypeScript：没有注解概念，始终保留 JSON 键名
  - 选「不生成」时会保留 JSON 键原样作为字段名，保证仍能对上
- 数组元素对象会按字段并集合并；类型冲突时退化为字符串并给警告

示例输出（C#）：

```csharp
using System.Collections.Generic;
using System.Text.Json.Serialization;

public class Order
{
    [JsonPropertyName("order_no")]
    public string OrderNo { get; set; }
    [JsonPropertyName("items")]
    public List<Item> Items { get; set; }
    [JsonPropertyName("remark")]
    public string? Remark { get; set; }
}
```

## 已知限制

- C#：只读声明，不执行构造/初始化表达式；`partial` 分散多文件不合并；泛型参数 `T` 视为未知类型
- TypeScript：构造函数参数属性、`namespace` 内部声明、交叉类型字段合并暂不支持
- Java：构造函数参数属性、注解处理器生成的内容不参与；私有字段按字段序列化处理
- 反向：只根据样例值推断类型，不识别日期/GUID（会当字符串）；数组元素类型冲突时退化为字符串

## 安装

DevToys → 设置 → 扩展（Manage extensions）→ 搜索「实体类」或标签 `devtoys-app` → 安装 → 重启。工具位于「转换器（Converters）」分组。

## 许可

插件代码采用 **0BSD**，不依赖第三方运行库。

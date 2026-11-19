using EntityJson.DevToys.Core;

int passed = 0;
int failed = 0;

Console.WriteLine("=== EntityJson.DevToys 解析 / 生成逻辑冒烟测试 ===");
Console.WriteLine();

Index("基础类型与占位值");
Check("简单类", () => Generate("""
public class User
{
    public string Name { get; set; }
    public int Age { get; set; }
    public bool Active { get; set; }
    public double Score { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid Id { get; set; }
}
""") == """{"name":"string","age":0,"active":false,"score":0,"createdAt":"2026-01-01T00:00:00","id":"00000000-0000-0000-0000-000000000000"}""");

Check("可空值默认输出 null", () => Generate("""
public class User
{
    public string? Nick { get; set; }
    public int? Age { get; set; }
}
""") == """{"nick":null,"age":null}""");

Check("关闭空值后跳过可空字段", () => Generate("""
public class User
{
    public string Name { get; set; }
    public string? Nick { get; set; }
}
""", o => o.IncludeNulls = false) == """{"name":"string"}""");

Check("char / TimeSpan / byte[]", () => Generate("""
public class Data
{
    public char Grade { get; set; }
    public TimeSpan Duration { get; set; }
    public byte[] Payload { get; set; }
}
""") == """{"grade":"a","duration":"00:00:00","payload":""}""");

Index("集合与字典");
Check("List / 数组 / 字典", () => Generate("""
public class Data
{
    public List<string> Tags { get; set; }
    public int[] Numbers { get; set; }
    public Dictionary<string, int> Map { get; set; }
    public IReadOnlyList<decimal> Prices { get; set; }
}
""") == """{"tags":["string"],"numbers":[0],"map":{"key":0},"prices":[0]}""");

Check("数组元素数量可配", () => Generate("""
public class Data { public List<string> Tags { get; set; } }
""", o => o.ArrayItemCount = 2) == """{"tags":["string","string"]}""");

Check("数组元素数量 0", () => Generate("""
public class Data { public List<string> Tags { get; set; } }
""", o => o.ArrayItemCount = 0) == """{"tags":[]}""");

Check("对象数组", () => Generate("""
public class Order { public List<Item> Items { get; set; } }
public class Item { public string Sku { get; set; } }
""") == """{"items":[{"sku":"string"}]}""");

Index("枚举");
Check("枚举取第一个成员名", () => Generate("""
public enum Status { Active, Disabled }
public class Order { public Status Status { get; set; } }
""") == """{"status":"Active"}""");

Check("EnumMember 改名后按注解输出", () => Generate("""
public enum Status
{
    [EnumMember(Value = "active")]
    Active,
    Disabled
}
public class Order { public Status Status { get; set; } }
""") == """{"status":"active"}""");

Check("可空枚举", () => Generate("""
public enum Status { Active }
public class Order { public Status? Status { get; set; } }
""") == """{"status":null}""");

Index("序列化注解");
Check("JsonPropertyName 优先于命名策略", () => Generate("""
public class User
{
    [JsonPropertyName("user_name")]
    public string UserName { get; set; }
}
""") == """{"user_name":"string"}""");

Check("完全限定名的注解", () => Generate("""
public class User
{
    [System.Text.Json.Serialization.JsonPropertyName("login")]
    public string UserName { get; set; }
}
""") == """{"login":"string"}""");

Check("JsonIgnore 不输出", () => Generate("""
public class User
{
    public string Name { get; set; }
    [JsonIgnore]
    public string Password { get; set; }
}
""") == """{"name":"string"}""");

Check("关闭注解后走命名策略", () => Generate("""
public class User
{
    [JsonPropertyName("user_name")]
    public string UserName { get; set; }
}
""", o => o.UseJsonAttributes = false) == """{"userName":"string"}""");

Index("命名策略");
Check("保持原名", () => Generate("public class A { public string UserName { get; set; } }",
    o => o.NamingPolicy = FieldNamingPolicy.Keep) == """{"UserName":"string"}""");

Check("camelCase（默认）", () => Generate("public class A { public string UserName { get; set; } }")
    == """{"userName":"string"}""");

Check("snake_case", () => Generate("public class A { public string UserName { get; set; } }",
    o => o.NamingPolicy = FieldNamingPolicy.SnakeCase) == """{"user_name":"string"}""");

Check("PascalCase", () => Generate("public class A { public string userName { get; set; } }",
    o => o.NamingPolicy = FieldNamingPolicy.PascalCase) == """{"UserName":"string"}""");

Check("kebab-case", () => Generate("public class A { public string UserName { get; set; } }",
    o => o.NamingPolicy = FieldNamingPolicy.KebabCase) == """{"user-name":"string"}""");

Check("缩写按词拆分（URLValue）", () => Generate("public class A { public string URLValue { get; set; } }")
    == """{"urlValue":"string"}""");

Index("引号与格式");
Check("键值都用单引号", () => Generate("public class A { public string Name { get; set; } }",
    o =>
    {
        o.KeyQuote = '\'';
        o.ValueQuote = '\'';
    }) == """{'name':'string'}""");

Check("键单引号、值双引号", () => Generate("public class A { public string Name { get; set; } }",
    o => o.KeyQuote = '\'') == """{'name':"string"}""");

Check("两空格缩进（默认）", () => Generate("public class A { public string Name { get; set; } }",
    o => o.IndentUnit = "  ") == "{" + Environment.NewLine
    + "  \"name\": \"string\"" + Environment.NewLine + "}");

Check("四空格缩进", () => Generate("public class A { public string Name { get; set; } }",
    o => o.IndentUnit = "    ") == "{" + Environment.NewLine
    + "    \"name\": \"string\"" + Environment.NewLine + "}");

Index("字段初始值");
const string WithInitializers = """
public class Config
{
    public string Name { get; set; } = "abc";
    public int Port { get; set; } = 8080;
    public bool Enabled { get; set; } = true;
    public decimal Rate { get; set; } = 1.5m;
    public string? Note { get; set; } = null;
}
""";

Check("默认使用占位值", () => Generate(WithInitializers)
    == """{"name":"string","port":0,"enabled":false,"rate":0,"note":null}""");

Check("开启后采用字面量初值", () => Generate(WithInitializers, o => o.UseInitialValues = true)
    == """{"name":"abc","port":8080,"enabled":true,"rate":1.5,"note":null}""");

Index("根节点与排序");
Check("根节点包裹", () => Generate("public class A { public string Name { get; set; } }",
    o => o.WrapRootName = "data") == """{"data":{"name":"string"}}""");

Check("根节点为数组", () => Generate("public class A { public string Name { get; set; } }",
    o => o.RootArray = true) == """[{"name":"string"}]""");

Check("按字母排序字段", () => Generate("public class A { public string B { get; set; } public int A { get; set; } }",
    o => o.SortFields = true) == """{"a":0,"b":"string"}""");

Index("多类型与继承");
Check("多类时按根类型选择", () => Generate("""
public class A { public string Name { get; set; } }
public class B { public int Code { get; set; } }
""", root: "B") == """{"code":0}""");

Check("继承字段排在前面", () => Generate("""
public class BaseEntity { public int Id { get; set; } }
public class User : BaseEntity { public string Name { get; set; } }
""") == """{"id":0,"name":"string"}""");

Check("record 位置参数", () => Generate("public record Point(int X, int Y);")
    == """{"x":0,"y":0}""");

Check("嵌套类引用", () => Generate("""
public class Outer
{
    public Inner Child { get; set; }
    public class Inner { public int X { get; set; } }
}
""") == """{"child":{"x":0}}""");

Check("接口不会被当成根", () =>
{
    EntityParseResult model = CSharpEntityParser.Parse("""
public interface IEntity { int Id { get; set; } }
public class User : IEntity { public string Name { get; set; } }
""");
    return model.Classes.Count == 1 && model.Classes[0].Name == "User";
});

Index("容错与诊断");
Check("自引用截断且给警告", () =>
{
    (string json, IReadOnlyList<Diagnostic> diagnostics) = GenerateWithDiagnostics("""
public class Node { public Node Next { get; set; } }
""");
    Console.WriteLine($"        {json}");
    return json == """{"next":{}}""" && diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning);
});

Check("泛型参数无法识别时输出空对象并警告", () =>
{
    (string json, IReadOnlyList<Diagnostic> diagnostics) = GenerateWithDiagnostics("""
public class Wrapper<T> { public T Data { get; set; } }
""");
    Console.WriteLine($"        {json}");
    return json == """{"data":{}}""" && diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning);
});

Check("深度限制", () =>
{
    (string json, _) = GenerateWithDiagnostics("""
public class A { public B B { get; set; } }
public class B { public C C { get; set; } }
public class C { public int X { get; set; } }
""", o => o.MaxDepth = 1);
    Console.WriteLine($"        {json}");
    return json == """{"b":{"c":{}}}""";
});

Check("private / static / const 不输出", () => Generate("""
public class A
{
    public string Name { get; set; }
    private string Secret { get; set; }
    public static int Count { get; set; }
    public const string Const = "x";
}
""") == """{"name":"string"}""");

Check("没有类时返回空对象和错误", () =>
{
    EntityParseResult model = CSharpEntityParser.Parse("public enum Color { Red }");
    JsonRenderResult result = JsonSampleGenerator.Generate(model, null, new JsonRenderOptions { IndentUnit = "" });
    return result.Json == "{}" && result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
});

Check("空输入不抛异常", () =>
{
    JsonRenderResult result = JsonSampleGenerator.Generate(
        CSharpEntityParser.Parse(""), null, new JsonRenderOptions { IndentUnit = "" });
    return result.Json == "{}";
});

Check("同名 JSON 字段后者覆盖", () => Generate("""
public class A
{
    [JsonPropertyName("x")] public string First { get; set; }
    [JsonPropertyName("x")] public int Second { get; set; }
}
""") == """{"x":0}""");

Check("注释与预处理指令不影响解析", () => Generate("""
#define FOO
using System;
// 注释
/* 块注释 */
public class A
{
    /// <summary>文档注释</summary>
    public string Name { get; set; }
}
""") == """{"name":"string"}""");

Index("TypeScript：基础");
Check("interface 基础类型", () => GenerateTs("""
export interface User {
  name: string;
  age: number;
  active: boolean;
}
""") == """{"name":"string","age":0,"active":false}""");

Check("可选与 null 联合", () => GenerateTs("""
interface User {
  nick?: string;
  note: string | null;
  extra?: number | undefined;
}
""") == """{"nick":null,"note":null,"extra":null}""");

Check("数组三种写法", () => GenerateTs("""
interface Data {
  tags: string[];
  scores: Array<number>;
  flags: ReadonlyArray<boolean>;
}
""") == """{"tags":["string"],"scores":[0],"flags":[false]}""");

Check("Record 当字典", () => GenerateTs("interface A { map: Record<string, number>; }")
    == """{"map":{"key":0}}""");

Check("嵌套 interface", () => GenerateTs("""
interface Order { items: Item[]; }
interface Item { sku: string; }
""") == """{"items":[{"sku":"string"}]}""");

Check("interface 继承", () => GenerateTs("""
interface Base { id: number; }
interface User extends Base { name: string; }
""") == """{"id":0,"name":"string"}""");

Check("引号属性名保留原样", () => GenerateTs("interface A { 'user-name': string; }")
    == """{"user-name":"string"}""");

Index("TypeScript：别名与枚举");
Check("字符串字面量联合当枚举", () => GenerateTs("""
type Status = 'active' | 'disabled';
interface Order { status: Status; }
""") == """{"status":"active"}""");

Check("enum 取值用字符串值", () => GenerateTs("""
enum Status { Active = 'active', Disabled = 'disabled' }
interface Order { status: Status; }
""") == """{"status":"active"}""");

Check("type 别名指向对象", () => GenerateTs("type User = { name: string; age: number };")
    == """{"name":"string","age":0}""");

Check("别名指向基础类型", () => GenerateTs("""
type UserId = string;
interface A { id: UserId; }
""") == """{"id":"string"}""");

Check("Partial 解包", () => GenerateTs("""
interface User { name: string; }
interface Wrapper { data: Partial<User>; }
""", root: "Wrapper") == """{"data":{"name":"string"}}""");

Index("TypeScript：类与容错");
Check("class 忽略 private/static/getter/方法", () => GenerateTs("""
export class User {
  public name: string = '';
  private secret: string = '';
  static count: number = 0;
  get display(): string { return this.name; }
  save(): void { }
}
""") == """{"name":"string"}""");

Check("无类型标注时按初始值推断", () => GenerateTs("""
class A {
  name = 'abc';
  count = 0;
  ok = true;
}
""") == """{"name":"string","count":0,"ok":false}""");

Check("推断 + 采用初值", () => GenerateTs("""
class A {
  name = 'abc';
  count = 0;
  ok = true;
}
""", o => o.UseInitialValues = true) == """{"name":"abc","count":0,"ok":true}""");

Check("联合类型取第一个非空类型", () => GenerateTs("interface A { v: string | number; }")
    == """{"v":"string"}""");

Check("元组按数组处理", () => GenerateTs("interface A { pair: [string, number]; }")
    == """{"pair":["string"]}""");

Check("内联匿名对象", () => GenerateTs("interface A { meta: { x: number; y: string } }")
    == """{"meta":{"x":0,"y":"string"}}""");

Check("索引签名与方法签名被跳过", () => GenerateTs("""
interface A {
  [key: string]: any;
  foo(): void;
  name: string;
}
""") == """{"name":"string"}""");

Check("命名与引号选项对 TS 同样生效", () => GenerateTs("interface A { UserName: string }",
    o =>
    {
        o.NamingPolicy = FieldNamingPolicy.SnakeCase;
        o.KeyQuote = '\'';
        o.ValueQuote = '\'';
    }) == "{'user_name':'string'}");

Index("Java：基础");
Check("POJO 字段 + getter", () => GenerateJava("""
public class User {
    private String name;
    private Integer age;
    private boolean active;

    public String getName() { return name; }
    public Integer getAge() { return age; }
    public boolean isActive() { return active; }
}
""") == """{"name":"string","age":0,"active":false}""");

Check("Jackson 注解", () => GenerateJava("""
public class User {
    @JsonProperty("user_name")
    private String userName;

    @JsonIgnore
    private String password;

    private static final long serialVersionUID = 1L;
    private transient String cache;
}
""") == """{"user_name":"string"}""");

Check("List / Map / Optional", () => GenerateJava("""
public class Data {
    private List<String> tags;
    private Map<String, Integer> map;
    private Optional<String> note;
    private int[] numbers;
}
""") == """{"tags":["string"],"map":{"key":0},"note":null,"numbers":[0]}""");

Check("Java record", () => GenerateJava("public record Point(int x, int y) { }")
    == """{"x":0,"y":0}""");

Check("枚举常量注解", () => GenerateJava("""
public enum Status {
    @JsonProperty("active") ACTIVE,
    DISABLED
}
public class Order {
    private Status status;
}
""") == """{"status":"active"}""");

Check("继承字段", () => GenerateJava("""
public class BaseEntity { private Long id; }
public class User extends BaseEntity { private String name; }
""") == """{"id":0,"name":"string"}""");

Check("只有 getter 也能生成字段", () => GenerateJava("""
public class User {
    public String getDisplayName() { return "x"; }
}
""") == """{"displayName":"string"}""");

Check("嵌套类", () => GenerateJava("""
public class Outer {
    private Inner child;
    public static class Inner { private int x; }
}
""") == """{"child":{"x":0}}""");

Index("反向：JSON → 实体");
const string OrderJson = """
{
  "order_no": "A1",
  "amount": 12.5,
  "count": 3,
  "big": 9999999999,
  "active": true,
  "remark": null,
  "items": [ { "sku": "x", "qty": 2 } ],
  "profile": { "name": "n" }
}
""";

Check("JSON → C#", () => GenerateReverse(OrderJson, SourceLanguage.CSharp) == """
using System.Collections.Generic;
using System.Text.Json.Serialization;

public class Root
{
    [JsonPropertyName("order_no")]
    public string OrderNo { get; set; }
    [JsonPropertyName("amount")]
    public double Amount { get; set; }
    [JsonPropertyName("count")]
    public int Count { get; set; }
    [JsonPropertyName("big")]
    public long Big { get; set; }
    [JsonPropertyName("active")]
    public bool Active { get; set; }
    [JsonPropertyName("remark")]
    public string? Remark { get; set; }
    [JsonPropertyName("items")]
    public List<Item> Items { get; set; }
    [JsonPropertyName("profile")]
    public Profile Profile { get; set; }
}

public class Item
{
    [JsonPropertyName("sku")]
    public string Sku { get; set; }
    [JsonPropertyName("qty")]
    public int Qty { get; set; }
}

public class Profile
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
}
""");

Check("JSON → C#：关闭注解后不生成 JsonPropertyName", () =>
{
    string code = GenerateReverse("""{ "order_no": "A1" }""", SourceLanguage.CSharp, o => o.AnnotateRenamed = false);
    return code.Contains("public string OrderNo { get; set; }", StringComparison.Ordinal)
           && !code.Contains("JsonPropertyName", StringComparison.Ordinal)
           && !code.Contains("using System.Text.Json.Serialization;", StringComparison.Ordinal);
});

Check("JSON → TypeScript", () => GenerateReverse(OrderJson, SourceLanguage.TypeScript, o => o.IndentUnit = "  ") == """
export interface Root {
  order_no: string;
  amount: number;
  count: number;
  big: number;
  active: boolean;
  remark?: unknown | null;
  items: Item[];
  profile: Profile;
}

export interface Item {
  sku: string;
  qty: number;
}

export interface Profile {
  name: string;
}
""");

Check("JSON → Java", () =>
{
    string code = GenerateReverse(OrderJson, SourceLanguage.Java, o => o.Naming = FieldNamingPolicy.CamelCase);
    Console.WriteLine(code);
    return code.Contains("import java.util.List;", StringComparison.Ordinal)
           && code.Contains("import com.fasterxml.jackson.annotation.JsonProperty;", StringComparison.Ordinal)
           && code.Contains("private String orderNo;", StringComparison.Ordinal)
           && code.Contains("private List<Item> items;", StringComparison.Ordinal)
           && code.Contains("public String getOrderNo() { return orderNo; }", StringComparison.Ordinal)
           && code.Contains("public void setOrderNo(String orderNo) { this.orderNo = orderNo; }", StringComparison.Ordinal)
           && code.Contains("public class Profile", StringComparison.Ordinal);
});

Check("JSON → C#：null 字段与可空数组元素", () => GenerateReverse("""
{ "id": null, "nums": [1, null] }
""", SourceLanguage.CSharp) == """
using System.Collections.Generic;
using System.Text.Json.Serialization;

public class Root
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonPropertyName("nums")]
    public List<int?> Nums { get; set; }
}
""");

Check("JSON → C#：根为对象数组", () => GenerateReverse("""
[ { "id": 1 }, { "name": "a" } ]
""", SourceLanguage.CSharp) == """
using System.Text.Json.Serialization;

public class Root
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; }
}
""");

Check("JSON → TypeScript：非法标识符加引号", () => GenerateReverse("""{ "user-name": "x" }""", SourceLanguage.TypeScript,
    o => o.IndentUnit = "  ") == """
export interface Root {
  "user-name": string;
}
""");

Check("JSON → Java：可空布尔用 Boolean", () => GenerateReverse("""{ "enabled": null }""", SourceLanguage.Java,
    o => o.Naming = FieldNamingPolicy.CamelCase).Contains("private Object enabled;", StringComparison.Ordinal));

Index("注解风格");
Check("C# Newtonsoft.Json", () =>
{
    string code = GenerateReverse("""{ "order_no": "A1" }""", SourceLanguage.CSharp,
        o => o.AnnotationStyle = AnnotationStyle.NewtonsoftJson);
    return code.Contains("using Newtonsoft.Json;", StringComparison.Ordinal)
           && code.Contains("[JsonProperty(\"order_no\")]", StringComparison.Ordinal);
});

Check("C# SqlSugar", () =>
{
    string code = GenerateReverse("""{ "order_no": "A1" }""", SourceLanguage.CSharp,
        o => o.AnnotationStyle = AnnotationStyle.SqlSugar);
    return code.Contains("using SqlSugar;", StringComparison.Ordinal)
           && code.Contains("[SugarColumn(ColumnName = \"order_no\")]", StringComparison.Ordinal);
});

Check("C# 不生成注解时保留原键名", () =>
{
    string code = GenerateReverse("""{ "order_no": "A1" }""", SourceLanguage.CSharp,
        o => o.AnnotationStyle = AnnotationStyle.None);
    return code.Contains("public string order_no { get; set; }", StringComparison.Ordinal)
           && !code.Contains("JsonProperty", StringComparison.Ordinal)
           && !code.Contains("using System.Text.Json.Serialization;", StringComparison.Ordinal);
});

Check("Java Gson", () =>
{
    string code = GenerateReverse("""{ "order_no": "A1" }""", SourceLanguage.Java,
        o => o.AnnotationStyle = AnnotationStyle.Gson);
    return code.Contains("import com.google.gson.annotations.SerializedName;", StringComparison.Ordinal)
           && code.Contains("@SerializedName(\"order_no\")", StringComparison.Ordinal);
});

Check("Java fastjson", () =>
{
    string code = GenerateReverse("""{ "order_no": "A1" }""", SourceLanguage.Java,
        o => o.AnnotationStyle = AnnotationStyle.FastJson);
    return code.Contains("import com.alibaba.fastjson.annotation.JSONField;", StringComparison.Ordinal)
           && code.Contains("@JSONField(name = \"order_no\")", StringComparison.Ordinal);
});

Check("Java MyBatis-Plus", () =>
{
    string code = GenerateReverse("""{ "order_no": "A1" }""", SourceLanguage.Java,
        o => o.AnnotationStyle = AnnotationStyle.MyBatisPlus);
    return code.Contains("import com.baomidou.mybatisplus.annotation.TableField;", StringComparison.Ordinal)
           && code.Contains("@TableField(\"order_no\")", StringComparison.Ordinal);
});

Check("Java 不生成注解时保留原键名", () =>
{
    string code = GenerateReverse("""{ "order_no": "A1" }""", SourceLanguage.Java,
        o => o.AnnotationStyle = AnnotationStyle.None);
    return code.Contains("private String order_no;", StringComparison.Ordinal)
           && !code.Contains("@JsonProperty", StringComparison.Ordinal);
});

Check("JSON 非法时给错误", () =>
{
    JsonEntityResult result = JsonEntityGenerator.Generate("{ bad", new JsonEntityOptions());
    return result.Code.Length == 0 && result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
});

Console.WriteLine();
Console.WriteLine($"===== 通过 {passed} 项，失败 {failed} 项 =====");
Console.WriteLine(failed == 0 ? "全部通过 ✅" : "存在失败 ❌");
return failed == 0 ? 0 : 1;

void Index(string title)
{
    Console.WriteLine();
    Console.WriteLine($"--- {title} ---");
}

void Check(string name, Func<bool> condition)
{
    bool ok;
    try
    {
        ok = condition();
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
        return;
    }

    if (ok)
    {
        passed++;
        Console.WriteLine($"PASS  {name}");
    }
    else
    {
        failed++;
        Console.WriteLine($"FAIL  {name}");
    }
}

string Generate(string code, Action<JsonRenderOptions>? configure = null, string? root = null)
    => GenerateWithDiagnostics(code, configure, root).Json;

string GenerateTs(string code, Action<JsonRenderOptions>? configure = null, string? root = null)
{
    EntityParseResult model = TypeScriptEntityParser.Parse(code);
    var options = new JsonRenderOptions { IndentUnit = string.Empty };
    configure?.Invoke(options);
    return JsonSampleGenerator.Generate(model, root, options).Json;
}

string GenerateJava(string code, Action<JsonRenderOptions>? configure = null, string? root = null)
{
    EntityParseResult model = JavaEntityParser.Parse(code);
    var options = new JsonRenderOptions { IndentUnit = string.Empty };
    configure?.Invoke(options);
    return JsonSampleGenerator.Generate(model, root, options).Json;
}

string GenerateReverse(string json, SourceLanguage target, Action<JsonEntityOptions>? configure = null)
{
    var options = new JsonEntityOptions { TargetLanguage = target };
    configure?.Invoke(options);
    return JsonEntityGenerator.Generate(json, options).Code;
}

(string Json, IReadOnlyList<Diagnostic> Diagnostics) GenerateWithDiagnostics(
    string code,
    Action<JsonRenderOptions>? configure = null,
    string? root = null)
{
    EntityParseResult model = CSharpEntityParser.Parse(code);
    var options = new JsonRenderOptions { IndentUnit = string.Empty };
    configure?.Invoke(options);
    JsonRenderResult result = JsonSampleGenerator.Generate(model, root, options);
    return (result.Json, result.Diagnostics);
}

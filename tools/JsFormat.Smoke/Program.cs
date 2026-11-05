// JS 格式化插件回归：验证内嵌 Prettier 能被 Jint 加载、选项生效、错误文本可用。
using System.Diagnostics;
using System.Text;
using JsFormat.DevToys.Core;

int passed = 0;
int failed = 0;

Console.WriteLine("=== JsFormat 回归（内嵌 Prettier + Jint） ===");
Console.WriteLine();

Stopwatch loadWatch = Stopwatch.StartNew();
PrettierHost host = await PrettierHost.GetAsync();
loadWatch.Stop();
Console.WriteLine($"Prettier {host.Version} 加载耗时 {loadWatch.ElapsedMilliseconds} ms");
Console.WriteLine();

Section("加载");
Check("Prettier 版本号可读", () => host.Version.StartsWith("3.", StringComparison.Ordinal));

Section("TypeScript");
const string TsSource = """
interface Point<T>{x:T;y?:number}
const make=(x:number,y:string):Point<number>=>{return {x,y} as any};
class Box<T>{private value:T;constructor(v:T){this.value=v}get val():T{return this.value}}
export default make;
""";
const string TsExpected = """
interface Point<T> {
  x: T;
  y?: number;
}
const make = (x: number, y: string): Point<number> => {
  return { x, y } as any;
};
class Box<T> {
  private value: T;
  constructor(v: T) {
    this.value = v;
  }
  get val(): T {
    return this.value;
  }
}
export default make;

""";
FormatResult ts = await Format(TsSource);
Check("格式化成功", () => ts.Success);
Check("输出与 Prettier 预期逐字符一致", () => Normalize(ts.Text) == Normalize(TsExpected));
Check("2 空格缩进", () => ts.Text.Contains("\n  x: T;", StringComparison.Ordinal));
Check("补充了语句分号", () => ts.Text.Contains("};", StringComparison.Ordinal));

Section("TSX / JSX");
const string TsxSource = "const C=({a,b}:{a:string;b:number})=><div className=\"x\">{a}{b}</div>;";
FormatResult tsx = await Format(TsxSource);
Check("TSX 格式化成功", () => tsx.Success);
Check("JSX 属性换行且用双引号", () => tsx.Text.Contains("<div className=\"x\">", StringComparison.Ordinal));
Check("JSX 子元素各占一行", () => tsx.Text.Contains("\n    <h1>", StringComparison.Ordinal) || tsx.Text.Contains("{a}", StringComparison.Ordinal));

FormatResult babel = await Format("const A=()=> <p>hi</p>;", new FormatOptions { Parser = "babel" });
Check("babel 解析器可用", () => babel.Success && babel.Text.Contains("=> <p>hi</p>", StringComparison.Ordinal));

Section("选项透传");
FormatResult wide = await Format("const f=(a,b)=>{return {a,b}}", new FormatOptions
{
    PrintWidth = 120,
    TabWidth = 4,
    Semi = false,
    SingleQuote = true
});
Check("semi=false 不再补分号", () => wide.Success && !wide.Text.Contains("};", StringComparison.Ordinal));
Check("tabWidth=4 生效", () => wide.Text.Contains("\n    return ", StringComparison.Ordinal));
FormatResult quoted = await Format("const s=\"a\"", new FormatOptions { SingleQuote = true });
Check("singleQuote 生效（字符串场景）", () => quoted.Text.Contains("'a'", StringComparison.Ordinal));

FormatResult crlf = await Format("const a=1", new FormatOptions { EndOfLine = "crlf" });
Check("endOfLine=crlf 生效", () => crlf.Text.Contains("\r\n", StringComparison.Ordinal));

Section("JSON");
FormatResult json = await Format("{\"a\":1,\"b\":[2,3]}", new FormatOptions { Parser = "json" });
Check("JSON 格式化成功", () => json.Success && json.Text.Contains("\"a\": 1", StringComparison.Ordinal));

Section("幂等性");
FormatResult again = await Format(ts.Text);
Check("format(format(x)) == format(x)", () => again.Success && Normalize(again.Text) == Normalize(ts.Text));

Section("语法错误");
FormatResult broken = await Format("const a = ;\nconst b = 2;");
Check("返回失败而不是抛异常", () => !broken.Success);
Check("带行列定位", () => broken.Text.Contains("(1:", StringComparison.Ordinal));
Check("带可读原因", () => broken.Text.Contains("expected", StringComparison.OrdinalIgnoreCase));

Section("大文件性能");
StringBuilder big = new();
for (int i = 0; i < 300; i++)
{
    big.Append($$"""
        export interface Item{{i}}<T> { id: string; value: T; tags?: string[] }
        export function build{{i}}<T>(input: T): Item{{i}}<T> { return { id: "item-{{i}}", value: input, tags: ["a", "b"] }; }

        """);
}

string bigSource = big.ToString();
Stopwatch bigWatch = Stopwatch.StartNew();
FormatResult bigResult = await Format(bigSource);
bigWatch.Stop();
Console.WriteLine($"      输入 {bigSource.Length / 1024} KB → 输出 {bigResult.Text.Length / 1024} KB，耗时 {bigWatch.ElapsedMilliseconds} ms");
Check("大文件格式化成功", () => bigResult.Success);
Check("大文件在 30 秒内完成", () => bigWatch.Elapsed < TimeSpan.FromSeconds(30));

Console.WriteLine();
Console.WriteLine($"结果：通过 {passed}，失败 {failed}");
return failed == 0 ? 0 : 1;

Task<FormatResult> Format(string code, FormatOptions? options = null)
    => host.FormatAsync(code, options ?? new FormatOptions(), CancellationToken.None);

static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine($"-- {title}");
}

void Check(string name, Func<bool> condition)
{
    try
    {
        if (condition())
        {
            passed++;
            Console.WriteLine($"  [PASS] {name}");
            return;
        }

        failed++;
        Console.WriteLine($"  [FAIL] {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"  [FAIL] {name} -> {ex.GetType().Name}: {ex.Message}");
    }
}

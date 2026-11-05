using System.Diagnostics;
using System.Reflection;
using System.Text;
using Jint;

namespace JsFormat.DevToys.Core;

/// <summary>
/// 内嵌 Prettier 的执行宿主。
///
/// 为什么这么做：.NET 侧没有能对齐 Prettier 输出的格式化器，而"细微差异"在格式化场景不可接受，
/// 所以直接把 Prettier 3 的 standalone 构建（UMD）塞进 Jint（纯托管 JS 解释器）里跑。
/// 实测：加载 4 个 bundle 约 0.45 秒（进程内一次），单次格式化 10~150 ms。
///
/// 线程模型：Jint 的引擎实例不是线程安全的，这里用信号量把调用串行化；
/// 所有 JS 执行都放在线程池里，绝不占用 UI 线程。
/// </summary>
internal sealed class PrettierHost
{
    private const string ResourcePrefix = "JsFormat.DevToys.Assets.prettier.";

    private static readonly string[] Bundles = ["standalone.js", "estree.js", "typescript.js", "babel.js"];

    private static readonly Lazy<Task<PrettierHost>> Shared =
        new(CreateAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Engine _engine;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private PrettierHost(Engine engine, string version)
    {
        _engine = engine;
        Version = version;
    }

    /// <summary>加载好的 Prettier 版本号，例如 "3.9.10"。</summary>
    public string Version { get; }

    /// <summary>取共享实例；首次调用会在后台线程加载 bundle（约 0.5 秒）。</summary>
    public static Task<PrettierHost> GetAsync() => Shared.Value;

    private static Task<PrettierHost> CreateAsync() => Task.Run(() =>
    {
        Engine engine = new(options => options
            .TimeoutInterval(TimeSpan.FromSeconds(30))
            .LimitMemory(2L * 1024 * 1024 * 1024));

        foreach (string bundle in Bundles)
        {
            string source = ReadBundle(bundle)
                ?? throw new InvalidOperationException($"插件内嵌资源缺失：{ResourcePrefix}{bundle}");

            engine.Execute(source);
        }

        string version = engine.Evaluate("prettier.version").ToString();
        return new PrettierHost(engine, version);
    });

    private static string? ReadBundle(string name)
    {
        Assembly assembly = typeof(PrettierHost).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourcePrefix + name);
        if (stream is null)
        {
            return null;
        }

        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 格式化代码。成功时 <see cref="FormatResult.Text" /> 是格式化结果；
    /// 失败时是 Prettier 的报错文本（含 <c>行:列</c> 与代码框），可直接展示给用户。
    /// </summary>
    public async Task<FormatResult> FormatAsync(string code, FormatOptions options, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => FormatCore(code, options, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private FormatResult FormatCore(string code, FormatOptions options, CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        _engine.SetValue("__code", code);
        _engine.SetValue("__parser", options.Parser);
        _engine.SetValue("__printWidth", options.PrintWidth);
        _engine.SetValue("__tabWidth", options.TabWidth);
        _engine.SetValue("__useTabs", options.UseTabs);
        _engine.SetValue("__semi", options.Semi);
        _engine.SetValue("__singleQuote", options.SingleQuote);
        _engine.SetValue("__trailingComma", options.TrailingComma);
        _engine.SetValue("__bracketSpacing", options.BracketSpacing);
        _engine.SetValue("__arrowParens", options.ArrowParens);
        _engine.SetValue("__quoteProps", options.QuoteProps);
        _engine.SetValue("__endOfLine", options.EndOfLine);

        _engine.Execute("globalThis.__out = undefined; globalThis.__err = undefined;");

        try
        {
            // Prettier 3 的 format 是异步 API，结果通过 then 回调取回
            _engine.Execute("""
                prettier.format(__code, {
                  parser: __parser,
                  plugins: prettierPlugins,
                  printWidth: __printWidth,
                  tabWidth: __tabWidth,
                  useTabs: __useTabs,
                  semi: __semi,
                  singleQuote: __singleQuote,
                  trailingComma: __trailingComma,
                  bracketSpacing: __bracketSpacing,
                  arrowParens: __arrowParens,
                  quoteProps: __quoteProps,
                  endOfLine: __endOfLine
                }).then(
                  function (r) { globalThis.__out = r; },
                  function (e) { globalThis.__err = String((e && e.message) || e); });
                """);

            while (!_engine.Evaluate("globalThis.__out !== undefined || globalThis.__err !== undefined").AsBoolean())
            {
                cancellationToken.ThrowIfCancellationRequested();
                _engine.Advanced.ProcessTasks();

                if (stopwatch.Elapsed > TimeSpan.FromSeconds(60))
                {
                    return new FormatResult(false, "格式化超时（超过 60 秒），请检查是否传入超大文件。", stopwatch.ElapsedMilliseconds);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Jint 层异常（语法/内存/超时等）也当成格式化错误展示
            return new FormatResult(false, $"格式化失败：{ex.Message}", stopwatch.ElapsedMilliseconds);
        }

        string? error = _engine.Evaluate("globalThis.__err === undefined ? null : globalThis.__err").ToObject() as string;
        if (error is not null)
        {
            return new FormatResult(false, error, stopwatch.ElapsedMilliseconds);
        }

        string output = _engine.Evaluate("globalThis.__out").ToString();
        return new FormatResult(true, output, stopwatch.ElapsedMilliseconds);
    }
}

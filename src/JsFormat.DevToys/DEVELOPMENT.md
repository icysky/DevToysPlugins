# JsFormat.DevToys — 开发文档

> 本文件只存在于源码仓库，**不会**随 nupkg 发布（打包只带 `README.md`）。

## 为什么是 Jint + Prettier

需求是「输出与 Prettier 一致 + 支持 TypeScript / TSX」。.NET 侧没有能对齐 Prettier 的格式化器
（自研 printer 折行逻辑差异明显，格式化场景不可接受），所以直接把 **Prettier 3 的 UMD 构建**
塞进 **Jint**（纯托管 JS 解释器）里执行。

实测过的备选：

| 方案 | 结论 |
| --- | --- |
| Acornima（ES2024 + JSX 解析）+ 自研 printer | 不支持 TS；折行与 Prettier 有差异，放弃 |
| ClearScript.V8 | 输出最准，但要带 ~30MB 原生库（按 RID），违背纯托管约定 |
| 调用本机 node / npx prettier | 依赖用户环境，插件不自包含 |
| **Jint 4.17 + prettier 3.9.10 standalone** | **采用**：纯托管、离线、输出即 Prettier |

## 内嵌资源

`Assets/prettier/` 下四个文件来自 jsDelivr 的官方构建（MIT）：

```
standalone.js   80 KB   03     prettier 主体（UMD，挂到 globalThis.prettier）
estree.js      207 KB   解析器的公共 part
typescript.js  880 KB   TS / TSX 解析（也能解析大部分 JS）
babel.js       311 KB   JS + JSX 解析
```

升级 Prettier：

```powershell
$d = 'src\JsFormat.DevToys\Assets\prettier'
Invoke-WebRequest https://cdn.jsdelivr.net/npm/prettier@3/standalone.js      -OutFile "$d\standalone.js"
Invoke-WebRequest https://cdn.jsdelivr.net/npm/prettier@3/plugins/estree.js  -OutFile "$d\estree.js"
Invoke-WebRequest https://cdn.jsdelivr.net/npm/prettier@3/plugins/typescript.js -OutFile "$d\typescript.js"
Invoke-WebRequest https://cdn.jsdelivr.net/npm/prettier@3/plugins/babel.js   -OutFile "$d\babel.js"
```

csproj 里用固定 LogicalName 嵌入：
`JsFormat.DevToys.Assets.prettier.%(Filename)%(Extension)`。
**冒烟工程要 `LogicalName` 写到一模一样**，否则插件能跑、测试报"资源缺失"。

## 三个必须知道的约定

1. **Prettier 3 的 `format` 是异步的**，直接 `Evaluate` 拿到的是 `[object Promise]`。
   现在的做法：`format(...).then(ok, err)` 把结果/错误写进 `globalThis.__out` / `__err`，
   然后循环 `engine.Advanced.ProcessTasks()` 直到回调落地（`PrettierHost.FormatCore`）。
2. **Jint 引擎不是线程安全的**，单例 + `SemaphoreSlim` 串行化；所有 JS 执行都包在 `Task.Run` 里，
   绝不占 UI 线程。加载只做一次（约 0.45 s），由 `Lazy<Task<PrettierHost>>` 保证。
3. **错误文本直接给用户**：Prettier 抛的 `e.message` 自带 `行:列` 与代码框，
   不需要自己再算位置；`FormatResult.Success=false` 时 UI 把它写进输出框。

## 性能（本机 Jint 解释执行）

| 场景 | 耗时 |
| --- | --- |
| 加载 4 个 bundle | ~0.45 s（一次） |
| 十几行 TS | 30–130 ms |
| 53 KB / 200 行×3 | ~4.9 s |
| 111 KB | 数秒 |

大文件慢是解释执行的固有代价，所以 UI 采用**点按钮触发**（不是边输入边格式化），
并在状态栏显示耗时，让用户自己判断值不值得。

## 构建与验证

```powershell
dotnet build .\src\JsFormat.DevToys\JsFormat.DevToys.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\tools\JsFormat.Smoke\run.ps1      # 20 项
powershell -ExecutionPolicy Bypass -File .\src\JsFormat.DevToys\pack.ps1 -Install
```

冒烟覆盖：版本可读、TS 输出逐字符等于预期、TSX/JSX、babel 解析器、选项透传
（semi / tabWidth / singleQuote / endOfLine）、JSON、幂等性、语法错误文本、53 KB 大文件。

## 打包与发布

`pack.ps1` 会校验 `lib/<tfm>/` 里有 `JsFormat.DevToys.dll` + `Jint.dll` + `Acornima.dll`，
且**不得**含 `DevToys.Api.dll` / `System.ComponentModel.Composition.dll`（宿主提供）。

**该插件不进 nuget**：`.github/workflows/publish.yml` 的打包列表刻意不含它
（与 CodeFormatter 类扩展功能重复），CI 只跑它的冒烟测试。

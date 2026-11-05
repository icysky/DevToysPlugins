# JsFormat.DevToys

DevToys 2.0 扩展：**用内嵌的 Prettier 格式化 JavaScript / TypeScript / TSX / JSX / JSON**。

> 本插件仅自用，**不发布到 nuget.org**（与现有 CodeFormatter 类扩展功能重复）。
> 需要时用 `pack.ps1 -Install` 装到本机即可。

## 功能

- **内嵌 Prettier 3**（standalone + estree/typescript/babel），输出与官方 Prettier **完全一致**，离线可用，不需要装 Node
- 支持 **TypeScript / TSX / JSX / JavaScript / JSON**
- **点击「格式化」才执行**（不做边输入边格式化），完成后状态栏显示 `字符数 → 字符数 · 耗时 · Prettier 版本`
- **出错信息直接显示在输出框**（含 Prettier 的 `行:列` 与代码框定位），不用去翻日志
- 选项持久化：下次打开沿用上次的语法、宽度、缩进、引号等设置
- 输出只读，一键复制

## 选项

| 选项 | 取值 | 默认 | 说明 |
| --- | --- | --- | --- |
| 语法 | TypeScript / TSX · JavaScript / JSX · JSON | TypeScript / TSX | `typescript` 也能直接格式化普通 JS；纯 JS + JSX 用 `babel` |
| printWidth | 40 ~ 200 | 80 | 折行阈值 |
| tabWidth | 1 ~ 8 | 2 | 缩进宽度（空格数） |
| 使用 Tab 缩进 | 开 / 关 | 关 | 对应 `useTabs` |
| 句尾分号 | 开 / 关 | 开 | 关闭时 Prettier 会自动补必要的前缀分号 |
| 单引号 | 开 / 关 | 关 | 仍遵循「最少转义」原则 |
| 尾逗号 | all / es5 / none | all | 多行时是否加尾逗号 |
| 箭头函数括号 | always / avoid | always | `(x) =>` 还是 `x =>` |
| 对象 key 引号 | as-needed / consistent / preserve | as-needed | 何时需要给对象 key 加引号 |
| 大括号内空格 | 开 / 关 | 开 | `{ a: 1 }` 还是 `{a: 1}` |
| 换行符 | LF / CRLF / auto | LF | 输出文件的行尾 |

## 用法

1. 左侧粘贴/输入代码（或先选好语法）
2. 右侧点 **格式化**
3. 结果在右侧输出框；若代码有语法错误，输出框直接显示类似：

```
Expression expected. (1:11)
> 1 | const a = ;
    |           ^
  2 | const b = 2;
```

4. **复制输出** 取走结果，**清空** 重置输入/输出

首次格式化会加载内嵌的 Prettier（约 0.5 秒，进程内只加载一次）；之后单次格式化通常在 10~150 ms。

## 限制（重要）

插件用 Jint（解释执行）跑 Prettier，**压缩成单行的代码**会让 Prettier 为整行构建巨大的排版树，
虚拟机的累计内存分配会轻易超过 8 GB，继续跑只会把 DevToys 卡死甚至崩溃。所以有两道硬限制：

| 限制 | 阈值 | 触发后的行为 |
| --- | --- | --- |
| 输入总长度 | 300 KB | 直接拒绝，不进入引擎 |
| 最长单行 | 4,000 字符 | 直接拒绝（判定为压缩代码） |

触发时输出框会给出明确原因和替代命令。**超过这个量级请用本地 node**，输出与插件完全一致：

```bash
npx prettier@3 --write "你的文件.js"
npx prettier@3 --parser typescript --write "你的文件.ts"
```

> 实测：118 KB 的单行文件在 8 GB 预算下能格式化但需要约 13 秒（进程峰值内存约 214 MB）；
> 306 KB 的单行文件会超出预算，因此被拦下。

## 性能参考

| 场景 | 耗时 |
| --- | --- |
| 首次加载 4 个 bundle | ~0.45 s |
| 十几行 TS | ~30–130 ms |
| 111 KB / 1201 行 TS | 秒级（后台线程执行，不卡界面） |

## 关于本插件

- 纯托管：`Jint`（JS 解释器）+ `Acornima`（其解析器）+ Prettier 的 UMD 构建，全部随插件分发，宿主机不需要 Node
- 第三方许可：Prettier（MIT）、Jint（BSD-2-Clause）、Acornima（MIT），全文随包发布在 `licenses/` 目录，详见 `THIRD-PARTY-NOTICES.md`

# 第三方组件声明（Third-Party Notices）

本仓库的插件代码采用 **0BSD** 许可（见 [LICENSE](LICENSE)）。
下表列出的第三方组件仍遵循各自原有许可证，不受本仓库许可影响；
随包分发其二进制文件的插件，会把对应许可证全文一并打进 nupkg 的 `licenses/` 目录。

## MyCert.DevToys

| 组件 | 版本 | 许可证 | 说明 |
| --- | --- | --- | --- |
| BouncyCastle.Cryptography | 2.7.0 | MIT | SM2 证书生成与解析；**随包分发** `BouncyCastle.Cryptography.dll` |

许可证全文：[`licenses/BouncyCastle-MIT.md`](licenses/BouncyCastle-MIT.md)

## GoogleAuthenticator.DevToys

| 组件 | 版本 | 许可证 | 说明 |
| --- | --- | --- | --- |
| ZXing.Net | 0.16.11 | Apache-2.0 | 二维码识别；**随包分发** `zxing.dll` |
| SixLabors.ImageSharp | 3.1.12 | Six Labors Split License | 仅编译引用，由 DevToys 宿主提供，**不随包分发** |

许可证全文：[`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt)

## GmCrypto.DevToys

无第三方运行时依赖：SM2 / SM3 / SM4 为纯托管实现（源码内联自作者自有的 GM 桌面程序）。

## ChineseIpsum.DevToys

无第三方运行时依赖：语料为四大名著公版节选，全部内嵌为资源。

## JsFormat.DevToys

| 组件 | 版本 | 许可证 | 说明 |
| --- | --- | --- | --- |
| Prettier | 3.9.10 | MIT | 格式化引擎，以 standalone + estree/typescript/babel 的 UMD 构建**内嵌**进插件 dll |
| Jint | 4.17.0 | BSD-2-Clause | 纯托管 JS 解释器，用于执行内嵌的 Prettier；**随包分发** `Jint.dll` |
| Acornima | 1.7.0 | BSD-3-Clause | Jint 的 JS 解析器；**随包分发** `Acornima.dll` |

许可证全文：[`licenses/Prettier-MIT.txt`](licenses/Prettier-MIT.txt)、
[`licenses/Jint-BSD-2.txt`](licenses/Jint-BSD-2.txt)、
[`licenses/Acornima-BSD-3.txt`](licenses/Acornima-BSD-3.txt)

> 该插件仅自用，不发布到 nuget.org。

## Cron.DevToys

无第三方运行时依赖：Cron 字段解析、下次执行时间推算与中文描述均为本仓库自研的纯托管实现，
除宿主提供的 `DevToys.Api` 外不引用任何包。

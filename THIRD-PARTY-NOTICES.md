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

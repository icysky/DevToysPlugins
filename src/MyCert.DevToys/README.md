# MyCert.DevToys

MyCert 的 DevToys 扩展：在 [DevToys](https://devtoys.app/) 中生成测试用 X.509 证书。

NuGet 包：<https://www.nuget.org/packages/MyCert.DevToys>　·　安装：DevToys → 设置 → 扩展 → 搜索「证书」或 `SM2`。

## 功能

- **RSA** 2048 / 3072 / 4096（SHA-256）与 **SM2**（sm2p256v1 + SM3withSM2，国密）
- 自签 CA + 终端证书，CA 提前 1 天生效、有效期延长 3 年
- 用途预设：HTTPS 服务器 / 客户端、电子签名 / PDF、代码签名、时间戳、OFD 签名 / 印章（Document Signing + Any Purpose）
- 双列布局：左侧生成参数 / Subject / Issuer，右侧用途扩展 / 导出
- Subject / Issuer 六项 DN（CN、O、OU、C、ST、L），国家/地区 C 使用 ISO 3166-1 下拉，默认 CN
- KU / EKU 勾选式开关（9 个 KU、8 个 EKU，含 critical），另支持自定义 EKU OID
- 可选 SAN（域名 / IP）
- 导出 PFX、CER、PEM + KEY、颁发者 CA 证书，并写入 `ReadMe_<证书基名>.txt`（记录各文件用途与 PFX 密码）
- 「解析证书」按钮：选择 CER/CRT/PEM/PFX/P12，PFX 会弹出密码输入框；解析结果显示 DN、有效期、序列号、SHA-1、签名算法、公钥/曲线、CA、KU/EKU/SAN
- 自动记住上次填写的全部参数（含 PFX 密码），下次打开自动恢复；数据保存在 DevToys 的 `settings.ini` 中
- 与 MyCert WPF 版共用同一份生成实现（`../TestCertGenerator/CertGen`），参数与产物一致

## 安装根证书（测试环境必读）

本工具生成的是**自签 CA + 由它签发的终端证书**。只把终端证书给别人、或只导入终端证书，
验证方仍然会因为"不认识签发者"而报「证书链不可信」。正确做法是把**颁发者 CA** 导入受信任根。

### 该导入哪个文件

| 文件 | 内容 | 用途 |
| --- | --- | --- |
| `<基名>_issuer_ca.cer` | 颁发者 CA 的证书（仅公钥） | **导入「受信任的根证书颁发机构」← 就装这个** |
| `<基名>.cer` / `.pem` | 终端证书（仅公钥） | 交给对方验证签名/建立链，一般无需手动导入 |
| `<基名>.pfx` | 终端证书 + 私钥 | 服务端（IIS / Kestrel）、签章工具加载 |
| `<基名>.key` | 私钥 PEM（PKCS#8） | 与 `.pem` 配套给 OpenSSL / Java 等工具 |

### Windows：图形界面

1. 双击 `<基名>_issuer_ca.cer` → **安装证书**
2. 存储位置：**当前用户**（推荐，无需管理员）或 **本地计算机**（影响全机，需管理员）
3. 选 **将所有证书放入下列存储** → 浏览 → **受信任的根证书颁发机构**
4. 完成 → 弹出"是否要安装此证书"安全警告 → **是**

> 第 3 步不要用默认的「根据证书类型自动选择存储」，那样可能落到「中间证书颁发机构」，链验证仍会失败。

### Windows：命令行（可脚本化）

```powershell
# 当前用户（推荐）
certutil -addstore -user Root ".\MyCert_20261029_120000_issuer_ca.cer"

# 本地计算机（需管理员）
certutil -addstore Root ".\MyCert_20261029_120000_issuer_ca.cer"

# 或者用 cmdlet
Import-Certificate -FilePath ".\MyCert_20261029_120000_issuer_ca.cer" -CertStoreLocation Cert:\CurrentUser\Root
```

### 验证是否装好

```powershell
# 1) 根证书已在列表里
Get-ChildItem Cert:\CurrentUser\Root |
    Where-Object { $_.Subject -like "*HTTP Server Root CA*" } |
    Format-List Subject, Thumbprint, NotAfter

# 2) 终端证书能验到这条链
certutil -verify ".\MyCert_20261029_120000.cer"
```

`certutil -verify` 输出里出现「证书链已正确安装」/ `The certificate chain was issued correctly` 即成功。

### 卸载 / 清理

```powershell
# 按主题筛选删除
Get-ChildItem Cert:\CurrentUser\Root | Where-Object { $_.Subject -like "*Root CA*" } | Remove-Item -Force

# 或按指纹删除
certutil -delstore -user Root <指纹>
```

### 各场景要点

- **HTTPS 服务端测试**（IIS / Kestrel）：根证书装「受信任的根」；站点/服务绑定用 `.pfx`（含私钥）
- **OFD / PDF 签章验证**：验签方需要根证书在受信任根里，否则提示「证书链不可信」
- **代码签名**：终端证书装「受信任的发布者」，根证书装「受信任的根证书颁发机构」，两者都要
- **重新生成终端证书**：只要仍用同一张 CA，就不必重装根证书
- **SM2（国密）证书**：Windows 的证书栈不支持 SM2 证书链校验，导入系统根一般无效，
  需要在目标工具（如 OFD 签章软件）自己的信任列表里导入 `_issuer_ca.cer`

### macOS / Linux

- **macOS**：钥匙串访问 → 导入 `_issuer_ca.cer` 到「系统」→ 双击 → 展开「信任」→ 用此证书时选**始终信任**
- **Debian / Ubuntu / 麒麟（apt 系）**：
  ```bash
  sudo cp xxx_issuer_ca.cer /usr/local/share/ca-certificates/xxx.crt
  sudo update-ca-certificates
  ```
- **RHEL / CentOS（rpm 系）**：
  ```bash
  sudo cp xxx_issuer_ca.cer /etc/pki/ca-trust/source/anchors/xxx.crt
  sudo update-ca-trust
  ```

### 安全提醒

- 这些证书**仅供测试**，自签 CA 的私钥没有任何保护，**绝不要用于生产环境**
- 测试完成后建议按上面的「卸载」把根证书删掉，避免机器长期信任一张弱根
- `.pfx` 与 `.key` 含私钥，不要提交到代码仓库或放到公共目录

## 使用提示

1. 选择密钥算法与用途预设（默认「OFD 签名 / 印章」+ RSA 2048）。
2. 填写 Subject（CN 必填）与 Issuer。
3. 按需启用 SAN、调整 KU / EKU。
4. 选择导出目录（输入框全宽，右侧「浏览...」）、PFX 密码（右侧「生成密码」一键生成 12 位随机安全密码）与导出格式，点击「生成证书」。
5. 生成结果写入所选目录，日志与成功/失败提示显示在页面底部。

SM2 证书使用 `sm2p256v1` 命名曲线与 SM3withSM2，可直接用于 ofdrw / ofdrwNet 的 OFD 签名与电子印章；
Windows 证书存储不支持 SM2，PFX 无需也无法导入系统证书库。

## 关于本插件

- 无第三方运行时依赖；SM2 证书生成使用随包附带的 `BouncyCastle.Cryptography`（MIT），
  许可证全文随包发布在 `licenses/` 目录，第三方组件声明见 `THIRD-PARTY-NOTICES.md`。

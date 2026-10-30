# GoogleAuthenticator.DevToys

DevToys 扩展：**Google Authenticator 风格的动态口令（TOTP / HOTP）管理器**。

NuGet 包：<https://www.nuget.org/packages/GoogleAuthenticator.DevToys>　·　安装：DevToys → 设置 → 扩展 → 搜索「动态口令」或 `TOTP`。

## 功能

- **三种添加方式**
  - 粘贴字符串：`otpauth://totp/...` 链接、Base32 密钥，或 Google Authenticator 的 `otpauth-migration://offline?data=...` 转移链接（可一次导入多个账号）
  - 扫码：从剪贴板读取二维码截图（`Win + Shift + S` 截图后直接识别），或选择一张二维码图片
  - 手动录入：类型、发行方、账号、密钥、位数、算法、周期 / 计数器
- **实时验证码**：每 400ms 刷新，按 Google Authenticator 的习惯分组显示（`123 456`），带圆形倒计时与剩余秒数
- **账号持久化**：每次增删改立即落盘；Windows 下用 **DPAPI** 加密（绑定当前用户，无需额外密码）
- **一键导出 / 导入**
  - 导出文件包含全部账号核心信息：发行方、账号、密钥、类型、位数、周期、算法、计数器、创建时间
  - 导出时密码可留空（不加密）；填密码则 AES-256-CBC + HMAC-SHA256（PBKDF2-HMAC-SHA256，210000 轮）
  - 导入时**自动识别文件是否需要密码**，需要时弹窗要求输入，密码错误可重试
  - 也支持直接导入纯文本的 `otpauth://` 链接清单
- **编辑 / 删除 / 复制**，HOTP 账号支持「下一位」推进计数器

## 关于 6~12 位验证码（重要）

位数（`digits`）**不是验证方随意选的，而是创建方在发放密钥时决定的参数**，写在 `otpauth://` 链接的 `digits` 里（默认 6）。
验证方必须使用完全相同的 `digits`，否则验证码永远对不上 —— 所以本质上是「创建方决定 + 双方必须一致」。

标准约束：

| 位数 | 说明 |
| --- | --- |
| 6 ~ 8 | RFC 4226 / RFC 6238 **标准范围**，使用 31 位动态截断。与 Google Authenticator、Microsoft Authenticator、1Password 等互通 |
| 9 ~ 12 | **超出 RFC 标准**。31 位动态截断最多只能安全表达 9 位十进制，所以 9 位已属边界，10 位以上标准算法根本无法产生 |

本插件为了满足 6~12 位的需求：

- **6~8 位**：严格走 RFC 4226 §5.4 动态截断，算法与 Google Authenticator 完全一致（已用 RFC 4226 / RFC 6238 官方测试向量验证，SHA1 / SHA256 / SHA512 全通过）。
- **9~12 位**：改用**扩展截断** —— 把整段 HMAC 结果当作大端无符号大整数，再 `mod 10^digits`。

你自己的验证端要支持 9~12 位时，必须实现同一套扩展截断，参考实现：

```csharp
static string Truncate(byte[] hmac, int digits)
{
    if (digits <= 9)   // 标准 RFC 4226 动态截断
    {
        int offset = hmac[^1] & 0x0F;
        long bin = ((long)(hmac[offset] & 0x7F) << 24)
                 | ((long)hmac[offset + 1] << 16)
                 | ((long)hmac[offset + 2] << 8)
                 | hmac[offset + 3];
        return (bin % (long)Math.Pow(10, digits)).ToString().PadLeft(digits, '0');
    }

    // 扩展截断（9~12 位）
    var big = new System.Numerics.BigInteger(hmac, isUnsigned: true, isBigEndian: true);
    var mod = System.Numerics.BigInteger.Pow(10, digits);
    return (big % mod).ToString().PadLeft(digits, '0');
}
```

其余参数同样是「创建方参数、双方一致」：

- `algorithm`：SHA1（默认）/ SHA256 / SHA512
- `period`：默认 30 秒
- `type`：TOTP（按时间）或 HOTP（按计数器，需要 `counter`）

## 数据文件

| 内容 | 位置 | 说明 |
| --- | --- | --- |
| 本地账号 | `%LOCALAPPDATA%\GoogleAuthenticator.DevToys\accounts.dat` | `GALT` 头 + DPAPI 加密的 JSON |
| 导出备份 | 由你在保存对话框中选择，扩展名 `.gauth` | 可选密码加密 |

备份文件格式（`.gauth`）：

```
"GAEX" | version(1) | flags(1) | 负载
flags bit0 = 0：负载 = UTF-8 JSON
flags bit0 = 1：负载 = salt(16) | iv(16) | ciphertext | hmac-sha256(32)
               密钥 = PBKDF2-HMAC-SHA256(密码, salt, 210000, 64)
               → 前 32 字节 AES-256-CBC 密钥，后 32 字节 HMAC 密钥
               HMAC 覆盖 头部 + salt + iv + ciphertext
```

## 注意事项

- 本插件不是 DevToys 官方扩展；`.gauth` 是本插件自定义格式，不能直接被 Google Authenticator 导入。
  但导入端支持 Google Authenticator 的 `otpauth-migration://` 转移二维码，可用于**从 GA 迁移过来**。
- 不要把 `accounts.dat` 和导出的备份文件放在不安全的位置；DPAPI 只能防止其他 Windows 用户读取，不能防止本用户被入侵后读取。

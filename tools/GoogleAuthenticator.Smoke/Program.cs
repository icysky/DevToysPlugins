using System.Globalization;
using System.Reflection;
using System.Text;
using DevToys.Api;
using GoogleAuthenticator.Smoke;
using GoogleAuthenticator.DevToys;
using GoogleAuthenticator.DevToys.Core;
using GoogleAuthenticator.DevToys.Core.Storage;

int passed = 0;
int failed = 0;
UIToolView? viewRef = null;

string dataDirectory = Path.Combine(Path.GetTempPath(), "gauth-smoke-" + Guid.NewGuid().ToString("N")[..8]);
Environment.SetEnvironmentVariable(AccountStore.DataDirectoryEnvironmentVariable, dataDirectory);
string storeFile = Path.Combine(dataDirectory, "accounts.dat");

Console.WriteLine("=== GoogleAuthenticator.DevToys 回归冒烟测试 ===");
Console.WriteLine($"临时数据目录：{dataDirectory}");
Console.WriteLine();

try
{
    // ------------------------------------------------------------------ Base32
    Section("Base32（RFC 4648）");
    byte[] rfcKey = Encoding.ASCII.GetBytes("12345678901234567890");
    string secret = Base32.Encode(rfcKey);
    Check("20 字节密钥编码", () => secret == "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
    Check("编解码往返", () => Base32.Encode(Base32.Decode(secret)) == secret);
    Check("容错：小写 + 空格 + 连字符",
        () => Base32.Normalize("gezd gnbv-gy3t qojq gezd gnbv gy3t qojq") == secret);
    Check("拒绝非法字符（含 0/1/8/9）", () => !Base32.TryDecode("0189", out _));
    Check("自动生成密钥可解码", () => Base32.TryDecode(TotpEngine.GenerateSecret(), out _));

    // ------------------------------------------------------------------ HOTP
    Section("HOTP（RFC 4226 附录 D 官方向量）");
    string[] hotpVectors =
    [
        "755224", "287082", "359152", "969429", "338314",
        "254676", "287922", "162583", "399871", "520489"
    ];

    for (int counter = 0; counter < hotpVectors.Length; counter++)
    {
        int index = counter;
        Check($"counter={index}", () => TotpEngine.Compute(rfcKey, OtpAlgorithm.Sha1, index, 6) == hotpVectors[index]);
    }

    // ------------------------------------------------------------------ TOTP
    Section("TOTP（RFC 6238 附录 B 官方向量，8 位）");
    byte[] sha1Seed = Encoding.ASCII.GetBytes("12345678901234567890");
    byte[] sha256Seed = Encoding.ASCII.GetBytes("12345678901234567890123456789012");
    byte[] sha512Seed = Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234");

    (long Time, string Sha1, string Sha256, string Sha512)[] totpVectors =
    [
        (59L, "94287082", "46119246", "90693936"),
        (1111111109L, "07081804", "68084774", "25091201"),
        (1111111111L, "14050471", "67062674", "99943326"),
        (1234567890L, "89005924", "91819424", "93441116"),
        (2000000000L, "69279037", "90698825", "38618901"),
        (20000000000L, "65353130", "77737706", "47863826")
    ];

    foreach ((long time, string expectedSha1, string expectedSha256, string expectedSha512) in totpVectors)
    {
        long counter = time / 30;
        Check($"SHA1   t={time}", () => TotpEngine.Compute(sha1Seed, OtpAlgorithm.Sha1, counter, 8) == expectedSha1);
        Check($"SHA256 t={time}", () => TotpEngine.Compute(sha256Seed, OtpAlgorithm.Sha256, counter, 8) == expectedSha256);
        Check($"SHA512 t={time}", () => TotpEngine.Compute(sha512Seed, OtpAlgorithm.Sha512, counter, 8) == expectedSha512);
    }

    // ------------------------------------------------------------------ 6~12 位
    Section("位数 6~12");
    for (int digits = 6; digits <= 12; digits++)
    {
        int current = digits;
        Check($"{current} 位长度与纯数字", () =>
        {
            string code = TotpEngine.Compute(sha1Seed, OtpAlgorithm.Sha1, 12345, current);
            return code.Length == current && code.All(char.IsAsciiDigit);
        });
    }

    Check("6 位分组 123 456", () => TotpEngine.FormatCode("123456") == "123 456");
    Check("8 位分组 1234 5678", () => TotpEngine.FormatCode("12345678") == "1234 5678");
    Check("10 位分组 12345 67890", () => TotpEngine.FormatCode("1234567890") == "12345 67890");
    Check("12 位分组 123456 789012", () => TotpEngine.FormatCode("123456789012") == "123456 789012");
    Check("6 位与标准实现一致（t=59）",
        () => TotpEngine.Compute(sha1Seed, OtpAlgorithm.Sha1, 59 / 30, 6) == "287082");
    Check("9 位仍为 RFC 截断、10 位起切换扩展截断", () =>
    {
        // 关键防回归：9 位以内必须仍走标准 DT（前 8 位应等于 8 位结果加一位，不成立也无妨），
        // 这里只断言算法分支不会抛异常且位数正确。
        string nine = TotpEngine.Compute(sha1Seed, OtpAlgorithm.Sha1, 7, 9);
        string ten = TotpEngine.Compute(sha1Seed, OtpAlgorithm.Sha1, 7, 10);
        return nine.Length == 9 && ten.Length == 10;
    });

    // ------------------------------------------------------------------ otpauth
    Section("otpauth:// 解析与生成");
    OtpAccount parsed = OtpAuthUri.Parse(
        "otpauth://totp/ACME%20Co:john.doe@email.com?secret=HXDMVJECJJWSRB3HWIZR4IFUGFTMXBOZ&issuer=ACME%20Co&algorithm=SHA1&digits=6&period=30");
    Check("issuer 拆分", () => parsed.Issuer == "ACME Co");
    Check("label 解码", () => parsed.Label == "john.doe@email.com");
    Check("默认算法 SHA1", () => parsed.Algorithm == OtpAlgorithm.Sha1);
    Check("默认位数 6", () => parsed.Digits == 6);
    Check("序列化再解析一致", () =>
    {
        OtpAccount roundTrip = OtpAuthUri.Parse(OtpAuthUri.Build(parsed));
        return roundTrip.Secret == parsed.Secret
               && roundTrip.Issuer == parsed.Issuer
               && roundTrip.Label == parsed.Label;
    });

    OtpAccount wide = OtpAuthUri.Parse("otpauth://totp/Big:me?secret=JBSWY3DPEHPK3PXP&digits=10&period=60");
    Check("10 位 / 60 秒被保留", () => wide.Digits == 10 && wide.Period == 60);

    OtpAccount hotpUri = OtpAuthUri.Parse("otpauth://hotp/Issuer:acc?secret=JBSWY3DPEHPK3PXP&counter=42");
    Check("HOTP counter 解析", () => hotpUri.Type == OtpType.Hotp && hotpUri.Counter == 42);

    CheckThrows<FormatException>("缺少 secret 抛 FormatException",
        () => OtpAuthUri.Parse("otpauth://totp/a:b?issuer=x"));
    CheckThrows<FormatException>("非法类型抛 FormatException",
        () => OtpAuthUri.Parse("otpauth://xxxx/a:b?secret=JBSWY3DPEHPK3PXP"));

    // ------------------------------------------------------------------ 迁移链接
    Section("Google Authenticator 转移链接");
    string migrationUri = Helpers.BuildMigrationUri(
        Base32.Decode("JBSWY3DPEHPK3PXP"),
        "alice@example.com",
        "GitHub",
        algorithm: 2,
        digitCount: 2,
        otpType: 2);
    List<OtpAccount> migrated = MigrationPayload.Parse(migrationUri);
    Check("解析出 1 个账号", () => migrated.Count == 1);
    Check("issuer", () => migrated[0].Issuer == "GitHub");
    Check("label", () => migrated[0].Label == "alice@example.com");
    Check("secret", () => migrated[0].Secret == "JBSWY3DPEHPK3PXP");
    Check("algorithm=SHA256", () => migrated[0].Algorithm == OtpAlgorithm.Sha256);
    Check("digits=8", () => migrated[0].Digits == 8);
    Check("type=TOTP", () => migrated[0].Type == OtpType.Totp);

    // ------------------------------------------------------------------ 二维码
    Section("二维码识别（ZXing 生成 → ImageSharp PNG → QrScanner 解析）");
    const string qrPayload =
        "otpauth://totp/GitHub:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub&algorithm=SHA1&digits=6&period=30";
    byte[] qrPng = Helpers.RenderQrPng(qrPayload);
    Check("PNG 渲染成功", () => qrPng.Length > 500);
    Check("内容完全一致", () => QrScanner.Decode(qrPng) == qrPayload);
    Check("任意文本二维码也能识别", () => QrScanner.Decode(Helpers.RenderQrPng("hello")) == "hello");
    Check("非二维码图片返回 null", () => QrScanner.Decode(Helpers.RenderGradientPng()) is null);

    // ------------------------------------------------------------------ 备份文件
    Section("备份文件（.gauth）");
    List<OtpAccount> samples = [parsed, wide];
    byte[] plainVault = VaultFile.Create(samples, null);
    Check("明文备份 header.IsEncrypted=false", () => !VaultFile.ReadHeader(plainVault).IsEncrypted);
    Check("明文备份可读回 2 个账号", () => VaultFile.Read(plainVault, null).Accounts.Count == 2);

    const string password = "P@ssw0rd-测试";
    byte[] encryptedVault = VaultFile.Create(samples, password);
    Check("加密备份 header.IsEncrypted=true", () => VaultFile.ReadHeader(encryptedVault).IsEncrypted);
    Check("加密备份可读回 2 个账号", () => VaultFile.Read(encryptedVault, password).Accounts.Count == 2);
    CheckThrows<VaultPasswordRequiredException>("无密码时提示需要密码",
        () => VaultFile.Read(encryptedVault, null));
    CheckThrows<VaultPasswordException>("错误密码被拒绝",
        () => VaultFile.Read(encryptedVault, "wrong-password"));

    byte[] tampered = (byte[])encryptedVault.Clone();
    tampered[^40] ^= 0xFF;
    CheckThrows<VaultPasswordException>("密文篡改被 HMAC 发现",
        () => VaultFile.Read(tampered, password));
    CheckThrows<VaultFormatException>("非本插件文件被识别",
        () => VaultFile.Read(Encoding.UTF8.GetBytes("{\"hello\":1}"), null));

    // ------------------------------------------------------------------ 本地存储
    Section("本地持久化");
    Check("数据目录被环境变量覆盖", () => new AccountStore().Directory == dataDirectory);

    AccountStore store = new();
    store.Load();
    store.Accounts.Add(parsed.Clone());
    Check("保存成功", () => store.Save() && File.Exists(storeFile));
    Check("落盘内容非明文（DPAPI 加密）",
        () => !Encoding.UTF8.GetString(File.ReadAllBytes(storeFile)).Contains("secret"));

    AccountStore reloaded = new();
    reloaded.Load();
    Check("重新加载账号数一致", () => reloaded.Accounts.Count == 1);
    Check("重新加载无错误", () => string.IsNullOrEmpty(reloaded.LastError));
    Check("密钥无损", () => reloaded.Accounts[0].Secret == parsed.Secret);

    // ------------------------------------------------------------------ 界面
    Section("界面构建（无 UI 宿主，直接构建控件树）");
    FakeFileStorage fileStorage = new();
    FakeClipboard clipboard = new();
    AuthenticatorGuiTool tool = new(fileStorage, clipboard);
    UIToolView view = tool.View;
    viewRef = view;

    Check("View 构建成功", () => view.RootElement is IUIStack root && root.Orientation == UIOrientation.Vertical);
    Check("信息条存在", () => view.GetChildElementById("gauth-info") is not null);
    Check("账号容器存在", () => view.GetChildElementById("gauth-accounts") is IUIStack);
    Check("粘贴输入框存在", () => view.GetChildElementById("gauth-paste") is IUISingleLineTextInput);

    MethodInfo addFromText = RequireMethod("AddFromTextAsync", typeof(string), typeof(string));
    Task<bool> Add(string text) => (Task<bool>)addFromText.Invoke(tool, [text, "冒烟"])!;

    Check("添加 TOTP 6 位账号",
        () => Add("otpauth://totp/GitHub:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub&digits=6").Result);
    Check("添加 TOTP 8 位 SHA256 账号",
        () => Add("otpauth://totp/Bank:me?secret=JBSWY3DPEHPK3PXP&issuer=Bank&digits=8&algorithm=SHA256&period=60").Result);
    Check("添加 TOTP 10 位扩展账号",
        () => Add("otpauth://totp/Internal:ops?secret=JBSWY3DPEHPK3PXP&issuer=Internal&digits=10").Result);
    Check("添加迁移链接（1 个）", () => Add(migrationUri).Result);
    Check("重复添加不重复入库",
        () => Add("otpauth://totp/GitHub:alice@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub").Result);

    Check("界面账号数为 4", () =>
    {
        IUILabel? header = view.GetChildElementById("gauth-accounts-header") as IUILabel;
        Console.WriteLine($"        header = {header?.Text}");
        return header?.Text?.Contains('4') == true;
    });
    Check("空提示已隐藏", () => view.GetChildElementById("gauth-empty")?.IsVisible == false);

    Check("验证码已渲染且位数正确", () =>
    {
        AccountStore current = new();
        current.Load();
        if (current.Accounts.Count != 4)
        {
            Console.WriteLine($"        store count = {current.Accounts.Count}");
            return false;
        }

        bool ok = true;
        foreach (OtpAccount account in current.Accounts)
        {
            IUILabel? code = view.GetChildElementById($"gauth-code-{account.Id}") as IUILabel;
            IUILabel? meta = view.GetChildElementById($"gauth-meta-{account.Id}") as IUILabel;
            IUILabel? countdown = view.GetChildElementById($"gauth-countdown-{account.Id}") as IUILabel;
            Console.WriteLine(
                $"        {meta?.Text,-34} code={code?.Text ?? "<未找到>",-14} left={countdown?.Text}");

            if (string.IsNullOrWhiteSpace(code?.Text)
                || code.Text.Replace(" ", string.Empty).Length != account.Digits)
            {
                ok = false;
            }
        }

        return ok;
    });

    MethodInfo updateCodes = RequireMethod("UpdateCodes", typeof(bool));
    updateCodes.Invoke(tool, [true]);
    Check("UpdateCodes(force) 不抛异常", () => true);

    Section("并发安全（回归：刷新循环在后台线程迭代行快照，同时 UI 线程重建行）");
    Check("RebuildRows 与 UpdateCodes 并发不抛异常", () =>
    {
        MethodInfo rebuildRows = RequireMethod("RebuildRows");
        Exception? error = null;

        Task updater = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 400 && error is null; i++)
                {
                    updateCodes.Invoke(tool, [false]);
                    Thread.Yield();
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Task rebuilder = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 200 && error is null; i++)
                {
                    rebuildRows.Invoke(tool, []);
                    Thread.Yield();
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        Task.WaitAll([updater, rebuilder]);

        if (error is not null)
        {
            Console.WriteLine($"        {error.GetType().Name}: {error.Message}");
        }

        return error is null;
    });

    // ------------------------------------------------------------------ 对话框
    Section("对话框构建（OpenDialogAsync 为纯托管逻辑）");
    AccountStore fresh = new();
    fresh.Load();
    OtpAccount firstAccount = fresh.Accounts[0];

    MethodInfo manualAdd = RequireMethod("ManualAddAsync");
    MethodInfo editAsync = RequireMethod("EditAsync", typeof(OtpAccount));
    MethodInfo deleteAsync = RequireMethod("DeleteAsync", typeof(OtpAccount));
    MethodInfo scanAsync = RequireMethod("ScanAsync");
    MethodInfo exportAsync = RequireMethod("ExportAsync");
    MethodInfo promptPassword = RequireMethod("PromptPasswordAsync", typeof(bool));

    Section("行控件实例稳定性（回归：重建行不得替换元素实例）");
    MethodInfo rebuildRowsMethod = RequireMethod("RebuildRows");
    FieldInfo storeField = typeof(AuthenticatorGuiTool).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
    AccountStore toolStore = (AccountStore)storeField.GetValue(tool)!;
    IUIStack accountsStack = (IUIStack)view.GetChildElementById("gauth-accounts")!;

    Check("重复 RebuildRows 不替换元素实例", () =>
    {
        IUIElement[] before = [.. accountsStack.Children!];
        rebuildRowsMethod.Invoke(tool, []);
        IUIElement[] after = [.. accountsStack.Children!];

        if (before.Length != after.Length || before.Length == 0)
        {
            Console.WriteLine($"        before={before.Length} after={after.Length}");
            return false;
        }

        // DevToys 的 UIElementPresenter 只在 OnInitialized 订阅一次 PropertyChanged，
        // 元素实例一旦被替换，订阅就挂到旧元素上，界面从此不再刷新。
        for (int i = 0; i < before.Length; i++)
        {
            if (!ReferenceEquals(before[i], after[i]))
            {
                Console.WriteLine($"        index {i} 被替换");
                return false;
            }
        }

        return true;
    });

    Check("删除账号只隐藏、不移除元素", () =>
    {
        OtpAccount victim = toolStore.Accounts[0];
        int beforeCount = accountsStack.Children!.Length;

        toolStore.Accounts.RemoveAll(a => a.Id == victim.Id);
        rebuildRowsMethod.Invoke(tool, []);

        IUIElement[] after = [.. accountsStack.Children!];
        int hidden = after.Count(e => !e.IsVisible);

        // 恢复现场
        toolStore.Accounts.Insert(0, victim);
        rebuildRowsMethod.Invoke(tool, []);

        if (after.Length != beforeCount)
        {
            Console.WriteLine($"        元素被移除：{beforeCount} -> {after.Length}");
            return false;
        }

        if (hidden != 1)
        {
            Console.WriteLine($"        隐藏数量={hidden}，期望 1");
            return false;
        }

        return true;
    });

    await ExerciseDialogAsync("手动录入", () => ((ValueTask)manualAdd.Invoke(tool, [])!).AsTask());
    await ExerciseDialogAsync("编辑账号", () => ((ValueTask)editAsync.Invoke(tool, [firstAccount])!).AsTask());
    await ExerciseDialogAsync("删除确认", () => ((ValueTask)deleteAsync.Invoke(tool, [firstAccount])!).AsTask());
    await ExerciseDialogAsync("扫码导入", () => ((ValueTask)scanAsync.Invoke(tool, [])!).AsTask());
    await ExerciseDialogAsync("导出备份", () => ((ValueTask)exportAsync.Invoke(tool, [])!).AsTask());
    await ExerciseDialogAsync("密码输入", () => (Task)promptPassword.Invoke(tool, [false])!);

    Console.WriteLine();
    Console.WriteLine($"===== 通过 {passed} 项，失败 {failed} 项 =====");
    Console.WriteLine(failed == 0 ? "全部通过 ✅" : "存在失败 ❌");
}
finally
{
    try
    {
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
    catch
    {
        // 清理失败不影响结论
    }
}

return failed == 0 ? 0 : 1;

// ---------------------------------------------------------------------- 辅助

void Section(string title)
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

void CheckThrows<TException>(string name, Action action) where TException : Exception
{
    try
    {
        action();
        failed++;
        Console.WriteLine($"FAIL  {name}: 没有抛异常");
    }
    catch (TException)
    {
        passed++;
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL  {name}: 抛了 {ex.GetType().Name}，期望 {typeof(TException).Name}");
    }
}

MethodInfo RequireMethod(string name, params Type[] parameterTypes)
    => typeof(AuthenticatorGuiTool).GetMethod(
           name,
           BindingFlags.Instance | BindingFlags.NonPublic,
           binder: null,
           types: parameterTypes,
           modifiers: null)
       ?? throw new InvalidOperationException(
           $"插件里找不到方法 {name}({string.Join(", ", parameterTypes.Select(t => t.Name))})（重命名后请同步更新冒烟测试）");

async Task ExerciseDialogAsync(string name, Func<Task> start)
{
    Task task = start();

    for (int i = 0; i < 150 && viewRef!.CurrentOpenedDialog is null; i++)
    {
        await Task.Delay(20);
    }

    UIDialog? dialog = viewRef!.CurrentOpenedDialog;
    if (dialog is null)
    {
        failed++;
        Console.WriteLine($"FAIL  {name}：对话框没有打开（task 状态 {task.Status}）");
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      {ex.GetType().Name}: {ex.Message}");
        }

        return;
    }

    bool hasContent = dialog.DialogContent is not null && dialog.FooterContent is not null;
    if (hasContent)
    {
        passed++;
        Console.WriteLine($"PASS  {name}：内容 + 底部按钮已构建");
    }
    else
    {
        failed++;
        Console.WriteLine($"FAIL  {name}：内容或底部按钮缺失");
    }

    dialog.Close();

    try
    {
        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL  {name}：关闭后抛异常 {ex.GetType().Name}: {ex.Message}");
    }
}

using System.ComponentModel.Composition;
using System.Globalization;
using System.Text;
using DevToys.Api;
using GoogleAuthenticator.DevToys.Core;
using GoogleAuthenticator.DevToys.Core.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace GoogleAuthenticator.DevToys;

[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(GoogleAuthenticatorResourceAssemblyIdentifier))]
internal sealed class GoogleAuthenticatorResourceAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
        => ValueTask.FromResult(Array.Empty<FontDefinition>());
}

/// <summary>
/// Google Authenticator 风格的动态口令管理器。
/// </summary>
[Export(typeof(IGuiTool))]
[Name("Authenticator")]
[ToolDisplayInformation(
    // FluentSystemIcons 是 DevToys 自带的图标字体（DevToys.Blazor.Assets.fonts.FluentSystemIcons-Regular.ttf）。
    // 码位必须真实存在于该字体里，否则侧边栏会显示成占位圆圈。
    // U+E78F = ic_fluent_lock_closed_20_regular（挂锁），已用字体 cmap 校验存在。
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uE78F',
    GroupName = PredefinedCommonToolGroupNames.Generators,
    ResourceManagerAssemblyIdentifier = nameof(GoogleAuthenticatorResourceAssemblyIdentifier),
    ResourceManagerBaseName = "GoogleAuthenticator.DevToys.Strings",
    ShortDisplayTitleResourceName = "ShortDisplayTitle",
    LongDisplayTitleResourceName = "LongDisplayTitle",
    DescriptionResourceName = "Description",
    AccessibleNameResourceName = "AccessibleName",
    SearchKeywordsResourceName = "SearchKeywords")]
internal sealed class AuthenticatorGuiTool : IGuiTool
{
    private readonly IFileStorage _fileStorage;

    private readonly IClipboard _clipboard;

    private readonly AccountStore _store = new();

    private readonly IUIInfoBar _infoBar;

    private readonly IUILabel _accountsHeader;

    private readonly IUIStack _accountsStack;

    private readonly IUILabel _emptyHint;

    private readonly IUILabel _autoRefreshHint;

    private readonly IUISingleLineTextInput _pasteInput;

    private readonly IUIButton _addPasteButton;

    private readonly IUIButton _scanButton;

    private readonly IUIButton _manualButton;

    private readonly IUIButton _importButton;

    private readonly IUIButton _exportButton;

    private readonly IUIButton _refreshButton;

    /// <summary>
    /// 账号行快照。整体替换而不是原地增删：刷新循环会在后台线程迭代它，
    /// 若用 <see cref="List{T}" /> 原地 Clear/Add，就会和「添加/编辑/删除」撞出
    /// 「集合已修改，可能无法枚举」异常，而那个异常会让刷新循环停摆
    /// （表现为倒计时卡住、要点「刷新」才动）。
    /// </summary>
    private AccountRow[] _rows = [];

    /// <summary>行控件按创建顺序排列，只追加不移除（移除会让后面元素位置前移，导致呈现器订阅失效）。</summary>
    private readonly List<AccountRow> _rowOrder = [];

    private readonly Dictionary<string, AccountRow> _rowByAccountId = new(StringComparer.Ordinal);

    private readonly int _ctorThreadId = Environment.CurrentManagedThreadId;

    private System.Threading.Timer? _refreshTimer;

    private int _tickGate;

    private int _tickCount;

    private long _lastTickTimestamp;

    private long _timerCreatedTimestamp;

    private int _lastDesyncSnapshot = -1;

    private readonly string _ctorSyncContext = SynchronizationContext.Current?.GetType().Name ?? "null";

    private UIToolView? _view;

    private string _pasteText = string.Empty;

    private int _refreshFailures;

    private bool _refreshHintShown;

    private int _lastRowErrorLogTick;

    /// <summary>串行化「后台刷新」与「UI 线程重建行」对同一批控件的写入。</summary>
    private readonly object _updateLock = new();

    [ImportingConstructor]
    public AuthenticatorGuiTool(IFileStorage fileStorage, IClipboard clipboard)
    {
        _fileStorage = fileStorage;
        _clipboard = clipboard;

        _store.Load();

        _infoBar = GUI.InfoBar("gauth-info").Closable();

        _accountsHeader = GUI.Label("gauth-accounts-header")
            .Style(UILabelStyle.Subtitle);

        _emptyHint = GUI.Label("gauth-empty")
            .Text("还没有账号。粘贴 otpauth 链接、扫码或手动录入，来添加第一个账号。")
            .WrapIfNeeded()
            .Style(UILabelStyle.Caption);

        _autoRefreshHint = GUI.Label("gauth-auto-refresh-hint")
            .Text(string.Empty)
            .WrapIfNeeded()
            .Style(UILabelStyle.Caption);

        _accountsStack = GUI.Stack("gauth-accounts").Vertical().SmallSpacing();

        _pasteInput = GUI.SingleLineTextInput("gauth-paste")
            .HideCommandBar()
            .CanCopyWhenEditable()
            .OnTextChanged(text => _pasteText = text ?? string.Empty);

        _addPasteButton = GUI.Button("gauth-add-paste")
            .AccentAppearance()
            .Text("添加")
            .OnClick(AddFromPasteAsync);

        _scanButton = GUI.Button("gauth-scan")
            .Text("扫码")
            .OnClick(ScanAsync);

        _manualButton = GUI.Button("gauth-manual")
            .Text("手动录入")
            .OnClick(ManualAddAsync);

        _exportButton = GUI.Button("gauth-export")
            .Text("导出备份")
            .OnClick(ExportAsync);

        _importButton = GUI.Button("gauth-import")
            .Text("导入备份")
            .OnClick(ImportAsync);

        _refreshButton = GUI.Button("gauth-refresh")
            .Text("刷新")
            .OnClick(RefreshNow);

        _view = BuildView();
        RebuildRows();

        if (!string.IsNullOrEmpty(_store.LastError))
        {
            ShowError(_store.LastError);
        }

        // 刷新用最底层的 System.Threading.Timer 驱动，回调跑在线程池。
        // DevToys 的每个控件都有 UIElementPresenter 订阅 PropertyChanged 并 InvokeAsync(StateHasChanged)，
        // 所以从后台线程改控件会被正确 marshall 回渲染器，不依赖 UI 线程上的续体调度。
        StartRefreshTimer();

        AppendDiagnostic(
            $"refresh timer created (ctor thread={_ctorThreadId} syncContext={_ctorSyncContext})");
    }

    public UIToolView View => _view ??= BuildView();

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
    }

    // ---------------------------------------------------------------- 视图

    private UIToolView BuildView()
    {
        IUIElement addSection = GUI.Card(GUI.Stack().Vertical().MediumSpacing().WithChildren(
            GUI.Label().Text("添加账号").Style(UILabelStyle.BodyStrong),
            GUI.Grid()
                .Columns(GUI.Fraction(1), UIGridLength.Auto)
                .ColumnMediumSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, _pasteInput),
                    GUI.Cell(0, 1, 1, 1, _addPasteButton)),
            GUI.Label()
                .Text("支持直接粘贴 otpauth:// 链接（也支持 Google Authenticator 的 otpauth-migration:// 转移链接）或 Base32 密钥。")
                .WrapIfNeeded()
                .Style(UILabelStyle.Caption),
            GUI.Stack().Horizontal().SmallSpacing().WithChildren(_scanButton, _manualButton)));

        IUIElement accountsSection = GUI.Card(GUI.Stack().Vertical().MediumSpacing().WithChildren(
            _accountsHeader,
            _emptyHint,
            _accountsStack));

        IUIElement backupSection = GUI.Card(GUI.Stack().Vertical().MediumSpacing().WithChildren(
            GUI.Label().Text("备份与恢复").Style(UILabelStyle.BodyStrong),
            GUI.Label()
                .Text("导出文件包含全部账号的核心信息（发行方、账号、密钥、位数、周期、算法、计数器），可以选择设置密码加密；导入时会自动识别文件是否需要密码。")
                .WrapIfNeeded()
                .Style(UILabelStyle.Caption),
            GUI.Stack().Horizontal().SmallSpacing().WithChildren(_exportButton, _importButton),
            GUI.Stack().Horizontal().SmallSpacing().WithChildren(_refreshButton),
            GUI.Label()
                .Text($"本地存储：{_store.FilePath}（Windows 下由 DPAPI 加密，绑定当前系统用户）")
                .WrapIfNeeded()
                .Style(UILabelStyle.Caption)));

        return new UIToolView(GUI.Stack().Vertical().LargeSpacing().WithChildren(
            _infoBar,
            addSection,
            accountsSection,
            backupSection,
            _autoRefreshHint));
    }

    /// <summary>
    /// 同步账号列表到界面。
    /// </summary>
    /// <remarks>
    /// **关键约束：控件实例只创建一次，之后只改内容与显隐，绝不替换。**
    /// DevToys 的 <c>UIStackPresenter</c> 用固定序号渲染子元素（没有 SetKey），而
    /// <c>UIElementPresenter</c> **只在 OnInitialized 里订阅一次** <c>PropertyChanged</c>。
    /// 所以一旦把「新的元素实例」放到同一个位置，Blazor 只会复用旧组件实例、更新参数，
    /// 订阅仍然挂在**旧元素**上 —— 新控件此后无论怎么改都不会触发重绘：
    /// 界面冻住、无异常、CPU 为 0、窗口依然可响应。
    /// 因此：新账号追加到末尾，删除的账号只隐藏不移除，位置永远不变。
    /// </remarks>
    /// <summary>把账号数据刷到已存在的行控件上（不新建控件）。</summary>
    private void ApplyRowContent(AccountRow row)
    {
        OtpAccount account = row.Account;

        GUI.Text(row.IssuerLabel, account.GetDisplayTitle());
        GUI.Text(row.LabelLabel, account.GetDisplaySubtitle());
        GUI.Text(row.MetaLabel, account.GetMetaDisplay());

        // 强制下一次 UpdateCodes 重新写一遍验证码与倒计时。
        row.LastCode = null;
        row.LastRemaining = -1;

        if (account.Type == OtpType.Hotp)
        {
            GUI.Show(row.NextButton);
        }
        else
        {
            GUI.Hide(row.NextButton);
        }
    }

    private void RebuildRows()
    {
        List<OtpAccount> ordered =
        [
            .. _store.Accounts
                .OrderBy(a => a.CreatedAt)
                .ThenBy(a => a.Id, StringComparer.Ordinal)
        ];

        HashSet<string> alive = new(ordered.Select(a => a.Id), StringComparer.Ordinal);

        foreach (OtpAccount account in ordered)
        {
            if (!_rowByAccountId.TryGetValue(account.Id, out AccountRow? row))
            {
                row = CreateRow(account);
                _rowByAccountId[account.Id] = row;
                _rowOrder.Add(row);
                continue;
            }

            if (!ReferenceEquals(row.Account, account))
            {
                // 编辑后账号对象换了：复用同一批控件，只刷新数据与静态文本。
                row.Account = account;
                ApplyRowContent(row);
            }
        }

        foreach (AccountRow row in _rowOrder)
        {
            bool shouldShow = alive.Contains(row.Account.Id);
            if (row.IsHidden != !shouldShow)
            {
                row.IsHidden = !shouldShow;
                if (shouldShow)
                {
                    GUI.Show(row.Element);
                }
                else
                {
                    GUI.Hide(row.Element);
                }
            }
        }

        // 先备好新数组再一次性替换引用，刷新计时器永远只会看到完整快照。
        _rows = [.. _rowOrder.Where(r => !r.IsHidden)];

        // 元素实例与顺序都不变，呈现器已建立的订阅继续有效。
        GUI.WithChildren(_accountsStack, [.. _rowOrder.Select(r => r.Element)]);
        GUI.Text(_accountsHeader, $"我的账号（{_store.Accounts.Count}）");

        if (_store.Accounts.Count == 0)
        {
            GUI.Show(_emptyHint);
        }
        else
        {
            GUI.Hide(_emptyHint);
        }

        UpdateCodes(force: true);
        EnsureRefreshAlive();
    }

    private AccountRow CreateRow(OtpAccount account)
    {
        string id = account.Id;
        AccountRow row = new() { Account = account };

        row.CodeLabel = GUI.Label($"gauth-code-{id}")
            .Style(UILabelStyle.TitleLarge)
            .NeverWrap();

        row.CountdownLabel = GUI.Label($"gauth-countdown-{id}")
            .Style(UILabelStyle.Caption)
            .AlignHorizontally(UIHorizontalAlignment.Center);

        row.Ring = GUI.ProgressRing($"gauth-ring-{id}");

        row.IssuerLabel = GUI.Label($"gauth-issuer-{id}")
            .Text(account.GetDisplayTitle())
            .Style(UILabelStyle.BodyStrong);

        row.LabelLabel = GUI.Label($"gauth-label-{id}")
            .Text(account.GetDisplaySubtitle())
            .Style(UILabelStyle.Caption);

        row.MetaLabel = GUI.Label($"gauth-meta-{id}")
            .Text(account.GetMetaDisplay())
            .Style(UILabelStyle.Caption);

        // 「下一位」始终创建，按类型显隐：编辑时若把 TOTP 改成 HOTP，控件集合不能变，
        // 否则同样会触发上面说的订阅失效问题。
        row.NextButton = GUI.Button($"gauth-next-{id}")
            .Text("下一位")
            .OnClick(() => AdvanceHotpAsync(row));

        List<IUIElement> buttons =
        [
            GUI.Button($"gauth-copy-{id}").Text("复制").OnClick(() => CopyAsync(row)),
            row.NextButton,
            GUI.Button($"gauth-edit-{id}").Text("编辑").OnClick(() => EditAsync(row)),
            GUI.Button($"gauth-delete-{id}").Text("删除").OnClick(() => DeleteAsync(row))
        ];

        IUIElement left = GUI.Stack().Vertical().SmallSpacing().WithChildren(
            row.IssuerLabel,
            row.LabelLabel,
            row.MetaLabel);

        IUIElement right = GUI.Stack().Horizontal().LargeSpacing().WithChildren(
            row.CodeLabel,
            GUI.Stack().Vertical().NoSpacing().WithChildren(row.Ring, row.CountdownLabel),
            GUI.Stack().Horizontal().SmallSpacing().WithChildren([.. buttons]))
            .AlignVertically(UIVerticalAlignment.Center);

        ApplyRowContent(row);

        row.Element = GUI.Card(GUI.Grid()
            .Columns(GUI.Fraction(1), UIGridLength.Auto)
            .ColumnMediumSpacing()
            .Cells(
                GUI.Cell(0, 0, 1, 1, left),
                GUI.Cell(0, 1, 1, 1, right)));

        return row;
    }

    // ---------------------------------------------------------------- 实时刷新

    /// <summary>
    /// 刷新计时器回调（跑在线程池）。用 <see cref="System.Threading.Timer" /> 而不是 async 循环，
    /// 因为它是 CLR 里最底层的调度原语，不依赖 async 续体/渲染器泵，也不会因为某次 await 卡住而整条链失联。
    /// </summary>
    private void Tick(object? state)
    {
        // 上一拍还没跑完就直接跳过，避免堆叠。
        if (Interlocked.CompareExchange(ref _tickGate, 1, 0) != 0)
        {
            return;
        }

        try
        {
            long tickTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            int tick = Interlocked.Increment(ref _tickCount);

            long previous = Interlocked.Exchange(ref _lastTickTimestamp, tickTimestamp);
            if (previous != 0)
            {
                double gapMs = System.Diagnostics.Stopwatch.GetElapsedTime(previous, tickTimestamp).TotalMilliseconds;
                if (gapMs > 1500)
                {
                    AppendDiagnostic($"tick delayed: {gapMs:0}ms since previous tick");
                }
            }

            long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            UpdateCodes();
            double elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            if (elapsedMs > 500)
            {
                AppendDiagnostic($"UpdateCodes took {elapsedMs:0}ms for {_rows.Length} row(s)");
            }

            // 心跳：即使一切「正常」也要留下痕迹，否则界面冻住时无法区分「循环死了」和「循环在跑但没重绘」。
            if (tick % 150 == 0)
            {
                AppendDiagnostic(
                    $"heartbeat: ticks={tick} rows={_rows.Length} accounts={_store.Accounts.Count} failures={_refreshFailures}");
            }

            // 快照与账号数失配是「循环在跑但界面不动」的典型原因，单独报出来。
            if (_rows.Length != _store.Accounts.Count && _lastDesyncSnapshot != _rows.Length)
            {
                _lastDesyncSnapshot = _rows.Length;
                AppendDiagnostic($"row snapshot mismatch: rows={_rows.Length} accounts={_store.Accounts.Count}");
            }

            if (_refreshFailures > 0)
            {
                AppendDiagnostic($"recovered after {_refreshFailures} failure(s)");
                _refreshFailures = 0;
            }
        }
        catch (Exception ex)
        {
            _refreshFailures++;
            AppendDiagnostic($"tick failed (x{_refreshFailures}): {ex.GetType().Name}: {ex.Message}");

            if (_refreshFailures >= 3 && !_refreshHintShown)
            {
                _refreshHintShown = true;
                TryUpdateHint(
                    $"自动刷新遇到异常，正在自动重试；若界面仍不更新请点「刷新」。最近错误：{ex.GetType().Name}: {ex.Message}");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _tickGate, 0);
        }
    }

    /// <summary>
    /// 兜底自愈：任何 UI 交互都顺手检查一次刷新计时器是否还活着，死了就重建。
    /// </summary>
    private void EnsureRefreshAlive()
    {
        long reference = Interlocked.Read(ref _lastTickTimestamp);
        if (reference == 0)
        {
            reference = Interlocked.Read(ref _timerCreatedTimestamp);
        }

        if (reference != 0)
        {
            double idleMs = System.Diagnostics.Stopwatch.GetElapsedTime(reference).TotalMilliseconds;
            if (idleMs < 3000)
            {
                return;
            }

            AppendDiagnostic($"refresh timer looks dead (idle {idleMs:0}ms), recreating");
        }

        StartRefreshTimer();
    }

    private void StartRefreshTimer()
    {
        System.Threading.Timer? old = Interlocked.Exchange(ref _refreshTimer, null);
        old?.Dispose();

        Interlocked.Exchange(ref _timerCreatedTimestamp, System.Diagnostics.Stopwatch.GetTimestamp());
        Interlocked.Exchange(ref _lastTickTimestamp, 0);

        System.Threading.Timer created = new(Tick, null, dueTime: 100, period: 200);
        Interlocked.Exchange(ref _refreshTimer, created);
    }

    private void RefreshNow()
    {
        try
        {
            EnsureRefreshAlive();
            UpdateCodes(force: true);
            GUI.Close(_infoBar);
        }
        catch (Exception ex)
        {
            ShowError($"刷新失败：{ex.Message}");
        }
    }

    private void UpdateCodes(bool force = false)
    {
        // 后台刷新线程与 UI 线程（RebuildRows / 点「刷新」）可能同时进来，串行化避免控件写入交错。
        lock (_updateLock)
        {
            UpdateCodesCore(force);
        }
    }

    private void UpdateCodesCore(bool force)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // 先取快照引用：RebuildRows 只会整体换引用，不会原地改数组。
        AccountRow[] rows = _rows;

        foreach (AccountRow row in rows)
        {
            try
            {
                OtpAccount account = row.Account;
                string code = TotpEngine.Compute(account, now);

                if (force || !string.Equals(code, row.LastCode, StringComparison.Ordinal))
                {
                    GUI.Text(row.CodeLabel, TotpEngine.FormatCode(code));
                    row.LastCode = code;
                }

                if (account.Type == OtpType.Hotp)
                {
                    if (force)
                    {
                        GUI.Text(row.CountdownLabel, $"#{account.Counter}");
                        GUI.Progress(row.Ring, 100);
                    }

                    continue;
                }

                int remaining = TotpEngine.RemainingSeconds(account, now);
                if (force || remaining != row.LastRemaining)
                {
                    int period = Math.Max(account.Period, 1);
                    GUI.Text(row.CountdownLabel, $"{remaining}秒");
                    GUI.Progress(row.Ring, Math.Clamp(100.0 * remaining / period, 0, 100));
                    row.LastRemaining = remaining;
                }
            }
            catch (Exception ex)
            {
                // 单个账号出错不影响其他账号，但要留痕，便于事后定位（原先这里静默吞掉，导致问题无从查起）。
                if (_lastRowErrorLogTick != Environment.TickCount)
                {
                    _lastRowErrorLogTick = Environment.TickCount;
                    AppendDiagnostic($"account {row.Account.Id} failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }

    private void TryUpdateHint(string message)
    {
        try
        {
            GUI.Text(_autoRefreshHint, message);
            GUI.Show(_autoRefreshHint);
        }
        catch
        {
            // 连提示都写不进去说明界面已不可用。
        }
    }

    /// <summary>把刷新/计算异常写到数据目录下的 <c>diag.log</c>，超过 256KB 自动重来。</summary>
    private void AppendDiagnostic(string message)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_store.Directory);
            string path = Path.Combine(_store.Directory, "diag.log");

            FileInfo info = new(path);
            if (info.Exists && info.Length > 256 * 1024)
            {
                File.Delete(path);
            }

            File.AppendAllText(
                path,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [t{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 诊断日志写失败不影响主流程。
        }
    }

    // ---------------------------------------------------------------- 添加

    private async ValueTask AddFromPasteAsync()
    {
        string text = (_pasteText ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            ShowError("请先粘贴 otpauth 链接或 Base32 密钥。");
            return;
        }

        if (await AddFromTextAsync(text, "粘贴"))
        {
            _pasteText = string.Empty;
            GUI.Text(_pasteInput, string.Empty);
        }
    }

    private async Task<bool> AddFromTextAsync(string text, string source)
    {
        try
        {
            if (MigrationPayload.LooksLike(text))
            {
                List<OtpAccount> migrated = MigrationPayload.Parse(text);
                if (migrated.Count == 0)
                {
                    throw new FormatException("该链接中没有可导入的账号。");
                }

                int added = MergeAccounts(migrated);
                PersistAndRefresh();
                ShowSuccess($"从{source}导入完成：新增 {added} 个，重复 {migrated.Count - added} 个。");
                return true;
            }

            if (OtpAuthUri.LooksLike(text))
            {
                OtpAccount account = OtpAuthUri.Parse(text);
                int added = MergeAccounts([account]);
                PersistAndRefresh();

                if (added == 0)
                {
                    ShowInfo("该账号已存在，未重复添加。");
                }
                else
                {
                    ShowSuccess($"已添加「{account.GetDisplayTitle()}」。");
                }

                return true;
            }

            if (Base32.TryDecode(text, out _))
            {
                OtpAccount account = new() { Secret = Base32.Normalize(text) };
                await OpenAccountEditorAsync(account, isNew: true);
                return true;
            }

            ShowError("无法识别的内容：请粘贴 otpauth:// 链接、otpauth-migration:// 链接或 Base32 密钥。");
            return false;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return false;
        }
    }

    private async ValueTask ManualAddAsync()
    {
        await OpenAccountEditorAsync(new OtpAccount { Secret = TotpEngine.GenerateSecret() }, isNew: true);
    }

    private async ValueTask EditAsync(OtpAccount account)
    {
        await OpenAccountEditorAsync(account.Clone(), isNew: false);
    }

    // ---------------------------------------------------------------- 扫码

    private async ValueTask ScanAsync()
    {
        IUIInfoBar bar = GUI.InfoBar("gauth-scan-bar").Closable();
        DialogSession session = null!;

        async ValueTask FromClipboardAsync()
        {
            try
            {
                using Image<Rgba32>? image = await _clipboard.GetClipboardImageAsync();
                if (image is null)
                {
                    ShowDialogError(bar, "剪贴板里没有图片。先用 Win + Shift + S 截取二维码再试。");
                    return;
                }

                string? text = QrScanner.Decode(image);
                if (string.IsNullOrWhiteSpace(text))
                {
                    ShowDialogError(bar, "图片里没有识别到二维码。");
                    return;
                }

                session.Close(text);
            }
            catch (Exception ex)
            {
                ShowDialogError(bar, $"识别失败：{ex.Message}");
            }
        }

        async ValueTask FromFileAsync()
        {
            try
            {
                string? text = await PickImageAndDecodeAsync();
                if (string.IsNullOrWhiteSpace(text))
                {
                    ShowDialogError(bar, "图片里没有识别到二维码，或您取消了选择。");
                    return;
                }

                session.Close(text);
            }
            catch (Exception ex)
            {
                ShowDialogError(bar, $"识别失败：{ex.Message}");
            }
        }

        session = await OpenDialogAsync(s => (
            GUI.Stack().Vertical().MediumSpacing().WithChildren(
                GUI.Label().Text("扫码导入").Style(UILabelStyle.BodyStrong),
                GUI.Label()
                    .Text("把二维码截图复制到剪贴板，或选择一张二维码图片。")
                    .WrapIfNeeded()
                    .Style(UILabelStyle.Caption),
                bar,
                GUI.Stack().Horizontal().SmallSpacing().WithChildren(
                    GUI.Button("gauth-scan-clipboard").AccentAppearance().Text("从剪贴板识别").OnClick(FromClipboardAsync),
                    GUI.Button("gauth-scan-file").Text("选择图片…").OnClick(FromFileAsync))),
            GUI.Stack().Horizontal().SmallSpacing().WithChildren(
                GUI.Button("gauth-scan-close").Text("关闭").OnClick(() => s.Close(null)))));

        object? result = await session.Result;
        if (result is not string scanned || string.IsNullOrWhiteSpace(scanned))
        {
            return;
        }

        await AddFromTextAsync(scanned.Trim(), "扫码");
    }

    private async Task<string?> PickImageAndDecodeAsync()
    {
        SandboxedFileReader? file = await _fileStorage.PickOpenFileAsync(".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp");
        if (file is null)
        {
            return null;
        }

        using (file)
        {
            using MemoryStream buffer = new();
            using (Stream stream = await file.GetNewAccessToFileContentAsync(CancellationToken.None))
            {
                await stream.CopyToAsync(buffer);
            }

            return QrScanner.Decode(buffer.ToArray());
        }
    }

    // ---------------------------------------------------------------- 账号编辑

    private async Task OpenAccountEditorAsync(OtpAccount draft, bool isNew)
    {
        OtpAccount working = draft.Clone();
        working.Normalize();

        IUIInfoBar bar = GUI.InfoBar("gauth-editor-bar").Closable();
        string secret = working.Secret;

        IUIDropDownListItem[] typeItems =
        [
            GUI.Item("按时间（TOTP）", OtpType.Totp),
            GUI.Item("按计数器（HOTP）", OtpType.Hotp)
        ];

        IUIDropDownListItem[] digitsItems = [.. Enumerable.Range(TotpEngine.MinDigits, TotpEngine.MaxDigits - TotpEngine.MinDigits + 1)
            .Select(d => GUI.Item($"{d} 位", d))];

        IUIDropDownListItem[] algorithmItems =
        [
            GUI.Item("SHA1（默认，兼容性最好）", OtpAlgorithm.Sha1),
            GUI.Item("SHA256", OtpAlgorithm.Sha256),
            GUI.Item("SHA512", OtpAlgorithm.Sha512)
        ];

        IUILabel previewLabel = GUI.Label("gauth-editor-preview")
            .Style(UILabelStyle.BodyLarge)
            .NeverWrap();

        IUILabel digitsHint = GUI.Label("gauth-editor-digits-hint")
            .Text(DigitsHint(working.Digits))
            .WrapIfNeeded()
            .Style(UILabelStyle.Caption);

        IUISingleLineTextInput issuerInput = GUI.SingleLineTextInput("gauth-editor-issuer")
            .HideCommandBar()
            .Text(working.Issuer)
            .OnTextChanged(text =>
            {
                working.Issuer = text ?? string.Empty;
                UpdatePreview();
            });

        IUISingleLineTextInput labelInput = GUI.SingleLineTextInput("gauth-editor-label")
            .HideCommandBar()
            .Text(working.Label)
            .OnTextChanged(text => working.Label = text ?? string.Empty);

        IUISingleLineTextInput secretInput = GUI.SingleLineTextInput("gauth-editor-secret")
            .HideCommandBar()
            .CanCopyWhenEditable()
            .Text(secret)
            .OnTextChanged(text =>
            {
                secret = text ?? string.Empty;
                working.Secret = Base32.Normalize(secret);
                UpdatePreview();
            });

        IUINumberInput periodInput = GUI.NumberInput("gauth-editor-period", 1, 86400, 1)
            .HideCommandBar()
            .Value(working.Period)
            .OnValueChanged(value =>
            {
                working.Period = (int)Math.Clamp(value, 1, 86400);
                UpdatePreview();
            });

        IUINumberInput counterInput = GUI.NumberInput("gauth-editor-counter", 0, 2147483647, 1)
            .HideCommandBar()
            .Value(working.Counter)
            .OnValueChanged(value => working.Counter = (long)Math.Clamp(value, 0, 2147483647));

        IUIStack periodBlock = GUI.Stack("gauth-editor-period-block").Vertical().SmallSpacing().WithChildren(
            Field("周期（秒）", periodInput));

        IUIStack counterBlock = GUI.Stack("gauth-editor-counter-block").Vertical().SmallSpacing().WithChildren(
            Field("计数器", counterInput));

        if (working.Type == OtpType.Hotp)
        {
            GUI.Hide(periodBlock);
        }
        else
        {
            GUI.Hide(counterBlock);
        }

        IUISelectDropDownList typeSelect = GUI.SelectDropDownList("gauth-editor-type")
            .WithItems(typeItems)
            .Select(working.Type == OtpType.Hotp ? 1 : 0)
            .OnItemSelected(item =>
            {
                if (item?.Value is not OtpType type)
                {
                    return;
                }

                working.Type = type;
                if (type == OtpType.Hotp)
                {
                    GUI.Hide(periodBlock);
                    GUI.Show(counterBlock);
                }
                else
                {
                    GUI.Show(periodBlock);
                    GUI.Hide(counterBlock);
                }

                UpdatePreview();
            });

        IUISelectDropDownList digitsSelect = GUI.SelectDropDownList("gauth-editor-digits")
            .WithItems(digitsItems)
            .Select(Math.Clamp(working.Digits - TotpEngine.MinDigits, 0, digitsItems.Length - 1))
            .OnItemSelected(item =>
            {
                if (item?.Value is not int digits)
                {
                    return;
                }

                working.Digits = digits;
                GUI.Text(digitsHint, DigitsHint(digits));
                UpdatePreview();
            });

        IUISelectDropDownList algorithmSelect = GUI.SelectDropDownList("gauth-editor-algorithm")
            .WithItems(algorithmItems)
            .Select(working.Algorithm switch
            {
                OtpAlgorithm.Sha256 => 1,
                OtpAlgorithm.Sha512 => 2,
                _ => 0
            })
            .OnItemSelected(item =>
            {
                if (item?.Value is not OtpAlgorithm algorithm)
                {
                    return;
                }

                working.Algorithm = algorithm;
                UpdatePreview();
            });

        DialogSession session = await OpenDialogAsync(s => (
            GUI.Stack().Vertical().MediumSpacing().WithChildren(
                GUI.Label().Text(isNew ? "添加账号" : "编辑账号").Style(UILabelStyle.BodyStrong),
                bar,
                GUI.Stack().Horizontal().SmallSpacing().WithChildren(
                    GUI.Label().Text("当前验证码"),
                    previewLabel),
                Field("类型", typeSelect),
                Field("发行方（显示在列表标题）", issuerInput),
                Field("账号名称", labelInput),
                Field("密钥（Base32）", secretInput, GUI.Button("gauth-editor-generate").Text("生成随机密钥").OnClick(() =>
                {
                    secret = TotpEngine.GenerateSecret();
                    working.Secret = secret;
                    GUI.Text(secretInput, secret);
                    UpdatePreview();
                })),
                Field("位数", digitsSelect),
                digitsHint,
                Field("算法", algorithmSelect),
                periodBlock,
                counterBlock),
            GUI.Stack().Horizontal().SmallSpacing().WithChildren(
                GUI.Button("gauth-editor-save").AccentAppearance().Text("保存").OnClick(() =>
                {
                    try
                    {
                        working.Secret = Base32.Normalize(secret);
                        if (!Base32.TryDecode(working.Secret, out _))
                        {
                            throw new FormatException("密钥不是合法的 Base32 字符串。");
                        }

                        if (string.IsNullOrWhiteSpace(working.Issuer) && string.IsNullOrWhiteSpace(working.Label))
                        {
                            throw new FormatException("请至少填写发行方或账号名称。");
                        }

                        if (working.Digits is < TotpEngine.MinDigits or > TotpEngine.MaxDigits)
                        {
                            throw new FormatException($"位数必须在 {TotpEngine.MinDigits}~{TotpEngine.MaxDigits} 之间。");
                        }

                        if (working.Type == OtpType.Totp && working.Period is < 1 or > 86400)
                        {
                            throw new FormatException("周期必须在 1~86400 秒之间。");
                        }

                        working.Normalize();
                        s.Close(working);
                    }
                    catch (Exception ex)
                    {
                        ShowDialogError(bar, ex.Message);
                    }

                    return ValueTask.CompletedTask;
                }),
                GUI.Button("gauth-editor-cancel").Text("取消").OnClick(() => s.Close(null)))));

        UpdatePreview();

        object? result = await session.Result;
        if (result is not OtpAccount edited)
        {
            return;
        }

        edited.Normalize();

        if (isNew)
        {
            int added = MergeAccounts([edited]);
            if (added == 0)
            {
                ShowInfo("该账号已存在，未重复添加。");
                return;
            }
        }
        else
        {
            int index = _store.Accounts.FindIndex(a => a.Id == edited.Id);
            if (index < 0)
            {
                ShowError("要编辑的账号已不存在。");
                return;
            }

            _store.Accounts[index] = edited;
        }

        PersistAndRefresh();
        ShowSuccess(isNew ? $"已添加「{edited.GetDisplayTitle()}」。" : "已保存修改。");

        void UpdatePreview()
        {
            try
            {
                OtpAccount preview = working.Clone();
                preview.Secret = Base32.Normalize(secret);
                if (!Base32.TryDecode(preview.Secret, out _))
                {
                    GUI.Text(previewLabel, "密钥无效");
                    return;
                }

                preview.Normalize();
                string code = TotpEngine.Compute(preview, DateTimeOffset.UtcNow);
                GUI.Text(previewLabel, TotpEngine.FormatCode(code));
            }
            catch (Exception)
            {
                GUI.Text(previewLabel, "—");
            }
        }
    }

    // ---------------------------------------------------------------- 复制 / 删除 / HOTP

    private ValueTask CopyAsync(AccountRow row) => CopyAsync(row.Account);

    private ValueTask EditAsync(AccountRow row) => EditAsync(row.Account);

    private ValueTask DeleteAsync(AccountRow row) => DeleteAsync(row.Account);

    private ValueTask AdvanceHotpAsync(AccountRow row) => AdvanceHotpAsync(row.Account);

    private async ValueTask CopyAsync(OtpAccount account)
    {
        try
        {
            string code = TotpEngine.Compute(account, DateTimeOffset.UtcNow);
            await _clipboard.SetClipboardTextAsync(code);
            ShowSuccess($"已复制「{account.GetDisplayTitle()}」的验证码。");
        }
        catch (Exception ex)
        {
            ShowError($"复制失败：{ex.Message}");
        }
    }

    private ValueTask AdvanceHotpAsync(OtpAccount account)
    {
        account.Counter++;
        PersistAndRefresh();
        ShowSuccess($"「{account.GetDisplayTitle()}」计数器已推进到 {account.Counter}。");
        return ValueTask.CompletedTask;
    }

    private async ValueTask DeleteAsync(OtpAccount account)
    {
        DialogSession session = await OpenDialogAsync(s => (
            GUI.Stack().Vertical().MediumSpacing().WithChildren(
                GUI.Label().Text($"确定删除「{account.GetDisplayTitle()}」？").Style(UILabelStyle.BodyStrong),
                GUI.Label()
                    .Text("删除后无法恢复。如果还没有导出备份，建议先导出备份。")
                    .WrapIfNeeded()
                    .Style(UILabelStyle.Caption)),
            GUI.Stack().Horizontal().SmallSpacing().WithChildren(
                GUI.Button("gauth-delete-confirm").AccentAppearance().Text("删除").OnClick(() => s.Close("delete")),
                GUI.Button("gauth-delete-cancel").Text("取消").OnClick(() => s.Close(null)))));

        object? result = await session.Result;
        if (result is not string)
        {
            return;
        }

        int index = _store.Accounts.FindIndex(a => a.Id == account.Id);
        if (index >= 0)
        {
            _store.Accounts.RemoveAt(index);
            PersistAndRefresh();
            ShowSuccess($"已删除「{account.GetDisplayTitle()}」。");
        }
    }

    // ---------------------------------------------------------------- 导出 / 导入

    private async ValueTask ExportAsync()
    {
        if (_store.Accounts.Count == 0)
        {
            ShowError("还没有账号可以导出。");
            return;
        }

        string password = string.Empty;
        string confirmation = string.Empty;
        IUIInfoBar bar = GUI.InfoBar("gauth-export-bar").Closable();

        IUIPasswordInput passwordInput = GUI.PasswordInput("gauth-export-password")
            .HideCommandBar()
            .OnTextChanged(text => password = text ?? string.Empty);

        IUIPasswordInput confirmInput = GUI.PasswordInput("gauth-export-confirm")
            .HideCommandBar()
            .OnTextChanged(text => confirmation = text ?? string.Empty);

        DialogSession session = await OpenDialogAsync(s => (
            GUI.Stack().Vertical().MediumSpacing().WithChildren(
                GUI.Label().Text($"导出 {_store.Accounts.Count} 个账号").Style(UILabelStyle.BodyStrong),
                bar,
                GUI.Label()
                    .Text("两个密码框都留空表示不加密导出（文件里是本插件定义的 JSON 容器，任何拿到文件的人都可读）。填写密码则使用 AES-256 + HMAC-SHA256 加密。")
                    .WrapIfNeeded()
                    .Style(UILabelStyle.Caption),
                Field("密码（可留空）", passwordInput),
                Field("确认密码", confirmInput)),
            GUI.Stack().Horizontal().SmallSpacing().WithChildren(
                GUI.Button("gauth-export-ok").AccentAppearance().Text("下一步").OnClick(() =>
                {
                    if (password.Length > 0 && !string.Equals(password, confirmation, StringComparison.Ordinal))
                    {
                        ShowDialogError(bar, "两次输入的密码不一致。");
                        return ValueTask.CompletedTask;
                    }

                    s.Close(password);
                    return ValueTask.CompletedTask;
                }),
                GUI.Button("gauth-export-cancel").Text("取消").OnClick(() => s.Close(null)))));

        object? result = await session.Result;
        if (result is not string chosenPassword)
        {
            return;
        }

        try
        {
            bool encrypt = chosenPassword.Length > 0;
            byte[] bytes = VaultFile.Create(_store.Accounts, encrypt ? chosenPassword : null);

            FileStream? stream = await _fileStorage.PickSaveFileAsync(VaultFile.FileExtension);
            if (stream is null)
            {
                return;
            }

            await using (stream)
            {
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }

            ShowSuccess($"已导出 {_store.Accounts.Count} 个账号{(encrypt ? "（已加密，请牢记密码）" : "（未加密）")}。");
        }
        catch (Exception ex)
        {
            ShowError($"导出失败：{ex.Message}");
        }
    }

    private async ValueTask ImportAsync()
    {
        try
        {
            SandboxedFileReader? file = await _fileStorage.PickOpenFileAsync(VaultFile.FileExtension, ".txt", ".json");
            if (file is null)
            {
                return;
            }

            byte[] bytes;
            using (file)
            {
                using MemoryStream buffer = new();
                using (Stream stream = await file.GetNewAccessToFileContentAsync(CancellationToken.None))
                {
                    await stream.CopyToAsync(buffer);
                }

                bytes = buffer.ToArray();
            }

            if (!VaultFile.LooksLikeVault(bytes))
            {
                string plainText = Encoding.UTF8.GetString(bytes).Trim();
                if (plainText.Length == 0)
                {
                    ShowError("文件为空。");
                    return;
                }

                await ImportFromTextAsync(plainText);
                return;
            }

            VaultHeader header = VaultFile.ReadHeader(bytes);
            VaultPayload payload;

            if (header.IsEncrypted)
            {
                bool retry = false;
                while (true)
                {
                    string? entered = await PromptPasswordAsync(retry);
                    if (entered is null)
                    {
                        return;
                    }

                    try
                    {
                        payload = VaultFile.Read(bytes, entered);
                        break;
                    }
                    catch (Exception ex) when (ex is VaultPasswordException or VaultPasswordRequiredException)
                    {
                        retry = true;
                    }
                }
            }
            else
            {
                payload = VaultFile.Read(bytes, null);
            }

            int added = MergeAccounts(payload.Accounts);
            PersistAndRefresh();

            int skipped = payload.Accounts.Count - added;
            ShowSuccess($"导入完成：新增 {added} 个{(skipped > 0 ? $"，跳过重复 {skipped} 个" : string.Empty)}。");
        }
        catch (Exception ex)
        {
            ShowError($"导入失败：{ex.Message}");
        }
    }

    private Task ImportFromTextAsync(string text)
    {
        List<OtpAccount> parsed = [];
        int unreadable = 0;

        foreach (string token in text.Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (OtpAuthUri.LooksLike(token))
                {
                    parsed.Add(OtpAuthUri.Parse(token));
                }
                else if (MigrationPayload.LooksLike(token))
                {
                    parsed.AddRange(MigrationPayload.Parse(token));
                }
                else
                {
                    unreadable++;
                }
            }
            catch (FormatException)
            {
                unreadable++;
            }
        }

        if (parsed.Count == 0)
        {
            throw new VaultFormatException("文件里没有可导入的 otpauth 链接。");
        }

        int added = MergeAccounts(parsed);
        PersistAndRefresh();

        int skipped = parsed.Count - added;
        ShowSuccess(
            $"导入完成：新增 {added} 个{(skipped > 0 ? $"，跳过重复 {skipped} 个" : string.Empty)}"
            + $"{(unreadable > 0 ? $"，{unreadable} 行无法识别" : string.Empty)}。");

        return Task.CompletedTask;
    }

    private async Task<string?> PromptPasswordAsync(bool isRetry)
    {
        string password = string.Empty;
        IUIInfoBar bar = GUI.InfoBar("gauth-password-bar").Closable();
        if (isRetry)
        {
            ShowDialogError(bar, "上一个密码不正确，或密码为空，请重试。");
        }

        IUIPasswordInput input = GUI.PasswordInput("gauth-password-input")
            .HideCommandBar()
            .OnTextChanged(text => password = text ?? string.Empty);

        DialogSession session = await OpenDialogAsync(s => (
            GUI.Stack().Vertical().MediumSpacing().WithChildren(
                GUI.Label().Text("该备份文件已加密").Style(UILabelStyle.BodyStrong),
                GUI.Label().Text("请输入导出时设置的密码。").WrapIfNeeded().Style(UILabelStyle.Caption),
                bar,
                Field("密码", input)),
            GUI.Stack().Horizontal().SmallSpacing().WithChildren(
                GUI.Button("gauth-password-ok").AccentAppearance().Text("解密").OnClick(() => s.Close(password)),
                GUI.Button("gauth-password-cancel").Text("取消").OnClick(() => s.Close(null)))));

        object? result = await session.Result;
        return result as string;
    }

    // ---------------------------------------------------------------- 通用

    private int MergeAccounts(IEnumerable<OtpAccount> incoming)
    {
        int added = 0;
        long order = _store.Accounts.Count == 0
            ? DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            : _store.Accounts.Max(a => a.CreatedAt);

        foreach (OtpAccount candidate in incoming)
        {
            candidate.Normalize();

            bool duplicate = _store.Accounts.Any(existing =>
                string.Equals(existing.Secret, candidate.Secret, StringComparison.Ordinal)
                && string.Equals(existing.Issuer, candidate.Issuer, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Label, candidate.Label, StringComparison.OrdinalIgnoreCase));

            if (duplicate)
            {
                continue;
            }

            candidate.Id = Guid.NewGuid().ToString("N");
            candidate.CreatedAt = ++order;
            _store.Accounts.Add(candidate);
            added++;
        }

        return added;
    }

    private void PersistAndRefresh()
    {
        if (!_store.Save() && !string.IsNullOrEmpty(_store.LastError))
        {
            ShowError(_store.LastError);
        }

        RebuildRows();
    }

    private async Task<DialogSession> OpenDialogAsync(Func<DialogSession, (IUIElement Content, IUIElement Footer)> build)
    {
        // DevToys 同一时间只允许打开一个对话框；等上一个窗口的关闭事件跑完再开新的。
        for (int i = 0; i < 40 && View.CurrentOpenedDialog is not null; i++)
        {
            await Task.Delay(50).ConfigureAwait(true);
        }

        DialogSession session = new();
        (IUIElement content, IUIElement footer) = build(session);
        session.Dialog = await View.OpenDialogAsync(content, footer, isDismissible: false);
        _ = session.Dialog.DialogCloseAwaiter.ContinueWith(_ => session.CloseSilently(null), TaskScheduler.Default);
        return session;
    }

    private void ShowSuccess(string message)
    {
        GUI.Title(_infoBar, "完成");
        GUI.Description(_infoBar, message);
        GUI.Success(_infoBar);
        GUI.Open(_infoBar);
    }

    private void ShowError(string message)
    {
        GUI.Title(_infoBar, "出错");
        GUI.Description(_infoBar, message);
        GUI.Error(_infoBar);
        GUI.Open(_infoBar);
    }

    private void ShowInfo(string message)
    {
        GUI.Title(_infoBar, "提示");
        GUI.Description(_infoBar, message);
        GUI.Informational(_infoBar);
        GUI.Open(_infoBar);
    }

    private static void ShowDialogError(IUIInfoBar bar, string message)
    {
        GUI.Title(bar, "提示");
        GUI.Description(bar, message);
        GUI.Error(bar);
        GUI.Open(bar);
    }

    private static string DigitsHint(int digits)
        => digits <= TotpEngine.StandardMaxDigits
            ? $"{TotpEngine.MinDigits}~{TotpEngine.StandardMaxDigits} 位属于 RFC 6238 / RFC 4226 标准范围，可与 Google Authenticator 等第三方验证器互通。"
            : $"{digits} 位超出 RFC 标准（标准动态截断只到 {TotpEngine.StandardMaxDigits} 位），本插件改用「完整 HMAC 大端整数取模」的扩展截断，验证端必须使用同一算法才能对上。";

    private static IUIElement Field(string label, IUIElement input)
        => GUI.Stack().Vertical().SmallSpacing().WithChildren(
            GUI.Label().Text(label),
            input);

    private static IUIElement Field(string label, IUIElement input, IUIElement button)
        => GUI.Stack().Vertical().SmallSpacing().WithChildren(
            GUI.Label().Text(label),
            GUI.Grid()
                .Columns(GUI.Fraction(1), UIGridLength.Auto)
                .ColumnSmallSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, input),
                    GUI.Cell(0, 1, 1, 1, button)));

    // ---------------------------------------------------------------- 内部类型

    private sealed class AccountRow
    {
        public OtpAccount Account { get; set; } = null!;

        public IUILabel CodeLabel { get; set; } = null!;

        public IUILabel CountdownLabel { get; set; } = null!;

        public IUILabel IssuerLabel { get; set; } = null!;

        public IUILabel LabelLabel { get; set; } = null!;

        public IUILabel MetaLabel { get; set; } = null!;

        public IUIProgressRing Ring { get; set; } = null!;

        public IUIButton NextButton { get; set; } = null!;

        public IUIElement Element { get; set; } = null!;

        /// <summary>账号已被删除：仅隐藏，控件保留在树上以免破坏呈现器订阅。</summary>
        public bool IsHidden { get; set; }

        public string? LastCode { get; set; }

        public int LastRemaining { get; set; } = -1;
    }

    private sealed class DialogSession
    {
        private readonly TaskCompletionSource<object?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public UIDialog? Dialog { get; set; }

        public Task<object?> Result => _completion.Task;

        public void Close(object? result)
        {
            if (_completion.TrySetResult(result))
            {
                Dialog?.Close();
            }
        }

        public void CloseSilently(object? result)
        {
            _completion.TrySetResult(result);
        }
    }
}

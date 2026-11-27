using System.ComponentModel.Composition;
using System.Text.Json;
using CodecHub.DevToys.Core;
using DevToys.Api;
using static DevToys.Api.GUI;

namespace CodecHub.DevToys;

/// <summary>
/// 单一左栏工具：模式切换 + 输入 + 并列多视图输出。
/// 严格限定在「编码 + 进制」，不做 JSON/YAML/CSV 等格式互转。
/// </summary>
[Export(typeof(IGuiTool))]
[Name("CodecHub")]
[ToolDisplayInformation(
    // U+E943 = ic_fluent_code_20_regular（本仓库 JsFormat 已验证存在）
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uE943',
    GroupName = PredefinedCommonToolGroupNames.EncodersDecoders,
    ResourceManagerAssemblyIdentifier = nameof(CodecHubResourceAssemblyIdentifier),
    ResourceManagerBaseName = "CodecHub.DevToys.Strings",
    ShortDisplayTitleResourceName = "CodecHubShortDisplayTitle",
    LongDisplayTitleResourceName = "CodecHubLongDisplayTitle",
    DescriptionResourceName = "CodecHubDescription",
    AccessibleNameResourceName = "CodecHubAccessibleName",
    SearchKeywordsResourceName = "CodecHubSearchKeywords")]
internal sealed class CodecHubGuiTool : IGuiTool
{
    private static readonly SettingDefinition<string> StateSetting = new("CodecHub.State", string.Empty);

    private static readonly (string Label, string Value)[] ModeDefinitions =
    [
        ("URL 组件", nameof(CodecMode.UrlComponent)),
        ("HTML 实体", nameof(CodecMode.HtmlEntity)),
        ("Unicode 转义", nameof(CodecMode.UnicodeEscape)),
        ("JSON 字符串", nameof(CodecMode.JsonEscape)),
        ("Base64", nameof(CodecMode.Base64)),
        ("Base64url", nameof(CodecMode.Base64Url)),
        ("Base32", nameof(CodecMode.Base32)),
        ("Base58", nameof(CodecMode.Base58)),
        ("Hex 字节", nameof(CodecMode.HexBytes)),
        ("字符集转码", nameof(CodecMode.Charset)),
        ("进制互转", nameof(CodecMode.Radix)),
        ("补码 / 字节序", nameof(CodecMode.TwosComplement)),
        ("IPv4 地址", nameof(CodecMode.IpAddress)),
        ("Unix 时间戳", nameof(CodecMode.UnixTime)),
        ("GUID 格式", nameof(CodecMode.GuidFormat))
    ];

    private static readonly (string Label, string Value)[] CharsetDefinitions =
    [
        ("UTF-8", nameof(CharsetName.Utf8)),
        ("GB18030", nameof(CharsetName.Gb18030)),
        ("UTF-16LE", nameof(CharsetName.Utf16Le)),
        ("UTF-16BE", nameof(CharsetName.Utf16Be)),
        ("ASCII", nameof(CharsetName.Ascii))
    ];

    private static readonly (string Label, string Value)[] EscapeStyleDefinitions =
    [
        ("\\uXXXX（JS / Java / C#）", nameof(TextEscapeStyle.Universal)),
        ("\\xNN / \\uXXXX", nameof(TextEscapeStyle.HexEscape)),
        ("U+XXXX 码点", nameof(TextEscapeStyle.CodePoint))
    ];

    private static readonly (string Label, string Value)[] FollowUpDefinitions =
    [
        ("不串联", ""),
        ("URL 解码", nameof(CodecMode.UrlComponent)),
        ("HTML 解码", nameof(CodecMode.HtmlEntity)),
        ("Unicode 解码", nameof(CodecMode.UnicodeEscape)),
        ("JSON 反转义", nameof(CodecMode.JsonEscape)),
        ("Base64 解码", nameof(CodecMode.Base64)),
        ("Base64url 解码", nameof(CodecMode.Base64Url)),
        ("Base32 解码", nameof(CodecMode.Base32)),
        ("Base58 解码", nameof(CodecMode.Base58)),
        ("Hex 解码", nameof(CodecMode.HexBytes))
    ];

    private readonly ISettingsProvider _settingsProvider;
    private readonly IClipboard _clipboard;

    // ---------------- 状态 ----------------
    private string _mode = nameof(CodecMode.UrlComponent);
    private string _direction = nameof(CodecDirection.Encode);
    private string _charset = nameof(CharsetName.Utf8);
    private string _escapeStyle = nameof(TextEscapeStyle.Universal);
    private string _followUp = string.Empty;
    private string _byteOrder = nameof(ByteOrder.Big);
    private string _timeUnit = "ms";
    private int _fromRadix = 10;
    private int _toRadix = 16;
    private int _bitWidth = 32;
    private bool _plusAsSpace;
    private bool _padding = true;
    private bool _includeBom;
    private bool _upperHex = true;
    private bool _signed = true;
    private bool _utc = true;
    private string _hexSeparator = string.Empty;
    private string _input = "Hello 中文";
    private string _primaryText = string.Empty;

    private bool _loading = true;

    // ---------------- 控件 ----------------
    private readonly IUIInfoBar _infoBar;
    private readonly IUISelectDropDownList _modeSelect;
    private readonly IUISelectDropDownList _directionSelect;
    private readonly IUISelectDropDownList _charsetSelect;
    private readonly IUISelectDropDownList _escapeStyleSelect;
    private readonly IUISelectDropDownList _followUpSelect;
    private readonly IUISelectDropDownList _byteOrderSelect;
    private readonly IUISelectDropDownList _timeUnitSelect;
    private readonly IUISelectDropDownList _bitWidthSelect;

    private readonly IUISwitch _plusSwitch;
    private readonly IUISwitch _paddingSwitch;
    private readonly IUISwitch _bomSwitch;
    private readonly IUISwitch _upperHexSwitch;
    private readonly IUISwitch _signedSwitch;
    private readonly IUISwitch _utcSwitch;

    private readonly IUINumberInput _fromRadixInput;
    private readonly IUINumberInput _toRadixInput;
    private readonly IUISingleLineTextInput _hexSeparatorInput;
    private readonly IUIMultiLineTextInput _inputBox;
    private readonly IUIMultiLineTextInput _primaryBox;
    private readonly IUILabel _primaryLabel;
    private readonly IUILabel _hintLabel;

    private readonly IUIElement _directionRow;
    private readonly IUIElement _charsetRow;
    private readonly IUIElement _urlRow;
    private readonly IUIElement _escapeRow;
    private readonly IUIElement _baseRow;
    private readonly IUIElement _hexRow;
    private readonly IUIElement _radixRow;
    private readonly IUIElement _twosRow;
    private readonly IUIElement _timeRow;
    private readonly IUIElement _followRow;

    private readonly IUILabel[] _viewLabels = new IUILabel[5];
    private readonly IUIMultiLineTextInput[] _viewBoxes = new IUIMultiLineTextInput[5];
    private readonly IUIElement[] _viewRows = new IUIElement[5];

    private UIToolView? _view;

    public UIToolView View => _view ??= BuildView();

    [ImportingConstructor]
    public CodecHubGuiTool(ISettingsProvider settingsProvider, IClipboard clipboard)
    {
        _settingsProvider = settingsProvider;
        _clipboard = clipboard;
        LoadState();

        IUIDropDownListItem[] modeItems = Items(ModeDefinitions);
        IUIDropDownListItem[] charsetItems = Items(CharsetDefinitions);
        IUIDropDownListItem[] escapeItems = Items(EscapeStyleDefinitions);
        IUIDropDownListItem[] followItems = Items(FollowUpDefinitions);
        IUIDropDownListItem[] byteOrderItems = [GUI.Item("大端（网络序）", nameof(ByteOrder.Big)), GUI.Item("小端", nameof(ByteOrder.Little))];
        IUIDropDownListItem[] timeUnitItems = [GUI.Item("秒（s）", "s"), GUI.Item("毫秒（ms）", "ms"), GUI.Item("微秒（us）", "us"), GUI.Item("纳秒（ns）", "ns")];
        IUIDropDownListItem[] bitWidthItems = [GUI.Item("8 位", 8), GUI.Item("16 位", 16), GUI.Item("32 位", 32), GUI.Item("64 位", 64)];
        IUIDropDownListItem[] directionItems = [GUI.Item("编码 →", nameof(CodecDirection.Encode)), GUI.Item("解码 ←", nameof(CodecDirection.Decode))];

        _infoBar = GUI.InfoBar("codec-info").Closable();

        _modeSelect = GUI.SelectDropDownList("codec-mode")
            .WithItems(modeItems)
            .OnItemSelected(item =>
            {
                if (_loading || item?.Value is not string value)
                {
                    return;
                }

                _mode = value;
                SaveState();
                RefreshVisibility();
                Convert();
            });

        _directionSelect = GUI.SelectDropDownList("codec-direction")
            .WithItems(directionItems)
            .OnItemSelected(item =>
            {
                if (_loading || item?.Value is not string value)
                {
                    return;
                }

                _direction = value;
                SaveState();
                Convert();
            });

        _charsetSelect = GUI.SelectDropDownList("codec-charset")
            .WithItems(charsetItems)
            .OnItemSelected(item =>
            {
                if (_loading || item?.Value is not string value)
                {
                    return;
                }

                _charset = value;
                SaveState();
                Convert();
            });

        _escapeStyleSelect = GUI.SelectDropDownList("codec-escape-style")
            .WithItems(escapeItems)
            .OnItemSelected(item =>
            {
                if (_loading || item?.Value is not string value)
                {
                    return;
                }

                _escapeStyle = value;
                SaveState();
                Convert();
            });

        _followUpSelect = GUI.SelectDropDownList("codec-follow-up")
            .WithItems(followItems)
            .OnItemSelected(item =>
            {
                if (_loading)
                {
                    return;
                }

                _followUp = item?.Value as string ?? string.Empty;
                SaveState();
                Convert();
            });

        _byteOrderSelect = GUI.SelectDropDownList("codec-byte-order")
            .WithItems(byteOrderItems)
            .OnItemSelected(item =>
            {
                if (_loading || item?.Value is not string value)
                {
                    return;
                }

                _byteOrder = value;
                SaveState();
                Convert();
            });

        _timeUnitSelect = GUI.SelectDropDownList("codec-time-unit")
            .WithItems(timeUnitItems)
            .OnItemSelected(item =>
            {
                if (_loading || item?.Value is not string value)
                {
                    return;
                }

                _timeUnit = value;
                SaveState();
                Convert();
            });

        _bitWidthSelect = GUI.SelectDropDownList("codec-bit-width")
            .WithItems(bitWidthItems)
            .OnItemSelected(item =>
            {
                if (_loading || item?.Value is not int value)
                {
                    return;
                }

                _bitWidth = value;
                SaveState();
                Convert();
            });

        _plusSwitch = GUI.Switch("codec-plus").OnToggle(on =>
        {
            if (_loading) return;
            _plusAsSpace = on;
            SaveState();
            Convert();
        });
        _paddingSwitch = GUI.Switch("codec-padding").OnToggle(on =>
        {
            if (_loading) return;
            _padding = on;
            SaveState();
            Convert();
        });
        _bomSwitch = GUI.Switch("codec-bom").OnToggle(on =>
        {
            if (_loading) return;
            _includeBom = on;
            SaveState();
            Convert();
        });
        _upperHexSwitch = GUI.Switch("codec-upper-hex").OnToggle(on =>
        {
            if (_loading) return;
            _upperHex = on;
            SaveState();
            Convert();
        });
        _signedSwitch = GUI.Switch("codec-signed").OnToggle(on =>
        {
            if (_loading) return;
            _signed = on;
            SaveState();
            Convert();
        });
        _utcSwitch = GUI.Switch("codec-utc").OnToggle(on =>
        {
            if (_loading) return;
            _utc = on;
            SaveState();
            Convert();
        });

        _fromRadixInput = GUI.NumberInput("codec-from-radix", 2, 36, 1)
            .OnValueChanged(value =>
            {
                if (_loading) return;
                _fromRadix = (int)Math.Clamp(value, 2, 36);
                SaveState();
                Convert();
            });
        _toRadixInput = GUI.NumberInput("codec-to-radix", 2, 36, 1)
            .OnValueChanged(value =>
            {
                if (_loading) return;
                _toRadix = (int)Math.Clamp(value, 2, 36);
                SaveState();
                Convert();
            });
        _hexSeparatorInput = GUI.SingleLineTextInput("codec-hex-separator")
            .HideCommandBar()
            .Text(_hexSeparator)
            .OnTextChanged(text =>
            {
                if (_loading) return;
                _hexSeparator = text;
                SaveState();
                Convert();
            });

        _directionRow = Setting("方向", "编码：文本 → 表示形式；解码：表示形式 → 文本。进制与 GUID 模式不需要方向。", _directionSelect);
        _charsetRow = Setting("文本字符集", "影响文本与字节之间的互转；含 GB18030。", _charsetSelect);
        _urlRow = Setting("表单模式（+ 表示空格）", "开启后空格编码为 +，解码时 + 还原为空格。", _plusSwitch);
        _escapeRow = Setting("Unicode 写法", "选择 \\uXXXX、\\xNN 或 U+XXXX 码点风格。", _escapeStyleSelect);
        _baseRow = Setting("保留 = 填充", "关闭后可得到无填充的标准 / URL 安全 Base64。", _paddingSwitch);
        _hexRow = Setting("Hex 显示", "大写/小写与字节分隔符（例如空格或 -）。", GUI.Stack().Horizontal().SmallSpacing().WithChildren(
            GUI.Label("codec-upper-hex-label").Text("大写"),
            _upperHexSwitch,
            _hexSeparatorInput));
        _radixRow = Setting("源进制 / 目标进制", "均支持 2~36，可输入负数与 0x 前缀。", GUI.Stack().Horizontal().SmallSpacing().WithChildren(
            GUI.Label("codec-from-radix-label").Text("从"),
            _fromRadixInput,
            GUI.Label("codec-to-radix-label").Text("到"),
            _toRadixInput));
        _twosRow = Setting("补码参数", "位宽 8/16/32/64、有符号/无符号、字节序。", GUI.Stack().Vertical().SmallSpacing().WithChildren(
            GUI.Label("codec-bit-width-label").Text("位宽"),
            _bitWidthSelect,
            GUI.Stack("codec-signed-row").Horizontal().SmallSpacing().WithChildren(
                GUI.Label("codec-signed-label").Text("有符号"),
                _signedSwitch),
            GUI.Label("codec-byte-order-label").Text("字节序"),
            _byteOrderSelect));
        _timeRow = Setting("时间参数", "没有显式时区的输入按这里的设置解释。", GUI.Stack().Vertical().SmallSpacing().WithChildren(
            GUI.Label("codec-time-unit-label").Text("单位"),
            _timeUnitSelect,
            GUI.Stack("codec-utc-row").Horizontal().SmallSpacing().WithChildren(
                GUI.Label("codec-utc-label").Text("按 UTC 解释输入"),
                _utcSwitch)));
        _followRow = Setting("二次解码（链式）", "对主结果再串一次解码，例如 Base64 → URL。", _followUpSelect);
        _hintLabel = GUI.Label("codec-hint").Text(string.Empty).WrapIfNeeded();

        _inputBox = GUI.MultiLineTextInput("codec-input")
            .Title("输入")
            .Text(_input)
            .OnTextChanged(text =>
            {
                if (_loading) return;
                _input = text;
                SaveState();
                Convert();
            });
        _primaryLabel = GUI.Label("codec-primary-label").Text("转换结果");
        _primaryBox = GUI.MultiLineTextInput("codec-output").ReadOnly().HideCommandBar();

        for (int i = 0; i < _viewLabels.Length; i++)
        {
            _viewLabels[i] = GUI.Label($"codec-view-label-{i}").Text("视图");
            _viewBoxes[i] = GUI.MultiLineTextInput($"codec-view-{i}").ReadOnly().HideCommandBar();
            _viewRows[i] = GUI.Stack("codec-view-row-" + i)
                .Vertical()
                .SmallSpacing()
                .WithChildren(_viewLabels[i], _viewBoxes[i]);
        }

        IUIElement left = GUI.Stack("codec-settings").Vertical().LargeSpacing().WithChildren(
            _infoBar,
            GUI.Text(GUI.Label("codec-settings-title"), "转换设置"),
            Setting("模式", "选择一种编码或进制转换；选项区会随模式变化。", _modeSelect),
            _directionRow,
            _charsetRow,
            _urlRow,
            _escapeRow,
            _baseRow,
            _hexRow,
            _radixRow,
            _twosRow,
            _timeRow,
            _followRow,
            _hintLabel);

        IUIElement buttons = GUI.Stack("codec-buttons").Horizontal().SmallSpacing().WithChildren(
            GUI.Button("codec-copy").Text("复制结果").OnClick(CopyAsync),
            GUI.Button("codec-clear").Text("清空").OnClick(Clear));

        IUIElement right = GUI.Stack("codec-workspace").Vertical().MediumSpacing().WithChildren(
            _inputBox.AlignVertically(UIVerticalAlignment.Stretch),
            _primaryLabel,
            _primaryBox.AlignVertically(UIVerticalAlignment.Stretch),
            buttons,
            _viewRows[0],
            _viewRows[1],
            _viewRows[2],
            _viewRows[3],
            _viewRows[4]);

        _view = new UIToolView(
            GUI.Grid()
                .Columns(GUI.Fraction(1), GUI.Fraction(1))
                .Rows(GUI.Fraction(1))
                .ColumnLargeSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, left),
                    GUI.Cell(0, 1, 1, 1, right)));

        Select(_modeSelect, modeItems, IndexOf(ModeDefinitions, _mode));
        Select(_directionSelect, directionItems, _direction == nameof(CodecDirection.Decode) ? 1 : 0);
        Select(_charsetSelect, charsetItems, IndexOf(CharsetDefinitions, _charset));
        Select(_escapeStyleSelect, escapeItems, IndexOf(EscapeStyleDefinitions, _escapeStyle));
        Select(_followUpSelect, followItems, IndexOf(FollowUpDefinitions, _followUp));
        Select(_byteOrderSelect, byteOrderItems, _byteOrder == nameof(ByteOrder.Little) ? 1 : 0);
        Select(_timeUnitSelect, timeUnitItems, IndexOf([("s", "s"), ("ms", "ms"), ("us", "us"), ("ns", "ns")], _timeUnit));
        Select(_bitWidthSelect, bitWidthItems, _bitWidth switch { 8 => 0, 16 => 1, 64 => 3, _ => 2 });
        SetSwitch(_plusSwitch, _plusAsSpace);
        SetSwitch(_paddingSwitch, _padding);
        SetSwitch(_bomSwitch, _includeBom);
        SetSwitch(_upperHexSwitch, _upperHex);
        SetSwitch(_signedSwitch, _signed);
        SetSwitch(_utcSwitch, _utc);
        GUI.Value(_fromRadixInput, _fromRadix);
        GUI.Value(_toRadixInput, _toRadix);

        _loading = false;
        RefreshVisibility();
        Convert();
    }

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
    }

    private UIToolView BuildView() => _view!;

    private void Convert()
    {
        try
        {
            CodecResult result = CodecEngine.Run(_input, BuildOptions());
            _primaryText = result.PrimaryText;
            GUI.Text(_primaryLabel, result.PrimaryTitle);
            GUI.Text(_primaryBox, result.PrimaryText);

            for (int i = 0; i < _viewRows.Length; i++)
            {
                if (i < result.Views.Count)
                {
                    GUI.Show(_viewRows[i]);
                    GUI.Text(_viewLabels[i], result.Views[i].Title);
                    GUI.Text(_viewBoxes[i], result.Views[i].Text);
                }
                else
                {
                    GUI.Hide(_viewRows[i]);
                }
            }

            GUI.Title(_infoBar, "转换完成");
            GUI.Description(_infoBar, $"{result.PrimaryTitle}：{result.Views.Count} 个辅助视图");
            GUI.Success(_infoBar);
            GUI.Open(_infoBar);
        }
        catch (CodecException ex)
        {
            GUI.Title(_infoBar, "转换失败");
            GUI.Description(_infoBar, ex.Message);
            GUI.Error(_infoBar);
            GUI.Open(_infoBar);
        }
    }

    private CodecOptions BuildOptions()
    {
        _ = Enum.TryParse(_mode, out CodecMode mode);
        _ = Enum.TryParse(_direction, out CodecDirection direction);
        _ = Enum.TryParse(_charset, out CharsetName charset);
        _ = Enum.TryParse(_escapeStyle, out TextEscapeStyle escapeStyle);
        _ = Enum.TryParse(_byteOrder, out ByteOrder byteOrder);
        _ = Enum.TryParse(_followUp, out CodecMode followUp);

        return new CodecOptions
        {
            Mode = mode,
            Direction = direction,
            Charset = charset,
            UnicodeStyle = escapeStyle,
            PlusAsSpace = _plusAsSpace,
            Padding = _padding,
            IncludeBom = _includeBom,
            UpperCaseHex = _upperHex,
            HexSeparator = _hexSeparator,
            FromRadix = _fromRadix,
            ToRadix = _toRadix,
            BitWidth = _bitWidth,
            Signed = _signed,
            ByteOrder = byteOrder,
            TimeUnit = _timeUnit,
            Utc = _utc,
            FollowUp = string.IsNullOrEmpty(_followUp) ? null : followUp
        };
    }

    private void RefreshVisibility()
    {
        CodecMode mode = Enum.TryParse(_mode, out CodecMode parsed) ? parsed : CodecMode.UrlComponent;
        Show(_directionRow, CodecEngine.HasDirection(mode));
        Show(_charsetRow, mode is CodecMode.Base64 or CodecMode.Base64Url or CodecMode.Base32 or CodecMode.Base58 or CodecMode.HexBytes or CodecMode.Charset);
        Show(_urlRow, mode == CodecMode.UrlComponent);
        Show(_escapeRow, mode == CodecMode.UnicodeEscape);
        Show(_baseRow, mode is CodecMode.Base64 or CodecMode.Base64Url);
        Show(_hexRow, mode is CodecMode.HexBytes or CodecMode.Charset);
        Show(_radixRow, mode == CodecMode.Radix);
        Show(_twosRow, mode == CodecMode.TwosComplement);
        Show(_timeRow, mode == CodecMode.UnixTime);
        Show(_followRow, mode is CodecMode.UrlComponent or CodecMode.HtmlEntity or CodecMode.UnicodeEscape or CodecMode.JsonEscape
            or CodecMode.Base64 or CodecMode.Base64Url or CodecMode.Base32 or CodecMode.Base58 or CodecMode.HexBytes or CodecMode.Charset);
        GUI.Text(_hintLabel, CodecEngine.ModeHint(mode) ?? string.Empty);
    }

    private async ValueTask CopyAsync()
    {
        if (string.IsNullOrEmpty(_primaryText))
        {
            GUI.Title(_infoBar, "没有可复制的内容");
            GUI.Warning(_infoBar);
            GUI.Open(_infoBar);
            return;
        }

        await _clipboard.SetClipboardTextAsync(_primaryText);
        GUI.Title(_infoBar, "已复制到剪贴板");
        GUI.Description(_infoBar, $"{_primaryText.Length} 个字符");
        GUI.Success(_infoBar);
        GUI.Open(_infoBar);
    }

    private void Clear()
    {
        _input = string.Empty;
        GUI.Text(_inputBox, string.Empty);
        SaveState();
        Convert();
    }

    private void LoadState()
    {
        try
        {
            string json = _settingsProvider.GetSetting(StateSetting);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            PersistedState? state = JsonSerializer.Deserialize<PersistedState>(json);
            if (state is null)
            {
                return;
            }

            if (state.Mode is not null) _mode = state.Mode;
            if (state.Direction is not null) _direction = state.Direction;
            if (state.Charset is not null) _charset = state.Charset;
            if (state.EscapeStyle is not null) _escapeStyle = state.EscapeStyle;
            if (state.FollowUp is not null) _followUp = state.FollowUp;
            if (state.ByteOrder is not null) _byteOrder = state.ByteOrder;
            if (state.TimeUnit is not null) _timeUnit = state.TimeUnit;
            if (state.FromRadix is not null) _fromRadix = Math.Clamp(state.FromRadix.Value, 2, 36);
            if (state.ToRadix is not null) _toRadix = Math.Clamp(state.ToRadix.Value, 2, 36);
            if (state.BitWidth is not null) _bitWidth = state.BitWidth.Value switch { 8 => 8, 16 => 16, 64 => 64, _ => 32 };
            if (state.PlusAsSpace is not null) _plusAsSpace = state.PlusAsSpace.Value;
            if (state.Padding is not null) _padding = state.Padding.Value;
            if (state.IncludeBom is not null) _includeBom = state.IncludeBom.Value;
            if (state.UpperCaseHex is not null) _upperHex = state.UpperCaseHex.Value;
            if (state.Signed is not null) _signed = state.Signed.Value;
            if (state.Utc is not null) _utc = state.Utc.Value;
            if (state.HexSeparator is not null) _hexSeparator = state.HexSeparator;
            if (state.Input is not null) _input = state.Input;
        }
        catch
        {
            // 设置损坏时保留默认值。
        }
    }

    private void SaveState()
    {
        if (_loading)
        {
            return;
        }

        try
        {
            _settingsProvider.SetSetting(StateSetting, JsonSerializer.Serialize(new PersistedState
            {
                Mode = _mode,
                Direction = _direction,
                Charset = _charset,
                EscapeStyle = _escapeStyle,
                FollowUp = _followUp,
                ByteOrder = _byteOrder,
                TimeUnit = _timeUnit,
                FromRadix = _fromRadix,
                ToRadix = _toRadix,
                BitWidth = _bitWidth,
                PlusAsSpace = _plusAsSpace,
                Padding = _padding,
                IncludeBom = _includeBom,
                UpperCaseHex = _upperHex,
                Signed = _signed,
                Utc = _utc,
                HexSeparator = _hexSeparator,
                Input = _input
            }));
        }
        catch
        {
            // 设置写入失败不影响转换。
        }
    }

    private static IUIElement Setting(string title, string description, IUIElement control)
        => GUI.Setting("codec-setting-" + title).Title(title).Description(description).InteractiveElement(control);

    private static IUIDropDownListItem[] Items((string Label, string Value)[] definitions)
        => Array.ConvertAll(definitions, d => GUI.Item(d.Label, d.Value));

    private static int IndexOf((string Label, string Value)[] definitions, string value)
        => Array.FindIndex(definitions, d => d.Value == value) is int index && index >= 0 ? index : 0;

    private static void Select(IUISelectDropDownList select, IUIDropDownListItem[] items, int index)
        => GUI.Select(select, items[Math.Clamp(index, 0, items.Length - 1)]);

    private static void SetSwitch(IUISwitch toggle, bool on)
    {
        if (on)
        {
            GUI.On(toggle);
        }
        else
        {
            GUI.Off(toggle);
        }
    }

    private static void Show(IUIElement element, bool visible)
    {
        if (visible)
        {
            GUI.Show(element);
        }
        else
        {
            GUI.Hide(element);
        }
    }

    private sealed class PersistedState
    {
        public string? Mode { get; set; }
        public string? Direction { get; set; }
        public string? Charset { get; set; }
        public string? EscapeStyle { get; set; }
        public string? FollowUp { get; set; }
        public string? ByteOrder { get; set; }
        public string? TimeUnit { get; set; }
        public int? FromRadix { get; set; }
        public int? ToRadix { get; set; }
        public int? BitWidth { get; set; }
        public bool? PlusAsSpace { get; set; }
        public bool? Padding { get; set; }
        public bool? IncludeBom { get; set; }
        public bool? UpperCaseHex { get; set; }
        public bool? Signed { get; set; }
        public bool? Utc { get; set; }
        public string? HexSeparator { get; set; }
        public string? Input { get; set; }
    }
}

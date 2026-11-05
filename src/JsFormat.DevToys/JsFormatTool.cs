using System.ComponentModel.Composition;
using System.Text.Json;
using DevToys.Api;
using JsFormat.DevToys.Core;

namespace JsFormat.DevToys;

[Export(typeof(IGuiTool))]
[Name("JsFormat")]
[ToolDisplayInformation(
    // U+F27E = ic_fluent_certificate_20_regular 同族字形已验证存在；这里用 code 语义最接近的 U+E943
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uE943',
    GroupName = PredefinedCommonToolGroupNames.Formatters,
    ResourceManagerAssemblyIdentifier = nameof(JsFormatResourceAssemblyIdentifier),
    ResourceManagerBaseName = "JsFormat.DevToys.Strings",
    ShortDisplayTitleResourceName = "JsFormatShortDisplayTitle",
    LongDisplayTitleResourceName = "JsFormatLongDisplayTitle",
    DescriptionResourceName = "JsFormatDescription",
    AccessibleNameResourceName = "JsFormatAccessibleName",
    SearchKeywordsResourceName = "JsFormatSearchKeywords")]
internal sealed class JsFormatTool : IGuiTool
{
    private static readonly SettingDefinition<string> StateSetting = new("JsFormat.State", string.Empty);

    private readonly ISettingsProvider _settingsProvider;

    private readonly IClipboard _clipboard;

    private readonly bool _isLoading;

    // ---------------- 状态 ----------------
    private string _parser = "typescript";
    private int _printWidth = 80;
    private int _tabWidth = 2;
    private bool _useTabs;
    private bool _semi = true;
    private bool _singleQuote;
    private string _trailingComma = "all";
    private bool _bracketSpacing = true;
    private string _arrowParens = "always";
    private string _quoteProps = "as-needed";
    private string _endOfLine = "lf";

    private string _inputText = string.Empty;
    private string _outputText = string.Empty;

    // ---------------- 控件 ----------------
    private readonly IUISelectDropDownList _parserSelect;

    private readonly IUINumberInput _printWidthInput;

    private readonly IUINumberInput _tabWidthInput;

    private readonly IUISwitch _useTabsSwitch;

    private readonly IUISwitch _semiSwitch;

    private readonly IUISwitch _singleQuoteSwitch;

    private readonly IUISwitch _bracketSpacingSwitch;

    private readonly IUISelectDropDownList _trailingCommaSelect;

    private readonly IUISelectDropDownList _arrowParensSelect;

    private readonly IUISelectDropDownList _quotePropsSelect;

    private readonly IUISelectDropDownList _endOfLineSelect;

    private readonly IUIMultiLineTextInput _inputBox;

    private readonly IUIMultiLineTextInput _outputBox;

    private readonly IUIButton _formatButton;

    private readonly IUIButton _copyButton;

    private readonly IUIButton _clearButton;

    private readonly IUILabel _statusLabel;

    private UIToolView? _view;

    public UIToolView View => _view ??= BuildView();

    [ImportingConstructor]
    public JsFormatTool(ISettingsProvider settingsProvider, IClipboard clipboard)
    {
        _settingsProvider = settingsProvider;
        _clipboard = clipboard;

        LoadState();
        _isLoading = true;

        IUIDropDownListItem[] parserItems =
        [
            GUI.Item("TypeScript / TSX", "typescript"),
            GUI.Item("JavaScript / JSX (babel)", "babel"),
            GUI.Item("JSON", "json")
        ];

        IUIDropDownListItem[] trailingCommaItems =
        [
            GUI.Item("all（函数参数也加）", "all"),
            GUI.Item("es5（仅对象/数组）", "es5"),
            GUI.Item("none", "none")
        ];

        IUIDropDownListItem[] arrowParensItems =
        [
            GUI.Item("always（(x) =>）", "always"),
            GUI.Item("avoid（x =>）", "avoid")
        ];

        IUIDropDownListItem[] quotePropsItems =
        [
            GUI.Item("as-needed（能省则省）", "as-needed"),
            GUI.Item("consistent（一致）", "consistent"),
            GUI.Item("preserve（保留原样）", "preserve")
        ];

        IUIDropDownListItem[] endOfLineItems =
        [
            GUI.Item("LF（Linux/macOS）", "lf"),
            GUI.Item("CRLF（Windows）", "crlf"),
            GUI.Item("auto（跟随原文件）", "auto")
        ];

        _parserSelect = GUI.SelectDropDownList("jsf-parser")
            .WithItems(parserItems)
            .Select(FindIndex(parserItems, _parser))
            .OnItemSelected(item =>
            {
                if (_isLoading)
                {
                    return;
                }

                _parser = item?.Value as string ?? "typescript";
                SaveState();
                UpdateHint();
            });

        _printWidthInput = GUI.NumberInput("jsf-print-width", 40, 200, 1)
            .HideCommandBar()
            .Value(_printWidth)
            .OnValueChanged(value =>
            {
                if (_isLoading)
                {
                    return;
                }

                _printWidth = (int)Math.Round(value);
                SaveState();
            });

        _tabWidthInput = GUI.NumberInput("jsf-tab-width", 1, 8, 1)
            .HideCommandBar()
            .Value(_tabWidth)
            .OnValueChanged(value =>
            {
                if (_isLoading)
                {
                    return;
                }

                _tabWidth = (int)Math.Round(value);
                SaveState();
            });

        _useTabsSwitch = GUI.Switch("jsf-use-tabs");
        _semiSwitch = GUI.Switch("jsf-semi");
        _singleQuoteSwitch = GUI.Switch("jsf-single-quote");
        _bracketSpacingSwitch = GUI.Switch("jsf-bracket-spacing");

        SetSwitchState(_useTabsSwitch, _useTabs);
        SetSwitchState(_semiSwitch, _semi);
        SetSwitchState(_singleQuoteSwitch, _singleQuote);
        SetSwitchState(_bracketSpacingSwitch, _bracketSpacing);

        _useTabsSwitch.OnToggle(on =>
        {
            if (_isLoading)
            {
                return;
            }

            _useTabs = on;
            SaveState();
        });

        _semiSwitch.OnToggle(on =>
        {
            if (_isLoading)
            {
                return;
            }

            _semi = on;
            SaveState();
        });

        _singleQuoteSwitch.OnToggle(on =>
        {
            if (_isLoading)
            {
                return;
            }

            _singleQuote = on;
            SaveState();
        });

        _bracketSpacingSwitch.OnToggle(on =>
        {
            if (_isLoading)
            {
                return;
            }

            _bracketSpacing = on;
            SaveState();
        });

        _trailingCommaSelect = GUI.SelectDropDownList("jsf-trailing-comma")
            .WithItems(trailingCommaItems)
            .Select(FindIndex(trailingCommaItems, _trailingComma))
            .OnItemSelected(item =>
            {
                if (_isLoading)
                {
                    return;
                }

                _trailingComma = item?.Value as string ?? "all";
                SaveState();
            });

        _arrowParensSelect = GUI.SelectDropDownList("jsf-arrow-parens")
            .WithItems(arrowParensItems)
            .Select(FindIndex(arrowParensItems, _arrowParens))
            .OnItemSelected(item =>
            {
                if (_isLoading)
                {
                    return;
                }

                _arrowParens = item?.Value as string ?? "always";
                SaveState();
            });

        _quotePropsSelect = GUI.SelectDropDownList("jsf-quote-props")
            .WithItems(quotePropsItems)
            .Select(FindIndex(quotePropsItems, _quoteProps))
            .OnItemSelected(item =>
            {
                if (_isLoading)
                {
                    return;
                }

                _quoteProps = item?.Value as string ?? "as-needed";
                SaveState();
            });

        _endOfLineSelect = GUI.SelectDropDownList("jsf-eol")
            .WithItems(endOfLineItems)
            .Select(FindIndex(endOfLineItems, _endOfLine))
            .OnItemSelected(item =>
            {
                if (_isLoading)
                {
                    return;
                }

                _endOfLine = item?.Value as string ?? "lf";
                SaveState();
            });

        _inputBox = GUI.MultiLineTextInput("jsf-input")
            .Title("输入（点击「格式化」后执行，不会边打边算）")
            .HideCommandBar()
            .Extendable()
            .Text(_inputText)
            .OnTextChanged(text =>
            {
                if (_isLoading)
                {
                    return;
                }

                _inputText = text;
                SaveState();
            });

        _outputBox = GUI.MultiLineTextInput("jsf-output")
            .Title("输出")
            .HideCommandBar()
            .Extendable()
            .ReadOnly();

        _formatButton = GUI.Button("jsf-format")
            .AccentAppearance()
            .Text("格式化")
            .OnClick(FormatAsync);

        _copyButton = GUI.Button("jsf-copy")
            .Text("复制输出")
            .OnClick(CopyOutputAsync);

        _clearButton = GUI.Button("jsf-clear")
            .Text("清空")
            .OnClick(ClearFields);

        _statusLabel = GUI.Label("jsf-status")
            .Style(UILabelStyle.Caption)
            .Text("准备就绪。首次格式化会加载内嵌的 Prettier（约 0.5 秒）。");

        _view = BuildView();
        GUI.Text(_inputBox, _inputText);
        GUI.Text(_outputBox, string.IsNullOrEmpty(_outputText)
            ? "格式化结果会显示在这里；出错时错误信息也直接显示在这里。"
            : _outputText);

        _isLoading = false;
        UpdateHint();
    }

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
    }

    // ---------------------------------------------------------------- 视图

    private UIToolView BuildView()
    {
        // 工具条放最上面：按钮 + 状态（耗时/错误摘要）
        IUIElement toolbar = GUI.Stack().Horizontal().SmallSpacing()
            .WithChildren(_formatButton, _copyButton, _clearButton, _statusLabel)
            .AlignVertically(UIVerticalAlignment.Center);



        IUIElement optionsCard = GUI.Card(
            "Prettier 选项（与 prettier 配置文件语义一致）",
            GUI.Grid()
                .Columns(GUI.Fraction(1), GUI.Fraction(1), GUI.Fraction(1), GUI.Fraction(1))
                .Rows(UIGridLength.Auto, UIGridLength.Auto, UIGridLength.Auto)
                .ColumnMediumSpacing()
                .RowMediumSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, LabeledField("语法", _parserSelect)),
                    GUI.Cell(0, 1, 1, 1, LabeledField("printWidth", _printWidthInput)),
                    GUI.Cell(0, 2, 1, 1, LabeledField("tabWidth", _tabWidthInput)),
                    GUI.Cell(0, 3, 1, 1, LabeledField("使用 Tab 缩进", _useTabsSwitch)),
                    GUI.Cell(1, 0, 1, 1, LabeledField("句尾分号", _semiSwitch)),
                    GUI.Cell(1, 1, 1, 1, LabeledField("单引号", _singleQuoteSwitch)),
                    GUI.Cell(1, 2, 1, 1, LabeledField("尾逗号", _trailingCommaSelect)),
                    GUI.Cell(1, 3, 1, 1, LabeledField("箭头函数括号", _arrowParensSelect)),
                    GUI.Cell(2, 0, 1, 1, LabeledField("对象 key 引号", _quotePropsSelect)),
                    GUI.Cell(2, 1, 1, 1, LabeledField("大括号内空格", _bracketSpacingSwitch)),
                    GUI.Cell(2, 2, 1, 1, LabeledField("换行符", _endOfLineSelect))));

        // 上：工具条 + 选项；下：输入 / 输出各占一半并撑满剩余高度（底部对齐）
        IUIElement body = GUI.Grid()
            .Columns(GUI.Fraction(1), GUI.Fraction(1))
            .Rows(UIGridLength.Auto, UIGridLength.Auto, GUI.Fraction(1))
            .ColumnLargeSpacing()
            .RowMediumSpacing()
            .Cells(
                GUI.Cell(0, 0, 1, 2, toolbar),
                GUI.Cell(1, 0, 1, 2, optionsCard),
                // 文本框直接放进格子（不套 Card）：套 Card 会按内容高度收缩，撑不满整行
                GUI.Cell(2, 0, 1, 1, _inputBox.AlignVertically(UIVerticalAlignment.Stretch)),
                GUI.Cell(2, 1, 1, 1, _outputBox.AlignVertically(UIVerticalAlignment.Stretch)));

        return new UIToolView(body);
    }

    private static IUIElement LabeledField(string label, IUIElement element)
        => GUI.Stack().Vertical().SmallSpacing().WithChildren(GUI.Label().Text(label), element);

    private static int FindIndex(IUIDropDownListItem[] items, string value)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].Value is string itemValue && string.Equals(itemValue, value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    private static void SetSwitchState(IUISwitch toggle, bool isOn)
    {
        if (isOn)
        {
            GUI.On(toggle);
        }
        else
        {
            GUI.Off(toggle);
        }
    }

    private void UpdateHint()
    {
        GUI.Text(_statusLabel, _parser switch
        {
            "json" => "JSON 模式：只做缩进与排版，不补分号。",
            "babel" => "Babel 模式：JavaScript + JSX（不支持 TypeScript 语法）。",
            _ => "TypeScript 模式：TS / TSX，也能直接格式化普通 JS。"
        });
    }

    // ---------------------------------------------------------------- 动作

    private async ValueTask FormatAsync()
    {
        string code = _inputText;
        if (string.IsNullOrWhiteSpace(code))
        {
            GUI.Text(_outputBox, "请先输入要格式化的代码。");
            return;
        }

        GUI.Text(_statusLabel, "正在格式化…（首次会先加载内嵌 Prettier，约 0.5 秒）");
        GUI.Text(_formatButton, "格式化中…");
        GUI.Disable(_formatButton);

        try
        {
            PrettierHost host = await PrettierHost.GetAsync();

            FormatOptions options = new()
            {
                Parser = _parser,
                PrintWidth = _printWidth,
                TabWidth = _tabWidth,
                UseTabs = _useTabs,
                Semi = _semi,
                SingleQuote = _singleQuote,
                TrailingComma = _trailingComma,
                BracketSpacing = _bracketSpacing,
                ArrowParens = _arrowParens,
                QuoteProps = _quoteProps,
                EndOfLine = _endOfLine
            };

            FormatResult result = await host.FormatAsync(code, options, CancellationToken.None);

            // 成功与否都写进输出框：出错时用户要能直接看到 Prettier 的行列定位
            _outputText = result.Text;
            GUI.Text(_outputBox, result.Text);
            SaveState();

            GUI.Text(_statusLabel, result.Success
                ? $"Prettier {host.Version} · {code.Length} → {result.Text.Length} 字符 · {result.ElapsedMs} ms"
                : $"格式化失败（{result.ElapsedMs} ms），错误见输出框");
        }
        catch (Exception ex)
        {
            string message = $"格式化失败：{ex.Message}";
            _outputText = message;
            GUI.Text(_outputBox, message);
            SaveState();
            GUI.Text(_statusLabel, "格式化失败，错误见输出框");
        }
        finally
        {
            GUI.Enable(_formatButton);
            GUI.Text(_formatButton, "格式化");
        }
    }

    private async ValueTask CopyOutputAsync()
    {
        if (string.IsNullOrEmpty(_outputText))
        {
            return;
        }

        try
        {
            await _clipboard.SetClipboardTextAsync(_outputText);
            GUI.Text(_statusLabel, "已复制输出。");
        }
        catch (Exception ex)
        {
            GUI.Text(_statusLabel, $"复制失败：{ex.Message}");
        }
    }

    private ValueTask ClearFields()
    {
        _inputText = string.Empty;
        _outputText = string.Empty;
        GUI.Text(_inputBox, string.Empty);
        GUI.Text(_outputBox, "格式化结果会显示在这里；出错时错误信息也直接显示在这里。");
        SaveState();
        GUI.Text(_statusLabel, "已清空。");
        return ValueTask.CompletedTask;
    }

    // ---------------------------------------------------------------- 持久化

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

            if (state.Parser is not null) _parser = state.Parser;
            if (state.PrintWidth is not null) _printWidth = Math.Clamp(state.PrintWidth.Value, 40, 200);
            if (state.TabWidth is not null) _tabWidth = Math.Clamp(state.TabWidth.Value, 1, 8);
            if (state.UseTabs is not null) _useTabs = state.UseTabs.Value;
            if (state.Semi is not null) _semi = state.Semi.Value;
            if (state.SingleQuote is not null) _singleQuote = state.SingleQuote.Value;
            if (state.TrailingComma is not null) _trailingComma = state.TrailingComma;
            if (state.BracketSpacing is not null) _bracketSpacing = state.BracketSpacing.Value;
            if (state.ArrowParens is not null) _arrowParens = state.ArrowParens;
            if (state.QuoteProps is not null) _quoteProps = state.QuoteProps;
            if (state.EndOfLine is not null) _endOfLine = state.EndOfLine;
            if (state.Input is not null) _inputText = state.Input;
            if (state.Output is not null) _outputText = state.Output;
        }
        catch
        {
            // 设置损坏时用默认值，不影响使用
        }
    }

    private void SaveState()
    {
        try
        {
            PersistedState state = new()
            {
                Parser = _parser,
                PrintWidth = _printWidth,
                TabWidth = _tabWidth,
                UseTabs = _useTabs,
                Semi = _semi,
                SingleQuote = _singleQuote,
                TrailingComma = _trailingComma,
                BracketSpacing = _bracketSpacing,
                ArrowParens = _arrowParens,
                QuoteProps = _quoteProps,
                EndOfLine = _endOfLine,
                Input = _inputText,
                Output = _outputText
            };

            _settingsProvider.SetSetting(StateSetting, JsonSerializer.Serialize(state));
        }
        catch
        {
            // 持久化失败不影响格式化
        }
    }

    private sealed class PersistedState
    {
        public string? Parser { get; set; }
        public int? PrintWidth { get; set; }
        public int? TabWidth { get; set; }
        public bool? UseTabs { get; set; }
        public bool? Semi { get; set; }
        public bool? SingleQuote { get; set; }
        public string? TrailingComma { get; set; }
        public bool? BracketSpacing { get; set; }
        public string? ArrowParens { get; set; }
        public string? QuoteProps { get; set; }
        public string? EndOfLine { get; set; }
        public string? Input { get; set; }
        public string? Output { get; set; }
    }
}

using System.ComponentModel.Composition;
using System.Text.Json;
using DevToys.Api;
using GM.Core.SM4;
using GmCrypto.DevToys.Core;
using Sm4 = GM.Core.SM4.SM4;
using static DevToys.Api.GUI;

namespace GmCrypto.DevToys;

[Export(typeof(IGuiTool))]
[Name("GmSm4Cipher")]
[ToolDisplayInformation(
    // U+E78F = ic_fluent_lock_closed_20_regular
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uE78F',
    GroupName = PredefinedCommonToolGroupNames.EncodersDecoders,
    ResourceManagerAssemblyIdentifier = nameof(GmCryptoResourceAssemblyIdentifier),
    ResourceManagerBaseName = "GmCrypto.DevToys.Strings",
    ShortDisplayTitleResourceName = "Sm4ShortDisplayTitle",
    LongDisplayTitleResourceName = "Sm4LongDisplayTitle",
    DescriptionResourceName = "Sm4Description",
    AccessibleNameResourceName = "Sm4AccessibleName",
    SearchKeywordsResourceName = "Sm4SearchKeywords")]
internal sealed class Sm4CipherTool : IGuiTool
{
    private static readonly SettingDefinition<string> StateSetting =
        new("GmCrypto.Sm4.State", string.Empty);

    private readonly IClipboard _clipboard;

    private readonly ISettingsProvider _settingsProvider;

    private readonly bool _isLoading;

    // ---------------- 状态 ----------------
    private string _operation = "encrypt";
    private string _encoding = TextEncodings.Utf8;
    private bool _inputIsHex;
    private string _mode = "ecb";
    private string _padding = "pkcs7";
    private string _key = "0123456789abcdeffedcba9876543210";
    private string _iv = "000102030405060708090a0b0c0d0e0f";
    private string _inputText = string.Empty;
    private string _outputText = string.Empty;

    // ---------------- 控件 ----------------
    private readonly IUIInfoBar _infoBar;

    private readonly IUISelectDropDownList _operationSelect;

    private readonly IUISelectDropDownList _encodingSelect;

    private readonly IUISelectDropDownList _modeSelect;

    private readonly IUISelectDropDownList _paddingSelect;

    private readonly IUISwitch _hexSwitch;

    private readonly IUISingleLineTextInput _keyInput;

    private readonly IUISingleLineTextInput _ivInput;

    private readonly IUIMultiLineTextInput _inputBox;

    private readonly IUIMultiLineTextInput _outputBox;

    private readonly IUILabel _hintLabel;

    private readonly IUIButton _runButton;

    private readonly IUIButton _copyButton;

    private readonly IUIButton _clearButton;

    private readonly IUIButton _generateKeyButton;

    private readonly IUIButton _generateIvButton;

    private IUIElement _ivRow = default!;

    private UIToolView? _view;

    public UIToolView View => _view ??= BuildView();

    [ImportingConstructor]
    public Sm4CipherTool(IClipboard clipboard, ISettingsProvider settingsProvider)
    {
        _clipboard = clipboard;
        _settingsProvider = settingsProvider;

        LoadState();
        _isLoading = true;

        _infoBar = GUI.InfoBar("gmc-sm4-info").Closable();

        IUIDropDownListItem[] operationItems = UiKit.Items(
            ("加密", "encrypt"),
            ("解密", "decrypt"));

        IUIDropDownListItem[] encodingItems = UiKit.Items(
            ("UTF-8", TextEncodings.Utf8),
            ("GB18030", TextEncodings.Gb18030),
            ("ASCII", TextEncodings.Ascii));

        IUIDropDownListItem[] modeItems = UiKit.Items(
            ("ECB", "ecb"),
            ("CBC", "cbc"),
            ("CTR", "ctr"));

        IUIDropDownListItem[] paddingItems = UiKit.Items(
            ("PKCS7", "pkcs7"),
            ("None", "none"),
            ("Zeros", "zeros"));

        _operationSelect = GUI.SelectDropDownList("gmc-sm4-operation")
            .WithItems(operationItems)
            .Select(UiKit.IndexOf(operationItems, _operation))
            .OnItemSelected(item =>
            {
                if (_isLoading || item?.Value is not string value)
                {
                    return;
                }

                _operation = value;
                UpdateVisibility();
                SaveState();
            });

        _encodingSelect = GUI.SelectDropDownList("gmc-sm4-encoding")
            .WithItems(encodingItems)
            .Select(UiKit.IndexOf(encodingItems, _encoding))
            .OnItemSelected(item =>
            {
                if (_isLoading || item?.Value is not string value)
                {
                    return;
                }

                _encoding = value;
                SaveState();
            });

        _modeSelect = GUI.SelectDropDownList("gmc-sm4-mode")
            .WithItems(modeItems)
            .Select(UiKit.IndexOf(modeItems, _mode))
            .OnItemSelected(item =>
            {
                if (_isLoading || item?.Value is not string value)
                {
                    return;
                }

                _mode = value;
                UpdateVisibility();
                SaveState();
            });

        _paddingSelect = GUI.SelectDropDownList("gmc-sm4-padding")
            .WithItems(paddingItems)
            .Select(UiKit.IndexOf(paddingItems, _padding))
            .OnItemSelected(item =>
            {
                if (_isLoading || item?.Value is not string value)
                {
                    return;
                }

                _padding = value;
                SaveState();
            });

        _hexSwitch = GUI.Switch("gmc-sm4-hex");
        UiKit.SetSwitchState(_hexSwitch, _inputIsHex);
        _hexSwitch.OnToggle(on =>
        {
            _inputIsHex = on;
            SaveState();
        });

        _hintLabel = GUI.Label().Text(BuildHint()).WrapIfNeeded();

        _keyInput = UiKit.TextBox("gmc-sm4-key", _key, text =>
        {
            _key = text;
            SaveState();
        });

        _ivInput = UiKit.TextBox("gmc-sm4-iv", _iv, text =>
        {
            _iv = text;
            SaveState();
        });

        _inputBox = GUI.MultiLineTextInput("gmc-sm4-input")
            .Title("输入")
            .HideCommandBar()
            .Text(_inputText)
            .OnTextChanged(text =>
            {
                _inputText = text;
                SaveState();
            });

        _outputBox = GUI.MultiLineTextInput("gmc-sm4-output")
            .Title("输出")
            .HideCommandBar()
            .ReadOnly();

        _runButton = GUI.Button("gmc-sm4-run")
            .AccentAppearance()
            .Text("加密")
            .OnClick(RunAsync);

        _copyButton = GUI.Button("gmc-sm4-copy")
            .Text("复制结果")
            .OnClick(CopyOutputAsync);

        _clearButton = GUI.Button("gmc-sm4-clear")
            .Text("清空")
            .OnClick(ClearFields);

        _generateKeyButton = GUI.Button("gmc-sm4-gen-key")
            .Text("随机")
            .OnClick(() =>
            {
                _key = HexCodec.RandomHex(16);
                GUI.Text(_keyInput, _key);
                SaveState();
                return ValueTask.CompletedTask;
            });

        _generateIvButton = GUI.Button("gmc-sm4-gen-iv")
            .Text("随机")
            .OnClick(() =>
            {
                _iv = HexCodec.RandomHex(16);
                GUI.Text(_ivInput, _iv);
                SaveState();
                return ValueTask.CompletedTask;
            });

        // 控件文本必须在视图构建后写入
        _view = BuildView();
        GUI.Text(_inputBox, _inputText);
        GUI.Text(_outputBox, string.IsNullOrEmpty(_outputText)
            ? "尚未执行。填写参数与输入后点击按钮。"
            : _outputText);
        GUI.Text(_keyInput, _key);
        GUI.Text(_ivInput, _iv);
        UpdateVisibility();

        _isLoading = false;
    }

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
    }

    // ---------------------------------------------------------------- 视图

    private UIToolView BuildView()
    {
        _ivRow = UiKit.FieldWithButton("IV（16 字节 Hex，CBC/CTR 必填）", _ivInput, _generateIvButton);

        IUIElement parametersCard = UiKit.Card(
            "SM4 参数",
            UiKit.LabeledField("操作", _operationSelect),
            _hintLabel,
            UiKit.LabeledField("模式", _modeSelect),
            UiKit.LabeledField("填充", _paddingSelect),
            UiKit.FieldWithButton("密钥 Key（16 字节 Hex）", _keyInput, _generateKeyButton),
            _ivRow,
            UiKit.LabeledField("文本编码", _encodingSelect),
            GUI.Setting("gmc-sm4-setting-hex")
                .Title("输入按 Hex 解析")
                .Description("加密时输入按十六进制解读；解密时结果也以 Hex 显示")
                .InteractiveElement(_hexSwitch));

        IUIElement inputCard = UiKit.Card("输入", _inputBox);

        IUIElement outputCard = UiKit.Card(
            "输出",
            _outputBox,
            GUI.Grid()
                .Columns(GUI.Fraction(1), GUI.Fraction(1), GUI.Fraction(1))
                .ColumnMediumSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, _runButton),
                    GUI.Cell(0, 1, 1, 1, _copyButton),
                    GUI.Cell(0, 2, 1, 1, _clearButton)));

        return new UIToolView(
            GUI.Stack().Vertical().LargeSpacing().WithChildren(
                _infoBar,
                UiKit.Layout(parametersCard, inputCard, outputCard)));
    }

    private void UpdateVisibility()
    {
        bool decrypt = _operation == "decrypt";
        UiKit.Show(_ivRow, _mode != "ecb");
        GUI.Text(_runButton, decrypt ? "解密" : "加密");
        GUI.Text(_hintLabel, BuildHint());
    }

    private string BuildHint()
        => _operation == "decrypt"
            ? "解密：输入必须是 Hex 密文，模式 / 填充 / 密钥 / IV 要与加密方完全一致。"
            : "加密：输出 Hex 密文。ECB 不需要 IV；CBC/CTR 需要 16 字节 IV。";

    // ---------------------------------------------------------------- 执行

    private async ValueTask RunAsync()
    {
        bool decrypt = _operation == "decrypt";
        GUI.Disable(_runButton);
        GUI.Close(_infoBar);

        try
        {
            string result = await Task.Run(() => decrypt ? Decrypt() : Encrypt());
            _outputText = result;
            SaveState();
            GUI.Text(_outputBox, result);

            GUI.Title(_infoBar, decrypt ? "解密完成" : "加密完成");
            GUI.Description(_infoBar, BuildHint());
            GUI.Success(_infoBar);
            GUI.Open(_infoBar);
        }
        catch (Exception ex)
        {
            GUI.Title(_infoBar, decrypt ? "解密失败" : "加密失败");
            GUI.Description(_infoBar, ex.Message);
            GUI.Error(_infoBar);
            GUI.Open(_infoBar);
        }
        finally
        {
            GUI.Text(_runButton, decrypt ? "解密" : "加密");
            GUI.Enable(_runButton);
        }
    }

    private string Encrypt()
    {
        byte[] plain = _inputIsHex
            ? HexCodec.Parse(_inputText, "输入")
            : TextEncodings.Resolve(_encoding).GetBytes(_inputText);

        return HexCodec.ToHex(Sm4.CreateDefault().Encrypt(plain, BuildParameters()));
    }

    private string Decrypt()
    {
        byte[] cipher = HexCodec.Parse(_inputText, "密文");
        byte[] plain = Sm4.CreateDefault().Decrypt(cipher, BuildParameters());
        return _inputIsHex
            ? HexCodec.ToHex(plain)
            : TextEncodings.Resolve(_encoding).GetString(plain);
    }

    private Sm4Parameters BuildParameters()
    {
        byte[] key = HexCodec.ParseFixed(_key, 16, "SM4 密钥");

        Sm4Mode mode = _mode switch
        {
            "cbc" => Sm4Mode.Cbc,
            "ctr" => Sm4Mode.Ctr,
            _ => Sm4Mode.Ecb
        };

        Sm4Padding padding = _padding switch
        {
            "none" => Sm4Padding.None,
            "zeros" => Sm4Padding.Zeros,
            _ => Sm4Padding.PKCS7
        };

        byte[]? iv = mode == Sm4Mode.Ecb ? null : HexCodec.ParseFixed(_iv, 16, "SM4 IV");
        return new Sm4Parameters(key, iv, mode, padding);
    }

    private async ValueTask CopyOutputAsync()
    {
        if (string.IsNullOrEmpty(_outputText))
        {
            GUI.Title(_infoBar, "没有可复制的内容");
            GUI.Description(_infoBar, "先执行一次加解密。");
            GUI.Warning(_infoBar);
            GUI.Open(_infoBar);
            return;
        }

        await _clipboard.SetClipboardTextAsync(_outputText);
        GUI.Title(_infoBar, "已复制到剪贴板");
        GUI.Description(_infoBar, $"{_outputText.Length} 个字符");
        GUI.Success(_infoBar);
        GUI.Open(_infoBar);
    }

    private void ClearFields()
    {
        _inputText = string.Empty;
        _outputText = string.Empty;
        GUI.Text(_inputBox, string.Empty);
        GUI.Text(_outputBox, string.Empty);
        SaveState();
    }

    // ---------------------------------------------------------------- 状态持久化

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

            if (state.Operation is not null) _operation = state.Operation;
            if (state.Encoding is not null) _encoding = state.Encoding;
            if (state.InputIsHex is not null) _inputIsHex = state.InputIsHex.Value;
            if (state.Mode is not null) _mode = state.Mode;
            if (state.Padding is not null) _padding = state.Padding;
            if (state.Key is not null) _key = state.Key;
            if (state.Iv is not null) _iv = state.Iv;
            if (state.Input is not null) _inputText = state.Input;
            if (state.Output is not null) _outputText = state.Output;
        }
        catch
        {
            // 状态损坏时回落到默认值。
        }
    }

    private void SaveState()
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            var state = new PersistedState
            {
                Operation = _operation,
                Encoding = _encoding,
                InputIsHex = _inputIsHex,
                Mode = _mode,
                Padding = _padding,
                Key = _key,
                Iv = _iv,
                Input = _inputText,
                Output = _outputText
            };

            _settingsProvider.SetSetting(StateSetting, JsonSerializer.Serialize(state));
        }
        catch
        {
            // 设置写入失败不影响加解密。
        }
    }

    private sealed class PersistedState
    {
        public string? Operation { get; set; }
        public string? Encoding { get; set; }
        public bool? InputIsHex { get; set; }
        public string? Mode { get; set; }
        public string? Padding { get; set; }
        public string? Key { get; set; }
        public string? Iv { get; set; }
        public string? Input { get; set; }
        public string? Output { get; set; }
    }
}

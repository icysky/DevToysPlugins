using System.ComponentModel.Composition;
using System.Text.Json;
using DevToys.Api;
using GmCrypto.DevToys.Core;
using Sm2 = GM.Core.SM2.SM2;
using Sm2SignerManaged = GM.Core.SM2.Sm2SignerManaged;
using static DevToys.Api.GUI;

namespace GmCrypto.DevToys;

[Export(typeof(IGuiTool))]
[Name("GmSm2Cipher")]
[ToolDisplayInformation(
    // U+F4B6 = ic_fluent_key_20_regular
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uF4B6',
    GroupName = PredefinedCommonToolGroupNames.EncodersDecoders,
    ResourceManagerAssemblyIdentifier = nameof(GmCryptoResourceAssemblyIdentifier),
    ResourceManagerBaseName = "GmCrypto.DevToys.Strings",
    ShortDisplayTitleResourceName = "Sm2CryptShortDisplayTitle",
    LongDisplayTitleResourceName = "Sm2CryptLongDisplayTitle",
    DescriptionResourceName = "Sm2CryptDescription",
    AccessibleNameResourceName = "Sm2CryptAccessibleName",
    SearchKeywordsResourceName = "Sm2CryptSearchKeywords")]
internal sealed class Sm2CipherTool : IGuiTool
{
    private static readonly SettingDefinition<string> StateSetting =
        new("GmCrypto.Sm2Cipher.State", string.Empty);

    private readonly IClipboard _clipboard;

    private readonly ISettingsProvider _settingsProvider;

    private readonly bool _isLoading;

    // ---------------- 状态 ----------------
    private string _operation = "encrypt";
    private string _encoding = TextEncodings.Utf8;
    private bool _inputIsHex;
    private string _cipherOrder = "c1c3c2";
    private string _privateKey = string.Empty;
    private string _publicKey = string.Empty;
    private string _inputText = string.Empty;
    private string _outputText = string.Empty;

    // ---------------- 控件 ----------------
    private readonly IUIInfoBar _infoBar;

    private readonly IUISelectDropDownList _operationSelect;

    private readonly IUISelectDropDownList _encodingSelect;

    private readonly IUISelectDropDownList _cipherOrderSelect;

    private readonly IUISwitch _hexSwitch;

    private readonly IUISingleLineTextInput _privateKeyInput;

    private readonly IUISingleLineTextInput _publicKeyInput;

    private readonly IUIMultiLineTextInput _inputBox;

    private readonly IUIMultiLineTextInput _outputBox;

    private readonly IUILabel _hintLabel;

    private readonly IUIButton _runButton;

    private readonly IUIButton _copyButton;

    private readonly IUIButton _clearButton;

    private readonly IUIButton _generateKeyPairButton;

    private IUIElement _keyPairRow = default!;
    private IUIElement _privateKeyRow = default!;
    private IUIElement _publicKeyRow = default!;

    private UIToolView? _view;

    public UIToolView View => _view ??= BuildView();

    [ImportingConstructor]
    public Sm2CipherTool(IClipboard clipboard, ISettingsProvider settingsProvider)
    {
        _clipboard = clipboard;
        _settingsProvider = settingsProvider;

        LoadState();
        _isLoading = true;

        _infoBar = GUI.InfoBar("gmc-sm2crypt-info").Closable();

        IUIDropDownListItem[] operationItems = UiKit.Items(
            ("加密", "encrypt"),
            ("解密", "decrypt"));

        IUIDropDownListItem[] encodingItems = UiKit.Items(
            ("UTF-8", TextEncodings.Utf8),
            ("GB18030", TextEncodings.Gb18030),
            ("ASCII", TextEncodings.Ascii));

        IUIDropDownListItem[] cipherOrderItems = UiKit.Items(
            ("C1C3C2（国标 / 默认）", "c1c3c2"),
            ("C1C2C3（部分老旧系统）", "c1c2c3"));

        _operationSelect = GUI.SelectDropDownList("gmc-sm2crypt-operation")
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

        _encodingSelect = GUI.SelectDropDownList("gmc-sm2crypt-encoding")
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

        _cipherOrderSelect = GUI.SelectDropDownList("gmc-sm2crypt-order")
            .WithItems(cipherOrderItems)
            .Select(UiKit.IndexOf(cipherOrderItems, _cipherOrder))
            .OnItemSelected(item =>
            {
                if (_isLoading || item?.Value is not string value)
                {
                    return;
                }

                _cipherOrder = value;
                SaveState();
            });

        _hexSwitch = GUI.Switch("gmc-sm2crypt-hex");
        UiKit.SetSwitchState(_hexSwitch, _inputIsHex);
        _hexSwitch.OnToggle(on =>
        {
            _inputIsHex = on;
            SaveState();
        });

        _hintLabel = GUI.Label().Text(BuildHint()).WrapIfNeeded();

        _privateKeyInput = UiKit.TextBox("gmc-sm2crypt-private", _privateKey, text =>
        {
            _privateKey = text;
            SaveState();
        });

        _publicKeyInput = UiKit.TextBox("gmc-sm2crypt-public", _publicKey, text =>
        {
            _publicKey = text;
            SaveState();
        });

        _inputBox = GUI.MultiLineTextInput("gmc-sm2crypt-input")
            .Title("输入")
            .HideCommandBar()
            .Text(_inputText)
            .OnTextChanged(text =>
            {
                _inputText = text;
                SaveState();
            });

        _outputBox = GUI.MultiLineTextInput("gmc-sm2crypt-output")
            .Title("输出")
            .HideCommandBar()
            .ReadOnly();

        _runButton = GUI.Button("gmc-sm2crypt-run")
            .AccentAppearance()
            .Text("加密")
            .OnClick(RunAsync);

        _copyButton = GUI.Button("gmc-sm2crypt-copy")
            .Text("复制结果")
            .OnClick(CopyOutputAsync);

        _clearButton = GUI.Button("gmc-sm2crypt-clear")
            .Text("清空")
            .OnClick(ClearFields);

        _generateKeyPairButton = GUI.Button("gmc-sm2crypt-gen-keypair")
            .Text("生成密钥对")
            .OnClick(GenerateKeyPairAsync);

        // 控件文本必须在视图构建后写入
        _view = BuildView();
        GUI.Text(_inputBox, _inputText);
        GUI.Text(_outputBox, string.IsNullOrEmpty(_outputText)
            ? "尚未执行。填写密钥与输入后开始。"
            : _outputText);
        GUI.Text(_privateKeyInput, _privateKey);
        GUI.Text(_publicKeyInput, _publicKey);
        UpdateVisibility();

        _isLoading = false;
    }

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
    }

    // ---------------------------------------------------------------- 视图

    private UIToolView BuildView()
    {
        _privateKeyRow = UiKit.LabeledField("私钥（32 字节 Hex）", _privateKeyInput);
        _publicKeyRow = UiKit.LabeledField("公钥（65 字节 Hex，04 开头）", _publicKeyInput);
        _keyPairRow = UiKit.Card(
            "密钥对",
            GUI.Label().Text("一键生成 32 字节私钥 + 65 字节公钥（Hex）。").WrapIfNeeded(),
            _generateKeyPairButton);

        IUIElement parametersCard = UiKit.Card(
            "SM2 参数",
            UiKit.LabeledField("操作", _operationSelect),
            _hintLabel,
            _keyPairRow,
            _publicKeyRow,
            _privateKeyRow,
            UiKit.LabeledField("密文顺序", _cipherOrderSelect),
            UiKit.LabeledField("文本编码", _encodingSelect),
            GUI.Setting("gmc-sm2crypt-setting-hex")
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
        UiKit.Show(_privateKeyRow, decrypt);
        UiKit.Show(_publicKeyRow, !decrypt);
        GUI.Text(_runButton, decrypt ? "解密" : "加密");
        GUI.Text(_hintLabel, BuildHint());
    }

    private string BuildHint()
        => _operation == "decrypt"
            ? "解密：输入 Hex 密文，密文顺序与密钥要与加密方保持一致。"
            : "加密：使用公钥加密；默认输出 C1C3C2，部分老旧系统要求 C1C2C3。";

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
        byte[] publicKey = HexCodec.ParseFixed(_publicKey, 65, "SM2 公钥");
        byte[] plain = _inputIsHex
            ? HexCodec.Parse(_inputText, "输入")
            : TextEncodings.Resolve(_encoding).GetBytes(_inputText);

        byte[] cipher = Sm2.CreateEncryptor().Encrypt(publicKey, plain);
        if (_cipherOrder == "c1c2c3")
        {
            cipher = Sm2CipherFormat.C1C3C2ToC1C2C3(cipher);
        }

        return HexCodec.ToHex(cipher);
    }

    private string Decrypt()
    {
        byte[] privateKey = HexCodec.ParseFixed(_privateKey, 32, "SM2 私钥");
        byte[] cipher = HexCodec.Parse(_inputText, "密文");
        if (_cipherOrder == "c1c2c3")
        {
            cipher = Sm2CipherFormat.C1C2C3ToC1C3C2(cipher);
        }

        byte[] plain = Sm2.CreateEncryptor().Decrypt(privateKey, cipher);
        return _inputIsHex
            ? HexCodec.ToHex(plain)
            : TextEncodings.Resolve(_encoding).GetString(plain);
    }

    private async ValueTask GenerateKeyPairAsync()
    {
        GUI.Text(_generateKeyPairButton, "生成中…");
        GUI.Disable(_generateKeyPairButton);
        GUI.Close(_infoBar);

        try
        {
            (string privateHex, string publicHex) = await Task.Run(() =>
            {
                byte[] privateKey = Sm2SignerManaged.GeneratePrivateKey();
                byte[] publicKey = Sm2SignerManaged.DerivePublicKey(privateKey);
                return (HexCodec.ToHex(privateKey), HexCodec.ToHex(publicKey));
            });

            _privateKey = privateHex;
            _publicKey = publicHex;
            GUI.Text(_privateKeyInput, privateHex);
            GUI.Text(_publicKeyInput, publicHex);
            SaveState();

            GUI.Title(_infoBar, "已生成 SM2 密钥对");
            GUI.Description(_infoBar, "私钥 32 字节、公钥 65 字节（04 开头），均为 Hex。");
            GUI.Success(_infoBar);
            GUI.Open(_infoBar);
        }
        catch (Exception ex)
        {
            GUI.Title(_infoBar, "生成密钥对失败");
            GUI.Description(_infoBar, ex.Message);
            GUI.Error(_infoBar);
            GUI.Open(_infoBar);
        }
        finally
        {
            GUI.Text(_generateKeyPairButton, "生成密钥对");
            GUI.Enable(_generateKeyPairButton);
        }
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
            if (state.CipherOrder is not null) _cipherOrder = state.CipherOrder;
            if (state.PrivateKey is not null) _privateKey = state.PrivateKey;
            if (state.PublicKey is not null) _publicKey = state.PublicKey;
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
                CipherOrder = _cipherOrder,
                PrivateKey = _privateKey,
                PublicKey = _publicKey,
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
        public string? CipherOrder { get; set; }
        public string? PrivateKey { get; set; }
        public string? PublicKey { get; set; }
        public string? Input { get; set; }
        public string? Output { get; set; }
    }
}

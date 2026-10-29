using System.ComponentModel.Composition;
using System.Text;
using System.Text.Json;
using DevToys.Api;
using GmCrypto.DevToys.Core;
using Sm2 = GM.Core.SM2.SM2;
using Sm2SignerManaged = GM.Core.SM2.Sm2SignerManaged;
using static DevToys.Api.GUI;

namespace GmCrypto.DevToys;

[Export(typeof(IGuiTool))]
[Name("GmSm2Signature")]
[ToolDisplayInformation(
    // U+F6BE = ic_fluent_shield_20_regular
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uF6BE',
    GroupName = PredefinedCommonToolGroupNames.EncodersDecoders,
    ResourceManagerAssemblyIdentifier = nameof(GmCryptoResourceAssemblyIdentifier),
    ResourceManagerBaseName = "GmCrypto.DevToys.Strings",
    ShortDisplayTitleResourceName = "Sm2SignShortDisplayTitle",
    LongDisplayTitleResourceName = "Sm2SignLongDisplayTitle",
    DescriptionResourceName = "Sm2SignDescription",
    AccessibleNameResourceName = "Sm2SignAccessibleName",
    SearchKeywordsResourceName = "Sm2SignSearchKeywords")]
internal sealed class Sm2SignatureTool : IGuiTool
{
    /// <summary>GB/T 32918 标准默认 UserId。</summary>
    private const string DefaultUserId = "1234567812345678";

    private static readonly SettingDefinition<string> StateSetting =
        new("GmCrypto.Sm2Signature.State", string.Empty);

    private readonly IClipboard _clipboard;

    private readonly ISettingsProvider _settingsProvider;

    private readonly bool _isLoading;

    // ---------------- 状态 ----------------
    private string _operation = "sign";
    private string _encoding = TextEncodings.Utf8;
    private bool _inputIsHex;
    private bool _rawSignature;
    private string _privateKey = string.Empty;
    private string _publicKey = string.Empty;
    private string _userId = DefaultUserId;
    private string _signature = string.Empty;
    private string _inputText = string.Empty;
    private string _outputText = string.Empty;

    // ---------------- 控件 ----------------
    private readonly IUIInfoBar _infoBar;

    private readonly IUISelectDropDownList _operationSelect;

    private readonly IUISelectDropDownList _encodingSelect;

    private readonly IUISwitch _hexSwitch;

    private readonly IUISwitch _rawSwitch;

    private readonly IUISingleLineTextInput _privateKeyInput;

    private readonly IUISingleLineTextInput _publicKeyInput;

    private readonly IUISingleLineTextInput _userIdInput;

    private readonly IUISingleLineTextInput _signatureInput;

    private readonly IUIMultiLineTextInput _inputBox;

    private readonly IUIMultiLineTextInput _outputBox;

    private readonly IUILabel _hintLabel;

    private readonly IUIButton _runButton;

    private readonly IUIButton _copyButton;

    private readonly IUIButton _clearButton;

    private readonly IUIButton _generateKeyPairButton;

    private IUIElement _keyPairRow = default!;
    private IUIElement _rawRow = default!;
    private IUIElement _privateKeyRow = default!;
    private IUIElement _publicKeyRow = default!;
    private IUIElement _signatureRow = default!;

    private UIToolView? _view;

    public UIToolView View => _view ??= BuildView();

    [ImportingConstructor]
    public Sm2SignatureTool(IClipboard clipboard, ISettingsProvider settingsProvider)
    {
        _clipboard = clipboard;
        _settingsProvider = settingsProvider;

        LoadState();
        _isLoading = true;

        _infoBar = GUI.InfoBar("gmc-sm2sign-info").Closable();

        IUIDropDownListItem[] operationItems = UiKit.Items(
            ("签名", "sign"),
            ("验签", "verify"));

        IUIDropDownListItem[] encodingItems = UiKit.Items(
            ("UTF-8", TextEncodings.Utf8),
            ("GB18030", TextEncodings.Gb18030),
            ("ASCII", TextEncodings.Ascii));

        _operationSelect = GUI.SelectDropDownList("gmc-sm2sign-operation")
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

        _encodingSelect = GUI.SelectDropDownList("gmc-sm2sign-encoding")
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

        _hexSwitch = GUI.Switch("gmc-sm2sign-hex");
        UiKit.SetSwitchState(_hexSwitch, _inputIsHex);
        _hexSwitch.OnToggle(on =>
        {
            _inputIsHex = on;
            SaveState();
        });

        _rawSwitch = GUI.Switch("gmc-sm2sign-raw");
        UiKit.SetSwitchState(_rawSwitch, _rawSignature);
        _rawSwitch.OnToggle(on =>
        {
            _rawSignature = on;
            SaveState();
        });

        _hintLabel = GUI.Label().Text(BuildHint()).WrapIfNeeded();

        _privateKeyInput = UiKit.TextBox("gmc-sm2sign-private", _privateKey, text =>
        {
            _privateKey = text;
            SaveState();
        });

        _publicKeyInput = UiKit.TextBox("gmc-sm2sign-public", _publicKey, text =>
        {
            _publicKey = text;
            SaveState();
        });

        _userIdInput = UiKit.TextBox("gmc-sm2sign-userid", _userId, text =>
        {
            _userId = text;
            SaveState();
        });

        _signatureInput = UiKit.TextBox("gmc-sm2sign-signature", _signature, text =>
        {
            _signature = text;
            SaveState();
        });

        _inputBox = GUI.MultiLineTextInput("gmc-sm2sign-input")
            .Title("输入（被签名的消息）")
            .HideCommandBar()
            .Text(_inputText)
            .OnTextChanged(text =>
            {
                _inputText = text;
                SaveState();
            });

        _outputBox = GUI.MultiLineTextInput("gmc-sm2sign-output")
            .Title("输出")
            .HideCommandBar()
            .ReadOnly();

        _runButton = GUI.Button("gmc-sm2sign-run")
            .AccentAppearance()
            .Text("签名")
            .OnClick(RunAsync);

        _copyButton = GUI.Button("gmc-sm2sign-copy")
            .Text("复制结果")
            .OnClick(CopyOutputAsync);

        _clearButton = GUI.Button("gmc-sm2sign-clear")
            .Text("清空")
            .OnClick(ClearFields);

        _generateKeyPairButton = GUI.Button("gmc-sm2sign-gen-keypair")
            .Text("生成密钥对")
            .OnClick(GenerateKeyPairAsync);

        // 控件文本必须在视图构建后写入
        _view = BuildView();
        GUI.Text(_inputBox, _inputText);
        GUI.Text(_outputBox, string.IsNullOrEmpty(_outputText)
            ? "尚未执行。填写密钥与消息后开始。"
            : _outputText);
        GUI.Text(_privateKeyInput, _privateKey);
        GUI.Text(_publicKeyInput, _publicKey);
        GUI.Text(_userIdInput, _userId);
        GUI.Text(_signatureInput, _signature);
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
        _keyPairRow = UiKit.Card(
            "密钥对",
            GUI.Label().Text("一键生成 32 字节私钥 + 65 字节公钥（Hex）。").WrapIfNeeded(),
            _generateKeyPairButton);
        _publicKeyRow = UiKit.LabeledField("公钥（65 字节 Hex，04 开头）", _publicKeyInput);
        _signatureRow = UiKit.LabeledField("签名（Hex，DER 或 64 字节 raw）", _signatureInput);
        _rawRow = GUI.Setting("gmc-sm2sign-setting-raw")
            .Title("签名输出 raw(r||s)")
            .Description("勾选输出 64 字节 raw，默认输出 DER")
            .InteractiveElement(_rawSwitch);

        IUIElement parametersCard = UiKit.Card(
            "SM2 参数",
            UiKit.LabeledField("操作", _operationSelect),
            _hintLabel,
            _keyPairRow,
            _privateKeyRow,
            _publicKeyRow,
            UiKit.LabeledField("UserId（Z 值，留空用标准默认值）", _userIdInput),
            _signatureRow,
            _rawRow,
            UiKit.LabeledField("文本编码", _encodingSelect),
            GUI.Setting("gmc-sm2sign-setting-hex")
                .Title("输入按 Hex 解析")
                .Description("勾选后输入框按十六进制字节解读")
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
        bool verify = _operation == "verify";
        UiKit.Show(_privateKeyRow, !verify);
        UiKit.Show(_publicKeyRow, verify);
        UiKit.Show(_signatureRow, verify);
        UiKit.Show(_rawRow, !verify);
        GUI.Text(_runButton, verify ? "验签" : "签名");
        GUI.Text(_hintLabel, BuildHint());
    }

    private string BuildHint()
        => _operation == "verify"
            ? "验签：填写公钥与签名 Hex（DER 或 64 字节 raw），结果显示在输出区。"
            : "签名：使用私钥对消息签名；UserId 留空时使用 GB/T 32918 默认值。";

    // ---------------------------------------------------------------- 执行

    private async ValueTask RunAsync()
    {
        bool verify = _operation == "verify";
        GUI.Disable(_runButton);
        GUI.Close(_infoBar);

        try
        {
            string result = await Task.Run(() => verify ? Verify() : Sign());
            _outputText = result;
            SaveState();
            GUI.Text(_outputBox, result);

            GUI.Title(_infoBar, verify ? "验签完成" : "签名完成");
            GUI.Description(_infoBar, result);
            GUI.Success(_infoBar);
            GUI.Open(_infoBar);
        }
        catch (Exception ex)
        {
            GUI.Title(_infoBar, verify ? "验签失败" : "签名失败");
            GUI.Description(_infoBar, ex.Message);
            GUI.Error(_infoBar);
            GUI.Open(_infoBar);
        }
        finally
        {
            GUI.Text(_runButton, verify ? "验签" : "签名");
            GUI.Enable(_runButton);
        }
    }

    private string Sign()
    {
        byte[] privateKey = HexCodec.ParseFixed(_privateKey, 32, "SM2 私钥");
        byte[] message = GetMessageBytes();
        byte[] signature = Sm2.CreateSigner().Sign(privateKey, message, GetUserIdBytes());

        if (!_rawSignature)
        {
            return HexCodec.ToHex(signature);
        }

        if (!Sm2SignerManaged.TryDerToRaw(signature, out byte[] raw))
        {
            throw new InvalidOperationException("DER 签名转换为 raw 失败。");
        }

        return HexCodec.ToHex(raw);
    }

    private string Verify()
    {
        byte[] publicKey = HexCodec.ParseFixed(_publicKey, 65, "SM2 公钥");
        byte[] message = GetMessageBytes();
        byte[] signature = HexCodec.Parse(_signature, "签名");

        // Core 的 Verify 只接受 DER；64 字节输入按 raw(r||s) 自动转换。
        if (signature.Length == 64)
        {
            signature = Sm2SignerManaged.RawToDer(signature);
        }

        bool ok = Sm2.CreateSigner().Verify(publicKey, message, signature, GetUserIdBytes());
        return ok
            ? "验签通过：签名与消息、公钥匹配。"
            : "验签失败：签名与消息或公钥不匹配。";
    }

    private byte[] GetMessageBytes()
        => _inputIsHex
            ? HexCodec.Parse(_inputText, "输入")
            : TextEncodings.Resolve(_encoding).GetBytes(_inputText);

    private byte[] GetUserIdBytes()
        => string.IsNullOrWhiteSpace(_userId)
            ? Array.Empty<byte>()
            : TextEncodings.Resolve(_encoding).GetBytes(_userId.Trim());

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
            GUI.Description(_infoBar, "先执行一次签名或验签。");
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
        _signature = string.Empty;
        GUI.Text(_inputBox, string.Empty);
        GUI.Text(_outputBox, string.Empty);
        GUI.Text(_signatureInput, string.Empty);
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
            if (state.RawSignature is not null) _rawSignature = state.RawSignature.Value;
            if (state.PrivateKey is not null) _privateKey = state.PrivateKey;
            if (state.PublicKey is not null) _publicKey = state.PublicKey;
            if (state.UserId is not null) _userId = state.UserId;
            if (state.Signature is not null) _signature = state.Signature;
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
                RawSignature = _rawSignature,
                PrivateKey = _privateKey,
                PublicKey = _publicKey,
                UserId = _userId,
                Signature = _signature,
                Input = _inputText,
                Output = _outputText
            };

            _settingsProvider.SetSetting(StateSetting, JsonSerializer.Serialize(state));
        }
        catch
        {
            // 设置写入失败不影响签名验签。
        }
    }

    private sealed class PersistedState
    {
        public string? Operation { get; set; }
        public string? Encoding { get; set; }
        public bool? InputIsHex { get; set; }
        public bool? RawSignature { get; set; }
        public string? PrivateKey { get; set; }
        public string? PublicKey { get; set; }
        public string? UserId { get; set; }
        public string? Signature { get; set; }
        public string? Input { get; set; }
        public string? Output { get; set; }
    }
}

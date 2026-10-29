using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using DevToys.Api;
using TestCertGenerator.CertGen;
using static DevToys.Api.GUI;

namespace MyCert.DevToys;

[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(MyCertCertificateGeneratorResourceAssemblyIdentifier))]
internal sealed class MyCertCertificateGeneratorResourceAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
        => ValueTask.FromResult(Array.Empty<FontDefinition>());
}

[Export(typeof(IGuiTool))]
[Name("CertificateGenerator")]
[ToolDisplayInformation(
    // FluentSystemIcons 是 DevToys 自带的图标字体。码位必须真实存在于该字体，否则侧边栏只显示占位圆圈。
    // 原值 \uE8A5 是圆形轮廓图标（与「动态口令」撞成同一个圆），改用证书图标：
    // U+F27E = ic_fluent_certificate_20_regular
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uF27E',
    GroupName = PredefinedCommonToolGroupNames.Generators,
    ResourceManagerAssemblyIdentifier = nameof(MyCertCertificateGeneratorResourceAssemblyIdentifier),
    ResourceManagerBaseName = "MyCert.DevToys.Strings",
    ShortDisplayTitleResourceName = "ShortDisplayTitle",
    LongDisplayTitleResourceName = "LongDisplayTitle",
    DescriptionResourceName = "Description",
    AccessibleNameResourceName = "AccessibleName",
    SearchKeywordsResourceName = "SearchKeywords")]
internal sealed class CertificateGuiTool : IGuiTool
{
    private static readonly (string Label, X509KeyUsageFlags Flag)[] KuDefinitions =
    [
        ("数字签名", X509KeyUsageFlags.DigitalSignature),
        ("不可否认（防抵赖）", X509KeyUsageFlags.NonRepudiation),
        ("密钥加密", X509KeyUsageFlags.KeyEncipherment),
        ("数据加密", X509KeyUsageFlags.DataEncipherment),
        ("密钥协商", X509KeyUsageFlags.KeyAgreement),
        ("证书签名", X509KeyUsageFlags.KeyCertSign),
        ("CRL 签名", X509KeyUsageFlags.CrlSign),
        ("仅加密", X509KeyUsageFlags.EncipherOnly),
        ("仅解密", X509KeyUsageFlags.DecipherOnly)
    ];

    private static readonly (string Label, string Oid)[] EkuDefinitions =
    [
        ("服务器身份验证", "1.3.6.1.5.5.7.3.1"),
        ("客户端身份验证", "1.3.6.1.5.5.7.3.2"),
        ("代码签名", "1.3.6.1.5.5.7.3.3"),
        ("文档签名", "1.3.6.1.4.1.311.10.3.12"),
        ("邮件保护", "1.3.6.1.5.5.7.3.4"),
        ("时间戳", "1.3.6.1.5.5.7.3.8"),
        ("OCSP 签名", "1.3.6.1.5.5.7.3.9"),
        ("任意用途", "2.5.29.37.0")
    ];

    /// <summary>
    /// 用途预设定义。下拉项、下拉索引、建议 CN（终端证书 + 自签 CA）与导出子目录名全部由此派生，
    /// 避免多处重复维护。CN 采用一看即懂的功能描述风格（内部开发用，不填域名）。
    /// Folder 为空字符串表示导出到根目录（不建子目录）；CN 为空表示不变更。
    /// </summary>
    private static readonly (string Value, string Label, string Folder, string SubjectCn, string IssuerCn)[] PresetDefinitions =
    [
        ("https-server", "HTTPS 服务器", "HTTPS-Server", "HTTP Server Dev Cert", "HTTP Server Root CA"),
        ("https-client", "HTTPS 客户端", "HTTPS-Client", "HTTP Client Dev Cert", "HTTP Client Root CA"),
        ("document", "电子签名 / PDF", "Document-Signing", "Document Signing Cert", "Document Signing Root CA"),
        ("code", "代码签名", "Code-Signing", "Code Signing Cert", "Code Signing Root CA"),
        ("timestamp", "时间戳", "Timestamp", "Timestamp Cert", "Timestamp Root CA"),
        ("data-protection", "数据保护（DataProtection）", "DataProtection", "Data Protection Cert", "Data Protection Root CA"),
        ("ofd", "OFD 签名 / 印章", "OFD-Signing", "OFD Signing Cert", "OFD Signing Root CA"),
        ("custom", "自定义（不变更）", string.Empty, string.Empty, string.Empty)
    ];

    /// <summary>ISO 3166-1 alpha-2 国家/地区代码（证书 DN 的 C 字段要求 2 位代码）。</summary>
    private static readonly string[] IsoCountryCodes =
    [
        "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT", "AU", "AW", "AX", "AZ",
        "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS", "BT", "BV", "BW", "BY", "BZ",
        "CA", "CC", "CD", "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN", "CO", "CR", "CU", "CV", "CW", "CX", "CY", "CZ",
        "DE", "DJ", "DK", "DM", "DO", "DZ",
        "EC", "EE", "EG", "EH", "ER", "ES", "ET",
        "FI", "FJ", "FK", "FM", "FO", "FR",
        "GA", "GB", "GD", "GE", "GF", "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS", "GT", "GU", "GW", "GY",
        "HK", "HM", "HN", "HR", "HT", "HU",
        "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT",
        "JE", "JM", "JO", "JP",
        "KE", "KG", "KH", "KI", "KM", "KN", "KP", "KR", "KW", "KY", "KZ",
        "LA", "LB", "LC", "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY",
        "MA", "MC", "MD", "ME", "MF", "MG", "MH", "MK", "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS", "MT", "MU", "MV", "MW", "MX", "MY", "MZ",
        "NA", "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU", "NZ",
        "OM",
        "PA", "PE", "PF", "PG", "PH", "PK", "PL", "PM", "PN", "PR", "PS", "PT", "PW", "PY",
        "QA",
        "RE", "RO", "RS", "RU", "RW",
        "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI", "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS", "ST", "SV", "SX", "SY", "SZ",
        "TC", "TD", "TF", "TG", "TH", "TJ", "TK", "TL", "TM", "TN", "TO", "TR", "TT", "TV", "TW", "TZ",
        "UA", "UG", "UM", "US", "UY", "UZ",
        "VA", "VC", "VE", "VG", "VI", "VN", "VU",
        "WF", "WS",
        "YE", "YT",
        "ZA", "ZM", "ZW"
    ];

    private static readonly char[] TokenSeparators = [',', ';', '\r', '\n', '\t', ' '];

    private static readonly SettingDefinition<string> StateSetting =
        new("MyCert.CertificateGenerator.State", string.Empty);

    private readonly IFileStorage _fileStorage;

    private readonly ISettingsProvider _settingsProvider;

    private readonly StringBuilder _log = new();

    private readonly bool _isLoading;

    private readonly IUIInfoBar _infoBar;

    private readonly IUILabel _algorithmHint;

    private readonly IUISingleLineTextInput _sanTextInput;

    private readonly IUISingleLineTextInput _subjectCnInput;

    private readonly IUISingleLineTextInput _issuerCnInput;

    private readonly IUISwitch _sanSwitch;

    private readonly IUISwitch[] _kuSwitches = new IUISwitch[KuDefinitions.Length];

    private readonly IUISwitch[] _ekuSwitches = new IUISwitch[EkuDefinitions.Length];

    private readonly IUISingleLineTextInput _customEkuInput;

    private readonly IUISwitch _kuCriticalSwitch;

    private readonly IUISwitch _ekuCriticalSwitch;

    private readonly IUISingleLineTextInput _folderInput;

    private readonly IUIPasswordInput _passwordInput;

    private readonly IUISwitch _pfxSwitch;

    private readonly IUISwitch _cerSwitch;

    private readonly IUISwitch _pemSwitch;

    private readonly IUISwitch _caSwitch;

    private readonly IUIButton _generateButton;

    private readonly IUIButton _parseButton;

    private readonly IUIMultiLineTextInput _logInput;

    private readonly IUIMultiLineTextInput _parseResultInput;

    private UIToolView? _view;

    // ---- 状态（由控件事件写入） ----
    private string _algorithm = "rsa2048";
    private string _preset = "ofd";
    private double _validityDays = 365;

    private string _subjectCn = "localhost";
    private string _subjectO = "My Test Org";
    private string _subjectOu = "Dev Team";
    private string _subjectC = "CN";
    private string _subjectSt = "Guangdong";
    private string _subjectL = "Shenzhen";

    private string _issuerCn = "My Test Root CA";
    private string _issuerO = "My Test Org";
    private string _issuerOu = "Certificate Authority";
    private string _issuerC = "CN";
    private string _issuerSt = "Guangdong";
    private string _issuerL = "Shenzhen";

    private bool _includeSan;
    private string _sanEntries = "localhost, 127.0.0.1";

    private X509KeyUsageFlags _kuFlags = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation;
    private bool _kuCritical = true;
    private bool _ekuCritical = true;
    private string _customEkuText = string.Empty;

    private string _outputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MyCertOutput");
    private string _pfxPassword = "123456";
    private bool _exportPfx = true;
    private bool _exportCer = true;
    private bool _exportPem = true;
    private bool _exportIssuerCer = true;

    public UIToolView View => _view ??= BuildView();

    [ImportingConstructor]
    public CertificateGuiTool(IFileStorage fileStorage, ISettingsProvider settingsProvider)
    {
        _fileStorage = fileStorage;
        _settingsProvider = settingsProvider;

        LoadState();
        _isLoading = true;

        _infoBar = GUI.InfoBar("mcert-info").Closable();

        _algorithmHint = GUI.Label()
            .Text("RSA 支持 2048/3072/4096 位（由上方算法决定），摘要 SHA-256。")
            .WrapIfNeeded();

        _sanTextInput = GUI.SingleLineTextInput("mcert-san")
            .HideCommandBar()
            .Text(_sanEntries)
            .OnTextChanged(text =>
            {
                _sanEntries = text;
                SaveState();
            });

        _subjectCnInput = GUI.SingleLineTextInput("mcert-subject-cn")
            .HideCommandBar()
            .Text(_subjectCn)
            .OnTextChanged(text =>
            {
                _subjectCn = text;
                SaveState();
            });

        _issuerCnInput = GUI.SingleLineTextInput("mcert-issuer-cn")
            .HideCommandBar()
            .Text(_issuerCn)
            .OnTextChanged(text =>
            {
                _issuerCn = text;
                SaveState();
            });

        if (!_includeSan)
        {
            GUI.Disable(_sanTextInput);
        }

        _sanSwitch = GUI.Switch("mcert-san-enabled");
        if (_includeSan)
        {
            GUI.On(_sanSwitch);
        }
        else
        {
            GUI.Off(_sanSwitch);
        }

        _sanSwitch.OnToggle(enabled =>
        {
            _includeSan = enabled;
            if (enabled)
            {
                GUI.Enable(_sanTextInput);
            }
            else
            {
                GUI.Disable(_sanTextInput);
            }

            SaveState();
        });

        for (int i = 0; i < KuDefinitions.Length; i++)
        {
            int index = i;
            (string label, X509KeyUsageFlags flag) = KuDefinitions[i];
            IUISwitch toggle = GUI.Switch($"mcert-ku-{index}");
            if ((_kuFlags & flag) == flag)
            {
                GUI.On(toggle);
            }
            else
            {
                GUI.Off(toggle);
            }

            toggle.OnToggle(on =>
            {
                if (on)
                {
                    _kuFlags |= flag;
                }
                else
                {
                    _kuFlags &= ~flag;
                }

                SaveState();
            });

            _kuSwitches[i] = toggle;
        }

        for (int i = 0; i < EkuDefinitions.Length; i++)
        {
            int index = i;
            string oid = EkuDefinitions[i].Oid;
            IUISwitch toggle = GUI.Switch($"mcert-eku-{index}");
            if (_ekuOids.Contains(oid))
            {
                GUI.On(toggle);
            }
            else
            {
                GUI.Off(toggle);
            }

            toggle.OnToggle(on =>
            {
                if (on)
                {
                    _ekuOids.Add(oid);
                }
                else
                {
                    _ekuOids.Remove(oid);
                }

                SaveState();
            });

            _ekuSwitches[i] = toggle;
        }

        _customEkuInput = GUI.SingleLineTextInput("mcert-eku-custom")
            .HideCommandBar()
            .Text(_customEkuText)
            .OnTextChanged(text =>
            {
                _customEkuText = text;
                SaveState();
            });

        _kuCriticalSwitch = GUI.Switch("mcert-ku-critical");
        if (_kuCritical)
        {
            GUI.On(_kuCriticalSwitch);
        }
        else
        {
            GUI.Off(_kuCriticalSwitch);
        }

        _kuCriticalSwitch.OnToggle(on =>
        {
            _kuCritical = on;
            SaveState();
        });

        _ekuCriticalSwitch = GUI.Switch("mcert-eku-critical");
        if (_ekuCritical)
        {
            GUI.On(_ekuCriticalSwitch);
        }
        else
        {
            GUI.Off(_ekuCriticalSwitch);
        }

        _ekuCriticalSwitch.OnToggle(on =>
        {
            _ekuCritical = on;
            SaveState();
        });

        _folderInput = GUI.SingleLineTextInput("mcert-folder")
            .HideCommandBar()
            .Text(_outputDirectory)
            .OnTextChanged(text =>
            {
                _outputDirectory = text;
                SaveState();
            });

        _passwordInput = GUI.PasswordInput("mcert-password")
            .HideCommandBar()
            .Text(_pfxPassword)
            .OnTextChanged(text =>
            {
                _pfxPassword = text;
                SaveState();
            });

        _pfxSwitch = GUI.Switch("mcert-export-pfx");
        _cerSwitch = GUI.Switch("mcert-export-cer");
        _pemSwitch = GUI.Switch("mcert-export-pem");
        _caSwitch = GUI.Switch("mcert-export-ca");

        SetSwitchState(_pfxSwitch, _exportPfx);
        SetSwitchState(_cerSwitch, _exportCer);
        SetSwitchState(_pemSwitch, _exportPem);
        SetSwitchState(_caSwitch, _exportIssuerCer);

        _pfxSwitch.OnToggle(on => { _exportPfx = on; SaveState(); });
        _cerSwitch.OnToggle(on => { _exportCer = on; SaveState(); });
        _pemSwitch.OnToggle(on => { _exportPem = on; SaveState(); });
        _caSwitch.OnToggle(on => { _exportIssuerCer = on; SaveState(); });

        _generateButton = GUI.Button("mcert-generate")
            .AccentAppearance()
            .Text("生成证书")
            .OnClick(GenerateAsync);

        _parseButton = GUI.Button("mcert-parse")
            .Text("解析证书")
            .OnClick(ParseCertificateAsync);

        _logInput = GUI.MultiLineTextInput("mcert-log")
            .Title("执行日志")
            .HideCommandBar()
            .ReadOnly();

        _parseResultInput = GUI.MultiLineTextInput("mcert-parse-result")
            .Title("证书详情")
            .HideCommandBar()
            .ReadOnly();

        _log.AppendLine("选择算法与用途预设，填写主题信息后点击“生成证书”。");

        // 控件文本必须在视图构建后写入（DevToys 的 UI 控件模型在注册前不接收更新）。
        _view = BuildView();
        GUI.Text(_sanTextInput, _sanEntries);
        GUI.Text(_subjectCnInput, _subjectCn);
        GUI.Text(_issuerCnInput, _issuerCn);
        GUI.Text(_customEkuInput, _customEkuText);
        GUI.Text(_folderInput, _outputDirectory);
        GUI.Text(_passwordInput, _pfxPassword);
        GUI.Text(_logInput, _log.ToString());
        GUI.Text(_parseResultInput, "尚未解析证书。点击“解析证书”选择证书文件。");
        _isLoading = false;
    }

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
    }

    private UIToolView BuildView()
    {
        int algorithmIndex = _algorithm switch
        {
            "rsa3072" => 1,
            "rsa4096" => 2,
            "sm2" => 3,
            _ => 0
        };

        IUIDropDownListItem[] algorithmItems =
        [
            GUI.Item("RSA 2048", "rsa2048"),
            GUI.Item("RSA 3072", "rsa3072"),
            GUI.Item("RSA 4096", "rsa4096"),
            GUI.Item("SM2（国密，固定 256 位）", "sm2")
        ];

        IUISelectDropDownList algorithmSelect = GUI.SelectDropDownList("mcert-algorithm")
            .WithItems(algorithmItems)
            .Select(algorithmItems[algorithmIndex])
            .OnItemSelected(item =>
            {
                if (item?.Value is not string algorithm)
                {
                    return;
                }

                _algorithm = algorithm;
                GUI.Text(_algorithmHint, _algorithm == "sm2"
                    ? "SM2 固定 sm2p256v1（256 位），签名算法 SM3withSM2。"
                    : "RSA 支持 2048/3072/4096 位（由上方算法决定），摘要 SHA-256。");
                SaveState();
            });

        int presetIndex = Array.FindIndex(
            PresetDefinitions,
            definition => string.Equals(definition.Value, _preset, StringComparison.Ordinal));
        if (presetIndex < 0)
        {
            presetIndex = Array.FindIndex(PresetDefinitions, definition => definition.Value == "custom");
        }

        IUIDropDownListItem[] presetItems = Array.ConvertAll(
            PresetDefinitions,
            definition => GUI.Item(definition.Label, definition.Value));

        IUISelectDropDownList presetSelect = GUI.SelectDropDownList("mcert-preset")
            .WithItems(presetItems)
            .Select(presetItems[presetIndex])
            .OnItemSelected(ApplyPreset);

        IUINumberInput validityInput = GUI.NumberInput("mcert-validity", 1, 36500, 1)
            .HideCommandBar()
            .Value(_validityDays)
            .OnValueChanged(value =>
            {
                _validityDays = value;
                SaveState();
            });

        IUIElement basicCard = Card(
            "生成参数",
            LabeledField("密钥算法", algorithmSelect),
            _algorithmHint,
            Field("用途预设", "应用预设会覆盖通用名称 CN、KU / EKU、SAN，并按预设类型写入导出子目录", presetSelect),
            LabeledField("有效期（天）", validityInput));

        IUIDropDownListItem[] countryItems = CreateCountryItems();
        int defaultCountryIndex = Array.FindIndex(
            countryItems,
            item => string.Equals(item.Value as string, "CN", StringComparison.OrdinalIgnoreCase));
        if (defaultCountryIndex < 0)
        {
            defaultCountryIndex = 0;
        }

        int subjectCountryIndex = FindCountryIndex(countryItems, _subjectC, defaultCountryIndex);
        int issuerCountryIndex = FindCountryIndex(countryItems, _issuerC, defaultCountryIndex);

        IUIElement subjectCard = Card(
            "颁发给（Subject）",
            LabeledFieldWithRole("通用名称 CN", "使用者 Subject", _subjectCnInput),
            LabeledField("组织 O", CreateTextField("mcert-subject-o", _subjectO, text => _subjectO = text)),
            LabeledField("部门 OU", CreateTextField("mcert-subject-ou", _subjectOu, text => _subjectOu = text)),
            LabeledField("国家/地区 C（ISO 3166）", GUI.SelectDropDownList("mcert-subject-c")
                .WithItems(countryItems)
                .Select(subjectCountryIndex)
                .OnItemSelected(item =>
                {
                    _subjectC = item?.Value as string ?? "CN";
                    SaveState();
                })),
            LabeledField("省/州 ST", CreateTextField("mcert-subject-st", _subjectSt, text => _subjectSt = text)),
            LabeledField("城市 L", CreateTextField("mcert-subject-l", _subjectL, text => _subjectL = text)));

        IUIElement issuerCard = Card(
            "颁发者（Issuer，自签 CA）",
            LabeledFieldWithRole("通用名称 CN", "颁发者 Issuer", _issuerCnInput),
            LabeledField("组织 O", CreateTextField("mcert-issuer-o", _issuerO, text => _issuerO = text)),
            LabeledField("部门 OU", CreateTextField("mcert-issuer-ou", _issuerOu, text => _issuerOu = text)),
            LabeledField("国家/地区 C（ISO 3166）", GUI.SelectDropDownList("mcert-issuer-c")
                .WithItems(countryItems)
                .Select(issuerCountryIndex)
                .OnItemSelected(item =>
                {
                    _issuerC = item?.Value as string ?? "CN";
                    SaveState();
                })),
            LabeledField("省/州 ST", CreateTextField("mcert-issuer-st", _issuerSt, text => _issuerSt = text)),
            LabeledField("城市 L", CreateTextField("mcert-issuer-l", _issuerL, text => _issuerL = text)));

        IUIElement usageCard = Card(
            "用途扩展（勾选）",
            GUI.Setting("mcert-setting-san").Title("包含 SAN").InteractiveElement(_sanSwitch),
            Field("SAN 列表", "多个域名/IP 用逗号分隔；留空时自动使用 CN（并附加 localhost）", _sanTextInput),
            GUI.Grid()
                .Columns(new UIGridLength(1, UIGridUnitType.Fraction), new UIGridLength(1, UIGridUnitType.Fraction))
                .ColumnLargeSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, BuildSwitchColumn("密钥用法（Key Usage）", KuDefinitions, _kuSwitches, _kuCriticalSwitch, "KU 标记为关键")),
                    GUI.Cell(0, 1, 1, 1, BuildEkuColumn())));

        IUIElement leftBottomCard = Card(
            "导出目录与导出格式",
            FieldWithButton(
                "导出目录",
                _folderInput,
                GUI.Button("mcert-browse").Text("浏览...").OnClick(BrowseFolderAsync)),
            FieldWithButton(
                "PFX 密码",
                _passwordInput,
                GUI.Button("mcert-generate-password").Text("生成密码").OnClick(GeneratePasswordAsync)),
            GUI.Setting("mcert-setting-pfx").Title("导出 PFX").InteractiveElement(_pfxSwitch),
            GUI.Setting("mcert-setting-cer").Title("导出 CER").InteractiveElement(_cerSwitch),
            GUI.Setting("mcert-setting-pem").Title("导出 PEM + KEY").InteractiveElement(_pemSwitch),
            GUI.Setting("mcert-setting-ca").Title("导出颁发者 CA 证书").InteractiveElement(_caSwitch));

        IUIElement leftTop = GUI.Stack().Vertical().LargeSpacing().WithChildren(
            basicCard,
            subjectCard,
            issuerCard);

        IUIElement layoutGrid = GUI.Grid()
            .Columns(new UIGridLength(1, UIGridUnitType.Fraction), new UIGridLength(1, UIGridUnitType.Fraction))
            .Rows(
                new UIGridLength(0, UIGridUnitType.Auto),
                new UIGridLength(0, UIGridUnitType.Auto))
            .ColumnLargeSpacing()
            .RowLargeSpacing()
            .Cells(
                GUI.Cell(0, 0, 1, 1, leftTop),
                GUI.Cell(0, 1, 1, 1, usageCard),
                GUI.Cell(1, 0, 1, 1, leftBottomCard),
                GUI.Cell(1, 1, 1, 1, _parseResultInput));

        IUIElement tipLabel = GUI.Label()
            .Text("提示：SM2 证书无法导入 Windows 证书存储，请在 OFD 签章工具中直接使用 PFX 或 PEM + KEY。")
            .WrapIfNeeded();

        IUIElement actionButtons = GUI.Grid()
            .Columns(new UIGridLength(1, UIGridUnitType.Fraction), new UIGridLength(1, UIGridUnitType.Fraction))
            .ColumnMediumSpacing()
            .Cells(
                GUI.Cell(0, 0, 1, 1, _generateButton),
                GUI.Cell(0, 1, 1, 1, _parseButton));

        return new UIToolView(
            GUI.Stack().Vertical().LargeSpacing().WithChildren(
                _infoBar,
                layoutGrid,
                tipLabel,
                actionButtons,
                _logInput));
    }

    private IUIElement BuildEkuColumn()
    {
        List<IUIElement> children =
        [
            GUI.Label().Text("扩展密钥用法（Enhanced Key Usage）").WrapIfNeeded()
        ];

        for (int i = 0; i < EkuDefinitions.Length; i++)
        {
            children.Add(GUI.Setting($"mcert-setting-eku-{i}")
                .Title(EkuDefinitions[i].Label)
                .Description(EkuDefinitions[i].Oid)
                .InteractiveElement(_ekuSwitches[i]));
        }

        children.Add(GUI.Setting("mcert-setting-eku-critical")
            .Title("EKU 标记为关键")
            .InteractiveElement(_ekuCriticalSwitch));

        children.Add(Field("自定义 EKU OID", "多个 OID 用逗号分隔", _customEkuInput));

        return GUI.Stack().Vertical().SmallSpacing().WithChildren([.. children]);
    }

    private static IUIElement BuildSwitchColumn(
        string title,
        (string Label, X509KeyUsageFlags Flag)[] definitions,
        IUISwitch[] switches,
        IUISwitch criticalSwitch,
        string criticalTitle)
    {
        List<IUIElement> children =
        [
            GUI.Label().Text(title).WrapIfNeeded()
        ];

        for (int i = 0; i < definitions.Length; i++)
        {
            children.Add(GUI.Setting($"mcert-setting-ku-{i}")
                .Title(definitions[i].Label)
                .InteractiveElement(switches[i]));
        }

        children.Add(GUI.Setting("mcert-setting-ku-critical")
            .Title(criticalTitle)
            .InteractiveElement(criticalSwitch));

        return GUI.Stack().Vertical().SmallSpacing().WithChildren([.. children]);
    }

    private static IUIElement Card(string title, params IUIElement[] children)
        => GUI.Card(title, GUI.Stack().Vertical().MediumSpacing().WithChildren(children));

    private static IUIElement Field(string label, string description, IUIElement input)
    {
        return GUI.Stack().Vertical().SmallSpacing().WithChildren(
            GUI.Label().Text(label),
            GUI.Label().Text(description).WrapIfNeeded(),
            input);
    }

    private static IUIElement FieldWithButton(string label, IUIElement input, IUIElement button)
    {
        return GUI.Stack().Vertical().SmallSpacing().WithChildren(
            GUI.Label().Text(label),
            GUI.Grid()
                .Columns(
                    new UIGridLength(1, UIGridUnitType.Fraction),
                    new UIGridLength(0, UIGridUnitType.Auto))
                .ColumnMediumSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, input),
                    GUI.Cell(0, 1, 1, 1, button)));
    }

    private static IUIElement LabeledField(string label, IUIElement input)
    {
        return GUI.Stack().Vertical().SmallSpacing().WithChildren(
            GUI.Label().Text(label),
            input);
    }

    /// <summary>
    /// 带右侧角色标注的字段行。「使用者 / 颁发者」两张卡片里都有「通用名称 CN」，
    /// 卡片滚出视野后光看标签区分不开，所以在输入框右侧补一个 Caption 级角色标识。
    /// </summary>
    private static IUIElement LabeledFieldWithRole(string label, string role, IUIElement input)
    {
        return GUI.Stack().Vertical().SmallSpacing().WithChildren(
            GUI.Label().Text(label),
            GUI.Grid()
                .Columns(GUI.Fraction(1), UIGridLength.Auto)
                .ColumnMediumSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, input),
                    GUI.Cell(0, 1, 1, 1, GUI.Label().Style(UILabelStyle.Caption).Text(role))));
    }

    private IUISingleLineTextInput CreateTextField(string id, string value, Action<string> setter)
    {
        return GUI.SingleLineTextInput(id)
            .HideCommandBar()
            .Text(value)
            .OnTextChanged(text =>
            {
                setter(text);
                SaveState();
            });
    }

    private static int FindCountryIndex(IUIDropDownListItem[] items, string code, int fallback)
    {
        int index = Array.FindIndex(
            items,
            item => string.Equals(item.Value as string, code, StringComparison.OrdinalIgnoreCase));

        return index >= 0 ? index : fallback;
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

    private static IUIDropDownListItem[] CreateCountryItems()
    {
        var items = new IUIDropDownListItem[IsoCountryCodes.Length];
        for (int i = 0; i < IsoCountryCodes.Length; i++)
        {
            string code = IsoCountryCodes[i];
            string name = GetCountryDisplayName(code);
            string text = string.Equals(name, code, StringComparison.OrdinalIgnoreCase)
                ? code
                : $"{code} - {name}";
            items[i] = GUI.Item(text, code);
        }

        return items;
    }

    private static string GetCountryDisplayName(string code)
    {
        try
        {
            string name = new RegionInfo(code).DisplayName;
            return string.IsNullOrWhiteSpace(name) ? code : name;
        }
        catch (ArgumentException)
        {
            return code;
        }
    }

    private async ValueTask BrowseFolderAsync()
    {
        string? folder = await _fileStorage.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(folder))
        {
            _outputDirectory = folder;
            GUI.Text(_folderInput, folder);
            SaveState();
        }
    }

    private ValueTask GeneratePasswordAsync()
    {
        string password = CreateSecurePassword(12);
        _pfxPassword = password;
        GUI.Text(_passwordInput, password);
        Log("已生成 12 位随机安全密码（已填入密码框）。");
        GUI.Text(_logInput, _log.ToString());
        SaveState();
        return ValueTask.CompletedTask;
    }

    private static string CreateSecurePassword(int length)
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*";
        const string all = upper + lower + digits + symbols;

        var chars = new List<char>(length)
        {
            PickRandom(upper),
            PickRandom(lower),
            PickRandom(digits),
            PickRandom(symbols)
        };

        while (chars.Count < length)
        {
            chars.Add(PickRandom(all));
        }

        for (int i = chars.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string([.. chars]);
    }

    private static char PickRandom(string pool)
        => pool[RandomNumberGenerator.GetInt32(pool.Length)];

    private void ApplyPreset(IUIDropDownListItem? item)
    {
        if (_isLoading || item?.Value is not string preset)
        {
            return;
        }

        _preset = preset;

        string suggestedCn = GetPresetSubjectCn(preset);
        if (!string.IsNullOrEmpty(suggestedCn))
        {
            _subjectCn = suggestedCn;
            GUI.Text(_subjectCnInput, suggestedCn);
        }

        string suggestedIssuerCn = GetPresetIssuerCn(preset);
        if (!string.IsNullOrEmpty(suggestedIssuerCn))
        {
            _issuerCn = suggestedIssuerCn;
            GUI.Text(_issuerCnInput, suggestedIssuerCn);
        }

        switch (_preset)
        {
            case "https-server":
                SetUsage(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    "1.3.6.1.5.5.7.3.1, 1.3.6.1.5.5.7.3.2",
                    includeSan: true);
                break;
            case "https-client":
                SetUsage(X509KeyUsageFlags.DigitalSignature, "1.3.6.1.5.5.7.3.2", includeSan: true);
                break;
            case "document":
                SetUsage(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
                    "1.3.6.1.5.5.7.3.3, 1.3.6.1.4.1.311.10.3.12",
                    includeSan: false);
                break;
            case "code":
                SetUsage(X509KeyUsageFlags.DigitalSignature, "1.3.6.1.5.5.7.3.3", includeSan: false);
                break;
            case "timestamp":
                SetUsage(X509KeyUsageFlags.DigitalSignature, "1.3.6.1.5.5.7.3.8", includeSan: false);
                break;
            case "data-protection":
                SetUsage(
                    X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment,
                    "2.5.29.37.0",
                    includeSan: false);
                break;
            case "ofd":
                SetUsage(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
                    "1.3.6.1.4.1.311.10.3.12, 2.5.29.37.0",
                    includeSan: false);
                break;
            default:
                break;
        }
    }

    private static string GetPresetFolderName(string preset)
    {
        foreach ((string value, _, string folder, _, _) in PresetDefinitions)
        {
            if (string.Equals(value, preset, StringComparison.Ordinal))
            {
                return folder;
            }
        }

        return string.Empty;
    }

    private static string GetPresetSubjectCn(string preset)
    {
        foreach ((string value, _, _, string subjectCn, _) in PresetDefinitions)
        {
            if (string.Equals(value, preset, StringComparison.Ordinal))
            {
                return subjectCn;
            }
        }

        return string.Empty;
    }

    private static string GetPresetIssuerCn(string preset)
    {
        foreach ((string value, _, _, _, string issuerCn) in PresetDefinitions)
        {
            if (string.Equals(value, preset, StringComparison.Ordinal))
            {
                return issuerCn;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 预设目录下再按用途类型创建分类子目录（如 HTTPS-Server、DataProtection）；
    /// “自定义”预设 Folder 为空，直接导出到所选目录。
    /// </summary>
    private string ResolveExportDirectory(string rootDirectory)
    {
        string folder = GetPresetFolderName(_preset);
        return string.IsNullOrWhiteSpace(folder) ? rootDirectory : Path.Combine(rootDirectory, folder);
    }

    private void SetUsage(X509KeyUsageFlags ku, string ekuOids, bool includeSan)
    {
        HashSet<string> oids = ekuOids
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < KuDefinitions.Length; i++)
        {
            if ((ku & KuDefinitions[i].Flag) == KuDefinitions[i].Flag)
            {
                GUI.On(_kuSwitches[i]);
            }
            else
            {
                GUI.Off(_kuSwitches[i]);
            }
        }

        for (int i = 0; i < EkuDefinitions.Length; i++)
        {
            if (oids.Contains(EkuDefinitions[i].Oid))
            {
                GUI.On(_ekuSwitches[i]);
            }
            else
            {
                GUI.Off(_ekuSwitches[i]);
            }
        }

        _kuFlags = ku;
        _customEkuText = string.Empty;
        GUI.Text(_customEkuInput, string.Empty);
        _includeSan = includeSan;

        if (includeSan)
        {
            GUI.On(_sanSwitch);
            GUI.Enable(_sanTextInput);
        }
        else
        {
            GUI.Off(_sanSwitch);
            GUI.Disable(_sanTextInput);
        }

        SaveState();
    }

    private async ValueTask GenerateAsync()
    {
        try
        {
            GUI.Text(_generateButton, "生成中...");
            GUI.Disable(_generateButton);
            GUI.Close(_infoBar);

            string outputDirectory = _outputDirectory.Trim();
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                string? picked = await _fileStorage.PickFolderAsync();
                if (string.IsNullOrWhiteSpace(picked))
                {
                    throw new InvalidOperationException("请先选择导出目录。");
                }

                outputDirectory = picked;
                _outputDirectory = picked;
                GUI.Text(_folderInput, picked);
            }

            if (_kuFlags == 0)
            {
                throw new InvalidOperationException("请至少勾选一个 Key Usage。");
            }

            DateTimeOffset notBefore = DateTimeOffset.Now.AddMinutes(-5);
            DateTimeOffset notAfter = notBefore.AddDays(_validityDays);

            var spec = new CertificateSpec
            {
                Algorithm = _algorithm == "sm2" ? CertificateAlgorithm.Sm2 : CertificateAlgorithm.Rsa,
                SubjectDn = BuildDistinguishedName(_subjectCn, _subjectO, _subjectOu, _subjectC, _subjectSt, _subjectL),
                IssuerDn = BuildDistinguishedName(_issuerCn, _issuerO, _issuerOu, _issuerC, _issuerSt, _issuerL),
                RsaKeySize = _algorithm switch
                {
                    "rsa3072" => 3072,
                    "rsa4096" => 4096,
                    _ => 2048
                },
                KeyUsage = _kuFlags,
                KeyUsageCritical = _kuCritical,
                ExtendedKeyUsageOids = BuildEkuOidList(),
                ExtendedKeyUsageCritical = _ekuCritical,
                IncludeSubjectAlternativeName = _includeSan,
                SubjectAlternativeNames = _includeSan
                    ? NormalizeSanEntries(_subjectCn, _sanEntries)
                    : Array.Empty<string>(),
                NotBefore = notBefore,
                NotAfter = notAfter,
                CreatePfx = _exportPfx,
                PfxPassword = _pfxPassword,
                FriendlyName = _subjectCn
            };

            if (_exportPfx && string.IsNullOrEmpty(_pfxPassword))
            {
                throw new InvalidOperationException("已选择导出 PFX，请填写 PFX 密码。");
            }

            string targetDirectory = ResolveExportDirectory(outputDirectory);
            Directory.CreateDirectory(targetDirectory);
            if (!string.Equals(targetDirectory, outputDirectory, StringComparison.OrdinalIgnoreCase))
            {
                Log($"导出子目录（按用途预设）: {targetDirectory}");
            }

            Log($"算法: {spec.Algorithm}, 有效期: {_validityDays:0} 天");

            GeneratedCertificate generated = await Task.Run(() => CertificateGenerator.Generate(spec));
            using (generated)
            {
                WriteOutputs(generated, targetDirectory);
            }

            Log($"导出目录: {targetDirectory}");
            Log("证书生成完成。");
            GUI.Text(_logInput, _log.ToString());

            GUI.Title(_infoBar, "证书生成成功");
            GUI.Description(_infoBar, targetDirectory);
            GUI.Success(_infoBar);
            GUI.Open(_infoBar);
        }
        catch (Exception ex)
        {
            Log($"错误: {ex.Message}");
            GUI.Text(_logInput, _log.ToString());

            GUI.Title(_infoBar, "证书生成失败");
            GUI.Description(_infoBar, ex.Message);
            GUI.Error(_infoBar);
            GUI.Open(_infoBar);
        }
        finally
        {
            GUI.Text(_generateButton, "生成证书");
            GUI.Enable(_generateButton);
        }
    }

    private List<string> BuildEkuOidList()
    {
        List<string> oids = [.. _ekuOids];
        foreach (string token in _customEkuText.Split(
                     TokenSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string oid = token.Trim().TrimEnd('.');
            if (!IsOid(oid))
            {
                throw new InvalidOperationException($"无法识别的 EKU OID: {token}");
            }

            if (!oids.Contains(oid, StringComparer.OrdinalIgnoreCase))
            {
                oids.Add(oid);
            }
        }

        if (oids.Count == 0)
        {
            throw new InvalidOperationException("请至少勾选一个 Enhanced Key Usage。");
        }

        return oids;
    }

    private async ValueTask ParseCertificateAsync()
    {
        try
        {
            SandboxedFileReader? selected = await _fileStorage.PickOpenFileAsync(
                ".cer", ".crt", ".pem", ".pfx", ".p12");

            if (selected is null)
            {
                return;
            }

            using SandboxedFileReader file = selected;
            string extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            string? password = null;
            if (extension is ".pfx" or ".p12")
            {
                password = await PromptForPasswordAsync();
                if (password is null)
                {
                    return;
                }
            }

            byte[] bytes;
            using (Stream stream = await file.GetNewAccessToFileContentAsync(CancellationToken.None))
            using (var memory = new MemoryStream())
            {
                await stream.CopyToAsync(memory);
                bytes = memory.ToArray();
            }

            FileInfo tempFile = _fileStorage.CreateSelfDestroyingTempFile(extension);
            string details;
            try
            {
                File.WriteAllBytes(tempFile.FullName, bytes);
                details = CertificateInspector.Describe(tempFile.FullName, password);
                details = details.Replace(tempFile.FullName, file.FileName, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try
                {
                    tempFile.Delete();
                }
                catch
                {
                    // 临时文件由 DevToys 在退出时兜底清理。
                }
            }

            GUI.Text(_parseResultInput, details);
            GUI.Show(_parseResultInput);

            GUI.Title(_infoBar, "证书解析完成");
            GUI.Description(_infoBar, file.FileName);
            GUI.Success(_infoBar);
            GUI.Open(_infoBar);
        }
        catch (Exception ex)
        {
            Log($"解析错误: {ex}");
            GUI.Text(_logInput, _log.ToString());

            GUI.Title(_infoBar, "证书解析失败");
            GUI.Description(_infoBar, ex.Message);
            GUI.Error(_infoBar);
            GUI.Open(_infoBar);
        }
    }

    private async Task<string?> PromptForPasswordAsync()
    {
        var completion = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        string suffix = Guid.NewGuid().ToString("N")[..8];
        string enteredPassword = string.Empty;

        IUIPasswordInput passwordInput = GUI.PasswordInput($"mcert-parse-password-{suffix}")
            .HideCommandBar()
            .OnTextChanged(text => enteredPassword = text);

        IUIElement content = GUI.Stack().Vertical().SmallSpacing().WithChildren(
            GUI.Label().Text("该 PFX/P12 证书受密码保护，请输入密码："),
            passwordInput);

        UIDialog? dialog = null;

        IUIElement footer = GUI.Stack().Horizontal().SmallSpacing().WithChildren(
            GUI.Button($"mcert-parse-ok-{suffix}")
                .AccentAppearance()
                .Text("确定")
                .OnClick(() =>
                {
                    completion.TrySetResult(enteredPassword);
                    dialog?.Close();
                    return ValueTask.CompletedTask;
                }),
            GUI.Button($"mcert-parse-cancel-{suffix}")
                .Text("取消")
                .OnClick(() =>
                {
                    completion.TrySetResult(null);
                    dialog?.Close();
                    return ValueTask.CompletedTask;
                }));

        dialog = await View.OpenDialogAsync(content, footer, isDismissible: false);
        _ = dialog.DialogCloseAwaiter.ContinueWith(
            _ => completion.TrySetResult(null),
            TaskScheduler.Default);

        return await completion.Task;
    }

    private void WriteOutputs(GeneratedCertificate generated, string outputDirectory)
    {
        string safeName = MakeSafeFileName(_subjectCn);
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        string basePath = Path.Combine(outputDirectory, $"{safeName}_{timestamp}");

        if (_exportPfx && generated.PfxBytes is { Length: > 0 })
        {
            string pfxPath = basePath + ".pfx";
            File.WriteAllBytes(pfxPath, generated.PfxBytes);
            Log($"已导出: {pfxPath}");
        }

        if (_exportCer)
        {
            string cerPath = basePath + ".cer";
            File.WriteAllBytes(cerPath, generated.CertificateDer);
            Log($"已导出: {cerPath}");
        }

        if (_exportPem)
        {
            string certPemPath = basePath + ".pem";
            string keyPemPath = basePath + ".key";
            File.WriteAllText(certPemPath, generated.CertificatePem);
            File.WriteAllText(keyPemPath, generated.PrivateKeyPem);
            Log($"已导出: {certPemPath}");
            Log($"已导出: {keyPemPath}");
        }

        if (_exportIssuerCer)
        {
            string issuerCerPath = basePath + "_issuer_ca.cer";
            File.WriteAllBytes(issuerCerPath, generated.IssuerCertificateDer);
            Log($"已导出: {issuerCerPath}");
        }

        string readmeFilePath = Path.Combine(
            outputDirectory,
            $"ReadMe_{Path.GetFileName(basePath)}.txt");
        string readmeContent = OutputReadmeBuilder.Build(
            subject: _subjectCn,
            baseName: Path.GetFileName(basePath),
            keyDescription: generated.KeyDescription,
            signatureAlgorithm: generated.SignatureAlgorithm,
            pfxPassword: _pfxPassword,
            exportedPfx: _exportPfx && generated.PfxBytes is { Length: > 0 },
            exportedCer: _exportCer,
            exportedPem: _exportPem,
            exportedIssuerCa: _exportIssuerCer,
            generatedAt: DateTime.Now);
        File.WriteAllText(readmeFilePath, readmeContent, Encoding.UTF8);
        Log($"已导出: {readmeFilePath}");
    }

    private void Log(string message)
    {
        _log.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
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

            if (state.Algorithm is not null) _algorithm = state.Algorithm;
            if (state.Preset is not null) _preset = state.Preset;
            if (state.ValidityDays is > 0) _validityDays = state.ValidityDays.Value;

            if (state.SubjectCn is not null) _subjectCn = state.SubjectCn;
            if (state.SubjectO is not null) _subjectO = state.SubjectO;
            if (state.SubjectOu is not null) _subjectOu = state.SubjectOu;
            if (state.SubjectC is not null) _subjectC = state.SubjectC;
            if (state.SubjectSt is not null) _subjectSt = state.SubjectSt;
            if (state.SubjectL is not null) _subjectL = state.SubjectL;

            if (state.IssuerCn is not null) _issuerCn = state.IssuerCn;
            if (state.IssuerO is not null) _issuerO = state.IssuerO;
            if (state.IssuerOu is not null) _issuerOu = state.IssuerOu;
            if (state.IssuerC is not null) _issuerC = state.IssuerC;
            if (state.IssuerSt is not null) _issuerSt = state.IssuerSt;
            if (state.IssuerL is not null) _issuerL = state.IssuerL;

            if (state.IncludeSan is not null) _includeSan = state.IncludeSan.Value;
            if (state.SanEntries is not null) _sanEntries = state.SanEntries;

            if (state.KuFlags is not null) _kuFlags = (X509KeyUsageFlags)state.KuFlags.Value;
            if (state.KuCritical is not null) _kuCritical = state.KuCritical.Value;
            if (state.EkuCritical is not null) _ekuCritical = state.EkuCritical.Value;
            if (state.CustomEku is not null) _customEkuText = state.CustomEku;

            if (state.EkuOids is not null)
            {
                _ekuOids.Clear();
                foreach (string oid in state.EkuOids.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    string value = oid.Trim();
                    if (value.Length > 0)
                    {
                        _ekuOids.Add(value);
                    }
                }
            }

            if (state.OutputDirectory is not null) _outputDirectory = state.OutputDirectory;
            if (state.PfxPassword is not null) _pfxPassword = state.PfxPassword;
            if (state.ExportPfx is not null) _exportPfx = state.ExportPfx.Value;
            if (state.ExportCer is not null) _exportCer = state.ExportCer.Value;
            if (state.ExportPem is not null) _exportPem = state.ExportPem.Value;
            if (state.ExportIssuerCer is not null) _exportIssuerCer = state.ExportIssuerCer.Value;
        }
        catch (Exception ex)
        {
            _log.AppendLine($"加载上次设置失败，已使用默认值: {ex.Message}");
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
                Algorithm = _algorithm,
                Preset = _preset,
                ValidityDays = _validityDays,
                SubjectCn = _subjectCn,
                SubjectO = _subjectO,
                SubjectOu = _subjectOu,
                SubjectC = _subjectC,
                SubjectSt = _subjectSt,
                SubjectL = _subjectL,
                IssuerCn = _issuerCn,
                IssuerO = _issuerO,
                IssuerOu = _issuerOu,
                IssuerC = _issuerC,
                IssuerSt = _issuerSt,
                IssuerL = _issuerL,
                IncludeSan = _includeSan,
                SanEntries = _sanEntries,
                KuFlags = (int)_kuFlags,
                EkuOids = string.Join(",", _ekuOids),
                KuCritical = _kuCritical,
                EkuCritical = _ekuCritical,
                CustomEku = _customEkuText,
                OutputDirectory = _outputDirectory,
                PfxPassword = _pfxPassword,
                ExportPfx = _exportPfx,
                ExportCer = _exportCer,
                ExportPem = _exportPem,
                ExportIssuerCer = _exportIssuerCer
            };

            _settingsProvider.SetSetting(StateSetting, JsonSerializer.Serialize(state));
        }
        catch
        {
            // 设置写入失败不影响证书生成。
        }
    }

    private sealed class PersistedState
    {
        public string? Algorithm { get; set; }
        public string? Preset { get; set; }
        public double? ValidityDays { get; set; }
        public string? SubjectCn { get; set; }
        public string? SubjectO { get; set; }
        public string? SubjectOu { get; set; }
        public string? SubjectC { get; set; }
        public string? SubjectSt { get; set; }
        public string? SubjectL { get; set; }
        public string? IssuerCn { get; set; }
        public string? IssuerO { get; set; }
        public string? IssuerOu { get; set; }
        public string? IssuerC { get; set; }
        public string? IssuerSt { get; set; }
        public string? IssuerL { get; set; }
        public bool? IncludeSan { get; set; }
        public string? SanEntries { get; set; }
        public int? KuFlags { get; set; }
        public string? EkuOids { get; set; }
        public bool? KuCritical { get; set; }
        public bool? EkuCritical { get; set; }
        public string? CustomEku { get; set; }
        public string? OutputDirectory { get; set; }
        public string? PfxPassword { get; set; }
        public bool? ExportPfx { get; set; }
        public bool? ExportCer { get; set; }
        public bool? ExportPem { get; set; }
        public bool? ExportIssuerCer { get; set; }
    }

    private static bool IsOid(string value)
    {
        string[] segments = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 && segments.All(segment => int.TryParse(segment, out _));
    }

    private static List<string> NormalizeSanEntries(string subjectCn, string raw)
    {
        List<string> entries =
        [
            .. raw
                .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];

        if (entries.Count == 0)
        {
            entries.Add(subjectCn);
            if (!subjectCn.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add("localhost");
            }
        }

        return entries;
    }

    private static string BuildDistinguishedName(
        string cn, string o, string ou, string c, string st, string l)
    {
        if (string.IsNullOrWhiteSpace(cn))
        {
            throw new InvalidOperationException("Subject CN 不能为空。");
        }

        if (!string.IsNullOrWhiteSpace(c) && c.Trim().Length != 2)
        {
            throw new InvalidOperationException("国家/地区 (C) 必须是 2 位代码，例如 CN、US。");
        }

        var parts = new List<string> { $"CN={Escape(cn)}" };
        Append(parts, "O", o);
        Append(parts, "OU", ou);
        Append(parts, "C", c);
        Append(parts, "ST", st);
        Append(parts, "L", l);
        return string.Join(", ", parts);
    }

    private static void Append(List<string> parts, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{key}={Escape(value)}");
        }
    }

    private static string Escape(string value) => value.Trim().Replace("\\", "\\\\").Replace(",", "\\,");

    private static string MakeSafeFileName(string raw)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            raw = raw.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(raw) ? "testcert" : raw;
    }

    private readonly HashSet<string> _ekuOids = new(StringComparer.OrdinalIgnoreCase)
    {
        "1.3.6.1.4.1.311.10.3.12"
    };
}

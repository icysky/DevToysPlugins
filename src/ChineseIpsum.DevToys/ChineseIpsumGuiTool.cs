using System.ComponentModel.Composition;
using DevToys.Api;
using ChineseIpsum.DevToys.Core;

namespace ChineseIpsum.DevToys;

[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(ChineseIpsumResourceAssemblyIdentifier))]
internal sealed class ChineseIpsumResourceAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
        => ValueTask.FromResult(Array.Empty<FontDefinition>());
}

/// <summary>
/// 中文假文生成器：用四大名著节选拼装占位文本，交互对齐 DevToys 自带的「乱数假文」。
/// </summary>
[Export(typeof(IGuiTool))]
[Name("ChineseIpsum")]
[ToolDisplayInformation(
    // U+F8FD = ic_fluent_book_20_regular（已用字体 cmap 校验存在）
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uF8FD',
    GroupName = PredefinedCommonToolGroupNames.Generators,
    ResourceManagerAssemblyIdentifier = nameof(ChineseIpsumResourceAssemblyIdentifier),
    ResourceManagerBaseName = "ChineseIpsum.DevToys.Strings",
    ShortDisplayTitleResourceName = "ShortDisplayTitle",
    LongDisplayTitleResourceName = "LongDisplayTitle",
    DescriptionResourceName = "Description",
    AccessibleNameResourceName = "AccessibleName",
    SearchKeywordsResourceName = "SearchKeywords")]
internal sealed class ChineseIpsumGuiTool : IGuiTool
{
    private static readonly SettingDefinition<string> CorpusSetting =
        new("Corpus", nameof(IpsumCorpus.Mixed));

    private static readonly SettingDefinition<string> UnitSetting =
        new("Unit", nameof(IpsumUnit.Paragraphs));

    private static readonly SettingDefinition<int> LengthSetting =
        new("Length", 3);

    private static readonly SettingDefinition<bool> OpeningSetting =
        new("StartWithOpening", true);

    private readonly ISettingsProvider _settingsProvider;

    private readonly IUIMultiLineTextInput _output;

    private readonly IUIInfoBar _infoBar;

    private readonly IUIButton _refreshButton;

    private IpsumCorpus _corpus;

    private IpsumUnit _unit;

    private int _length;

    private bool _startWithOpening;

    private bool _loading = true;

    private UIToolView? _view;

    [ImportingConstructor]
    public ChineseIpsumGuiTool(ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;

        _corpus = ParseEnum(_settingsProvider.GetSetting(CorpusSetting), IpsumCorpus.Mixed);
        _unit = ParseEnum(_settingsProvider.GetSetting(UnitSetting), IpsumUnit.Paragraphs);
        _length = Math.Clamp(_settingsProvider.GetSetting(LengthSetting), IpsumGenerator.MinLength, IpsumGenerator.MaxLength);
        _startWithOpening = _settingsProvider.GetSetting(OpeningSetting);

        _infoBar = GUI.InfoBar("ci-info").Closable();

        _refreshButton = GUI.Button("ci-refresh")
            .Text("换一批")
            .OnClick(Generate);

        _output = GUI.MultiLineTextInput("ci-output")
            .Title("生成结果")
            .AlwaysWrap()
            .ReadOnly();

        _view = BuildView();
        _loading = false;
        Generate();
    }

    public UIToolView View => _view ??= BuildView();

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
    }

    private UIToolView BuildView()
    {
        IUIDropDownListItem[] corpusItems =
        [
            GUI.Item("红楼梦", IpsumCorpus.Honglou),
            GUI.Item("西游记", IpsumCorpus.Xiyou),
            GUI.Item("水浒传", IpsumCorpus.Shuihu),
            GUI.Item("三国演义", IpsumCorpus.Sanguo),
            GUI.Item("四大名著混合", IpsumCorpus.Mixed)
        ];

        IUIDropDownListItem[] unitItems =
        [
            GUI.Item("段落", IpsumUnit.Paragraphs),
            GUI.Item("句子", IpsumUnit.Sentences),
            GUI.Item("字数", IpsumUnit.Characters)
        ];

        IUISelectDropDownList corpusSelect = GUI.SelectDropDownList("ci-corpus")
            .WithItems(corpusItems)
            .Select(Math.Clamp((int)_corpus, 0, corpusItems.Length - 1))
            .OnItemSelected(item =>
            {
                if (item?.Value is not IpsumCorpus corpus)
                {
                    return;
                }

                _corpus = corpus;
                SaveSettings();
                Generate();
            });

        IUISelectDropDownList unitSelect = GUI.SelectDropDownList("ci-unit")
            .WithItems(unitItems)
            .Select(Math.Clamp((int)_unit, 0, unitItems.Length - 1))
            .OnItemSelected(item =>
            {
                if (item?.Value is not IpsumUnit unit)
                {
                    return;
                }

                _unit = unit;
                SaveSettings();
                Generate();
            });

        IUINumberInput lengthInput = GUI.NumberInput(
                "ci-length",
                IpsumGenerator.MinLength,
                IpsumGenerator.MaxLength,
                1)
            .HideCommandBar()
            .Value(_length)
            .OnValueChanged(value =>
            {
                _length = (int)Math.Clamp(value, IpsumGenerator.MinLength, IpsumGenerator.MaxLength);
                SaveSettings();
                Generate();
            });

        IUISwitch openingSwitch = _startWithOpening ? GUI.Switch("ci-opening").On() : GUI.Switch("ci-opening").Off();
        openingSwitch.OnToggle(on =>
        {
            _startWithOpening = on;
            SaveSettings();
            Generate();
        });

        IUIElement settings = GUI.Stack().Vertical().MediumSpacing().WithChildren(
            GUI.Label().Text("配置").Style(UILabelStyle.BodyStrong),
            GUI.Setting("ci-setting-corpus")
                .Title("来源")
                .Description("语料出处，均为公版原著的节选片段")
                .InteractiveElement(corpusSelect),
            GUI.Setting("ci-setting-unit")
                .Title("类型")
                .Description("按段落 / 句子 / 字数生成")
                .InteractiveElement(unitSelect),
            GUI.Setting("ci-setting-length")
                .Title("数量")
                .Description("段落模式为段落数，句子模式为句子数，字数模式为目标汉字数")
                .InteractiveElement(lengthInput),
            GUI.Setting("ci-setting-opening")
                .Title("以原著开篇起句")
                .Description("首句固定为该书的著名开头，类似 Lorem ipsum dolor sit amet")
                .InteractiveElement(openingSwitch));

        IUIElement resultArea = GUI.CommandBarExtraContent<IUIMultiLineTextInput>(_output, _refreshButton);

        return new UIToolView(
            GUI.Grid()
                .Columns(GUI.Fraction(1))
                .Rows(UIGridLength.Auto, GUI.Fraction(1))
                .RowLargeSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, GUI.Stack().Vertical().MediumSpacing().WithChildren(_infoBar, settings)),
                    GUI.Cell(1, 0, 1, 1, resultArea)));
    }

    private void Generate()
    {
        if (_loading)
        {
            return;
        }

        try
        {
            string text = IpsumGenerator.Generate(_corpus, _unit, _length, _startWithOpening);
            GUI.Text(_output, text);
            GUI.Close(_infoBar);

            if (string.IsNullOrEmpty(text))
            {
                ShowError("没有可用的语料，请检查插件的 Assets/Corpus 资源是否完整。");
            }
        }
        catch (Exception ex)
        {
            ShowError($"生成失败：{ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        GUI.Title(_infoBar, "出错");
        GUI.Description(_infoBar, message);
        GUI.Error(_infoBar);
        GUI.Open(_infoBar);
    }

    private void SaveSettings()
    {
        try
        {
            _settingsProvider.SetSetting(CorpusSetting, _corpus.ToString());
            _settingsProvider.SetSetting(UnitSetting, _unit.ToString());
            _settingsProvider.SetSetting(LengthSetting, _length);
            _settingsProvider.SetSetting(OpeningSetting, _startWithOpening);
        }
        catch
        {
            // 设置写入失败不影响生成。
        }
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
        => Enum.TryParse(value, ignoreCase: true, out TEnum parsed) ? parsed : fallback;
}

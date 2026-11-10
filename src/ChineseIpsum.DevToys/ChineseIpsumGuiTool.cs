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
    private static readonly SettingDefinition<string> ModeSetting =
        new("Mode", nameof(IpsumMode.Corpus));

    private static readonly SettingDefinition<string> CorpusSetting =
        new("Corpus", nameof(IpsumCorpus.Mixed));

    private static readonly SettingDefinition<string> UnitSetting =
        new("Unit", nameof(IpsumUnit.Paragraphs));

    private static readonly SettingDefinition<int> LengthSetting =
        new("Length", 3);

    private static readonly SettingDefinition<bool> OpeningSetting =
        new("StartWithOpening", true);

    private static readonly SettingDefinition<string> CountModeSetting =
        new("RandomCountMode", nameof(IpsumCountMode.Characters));

    private static readonly SettingDefinition<bool> RareSetting =
        new("RandomIncludeRare", false);

    private static readonly SettingDefinition<bool> ParagraphSetting =
        new("RandomParagraphs", true);

    private static readonly SettingDefinition<bool> PunctuationSetting =
        new("RandomPunctuation", true);

    private static readonly SettingDefinition<bool> MixedLanguageSetting =
        new("RandomMixedLanguages", false);

    private static readonly SettingDefinition<int> RandomLengthSetting =
        new("RandomLength", 300);

    private static readonly SettingDefinition<int> MinSentenceSetting =
        new("ParagraphMinSentences", 3);

    private static readonly SettingDefinition<int> MaxSentenceSetting =
        new("ParagraphMaxSentences", 6);

    private static readonly SettingDefinition<int> RandomMinSentenceSetting =
        new("RandomParagraphMinSentences", 2);

    private static readonly SettingDefinition<int> RandomMaxSentenceSetting =
        new("RandomParagraphMaxSentences", 4);

    private readonly ISettingsProvider _settingsProvider;

    private readonly IUIMultiLineTextInput _output;

    private readonly IUIInfoBar _infoBar;

    private readonly IUIButton _refreshButton;

    private IpsumMode _mode;

    private IpsumCorpus _corpus;

    private IpsumUnit _unit;

    private int _length;

    private int _randomLength;

    private bool _startWithOpening;

    private IpsumCountMode _countMode;

    private bool _includeRare;

    private bool _paragraphs;

    private bool _punctuation;

    private bool _mixedLanguages;

    private int _minSentences;

    private int _maxSentences;

    private int _randomMinSentences;

    private int _randomMaxSentences;

    // 按模式显示/隐藏的整行 / 整块（DevToys 要求只改可见性，不能替换控件实例）
    private IUIElement _corpusGrid = default!;

    private IUIElement _randomGrid = default!;

    private IUIElement _corpusSentenceRow = default!;

    private IUIElement _randomSentenceRow = default!;

    private IUIElement _corpusRow = default!;

    private IUIElement _unitRow = default!;

    private IUIElement _lengthRow = default!;

    private IUIElement _openingRow = default!;

    private IUIElement _randomLengthRow = default!;

    private IUIElement _countModeRow = default!;

    private IUIElement _rareRow = default!;

    private IUIElement _paragraphRow = default!;

    private IUIElement _punctuationRow = default!;

    private IUIElement _mixedRow = default!;

    private bool _loading = true;

    private UIToolView? _view;

    [ImportingConstructor]
    public ChineseIpsumGuiTool(ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;

        _mode = ParseEnum(_settingsProvider.GetSetting(ModeSetting), IpsumMode.Corpus);
        _corpus = ParseEnum(_settingsProvider.GetSetting(CorpusSetting), IpsumCorpus.Mixed);
        _unit = ParseEnum(_settingsProvider.GetSetting(UnitSetting), IpsumUnit.Paragraphs);
        _length = Math.Clamp(_settingsProvider.GetSetting(LengthSetting), IpsumGenerator.MinLength, IpsumGenerator.MaxLength);
        _startWithOpening = _settingsProvider.GetSetting(OpeningSetting);
        _countMode = ParseEnum(_settingsProvider.GetSetting(CountModeSetting), IpsumCountMode.Characters);
        _randomLength = Math.Clamp(_settingsProvider.GetSetting(RandomLengthSetting), RandomTextGenerator.MinLength, RandomTextGenerator.MaxLength);
        _includeRare = _settingsProvider.GetSetting(RareSetting);
        _paragraphs = _settingsProvider.GetSetting(ParagraphSetting);
        _punctuation = _settingsProvider.GetSetting(PunctuationSetting);
        _mixedLanguages = _settingsProvider.GetSetting(MixedLanguageSetting);
        _minSentences = Math.Clamp(
            _settingsProvider.GetSetting(MinSentenceSetting),
            IpsumGenerator.MinSentencesPerParagraph,
            IpsumGenerator.MaxSentencesPerParagraph);
        _maxSentences = Math.Clamp(
            _settingsProvider.GetSetting(MaxSentenceSetting),
            _minSentences,
            IpsumGenerator.MaxSentencesPerParagraph);
        _randomMinSentences = Math.Clamp(
            _settingsProvider.GetSetting(RandomMinSentenceSetting),
            IpsumGenerator.MinSentencesPerParagraph,
            IpsumGenerator.MaxSentencesPerParagraph);
        _randomMaxSentences = Math.Clamp(
            _settingsProvider.GetSetting(RandomMaxSentenceSetting),
            _randomMinSentences,
            IpsumGenerator.MaxSentencesPerParagraph);

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
        UpdateVisibility();
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
            .Select(corpusItems[Math.Clamp((int)_corpus, 0, corpusItems.Length - 1)])
            .OnItemSelected(item =>
            {
                if (_loading || item?.Value is not IpsumCorpus corpus)
                {
                    return;
                }

                _corpus = corpus;
                SaveSettings();
                Generate();
            });

        IUISelectDropDownList unitSelect = GUI.SelectDropDownList("ci-unit")
            .WithItems(unitItems)
            .Select(unitItems[Math.Clamp((int)_unit, 0, unitItems.Length - 1)])
            .OnItemSelected(item =>
            {
                if (_loading || item?.Value is not IpsumUnit unit)
                {
                    return;
                }

                _unit = unit;
                SaveSettings();
                UpdateVisibility();
                Generate();
            });

        // 注意：用「项实例」选中，而不是 Select(index)。
        // 索引版本在视图初次渲染时可能不生效，下拉框会落到最后一项并触发一次
        // OnItemSelected，把错误的值写进设置（实测踩过）。
        IUIDropDownListItem[] modeItems =
        [
            GUI.Item("名著节选（有语义）", IpsumMode.Corpus),
            GUI.Item("随机乱文（无意义）", IpsumMode.Random)
        ];

        IUISelectDropDownList modeSelect = GUI.SelectDropDownList("ci-mode")
            .WithItems(modeItems)
            .Select(modeItems[Math.Clamp((int)_mode, 0, modeItems.Length - 1)])
            .OnItemSelected(item =>
            {
                // 视图构建期间控件会触发一次回调，必须挡掉，否则会把默认值/错误项写进设置
                if (_loading || item?.Value is not IpsumMode mode)
                {
                    return;
                }

                _mode = mode;
                SaveSettings();
                UpdateVisibility();
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
                if (_loading)
                {
                    return;
                }

                _length = (int)Math.Clamp(value, IpsumGenerator.MinLength, IpsumGenerator.MaxLength);
                SaveSettings();
                Generate();
            });

        IUINumberInput randomLengthInput = GUI.NumberInput(
                "ci-random-length",
                RandomTextGenerator.MinLength,
                RandomTextGenerator.MaxLength,
                1)
            .HideCommandBar()
            .Value(_randomLength)
            .OnValueChanged(value =>
            {
                if (_loading)
                {
                    return;
                }

                _randomLength = (int)Math.Clamp(value, RandomTextGenerator.MinLength, RandomTextGenerator.MaxLength);
                SaveSettings();
                Generate();
            });

        IUIDropDownListItem[] countModeItems =
        [
            GUI.Item("字符数（含标点）", IpsumCountMode.Characters),
            GUI.Item("字词数", IpsumCountMode.Words)
        ];

        IUISelectDropDownList countModeSelect = GUI.SelectDropDownList("ci-count-mode")
            .WithItems(countModeItems)
            .Select(countModeItems[Math.Clamp((int)_countMode, 0, countModeItems.Length - 1)])
            .OnItemSelected(item =>
            {
                if (_loading || item?.Value is not IpsumCountMode countMode)
                {
                    return;
                }

                _countMode = countMode;
                SaveSettings();
                Generate();
            });

        IUISwitch openingSwitch = _startWithOpening ? GUI.Switch("ci-opening").On() : GUI.Switch("ci-opening").Off();
        openingSwitch.OnToggle(on =>
        {
            if (_loading)
            {
                return;
            }

            _startWithOpening = on;
            SaveSettings();
            Generate();
        });

        IUISwitch rareSwitch = _includeRare ? GUI.Switch("ci-rare").On() : GUI.Switch("ci-rare").Off();
        rareSwitch.OnToggle(on =>
        {
            if (_loading)
            {
                return;
            }

            _includeRare = on;
            SaveSettings();
            Generate();
        });

        IUISwitch paragraphSwitch = _paragraphs ? GUI.Switch("ci-paragraphs").On() : GUI.Switch("ci-paragraphs").Off();
        paragraphSwitch.OnToggle(on =>
        {
            if (_loading)
            {
                return;
            }

            _paragraphs = on;
            SaveSettings();
            Generate();
        });

        IUISwitch punctuationSwitch = _punctuation ? GUI.Switch("ci-punctuation").On() : GUI.Switch("ci-punctuation").Off();
        punctuationSwitch.OnToggle(on =>
        {
            if (_loading)
            {
                return;
            }

            _punctuation = on;
            SaveSettings();
            Generate();
        });

        IUISwitch mixedSwitch = _mixedLanguages ? GUI.Switch("ci-mixed").On() : GUI.Switch("ci-mixed").Off();
        mixedSwitch.OnToggle(on =>
        {
            if (_loading)
            {
                return;
            }

            _mixedLanguages = on;
            SaveSettings();
            Generate();
        });

        static IUIElement RangeInput(IUIElement min, IUIElement max)
            => GUI.Stack().Horizontal().SmallSpacing().WithChildren(
                min,
                GUI.Label().Text("~"),
                max);

        IUINumberInput corpusMinInput = GUI.NumberInput(
                "ci-para-min",
                IpsumGenerator.MinSentencesPerParagraph,
                IpsumGenerator.MaxSentencesPerParagraph,
                1)
            .HideCommandBar()
            .Value(_minSentences)
            .OnValueChanged(value =>
            {
                if (_loading)
                {
                    return;
                }

                _minSentences = (int)Math.Clamp(value, IpsumGenerator.MinSentencesPerParagraph, IpsumGenerator.MaxSentencesPerParagraph);
                SaveSettings();
                Generate();
            });

        IUINumberInput corpusMaxInput = GUI.NumberInput(
                "ci-para-max",
                IpsumGenerator.MinSentencesPerParagraph,
                IpsumGenerator.MaxSentencesPerParagraph,
                1)
            .HideCommandBar()
            .Value(_maxSentences)
            .OnValueChanged(value =>
            {
                if (_loading)
                {
                    return;
                }

                _maxSentences = (int)Math.Clamp(value, IpsumGenerator.MinSentencesPerParagraph, IpsumGenerator.MaxSentencesPerParagraph);
                SaveSettings();
                Generate();
            });

        IUINumberInput randomMinInput = GUI.NumberInput(
                "ci-random-para-min",
                IpsumGenerator.MinSentencesPerParagraph,
                IpsumGenerator.MaxSentencesPerParagraph,
                1)
            .HideCommandBar()
            .Value(_randomMinSentences)
            .OnValueChanged(value =>
            {
                if (_loading)
                {
                    return;
                }

                _randomMinSentences = (int)Math.Clamp(value, IpsumGenerator.MinSentencesPerParagraph, IpsumGenerator.MaxSentencesPerParagraph);
                SaveSettings();
                Generate();
            });

        IUINumberInput randomMaxInput = GUI.NumberInput(
                "ci-random-para-max",
                IpsumGenerator.MinSentencesPerParagraph,
                IpsumGenerator.MaxSentencesPerParagraph,
                1)
            .HideCommandBar()
            .Value(_randomMaxSentences)
            .OnValueChanged(value =>
            {
                if (_loading)
                {
                    return;
                }

                _randomMaxSentences = (int)Math.Clamp(value, IpsumGenerator.MinSentencesPerParagraph, IpsumGenerator.MaxSentencesPerParagraph);
                SaveSettings();
                Generate();
            });

        _corpusRow = GUI.Setting("ci-setting-corpus")
            .Title("来源")
            .Description("语料出处，均为公版原著的节选片段")
            .InteractiveElement(corpusSelect);

        _unitRow = GUI.Setting("ci-setting-unit")
            .Title("类型")
            .Description("按段落 / 句子 / 字数生成")
            .InteractiveElement(unitSelect);

        _lengthRow = GUI.Setting("ci-setting-length")
            .Title("数量")
            .Description("段落数为段数，句子数为句数，字数为目标汉字数")
            .InteractiveElement(lengthInput);

        _openingRow = GUI.Setting("ci-setting-opening")
            .Title("以原著开篇起句")
            .Description("首句固定为该书的著名开头，类似 Lorem ipsum dolor sit amet")
            .InteractiveElement(openingSwitch);

        _corpusSentenceRow = GUI.Setting("ci-setting-para")
            .Title("每段句子数")
            .Description("每段随机取 N~M 句（含端点），仅「段落」类型生效")
            .InteractiveElement(RangeInput(corpusMinInput, corpusMaxInput));

        _countModeRow = GUI.Setting("ci-setting-count-mode")
            .Title("长度按")
            .Description("字符数含标点与外文；字词数按汉字/外文单词计")
            .InteractiveElement(countModeSelect);

        _randomLengthRow = GUI.Setting("ci-setting-random-length")
            .Title("数量")
            .Description("按上面的计量方式计数，达到目标后停止（会略微超出）")
            .InteractiveElement(randomLengthInput);

        _rareRow = GUI.Setting("ci-setting-rare")
            .Title("包含生僻字")
            .Description("混入 CJK 扩展区（Ext A / Ext B / 基本区尾部）的少见字符")
            .InteractiveElement(rareSwitch);

        _paragraphRow = GUI.Setting("ci-setting-paragraph")
            .Title("模拟文章段落")
            .Description("按每段句子数分段、段间空行；关闭则输出连续的一段")
            .InteractiveElement(paragraphSwitch);

        _randomSentenceRow = GUI.Setting("ci-setting-random-para")
            .Title("每段句子数")
            .Description("每段随机取 N~M 句（含端点），开启「模拟文章段落」时生效")
            .InteractiveElement(RangeInput(randomMinInput, randomMaxInput));

        _punctuationRow = GUI.Setting("ci-setting-punctuation")
            .Title("包含标点符号")
            .Description("句末用句号 / 感叹号 / 问号，句中随机插入逗号")
            .InteractiveElement(punctuationSwitch);

        _mixedRow = GUI.Setting("ci-setting-mixed")
            .Title("多语言混合")
            .Description("随机插入英文 / 西里尔 / 希腊文伪单词（无意义，长度不一）")
            .InteractiveElement(mixedSwitch);

        // 三列网格：一行放三个设置，避免纵向拉得过长
        _corpusGrid = GUI.Grid()
            .Columns(GUI.Fraction(1), GUI.Fraction(1), GUI.Fraction(1))
            .Rows(UIGridLength.Auto, UIGridLength.Auto)
            .ColumnMediumSpacing()
            .RowMediumSpacing()
            .Cells(
                GUI.Cell(0, 0, 1, 1, _corpusRow),
                GUI.Cell(0, 1, 1, 1, _unitRow),
                GUI.Cell(0, 2, 1, 1, _lengthRow),
                GUI.Cell(1, 0, 1, 1, _corpusSentenceRow),
                GUI.Cell(1, 1, 1, 1, _openingRow));

        _randomGrid = GUI.Grid()
            .Columns(GUI.Fraction(1), GUI.Fraction(1), GUI.Fraction(1))
            .Rows(UIGridLength.Auto, UIGridLength.Auto, UIGridLength.Auto)
            .ColumnMediumSpacing()
            .RowMediumSpacing()
            .Cells(
                GUI.Cell(0, 0, 1, 1, _countModeRow),
                GUI.Cell(0, 1, 1, 1, _randomLengthRow),
                GUI.Cell(0, 2, 1, 1, _rareRow),
                GUI.Cell(1, 0, 1, 1, _paragraphRow),
                GUI.Cell(1, 1, 1, 1, _randomSentenceRow),
                GUI.Cell(1, 2, 1, 1, _punctuationRow),
                GUI.Cell(2, 0, 1, 1, _mixedRow));

        IUIElement settings = GUI.Stack().Vertical().MediumSpacing().WithChildren(
            GUI.Label().Text("配置").Style(UILabelStyle.BodyStrong),
            GUI.Setting("ci-setting-mode")
                .Title("来源类型")
                .Description("名著节选有语义；随机乱文按规则生成无意义文本，适合测试排版与字号")
                .InteractiveElement(modeSelect),
            _corpusGrid,
            _randomGrid);

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
            string text = _mode == IpsumMode.Random
                ? RandomTextGenerator.Generate(new RandomTextOptions
                {
                    Length = _randomLength,
                    CountMode = _countMode,
                    IncludeRareCharacters = _includeRare,
                    Paragraphs = _paragraphs,
                    MinSentencesPerParagraph = _randomMinSentences,
                    MaxSentencesPerParagraph = _randomMaxSentences,
                    Punctuation = _punctuation,
                    MixedLanguages = _mixedLanguages
                })
                : IpsumGenerator.Generate(
                    _corpus,
                    _unit,
                    _length,
                    _startWithOpening,
                    _minSentences,
                    _maxSentences);

            GUI.Text(_output, text);
            GUI.Close(_infoBar);

            if (string.IsNullOrEmpty(text))
            {
                ShowError(_mode == IpsumMode.Random
                    ? "生成结果为空，请把数量调大一些。"
                    : "没有可用的语料，请检查插件的 Assets/Corpus 资源是否完整。");
            }
        }
        catch (Exception ex)
        {
            ShowError($"生成失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 按模式切换参数行。DevToys 的控件只在初始化时订阅一次事件，
    /// 所以这里只改可见性，绝不重建或替换实例。
    /// </summary>
    private void UpdateVisibility()
    {
        bool corpus = _mode == IpsumMode.Corpus;

        Show(_corpusGrid, corpus);
        Show(_randomGrid, !corpus);

        // 「每段句子数」只在真正按段输出时才有意义
        Show(_corpusSentenceRow, corpus && _unit == IpsumUnit.Paragraphs);
        Show(_randomSentenceRow, _paragraphs);
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
            _settingsProvider.SetSetting(ModeSetting, _mode.ToString());
            _settingsProvider.SetSetting(CorpusSetting, _corpus.ToString());
            _settingsProvider.SetSetting(UnitSetting, _unit.ToString());
            _settingsProvider.SetSetting(LengthSetting, _length);
            _settingsProvider.SetSetting(OpeningSetting, _startWithOpening);
            _settingsProvider.SetSetting(CountModeSetting, _countMode.ToString());
            _settingsProvider.SetSetting(RandomLengthSetting, _randomLength);
            _settingsProvider.SetSetting(RareSetting, _includeRare);
            _settingsProvider.SetSetting(ParagraphSetting, _paragraphs);
            _settingsProvider.SetSetting(PunctuationSetting, _punctuation);
            _settingsProvider.SetSetting(MixedLanguageSetting, _mixedLanguages);
            _settingsProvider.SetSetting(MinSentenceSetting, _minSentences);
            _settingsProvider.SetSetting(MaxSentenceSetting, _maxSentences);
            _settingsProvider.SetSetting(RandomMinSentenceSetting, _randomMinSentences);
            _settingsProvider.SetSetting(RandomMaxSentenceSetting, _randomMaxSentences);
        }
        catch
        {
            // 设置写入失败不影响生成。
        }
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
        => Enum.TryParse(value, ignoreCase: true, out TEnum parsed) ? parsed : fallback;
}

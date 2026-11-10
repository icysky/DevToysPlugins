using System.ComponentModel.Composition;
using System.Text.Json;
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

    private static readonly SettingDefinition<string> PunctuationStyleSetting =
        new("RandomPunctuationStyle", nameof(IpsumPunctuationStyle.Chinese));

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

    private static readonly SettingDefinition<bool> BlankLineSetting =
        new("BlankLineBetweenParagraphs", true);

    private static readonly SettingDefinition<bool> RandomBlankLineSetting =
        new("RandomBlankLineBetweenParagraphs", true);

    // 字符压力 / 结构化片段 / 段落形态：项太多，存成一条 JSON（与 GmCrypto 的做法一致）
    private static readonly SettingDefinition<string> StressStateSetting =
        new("RandomStressState", string.Empty);

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

    private IpsumPunctuationStyle _punctuationStyle;

    private bool _mixedLanguages;

    private int _minSentences;

    private int _maxSentences;

    private int _randomMinSentences;

    private int _randomMaxSentences;

    private bool _blankLineParagraphs;

    private bool _randomBlankLineParagraphs;

    private IpsumStressFlags _stress;

    private IpsumDensity _stressDensity = IpsumDensity.Medium;

    private IpsumFragmentFlags _fragments;

    private IpsumDensity _fragmentDensity = IpsumDensity.Medium;

    private IpsumDensity _punctuationDensity = IpsumDensity.Medium;

    private IpsumParagraphShape _paragraphShape = IpsumParagraphShape.Mixed;

    private bool _indentFirstLine;

    private IpsumListStyle _listStyle = IpsumListStyle.None;

    private bool _quoteBlocks;

    private bool _inlineCode;

    private IpsumSpaceStyle _spaceStyle = IpsumSpaceStyle.Single;



    // 开关一律保留引用：状态从控件读，而不是从事件参数读（见 PullSwitchState）
    private IUISwitch _openingSwitch = default!;

    private IUISwitch _rareSwitch = default!;

    private IUISwitch _paragraphSwitch = default!;

    private IUISwitch _punctuationSwitch = default!;

    private IUISwitch _mixedSwitch = default!;

    private IUISwitch _blankLineSwitch = default!;

    private IUISwitch _randomBlankLineSwitch = default!;



    private IUISwitch _indentSwitch = default!;

    private IUISwitch _quoteSwitch = default!;

    private IUISwitch _inlineCodeSwitch = default!;

    private readonly List<(IpsumStressFlags Flag, IUISwitch Switch)> _stressSwitches = [];

    private readonly List<(IpsumFragmentFlags Flag, IUISwitch Switch)> _fragmentSwitches = [];

    // 按模式显示/隐藏的整行 / 整块（DevToys 要求只改可见性，不能替换控件实例）
    private IUIElement _corpusGrid = default!;

    private IUIElement _corpusBlankRow = default!;

    private IUIElement _randomBlankRow = default!;

    private IUIElement _randomGrid = default!;

    private IUIElement _stressCard = default!;

    private IUIElement _fragmentCard = default!;

    private IUIElement _shapeCard = default!;



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

    private IUIElement _punctStyleRow = default!;

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
        _punctuationStyle = ParseEnum(
            _settingsProvider.GetSetting(PunctuationStyleSetting),
            IpsumPunctuationStyle.Chinese);
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
        _blankLineParagraphs = _settingsProvider.GetSetting(BlankLineSetting);
        _randomBlankLineParagraphs = _settingsProvider.GetSetting(RandomBlankLineSetting);
        LoadStressState();

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

    /// <summary>开关变化：统一走 Generate（内部会读控件状态、存设置、刷新可见性）。</summary>
    private void OnSwitchChanged()
    {
        if (_loading)
        {
            return;
        }

        Generate();
    }

    /// <summary>
    /// 开关状态一律从控件 <c>IsOn</c> 读取，不采信事件参数。
    ///
    /// 原因：DevToys 在首次渲染时会抛一次 Toggled，且携带的值是初始状态的"反面"
    /// （初始 true 抛 false、初始 false 抛 true）。按事件值写状态会把默认值写反；
    /// 而"丢弃第一次事件"又会把用户的第一次点击吞掉（实测点折叠/生僻字没反应）。
    /// 读控件状态可以同时避开这两个坑——渲染事件触发的重新生成结果也是一样的。
    /// </summary>
    private void PullSwitchState()
    {
        _startWithOpening = _openingSwitch.IsOn;
        _includeRare = _rareSwitch.IsOn;
        _paragraphs = _paragraphSwitch.IsOn;
        _punctuation = _punctuationSwitch.IsOn;
        _mixedLanguages = _mixedSwitch.IsOn;
        _blankLineParagraphs = _blankLineSwitch.IsOn;
        _randomBlankLineParagraphs = _randomBlankLineSwitch.IsOn;
        _indentFirstLine = _indentSwitch.IsOn;
        _quoteBlocks = _quoteSwitch.IsOn;
        _inlineCode = _inlineCodeSwitch.IsOn;

        _stress = IpsumStressFlags.None;
        foreach ((IpsumStressFlags flag, IUISwitch toggle) in _stressSwitches)
        {
            if (toggle.IsOn)
            {
                _stress |= flag;
            }
        }

        _fragments = IpsumFragmentFlags.None;
        foreach ((IpsumFragmentFlags flag, IUISwitch toggle) in _fragmentSwitches)
        {
            if (toggle.IsOn)
            {
                _fragments |= flag;
            }
        }
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

        // 高级选项折叠：默认收起，把纵向空间让给结果框
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

        _openingSwitch = _startWithOpening ? GUI.Switch("ci-opening").On() : GUI.Switch("ci-opening").Off();
        _openingSwitch.OnToggle(_ => OnSwitchChanged());

        _rareSwitch = _includeRare ? GUI.Switch("ci-rare").On() : GUI.Switch("ci-rare").Off();
        _rareSwitch.OnToggle(_ => OnSwitchChanged());

        _paragraphSwitch = _paragraphs ? GUI.Switch("ci-paragraphs").On() : GUI.Switch("ci-paragraphs").Off();
        _paragraphSwitch.OnToggle(_ => OnSwitchChanged());

        _punctuationSwitch = _punctuation ? GUI.Switch("ci-punctuation").On() : GUI.Switch("ci-punctuation").Off();
        _punctuationSwitch.OnToggle(_ => OnSwitchChanged());

        IUIDropDownListItem[] punctStyleItems =
        [
            GUI.Item("中文标点（，。！？）", IpsumPunctuationStyle.Chinese),
            GUI.Item("西文标点（, . ! ?）", IpsumPunctuationStyle.Western),
            GUI.Item("混合（两种都用）", IpsumPunctuationStyle.Mixed)
        ];

        IUISelectDropDownList punctStyleSelect = GUI.SelectDropDownList("ci-punct-style")
            .WithItems(punctStyleItems)
            .Select(punctStyleItems[Math.Clamp((int)_punctuationStyle, 0, punctStyleItems.Length - 1)])
            .OnItemSelected(item =>
            {
                if (_loading || item?.Value is not IpsumPunctuationStyle style)
                {
                    return;
                }

                _punctuationStyle = style;
                SaveSettings();
                Generate();
            });

        _mixedSwitch = _mixedLanguages ? GUI.Switch("ci-mixed").On() : GUI.Switch("ci-mixed").Off();
        _mixedSwitch.OnToggle(_ => OnSwitchChanged());

        _blankLineSwitch = _blankLineParagraphs ? GUI.Switch("ci-blank-line").On() : GUI.Switch("ci-blank-line").Off();
        _blankLineSwitch.OnToggle(_ => OnSwitchChanged());

        _randomBlankLineSwitch = _randomBlankLineParagraphs ? GUI.Switch("ci-random-blank-line").On() : GUI.Switch("ci-random-blank-line").Off();
        _randomBlankLineSwitch.OnToggle(_ => OnSwitchChanged());

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
            .InteractiveElement(_openingSwitch);

        _corpusSentenceRow = GUI.Setting("ci-setting-para")
            .Title("每段句子数")
            .Description("每段随机取 N~M 句（含端点），仅「段落」类型生效")
            .InteractiveElement(RangeInput(corpusMinInput, corpusMaxInput));

        _corpusBlankRow = GUI.Setting("ci-setting-blank")
            .Title("段落间空行")
            .Description("段落之间空一行；关闭则只用单个换行分隔")
            .InteractiveElement(_blankLineSwitch);

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
            .InteractiveElement(_rareSwitch);

        _paragraphRow = GUI.Setting("ci-setting-paragraph")
            .Title("模拟文章段落")
            .Description("按每段句子数分段、段间空行；关闭则输出连续的一段")
            .InteractiveElement(_paragraphSwitch);

        _randomSentenceRow = GUI.Setting("ci-setting-random-para")
            .Title("每段句子数")
            .Description("每段随机取 N~M 句（含端点），开启「模拟文章段落」时生效")
            .InteractiveElement(RangeInput(randomMinInput, randomMaxInput));

        _randomBlankRow = GUI.Setting("ci-setting-random-blank")
            .Title("段落间空行")
            .Description("段落之间空一行；关闭则只用单个换行分隔")
            .InteractiveElement(_randomBlankLineSwitch);

        _punctuationRow = GUI.Setting("ci-setting-punctuation")
            .Title("包含标点符号")
            .Description("句末用句号 / 感叹号 / 问号，句中随机插入逗号")
            .InteractiveElement(_punctuationSwitch);

        _punctStyleRow = GUI.Setting("ci-setting-punct-style")
            .Title("标点风格")
            .Description("中文标点 / 西文标点 / 两者混用，仅开启标点时生效")
            .InteractiveElement(punctStyleSelect);

        _mixedRow = GUI.Setting("ci-setting-mixed")
            .Title("多语言混合")
            .Description("随机插入英文 / 西里尔 / 希腊文伪单词（无意义，长度不一）")
            .InteractiveElement(_mixedSwitch);

        // 双列网格：左侧设置区变窄了，一行两个设置刚好
        _corpusGrid = GUI.Grid()
            .Columns(GUI.Fraction(1), GUI.Fraction(1))
            .Rows(UIGridLength.Auto, UIGridLength.Auto, UIGridLength.Auto)
            .ColumnMediumSpacing()
            .RowMediumSpacing()
            .Cells(
                GUI.Cell(0, 0, 1, 1, _corpusRow),
                GUI.Cell(0, 1, 1, 1, _unitRow),
                GUI.Cell(1, 0, 1, 1, _lengthRow),
                GUI.Cell(1, 1, 1, 1, _corpusSentenceRow),
                GUI.Cell(2, 0, 1, 1, _corpusBlankRow),
                GUI.Cell(2, 1, 1, 1, _openingRow));

        _randomGrid = GUI.Grid()
            .Columns(GUI.Fraction(1), GUI.Fraction(1))
            .Rows(
                UIGridLength.Auto, UIGridLength.Auto, UIGridLength.Auto,
                UIGridLength.Auto, UIGridLength.Auto)
            .ColumnMediumSpacing()
            .RowMediumSpacing()
            .Cells(
                GUI.Cell(0, 0, 1, 1, _countModeRow),
                GUI.Cell(0, 1, 1, 1, _randomLengthRow),
                GUI.Cell(1, 0, 1, 1, _rareRow),
                GUI.Cell(1, 1, 1, 1, _paragraphRow),
                GUI.Cell(2, 0, 1, 1, _randomSentenceRow),
                GUI.Cell(2, 1, 1, 1, _randomBlankRow),
                GUI.Cell(3, 0, 1, 1, _punctuationRow),
                GUI.Cell(3, 1, 1, 1, _punctStyleRow),
                GUI.Cell(4, 0, 1, 1, _mixedRow));

        // ---- 字符压力 / 结构化片段 / 段落形态（仅随机模式可见，默认全关） ----
        IUISwitch StressSwitch(string id, IpsumStressFlags flag)
        {
            IUISwitch toggle = (_stress & flag) != 0 ? GUI.Switch(id).On() : GUI.Switch(id).Off();
            toggle.OnToggle(_ => OnSwitchChanged());
            _stressSwitches.Add((flag, toggle));
            return toggle;
        }

        IUISwitch FragmentSwitch(string id, IpsumFragmentFlags flag)
        {
            IUISwitch toggle = (_fragments & flag) != 0 ? GUI.Switch(id).On() : GUI.Switch(id).Off();
            toggle.OnToggle(_ => OnSwitchChanged());
            _fragmentSwitches.Add((flag, toggle));
            return toggle;
        }

        IUISwitch Toggle(string id, bool initial)
        {
            IUISwitch toggle = initial ? GUI.Switch(id).On() : GUI.Switch(id).Off();
            toggle.OnToggle(_ => OnSwitchChanged());
            return toggle;
        }

        IUISelectDropDownList DensitySelect(string id, IpsumDensity current, Action<IpsumDensity> apply)
        {
            IUIDropDownListItem[] items =
            [
                GUI.Item("低", IpsumDensity.Low),
                GUI.Item("中", IpsumDensity.Medium),
                GUI.Item("高", IpsumDensity.High)
            ];

            return GUI.SelectDropDownList(id)
                .WithItems(items)
                .Select(items[Math.Clamp((int)current, 0, items.Length - 1)])
                .OnItemSelected(item =>
                {
                    if (_loading || item?.Value is not IpsumDensity density)
                    {
                        return;
                    }

                    apply(density);
                    SaveSettings();
                    Generate();
                });
        }

        IUISelectDropDownList EnumSelect<TEnum>(string id, TEnum current, (string Label, TEnum Value)[] options, Action<TEnum> apply)
            where TEnum : struct, Enum
        {
            IUIDropDownListItem[] items = [.. options.Select(o => GUI.Item(o.Label, o.Value))];
            int index = Array.FindIndex(options, o => EqualityComparer<TEnum>.Default.Equals(o.Value, current));

            return GUI.SelectDropDownList(id)
                .WithItems(items)
                .Select(items[index < 0 ? 0 : index])
                .OnItemSelected(item =>
                {
                    if (_loading || item?.Value is not TEnum value)
                    {
                        return;
                    }

                    apply(value);
                    SaveSettings();
                    Generate();
                });
        }

        IUIElement Row(string id, string title, string description, IUIElement control)
            => GUI.Setting(id).Title(title).Description(description).InteractiveElement(control);

        _stressCard = GUI.Card(
            "字符压力（勾选后才混入，默认全关）",
            GUI.Grid()
                .Columns(GUI.Fraction(1), GUI.Fraction(1))
                .Rows(
                    UIGridLength.Auto, UIGridLength.Auto, UIGridLength.Auto,
                    UIGridLength.Auto, UIGridLength.Auto)
                .ColumnMediumSpacing()
                .RowMediumSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, Row("ci-stress-emoji", "Emoji", "含 ZWJ 组合、肤色修饰、国旗", StressSwitch("ci-sw-emoji", IpsumStressFlags.Emoji))),
                    GUI.Cell(0, 1, 1, 1, Row("ci-stress-cjk", "日文 / 韩文", "假名与谚文，测字体回退", StressSwitch("ci-sw-cjk", IpsumStressFlags.EastAsian))),
                    GUI.Cell(1, 0, 1, 1, Row("ci-stress-rtl", "RTL 文字", "阿拉伯 / 希伯来，测 BiDi 与镜像", StressSwitch("ci-sw-rtl", IpsumStressFlags.Rtl))),
                    GUI.Cell(1, 1, 1, 1, Row("ci-stress-math", "数学与符号", "∑ ∫ √ ≤ → ↔ ┌┬┐ • † ※", StressSwitch("ci-sw-math", IpsumStressFlags.MathSymbols))),
                    GUI.Cell(2, 0, 1, 1, Row("ci-stress-latin", "拉丁扩展", "é ü ñ ç ß 与组合音标", StressSwitch("ci-sw-latin", IpsumStressFlags.LatinExtended))),
                    GUI.Cell(2, 1, 1, 1, Row("ci-stress-forms", "特殊数字与单位", "¹²³ ½ ① Ⅰ ¥ ℃ ㎡ ‰", StressSwitch("ci-sw-forms", IpsumStressFlags.SpecialForms))),
                    GUI.Cell(3, 0, 1, 1, Row("ci-stress-fullwidth", "全角字符", "ＡＢＣ１２３，测中西文混排", StressSwitch("ci-sw-fullwidth", IpsumStressFlags.FullWidth))),
                    GUI.Cell(3, 1, 1, 1, Row("ci-stress-invisible", "不可见字符", "NBSP / 零宽空格 / 软连字符", StressSwitch("ci-sw-invisible", IpsumStressFlags.Invisible))),
                    GUI.Cell(4, 0, 1, 1, Row("ci-stress-longrun", "超长不可断串", "40~200 字母，测 overflow-wrap", StressSwitch("ci-sw-longrun", IpsumStressFlags.LongRun))),
                    GUI.Cell(4, 1, 1, 1, Row("ci-stress-density", "压力强度", "插入压力字符的概率", DensitySelect("ci-stress-density-select", _stressDensity, d => _stressDensity = d)))));

        _fragmentCard = GUI.Card(
            "结构化片段（有格式、无意义）",
            GUI.Grid()
                .Columns(GUI.Fraction(1), GUI.Fraction(1))
                .Rows(UIGridLength.Auto, UIGridLength.Auto, UIGridLength.Auto, UIGridLength.Auto)
                .ColumnMediumSpacing()
                .RowMediumSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, Row("ci-frag-url", "URL", "https://…?id=…", FragmentSwitch("ci-sw-url", IpsumFragmentFlags.Url))),
                    GUI.Cell(0, 1, 1, 1, Row("ci-frag-email", "邮箱", "name.tag@sub.example.co.uk", FragmentSwitch("ci-sw-email", IpsumFragmentFlags.Email))),
                    GUI.Cell(1, 0, 1, 1, Row("ci-frag-id", "UUID / 哈希", "32 / 40 / 64 位 hex", FragmentSwitch("ci-sw-id", IpsumFragmentFlags.Identifier))),
                    GUI.Cell(1, 1, 1, 1, Row("ci-frag-version", "版本号", "v1.2.3-beta.4+build.567", FragmentSwitch("ci-sw-version", IpsumFragmentFlags.Version))),
                    GUI.Cell(2, 0, 1, 1, Row("ci-frag-datetime", "日期时间", "2026-11-10 12:34:56 等格式", FragmentSwitch("ci-sw-datetime", IpsumFragmentFlags.DateTime))),
                    GUI.Cell(2, 1, 1, 1, Row("ci-frag-amount", "金额数值", "1,234,567.89 / 99.99% / 12,345 kg", FragmentSwitch("ci-sw-amount", IpsumFragmentFlags.Amount))),
                    GUI.Cell(3, 0, 1, 1, Row("ci-frag-path", "文件路径", "/var/www/a/b/file.tar.gz", FragmentSwitch("ci-sw-path", IpsumFragmentFlags.FilePath))),
                    GUI.Cell(3, 1, 1, 1, Row("ci-frag-density", "出现频率", "每个片段出现的概率", DensitySelect("ci-frag-density-select", _fragmentDensity, d => _fragmentDensity = d)))));

        _shapeCard = GUI.Card(
            "段落形态",
            GUI.Grid()
                .Columns(GUI.Fraction(1), GUI.Fraction(1))
                .Rows(UIGridLength.Auto, UIGridLength.Auto, UIGridLength.Auto, UIGridLength.Auto)
                .ColumnMediumSpacing()
                .RowMediumSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, Row("ci-shape-length", "段落长短", "以「每段句子数」为基准", EnumSelect(
                        "ci-shape-length-select",
                        _paragraphShape,
                        [("短段多", IpsumParagraphShape.Short), ("混合", IpsumParagraphShape.Mixed), ("长段多", IpsumParagraphShape.Long)],
                        v => _paragraphShape = v))),
                    GUI.Cell(0, 1, 1, 1, Row("ci-shape-punct", "标点密度", "句中逗号的密集程度", DensitySelect("ci-shape-punct-select", _punctuationDensity, d => _punctuationDensity = d))),
                    GUI.Cell(1, 0, 1, 1, Row("ci-shape-space", "空格策略", "外文词前：无 / 单 / 多", EnumSelect(
                        "ci-shape-space-select",
                        _spaceStyle,
                        [("无空格", IpsumSpaceStyle.None), ("单空格", IpsumSpaceStyle.Single), ("多空格", IpsumSpaceStyle.Multiple)],
                        v => _spaceStyle = v))),
                    GUI.Cell(1, 1, 1, 1, Row("ci-shape-indent", "首行缩进", "段首加两个全角空格", (_indentSwitch = Toggle("ci-sw-indent", _indentFirstLine)))),
                    GUI.Cell(2, 0, 1, 1, Row("ci-shape-list", "列表符号", "段首加项目符号或序号", EnumSelect(
                        "ci-shape-list-select",
                        _listStyle,
                        [("不加", IpsumListStyle.None), ("项目符号 •", IpsumListStyle.Bullets), ("有序编号 1.", IpsumListStyle.Ordered)],
                        v => _listStyle = v))),
                    GUI.Cell(2, 1, 1, 1, Row("ci-shape-quote", "引用块", "段首加 > 前缀", (_quoteSwitch = Toggle("ci-sw-quote", _quoteBlocks)))),
                    GUI.Cell(3, 0, 1, 1, Row("ci-shape-code", "行内代码", "把词包成 `code`，测等宽字体", (_inlineCodeSwitch = Toggle("ci-sw-code", _inlineCode))))));

        IUIElement settings = GUI.Stack().Vertical().MediumSpacing().WithChildren(
            GUI.Label().Text("配置").Style(UILabelStyle.BodyStrong),
            GUI.Setting("ci-setting-mode")
                .Title("来源类型")
                .Description("名著节选有语义；随机乱文按规则生成无意义文本，适合测试排版与字号")
                .InteractiveElement(modeSelect),
            _corpusGrid,
            _randomGrid,
            _stressCard,
            _fragmentCard,
            _shapeCard);

        IUIElement resultArea = GUI.CommandBarExtraContent<IUIMultiLineTextInput>(_output, _refreshButton);

        // 全局左右分栏：左侧设置（双列），右侧结果占满整个高度
        return new UIToolView(
            GUI.Grid()
                .Columns(GUI.Fraction(1), GUI.Fraction(1))
                .ColumnLargeSpacing()
                .Rows(GUI.Fraction(1))
                .Cells(
                    GUI.Cell(0, 0, 1, 1, GUI.Stack().Vertical().MediumSpacing().WithChildren(_infoBar, settings)),
                    GUI.Cell(0, 1, 1, 1, resultArea)));
    }

    private void Generate()
    {
        if (_loading)
        {
            return;
        }

        // 以控件为准：同步开关状态 → 落盘 → 刷新可见性（折叠开关就靠这一步生效）
        PullSwitchState();
        SaveSettings();
        UpdateVisibility();

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
                    BlankLineBetweenParagraphs = _randomBlankLineParagraphs,
                    Punctuation = _punctuation,
                    PunctuationStyle = _punctuationStyle,
                    MixedLanguages = _mixedLanguages,
                    Stress = _stress,
                    StressDensity = _stressDensity,
                    Fragments = _fragments,
                    FragmentDensity = _fragmentDensity,
                    PunctuationDensity = _punctuationDensity,
                    ParagraphShape = _paragraphShape,
                    IndentFirstLine = _indentFirstLine,
                    ListStyle = _listStyle,
                    QuoteBlocks = _quoteBlocks,
                    InlineCode = _inlineCode,
                    SpaceStyle = _spaceStyle
                })
                : IpsumGenerator.Generate(
                    _corpus,
                    _unit,
                    _length,
                    _startWithOpening,
                    _minSentences,
                    _maxSentences,
                    _blankLineParagraphs);

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

        // 「每段句子数 / 段落间空行」只在真正按段输出时才有意义
        bool corpusParagraphs = corpus && _unit == IpsumUnit.Paragraphs;
        Show(_corpusSentenceRow, corpusParagraphs);
        Show(_corpusBlankRow, corpusParagraphs);
        Show(_randomSentenceRow, _paragraphs);
        Show(_randomBlankRow, _paragraphs);
        Show(_punctStyleRow, _punctuation);

        // 三张高级卡片常驻显示（左右布局里纵向空间不再紧张，不需要收起/分组）
        Show(_stressCard, !corpus);
        Show(_fragmentCard, !corpus);
        Show(_shapeCard, !corpus);
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
            _settingsProvider.SetSetting(PunctuationStyleSetting, _punctuationStyle.ToString());
            _settingsProvider.SetSetting(MixedLanguageSetting, _mixedLanguages);
            _settingsProvider.SetSetting(MinSentenceSetting, _minSentences);
            _settingsProvider.SetSetting(MaxSentenceSetting, _maxSentences);
            _settingsProvider.SetSetting(RandomMinSentenceSetting, _randomMinSentences);
            _settingsProvider.SetSetting(RandomMaxSentenceSetting, _randomMaxSentences);
            _settingsProvider.SetSetting(BlankLineSetting, _blankLineParagraphs);
            _settingsProvider.SetSetting(RandomBlankLineSetting, _randomBlankLineParagraphs);
            SaveStressState();
        }
        catch
        {
            // 设置写入失败不影响生成。
        }
    }

    /// <summary>字符压力 / 结构化片段 / 段落形态：项太多，整组存成一条 JSON。</summary>
    private void LoadStressState()
    {
        try
        {
            string json = _settingsProvider.GetSetting(StressStateSetting);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            PersistedStress? state = JsonSerializer.Deserialize<PersistedStress>(json);
            if (state is null)
            {
                return;
            }

            _stress = ParseEnum(state.Stress, IpsumStressFlags.None);
            _stressDensity = ParseEnum(state.StressDensity, IpsumDensity.Medium);
            _fragments = ParseEnum(state.Fragments, IpsumFragmentFlags.None);
            _fragmentDensity = ParseEnum(state.FragmentDensity, IpsumDensity.Medium);
            _punctuationDensity = ParseEnum(state.PunctuationDensity, IpsumDensity.Medium);
            _paragraphShape = ParseEnum(state.ParagraphShape, IpsumParagraphShape.Mixed);
            if (state.IndentFirstLine is not null) _indentFirstLine = state.IndentFirstLine.Value;
            _listStyle = ParseEnum(state.ListStyle, IpsumListStyle.None);
            if (state.QuoteBlocks is not null) _quoteBlocks = state.QuoteBlocks.Value;
            if (state.InlineCode is not null) _inlineCode = state.InlineCode.Value;
            _spaceStyle = ParseEnum(state.SpaceStyle, IpsumSpaceStyle.Single);

        }
        catch
        {
            // 设置损坏时用默认值
        }
    }

    private void SaveStressState()
    {
        try
        {
            _settingsProvider.SetSetting(StressStateSetting, JsonSerializer.Serialize(new PersistedStress
            {
                Stress = _stress.ToString(),
                StressDensity = _stressDensity.ToString(),
                Fragments = _fragments.ToString(),
                FragmentDensity = _fragmentDensity.ToString(),
                PunctuationDensity = _punctuationDensity.ToString(),
                ParagraphShape = _paragraphShape.ToString(),
                IndentFirstLine = _indentFirstLine,
                ListStyle = _listStyle.ToString(),
                QuoteBlocks = _quoteBlocks,
                InlineCode = _inlineCode,
                SpaceStyle = _spaceStyle.ToString()
            }));
        }
        catch
        {
            // 持久化失败不影响生成
        }
    }

    private sealed class PersistedStress
    {
        public string? Stress { get; set; }
        public string? StressDensity { get; set; }
        public string? Fragments { get; set; }
        public string? FragmentDensity { get; set; }
        public string? PunctuationDensity { get; set; }
        public string? ParagraphShape { get; set; }
        public bool? IndentFirstLine { get; set; }
        public string? ListStyle { get; set; }
        public bool? QuoteBlocks { get; set; }
        public bool? InlineCode { get; set; }
        public string? SpaceStyle { get; set; }
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
        => Enum.TryParse(value, ignoreCase: true, out TEnum parsed) ? parsed : fallback;
}

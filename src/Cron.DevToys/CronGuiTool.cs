using System.ComponentModel.Composition;
using System.Text.Json;
using Cron.DevToys.Core;
using DevToys.Api;

namespace Cron.DevToys;

[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(CronResourceAssemblyIdentifier))]
internal sealed class CronResourceAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
        => ValueTask.FromResult(Array.Empty<FontDefinition>());
}

/// <summary>
/// Cron 表达式生成器 + 解析预览。
/// 左侧按字段拼表达式（秒 / 分 / 时 / 日 / 月 / 周），右侧实时显示中文描述与下次执行时间。
/// </summary>
[Export(typeof(IGuiTool))]
[Name("CronExpression")]
[ToolDisplayInformation(
    // U+ED88 = ic_fluent_timer_20_regular（计时器语义，已用字体 cmap 校验存在）
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uED88',
    GroupName = PredefinedCommonToolGroupNames.Generators,
    ResourceManagerAssemblyIdentifier = nameof(CronResourceAssemblyIdentifier),
    ResourceManagerBaseName = "Cron.DevToys.Strings",
    ShortDisplayTitleResourceName = "CronShortDisplayTitle",
    LongDisplayTitleResourceName = "CronLongDisplayTitle",
    DescriptionResourceName = "CronDescription",
    AccessibleNameResourceName = "CronAccessibleName",
    SearchKeywordsResourceName = "CronSearchKeywords")]
internal sealed class CronGuiTool : IGuiTool
{
    private const string DefaultFormat = "yyyy-MM-dd ddd HH:mm:ss";

    private static readonly SettingDefinition<string> StateSetting = new("Cron.State", string.Empty);

    private readonly ISettingsProvider _settingsProvider;
    private readonly IClipboard _clipboard;

    // ---------------- 状态 ----------------
    private readonly CronBuilderState _state = new();
    private string _expression = string.Empty;
    private int _count = 5;
    private string _format = DefaultFormat;
    private string _description = string.Empty;
    private string _nextRuns = string.Empty;

    /// <summary>构建视图、程序化写控件、切换模板时置位，避免回调递归。</summary>
    private bool _suppress = true;

    // ---------------- 生成器控件 ----------------
    private IUISwitch _secondsSwitch = default!;

    private IUISelectDropDownList _secondMode = default!;
    private IUINumberInput _secondStep = default!;
    private IUISingleLineTextInput _secondText = default!;
    private IUISetting _secondRow = default!;

    private IUISelectDropDownList _minuteMode = default!;
    private IUINumberInput _minuteStep = default!;
    private IUISingleLineTextInput _minuteText = default!;
    private IUISetting _minuteRow = default!;

    private IUISelectDropDownList _hourMode = default!;
    private IUINumberInput _hourStep = default!;
    private IUISingleLineTextInput _hourText = default!;
    private IUISetting _hourRow = default!;

    private IUISelectDropDownList _domMode = default!;
    private IUINumberInput _domStep = default!;
    private IUISingleLineTextInput _domText = default!;
    private IUINumberInput _domNearest = default!;
    private IUISetting _domRow = default!;

    private IUISelectDropDownList _monthMode = default!;
    private IUINumberInput _monthStep = default!;
    private IUISingleLineTextInput _monthText = default!;
    private IUISetting _monthRow = default!;

    private IUISelectDropDownList _dowMode = default!;
    private IUISingleLineTextInput _dowText = default!;
    private IUISelectDropDownList _dowWeekday = default!;
    private IUISelectDropDownList _dowNth = default!;
    private IUISetting _dowRow = default!;

    private IUISetting _generatedRow = default!;
    private IUISingleLineTextInput _generatedOutput = default!;

    // ---------------- 预览控件 ----------------
    private IUISingleLineTextInput _expressionInput = default!;
    private IUINumberInput _countInput = default!;
    private IUISingleLineTextInput _formatInput = default!;
    private IUISingleLineTextInput _descriptionInput = default!;
    private IUIMultiLineTextInput _nextRunsInput = default!;
    private IUIInfoBar _infoBar = default!;

    // ---------------- 下拉项（用于程序化选中） ----------------
    private IUIDropDownListItem[] _secondModeItems = default!;
    private IUIDropDownListItem[] _minuteModeItems = default!;
    private IUIDropDownListItem[] _hourModeItems = default!;
    private IUIDropDownListItem[] _domModeItems = default!;
    private IUIDropDownListItem[] _monthModeItems = default!;
    private IUIDropDownListItem[] _dowModeItems = default!;
    private IUIDropDownListItem[] _weekdayItems = default!;
    private IUIDropDownListItem[] _nthItems = default!;

    private UIToolView? _view;

    public UIToolView View => _view ??= BuildView();

    [ImportingConstructor]
    public CronGuiTool(ISettingsProvider settingsProvider, IClipboard clipboard)
    {
        _settingsProvider = settingsProvider;
        _clipboard = clipboard;

        LoadState();
    }

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
    }

    // ---------------------------------------------------------------- 视图

    private UIToolView BuildView()
    {
        _infoBar = GUI.InfoBar("cron-info").Closable();

        CreateGeneratorControls();
        CreatePreviewControls();

        // 刻意不用 GUI.SettingGroup：DevToys 会把它渲染成 Expander，
        // 而 API 没有设置 IsExpanded 的入口（默认折叠），用户每次打开都要手动展开。
        // 内置 Cron 解析器也是用 Label + Stack 平铺设置项，这里保持一致。
        IUIElement generator = GUI.Stack()
            .Vertical()
            .SmallSpacing()
            .WithChildren(
                GUI.Text(GUI.Label("cron-generator-label"), "生成器配置"),
                GUI.Setting("cron-seconds-setting")
                    .Title("包含秒数")
                    .Description("生成 6 段表达式（秒 分 时 日 月 周）；关闭则生成 5 段（分 时 日 月 周）")
                    .InteractiveElement(_secondsSwitch),
                GUI.Setting("cron-template-setting")
                    .Title("快捷模板")
                    .Description("一键套用常见计划，之后可以继续改下面的字段")
                    .InteractiveElement(BuildTemplateButtons()),
                _secondRow,
                _minuteRow,
                _hourRow,
                _domRow,
                _monthRow,
                _dowRow);

        _generatedOutput = GUI.SingleLineTextInput("cron-generated")
            .ReadOnly()
            .HideCommandBar();

        _generatedRow = GUI.Setting("cron-generated-setting")
            .Title("生成的表达式")
            .Description("左侧字段变化会实时重写右侧的表达式")
            .InteractiveElement(GUI.Stack().Horizontal().SmallSpacing().WithChildren(
                _generatedOutput,
                GUI.Button("cron-copy-generated")
                    .Text("复制")
                    .OnClick(CopyGeneratedAsync)));

        IUIElement left = GUI.Stack().Vertical().MediumSpacing().WithChildren(generator, _generatedRow);

        IUIElement parseSettings = GUI.Stack()
            .Vertical()
            .SmallSpacing()
            .WithChildren(
                GUI.Text(GUI.Label("cron-parse-label"), "解析设置"),
                GUI.Setting("cron-count-setting")
                    .Title("下次执行时间")
                    .Description("要列出多少个未来的执行时间（1~100）")
                    .InteractiveElement(_countInput),
                GUI.Setting("cron-format-setting")
                    .Title("输出格式")
                    .Description("任意 .NET 时间格式串，例如 yyyy-MM-dd ddd HH:mm:ss")
                    .InteractiveElement(_formatInput));

        IUIElement right = GUI.Grid()
            .Columns(GUI.Fraction(1))
            .Rows(UIGridLength.Auto, UIGridLength.Auto, UIGridLength.Auto, GUI.Fraction(1))
            .RowMediumSpacing()
            .Cells(
                GUI.Cell(0, 0, 1, 1, GUI.Stack().Vertical().SmallSpacing().WithChildren(_infoBar, _expressionInput)),
                GUI.Cell(1, 0, 1, 1, parseSettings),
                GUI.Cell(2, 0, 1, 1, _descriptionInput),
                GUI.Cell(3, 0, 1, 1, _nextRunsInput.AlignVertically(UIVerticalAlignment.Stretch)));

        _view = new UIToolView(
            GUI.Grid()
                .Columns(GUI.Fraction(1), GUI.Fraction(1))
                .Rows(GUI.Fraction(1))
                .ColumnLargeSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, left),
                    GUI.Cell(0, 1, 1, 1, right)));

        // 控件建好后把状态灌进去，再打开回调
        SyncControlsFromState();
        _suppress = false;

        if (string.IsNullOrWhiteSpace(_expression))
        {
            ApplyGenerator();
        }
        else
        {
            Preview();
        }

        return _view;
    }

    private void CreateGeneratorControls()
    {
        _secondsSwitch = _state.IncludeSeconds ? GUI.Switch("cron-seconds").On() : GUI.Switch("cron-seconds").Off();
        _secondsSwitch.OnToggle(_ =>
        {
            if (_suppress)
            {
                return;
            }

            _state.IncludeSeconds = _secondsSwitch.IsOn;
            SaveState();
            UpdateFieldVisibility();
            ApplyGenerator();
        });

        _secondModeItems =
        [
            GUI.Item("每个值（*）", CronUnitMode.Every),
            GUI.Item("间隔（*/n）", CronUnitMode.Step),
            GUI.Item("指定值", CronUnitMode.Specific)
        ];
        _minuteModeItems = _secondModeItems;
        _hourModeItems = _secondModeItems;
        _monthModeItems = _secondModeItems;

        _domModeItems =
        [
            GUI.Item("每天（*）", CronDayOfMonthMode.Every),
            GUI.Item("间隔（*/n）", CronDayOfMonthMode.Step),
            GUI.Item("指定日期", CronDayOfMonthMode.Specific),
            GUI.Item("当月最后一天（L）", CronDayOfMonthMode.LastDay),
            GUI.Item("当月最后一个工作日（LW）", CronDayOfMonthMode.LastWeekday),
            GUI.Item("最接近某天的工作日（nW）", CronDayOfMonthMode.NearestWeekday)
        ];

        _dowModeItems =
        [
            GUI.Item("每天都算（*）", CronDayOfWeekMode.Every),
            GUI.Item("交给「日」判断（?）", CronDayOfWeekMode.Unspecified),
            GUI.Item("指定星期", CronDayOfWeekMode.Specific),
            GUI.Item("工作日（1-5）", CronDayOfWeekMode.Weekdays),
            GUI.Item("周末（0,6）", CronDayOfWeekMode.Weekend),
            GUI.Item("第 N 个星期几（n#k）", CronDayOfWeekMode.NthWeekday),
            GUI.Item("最后一个星期几（nL）", CronDayOfWeekMode.LastWeekday)
        ];

        _weekdayItems =
        [
            GUI.Item("周日", 0), GUI.Item("周一", 1), GUI.Item("周二", 2), GUI.Item("周三", 3),
            GUI.Item("周四", 4), GUI.Item("周五", 5), GUI.Item("周六", 6)
        ];

        _nthItems =
        [
            GUI.Item("第 1 个", 1), GUI.Item("第 2 个", 2), GUI.Item("第 3 个", 3),
            GUI.Item("第 4 个", 4), GUI.Item("第 5 个", 5)
        ];

        _secondMode = UnitModeSelect("cron-second-mode", _secondModeItems, () => _state.Second.Mode, v => _state.Second.Mode = v);
        _secondStep = StepInput("cron-second-step", 1, 59, () => _state.Second.Step, v => _state.Second.Step = v);
        _secondText = SpecificInput("cron-second-text", "0,30 / 0-30/5", () => _state.Second.Specific, v => _state.Second.Specific = v);
        _secondRow = FieldRow("cron-second-setting", "秒", "0~59，支持 * / 步长 / 列表 / 范围", _secondMode, _secondStep, _secondText);

        _minuteMode = UnitModeSelect("cron-minute-mode", _minuteModeItems, () => _state.Minute.Mode, v => _state.Minute.Mode = v);
        _minuteStep = StepInput("cron-minute-step", 1, 59, () => _state.Minute.Step, v => _state.Minute.Step = v);
        _minuteText = SpecificInput("cron-minute-text", "0,30 / 0-30/5", () => _state.Minute.Specific, v => _state.Minute.Specific = v);
        _minuteRow = FieldRow("cron-minute-setting", "分", "0~59，支持 * / 步长 / 列表 / 范围", _minuteMode, _minuteStep, _minuteText);

        _hourMode = UnitModeSelect("cron-hour-mode", _hourModeItems, () => _state.Hour.Mode, v => _state.Hour.Mode = v);
        _hourStep = StepInput("cron-hour-step", 1, 23, () => _state.Hour.Step, v => _state.Hour.Step = v);
        _hourText = SpecificInput("cron-hour-text", "9 / 9-18 / 9,18", () => _state.Hour.Specific, v => _state.Hour.Specific = v);
        _hourRow = FieldRow("cron-hour-setting", "时", "0~23，支持 * / 步长 / 列表 / 范围", _hourMode, _hourStep, _hourText);

        _domMode = GUI.SelectDropDownList("cron-dom-mode")
            .WithItems(_domModeItems)
            .Select(_domModeItems[(int)Math.Clamp((int)_state.DayOfMonth.Mode, 0, _domModeItems.Length - 1)])
            .OnItemSelected(item =>
            {
                if (_suppress || item?.Value is not CronDayOfMonthMode mode)
                {
                    return;
                }

                _state.DayOfMonth.Mode = mode;
                SaveState();
                UpdateFieldVisibility();
                ApplyGenerator();
            });

        _domStep = StepInput("cron-dom-step", 1, 31, () => _state.DayOfMonth.Step, v => _state.DayOfMonth.Step = v);
        _domText = SpecificInput("cron-dom-text", "1 / 1,15 / L", () => _state.DayOfMonth.Specific, v => _state.DayOfMonth.Specific = v);
        _domNearest = NumberControl("cron-dom-nearest", 1, 31, () => _state.DayOfMonth.NearestDay, v => _state.DayOfMonth.NearestDay = v);
        _domRow = FieldRow("cron-dom-setting", "日", "1~31；也可用 L / LW / 15W 这类 Quartz 写法", _domMode, _domStep, _domText, _domNearest);

        _monthMode = UnitModeSelect("cron-month-mode", _monthModeItems, () => _state.Month.Mode, v => _state.Month.Mode = v);
        _monthStep = StepInput("cron-month-step", 1, 12, () => _state.Month.Step, v => _state.Month.Step = v);
        _monthText = SpecificInput("cron-month-text", "1,6,12 / JAN-MAR", () => _state.Month.Specific, v => _state.Month.Specific = v);
        _monthRow = FieldRow("cron-month-setting", "月", "1~12，也支持 JAN~DEC", _monthMode, _monthStep, _monthText);

        _dowMode = GUI.SelectDropDownList("cron-dow-mode")
            .WithItems(_dowModeItems)
            .Select(_dowModeItems[(int)Math.Clamp((int)_state.DayOfWeek.Mode, 0, _dowModeItems.Length - 1)])
            .OnItemSelected(item =>
            {
                if (_suppress || item?.Value is not CronDayOfWeekMode mode)
                {
                    return;
                }

                _state.DayOfWeek.Mode = mode;
                SaveState();
                UpdateFieldVisibility();
                ApplyGenerator();
            });

        _dowText = SpecificInput("cron-dow-text", "1 / 1-5 / MON-FRI", () => _state.DayOfWeek.Specific, v => _state.DayOfWeek.Specific = v);

        _dowWeekday = GUI.SelectDropDownList("cron-dow-weekday")
            .WithItems(_weekdayItems)
            .Select(_weekdayItems[Math.Clamp(_state.DayOfWeek.Weekday, 0, 6)])
            .OnItemSelected(item =>
            {
                if (_suppress || item?.Value is not int weekday)
                {
                    return;
                }

                _state.DayOfWeek.Weekday = weekday;
                SaveState();
                ApplyGenerator();
            });

        _dowNth = GUI.SelectDropDownList("cron-dow-nth")
            .WithItems(_nthItems)
            .Select(_nthItems[Math.Clamp(_state.DayOfWeek.Nth, 1, 5) - 1])
            .OnItemSelected(item =>
            {
                if (_suppress || item?.Value is not int nth)
                {
                    return;
                }

                _state.DayOfWeek.Nth = nth;
                SaveState();
                ApplyGenerator();
            });

        _dowRow = FieldRow(
            "cron-dow-setting",
            "周",
            "0=周日 … 6=周六（7 也当周日）；可用 ? 交给「日」字段",
            _dowMode,
            _dowText,
            _dowWeekday,
            _dowNth);
    }

    private void CreatePreviewControls()
    {
        _expressionInput = GUI.SingleLineTextInput("cron-expression")
            .Title("Cron 表达式")
            .Text(_expression)
            .OnTextChanged(text =>
            {
                if (_suppress)
                {
                    return;
                }

                _expression = text;
                SaveState();
                Preview();
            });

        _countInput = GUI.NumberInput("cron-count", 1, 100, 1)
            .HideCommandBar()
            .Value(_count)
            .OnValueChanged(value =>
            {
                if (_suppress)
                {
                    return;
                }

                _count = (int)Math.Clamp(Math.Round(value), 1, 100);
                SaveState();
                Preview();
            });

        _formatInput = GUI.SingleLineTextInput("cron-format")
            .HideCommandBar()
            .Text(_format)
            .OnTextChanged(text =>
            {
                if (_suppress)
                {
                    return;
                }

                _format = text;
                SaveState();
                Preview();
            });

        _descriptionInput = GUI.SingleLineTextInput("cron-description")
            .Title("Cron 描述")
            .ReadOnly();

        _nextRunsInput = GUI.MultiLineTextInput("cron-next-runs")
            .Title("下次执行时间")
            .ReadOnly()
            .Extendable()
            .NeverWrap();
    }

    // ---------------------------------------------------------------- 控件工厂

    private IUISelectDropDownList UnitModeSelect(
        string id,
        IUIDropDownListItem[] items,
        Func<CronUnitMode> read,
        Action<CronUnitMode> write)
    {
        return GUI.SelectDropDownList(id)
            .WithItems(items)
            .Select(items[(int)read()])
            .OnItemSelected(item =>
            {
                if (_suppress || item?.Value is not CronUnitMode mode)
                {
                    return;
                }

                write(mode);
                SaveState();
                UpdateFieldVisibility();
                ApplyGenerator();
            });
    }

    private IUINumberInput StepInput(string id, int min, int max, Func<int> read, Action<int> write)
        => NumberControl(id, min, max, read, write);

    private IUINumberInput NumberControl(string id, int min, int max, Func<int> read, Action<int> write)
    {
        return GUI.NumberInput(id, min, max, 1)
            .HideCommandBar()
            .Value(Math.Clamp(read(), min, max))
            .OnValueChanged(value =>
            {
                if (_suppress)
                {
                    return;
                }

                write((int)Math.Clamp(Math.Round(value), min, max));
                SaveState();
                ApplyGenerator();
            });
    }

    private IUISingleLineTextInput SpecificInput(string id, string hint, Func<string> read, Action<string> write)
    {
        return GUI.SingleLineTextInput(id)
            .HideCommandBar()
            .Text(read())
            .OnTextChanged(text =>
            {
                if (_suppress)
                {
                    return;
                }

                write(text);
                SaveState();
                ApplyGenerator();
            });
    }

    private static IUISetting FieldRow(string id, string title, string description, params IUIElement[] children)
    {
        return GUI.Setting(id)
            .Title(title)
            .Description(description)
            .InteractiveElement(
                GUI.Stack()
                    .Horizontal()
                    .SmallSpacing()
                    .WithChildren(children)
                    .AlignVertically(UIVerticalAlignment.Center));
    }

    private IUIElement BuildTemplateButtons()
    {
        IUIElement[] buttons = [.. CronTemplates.All.Select(template =>
        {
            CronTemplate current = template;
            return GUI.Button($"cron-template-{current.Name}")
                .Text(current.Name)
                .OnClick(() => ApplyTemplateAndSync(current));
        })];

        return GUI.Wrap().SmallSpacing().WithChildren(buttons);
    }

    private void ApplyTemplateAndSync(CronTemplate template)
    {
        if (_suppress)
        {
            return;
        }

        template.Apply(_state);
        SaveState();
        SyncControlsFromState();
        ApplyGenerator();
    }

    // ---------------------------------------------------------------- 状态同步

    /// <summary>把 <see cref="_state"/> 写回全部控件（模板、还原设置时用）。</summary>
    private void SyncControlsFromState()
    {
        bool previous = _suppress;
        _suppress = true;

        try
        {
            SetSwitch(_secondsSwitch, _state.IncludeSeconds);

            SyncUnitField(_secondMode, _secondModeItems, _secondStep, _secondText, _state.Second);
            SyncUnitField(_minuteMode, _minuteModeItems, _minuteStep, _minuteText, _state.Minute);
            SyncUnitField(_hourMode, _hourModeItems, _hourStep, _hourText, _state.Hour);
            SyncUnitField(_monthMode, _monthModeItems, _monthStep, _monthText, _state.Month);

            Select(_domMode, _domModeItems, (int)_state.DayOfMonth.Mode);
            GUI.Value(_domStep, Math.Clamp(_state.DayOfMonth.Step, 1, 31));
            GUI.Text(_domText, _state.DayOfMonth.Specific);
            GUI.Value(_domNearest, Math.Clamp(_state.DayOfMonth.NearestDay, 1, 31));

            Select(_dowMode, _dowModeItems, (int)_state.DayOfWeek.Mode);
            GUI.Text(_dowText, _state.DayOfWeek.Specific);
            Select(_dowWeekday, _weekdayItems, Math.Clamp(_state.DayOfWeek.Weekday, 0, 6));
            Select(_dowNth, _nthItems, Math.Clamp(_state.DayOfWeek.Nth, 1, 5) - 1);

            GUI.Value(_countInput, _count);
            GUI.Text(_formatInput, _format);
            GUI.Text(_expressionInput, _expression);
        }
        finally
        {
            _suppress = previous;
        }

        UpdateFieldVisibility();
    }

    private static void SyncUnitField(
        IUISelectDropDownList modeSelect,
        IUIDropDownListItem[] items,
        IUINumberInput stepInput,
        IUISingleLineTextInput textInput,
        CronUnitField field)
    {
        Select(modeSelect, items, (int)field.Mode);
        GUI.Value(stepInput, Math.Max(1, field.Step));
        GUI.Text(textInput, field.Specific);
    }

    private static void Select(IUISelectDropDownList select, IUIDropDownListItem[] items, int index)
    {
        int actual = Math.Clamp(index, 0, items.Length - 1);
        GUI.Select(select, items[actual]);
    }

    private static void SetSwitch(IUISwitch toggle, bool isOn)
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

    /// <summary>按当前模式/开关显示对应的输入控件（只改可见性，不重建控件）。</summary>
    private void UpdateFieldVisibility()
    {
        Show(_secondRow, _state.IncludeSeconds);

        Show(_secondStep, _state.Second.Mode == CronUnitMode.Step);
        Show(_secondText, _state.Second.Mode == CronUnitMode.Specific);
        Show(_minuteStep, _state.Minute.Mode == CronUnitMode.Step);
        Show(_minuteText, _state.Minute.Mode == CronUnitMode.Specific);
        Show(_hourStep, _state.Hour.Mode == CronUnitMode.Step);
        Show(_hourText, _state.Hour.Mode == CronUnitMode.Specific);
        Show(_monthStep, _state.Month.Mode == CronUnitMode.Step);
        Show(_monthText, _state.Month.Mode == CronUnitMode.Specific);

        Show(_domStep, _state.DayOfMonth.Mode == CronDayOfMonthMode.Step);
        Show(_domText, _state.DayOfMonth.Mode == CronDayOfMonthMode.Specific);
        Show(_domNearest, _state.DayOfMonth.Mode == CronDayOfMonthMode.NearestWeekday);

        Show(_dowText, _state.DayOfWeek.Mode == CronDayOfWeekMode.Specific);
        Show(_dowWeekday, _state.DayOfWeek.Mode is CronDayOfWeekMode.NthWeekday or CronDayOfWeekMode.LastWeekday);
        Show(_dowNth, _state.DayOfWeek.Mode == CronDayOfWeekMode.NthWeekday);
    }

    private static void Show(IUIElement? element, bool visible)
    {
        if (element is null)
        {
            return;
        }

        if (visible)
        {
            GUI.Show(element);
        }
        else
        {
            GUI.Hide(element);
        }
    }

    // ---------------------------------------------------------------- 生成 / 预览

    private void ApplyGenerator()
    {
        if (_suppress)
        {
            return;
        }

        _state.IncludeSeconds = _secondsSwitch.IsOn;

        _expression = CronExpressionBuilder.Build(_state);

        _suppress = true;
        try
        {
            GUI.Text(_expressionInput, _expression);
            GUI.Text(_generatedOutput, _expression);
        }
        finally
        {
            _suppress = false;
        }

        SaveState();
        Preview();
    }

    private void Preview()
    {
        string expression = _expression.Trim();

        if (expression.Length == 0)
        {
            _description = "请输入或生成一个 Cron 表达式。";
            _nextRuns = string.Empty;
            ApplyPreviewText();
            GUI.Close(_infoBar);
            return;
        }

        CronParseResult result = CronSchedule.Parse(expression);
        if (!result.Success)
        {
            _description = "表达式无效。";
            _nextRuns = string.Empty;
            ApplyPreviewText();
            ShowError(result.Error ?? "表达式无效。");
            return;
        }

        CronSchedule schedule = result.Schedule!;
        _description = schedule.Describe();

        try
        {
            IReadOnlyList<DateTime> next = schedule.GetNextOccurrences(DateTime.Now, _count, out bool limited);

            if (next.Count == 0)
            {
                _nextRuns = $"未来 {CronSchedule.SearchYears} 年内没有匹配时间（例如 2 月 30 日这种不存在的日期）。";
            }
            else
            {
                var lines = new List<string>(next.Count + 1);
                for (int i = 0; i < next.Count; i++)
                {
                    lines.Add($"{i + 1,2}. {CronSchedule.FormatTime(next[i], _format)}");
                }

                if (limited)
                {
                    lines.Add(string.Empty);
                    lines.Add($"（未来 {CronSchedule.SearchYears} 年内只找到 {next.Count} 个匹配时间）");
                }

                _nextRuns = string.Join(Environment.NewLine, lines);
            }

            ApplyPreviewText();
            GUI.Close(_infoBar);
        }
        catch (FormatException ex)
        {
            _nextRuns = string.Empty;
            ApplyPreviewText();
            ShowError($"输出格式无效：{ex.Message}");
        }
        catch (Exception ex)
        {
            _nextRuns = string.Empty;
            ApplyPreviewText();
            ShowError($"计算下次执行时间失败：{ex.Message}");
        }
    }

    private void ApplyPreviewText()
    {
        GUI.Text(_descriptionInput, _description);
        GUI.Text(_nextRunsInput, _nextRuns);
    }

    private async ValueTask CopyGeneratedAsync()
    {
        try
        {
            await _clipboard.SetClipboardTextAsync(_expression);
            GUI.Title(_infoBar, "已复制");
            GUI.Description(_infoBar, _expression);
            GUI.Success(_infoBar);
            GUI.Open(_infoBar);
        }
        catch (Exception ex)
        {
            ShowError($"复制失败：{ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        GUI.Title(_infoBar, "出错了");
        GUI.Description(_infoBar, message);
        GUI.Error(_infoBar);
        GUI.Open(_infoBar);
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

            if (state.Builder is not null)
            {
                CopyBuilderState(state.Builder, _state);
            }

            if (state.Expression is not null)
            {
                _expression = state.Expression;
            }

            if (state.Count is not null)
            {
                _count = Math.Clamp(state.Count.Value, 1, 100);
            }

            if (!string.IsNullOrWhiteSpace(state.Format))
            {
                _format = state.Format;
            }
        }
        catch
        {
            // 设置损坏时用默认值
        }
    }

    private static void CopyBuilderState(CronBuilderState source, CronBuilderState target)
    {
        target.IncludeSeconds = source.IncludeSeconds;
        target.Second = source.Second ?? target.Second;
        target.Minute = source.Minute ?? target.Minute;
        target.Hour = source.Hour ?? target.Hour;
        target.DayOfMonth = source.DayOfMonth ?? target.DayOfMonth;
        target.Month = source.Month ?? target.Month;
        target.DayOfWeek = source.DayOfWeek ?? target.DayOfWeek;
    }

    private void SaveState()
    {
        try
        {
            _settingsProvider.SetSetting(
                StateSetting,
                JsonSerializer.Serialize(new PersistedState
                {
                    Builder = _state,
                    Expression = _expression,
                    Count = _count,
                    Format = _format
                }));
        }
        catch
        {
            // 持久化失败不影响使用
        }
    }

    private sealed class PersistedState
    {
        public CronBuilderState? Builder { get; set; }

        public string? Expression { get; set; }

        public int? Count { get; set; }

        public string? Format { get; set; }
    }
}

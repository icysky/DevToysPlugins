using System.ComponentModel.Composition;
using System.Text;
using System.Text.Json;
using DevToys.Api;
using EntityJson.DevToys.Core;

namespace EntityJson.DevToys;

[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(EntityJsonResourceAssemblyIdentifier))]
internal sealed class EntityJsonResourceAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
        => ValueTask.FromResult(Array.Empty<FontDefinition>());
}

/// <summary>
/// 实体类转 JSON：把 C# 类 / record 的公开成员渲染成带占位值的 JSON 请求体。
/// 转换逻辑在 Core/ 下，纯字符串进、纯字符串出，便于冒烟测试。
/// </summary>
[Export(typeof(IGuiTool))]
[Name("EntityToJson")]
[ToolDisplayInformation(
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uF8FD',
    GroupName = PredefinedCommonToolGroupNames.Converters,
    ResourceManagerAssemblyIdentifier = nameof(EntityJsonResourceAssemblyIdentifier),
    ResourceManagerBaseName = "EntityJson.DevToys.Strings",
    ShortDisplayTitleResourceName = "EntityJsonShortDisplayTitle",
    LongDisplayTitleResourceName = "EntityJsonLongDisplayTitle",
    DescriptionResourceName = "EntityJsonDescription",
    AccessibleNameResourceName = "EntityJsonAccessibleName",
    SearchKeywordsResourceName = "EntityJsonSearchKeywords")]
internal sealed class EntityJsonGuiTool : IGuiTool
{
    private const string CSharpSample = """
public class CreateOrderRequest
{
    [JsonPropertyName("order_no")]
    public string OrderNo { get; set; }

    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderItem> Items { get; set; }
    public string? Remark { get; set; }
}

public class OrderItem
{
    public string Sku { get; set; }
    public int Quantity { get; set; }
}
""";

    private const string TypeScriptSample = """
export interface CreateOrderRequest {
  order_no: string;
  amount: number;
  createdAt: Date;
  items: OrderItem[];
  remark?: string | null;
}

export interface OrderItem {
  sku: string;
  quantity: number;
}
""";

    private const string JavaSample = """
public class CreateOrderRequest {
    @JsonProperty("order_no")
    private String orderNo;

    private BigDecimal amount;
    private List<OrderItem> items;
    private String remark;
}

public class OrderItem {
    private String sku;
    private int quantity;
}
""";

    private const string JsonSample = """
{
  "order_no": "A001",
  "amount": 12.5,
  "active": true,
  "items": [ { "sku": "X-1", "quantity": 2 } ],
  "remark": null
}
""";

    private enum Direction
    {
        EntityToJson,
        JsonToEntity
    }

    private static readonly SettingDefinition<string> StateSetting = new("EntityJson.State", string.Empty);

    private static readonly string[] IndentUnits = ["  ", "    ", "\t", ""];

    private static readonly string[] IndentLabels = ["2 空格", "4 空格", "Tab", "压缩（单行）"];

    private readonly ISettingsProvider _settingsProvider;

    private Direction _direction = Direction.EntityToJson;
    private SourceLanguage _language = SourceLanguage.CSharp;
    private string _code = CSharpSample;
    private string? _rootType;
    private string _className = "Root";
    private char _keyQuote = '"';
    private char _valueQuote = '"';
    private FieldNamingPolicy _naming = FieldNamingPolicy.CamelCase;
    private bool _namingTouched;
    private AnnotationStyle _annotation = AnnotationStyle.SystemTextJson;
    private int _indentIndex;
    private int _arrayItems = 1;
    private int _maxDepth = 5;
    private bool _includeNulls = true;
    private bool _sortFields;
    private bool _useInitials;
    private bool _rootArray;
    private string _wrapName = string.Empty;

    private EntityParseResult _model = new([], []);
    private bool _suppress = true;

    private IUISelectDropDownList _directionSelect = default!;
    private IUISelectDropDownList _languageSelect = default!;
    private IUISingleLineTextInput _classNameInput = default!;
    private IUISetting _directionRow = default!;
    private IUISetting _languageRow = default!;
    private IUISetting _rootRow = default!;
    private IUISetting _classNameRow = default!;
    private IUISetting _keyQuoteRow = default!;
    private IUISetting _valueQuoteRow = default!;
    private IUISetting _namingRow = default!;
    private IUISetting _annotationRow = default!;
    private IUISetting _indentRow = default!;
    private IUISetting _arrayRow = default!;
    private IUISetting _depthRow = default!;
    private IUISetting _wrapRow = default!;
    private IUISetting _nullRow = default!;
    private IUISetting _sortRow = default!;
    private IUISetting _initialRow = default!;
    private IUISetting _rootArrayRow = default!;
    private IUIMultiLineTextInput _sourceInput = default!;
    private IUIMultiLineTextInput _outputInput = default!;
    private IUISingleLineTextInput _statusInput = default!;
    private IUISelectDropDownList _rootSelect = default!;
    private IUISelectDropDownList _keyQuoteSelect = default!;
    private IUISelectDropDownList _valueQuoteSelect = default!;
    private IUISelectDropDownList _namingSelect = default!;
    private IUISelectDropDownList _annotationSelect = default!;
    private IUISelectDropDownList _indentSelect = default!;
    private IUINumberInput _arrayInput = default!;
    private IUINumberInput _depthInput = default!;
    private IUISingleLineTextInput _wrapInput = default!;
    private IUISwitch _includeNullsSwitch = default!;
    private IUISwitch _sortSwitch = default!;
    private IUISwitch _initialSwitch = default!;
    private IUISwitch _rootArraySwitch = default!;

    private UIToolView? _view;

    public UIToolView View => _view ??= BuildView();

    [ImportingConstructor]
    public EntityJsonGuiTool(ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;
        LoadState();
    }

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
    }

    // ---------------------------------------------------------------- 视图

    private UIToolView BuildView()
    {
        CreateControls();

        _directionRow = Row("entity-direction-setting", "转换方向", _directionSelect);
        _languageRow = Row("entity-language-setting", "源语言", _languageSelect);
        _rootRow = Row("entity-root-setting", "根类型", _rootSelect);
        _classNameRow = Row("entity-classname-setting", "类名", _classNameInput);
        _keyQuoteRow = Row("entity-keyquote-setting", "键引号", _keyQuoteSelect);
        _valueQuoteRow = Row("entity-valuequote-setting", "值引号", _valueQuoteSelect);
        _namingRow = Row("entity-naming-setting", "字段命名", _namingSelect);
        _annotationRow = Row("entity-annotation-setting", "字段注解", _annotationSelect);
        _indentRow = Row("entity-indent-setting", "缩进", _indentSelect);
        _arrayRow = Row("entity-array-setting", "数组元素个数", _arrayInput);
        _depthRow = Row("entity-depth-setting", "嵌套深度", _depthInput);
        _wrapRow = Row("entity-wrap-setting", "根节点包裹名", _wrapInput);
        _nullRow = Row("entity-null-setting", "包含空值字段", _includeNullsSwitch);
        _sortRow = Row("entity-sort-setting", "按字段名排序", _sortSwitch);
        _initialRow = Row("entity-initial-setting", "采用字段初始值", _initialSwitch);
        _rootArrayRow = Row("entity-rootarray-setting", "根节点输出为数组", _rootArraySwitch);

        IUIElement left = GUI.Stack()
            .Vertical()
            .SmallSpacing()
            .WithChildren(
                GUI.Text(GUI.Label("entity-settings-title"), "转换设置"),
                _directionRow,
                _languageRow,
                _rootRow,
                _classNameRow,
                _keyQuoteRow,
                _valueQuoteRow,
                _namingRow,
                _annotationRow,
                _indentRow,
                _arrayRow,
                _depthRow,
                _wrapRow,
                _nullRow,
                _sortRow,
                _initialRow,
                _rootArrayRow);

        IUIElement right = GUI.Grid()
            .Columns(GUI.Fraction(1))
            .Rows(GUI.Fraction(1), GUI.Fraction(1), UIGridLength.Auto)
            .RowMediumSpacing()
            .Cells(
                GUI.Cell(0, 0, 1, 1, _sourceInput),
                GUI.Cell(1, 0, 1, 1, _outputInput),
                GUI.Cell(2, 0, 1, 1, _statusInput));

        _view = new UIToolView(
            GUI.Grid()
                .Columns(GUI.Fraction(2), GUI.Fraction(3))
                .Rows(GUI.Fraction(1))
                .ColumnLargeSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, left),
                    GUI.Cell(0, 1, 1, 1, right)));

        SyncControlsFromState();
        RefreshAnnotationItems();
        UpdateDirectionUi();
        _suppress = false;
        Refresh();

        return _view;
    }

    private void CreateControls()
    {
        IUIDropDownListItem[] directionItems =
        [
            GUI.Item("实体类 → JSON", Direction.EntityToJson),
            GUI.Item("JSON → 实体类", Direction.JsonToEntity)
        ];

        _directionSelect = GUI.SelectDropDownList("entity-direction")
            .WithItems(directionItems)
            .Select(directionItems[(int)_direction])
            .OnItemSelected(_ =>
            {
                // 读控件当前的选中项，而不是事件参数：首次渲染时可能收到一次过期的旧事件
                if (_suppress || _directionSelect.SelectedItem?.Value is not Direction direction
                    || direction == _direction)
                {
                    return;
                }

                _direction = direction;

                if (IsSampleCode(_code))
                {
                    _code = SampleFor(_direction, _language);
                    _suppress = true;
                    GUI.Text(_sourceInput, _code);
                    _suppress = false;
                }

                _rootType = null;
                ApplyDefaultNaming();
                UpdateDirectionUi();
                SaveState();
                Refresh();
            });

        IUIDropDownListItem[] languageItems =
        [
            GUI.Item("C#", SourceLanguage.CSharp),
            GUI.Item("TypeScript", SourceLanguage.TypeScript),
            GUI.Item("Java", SourceLanguage.Java)
        ];

        _languageSelect = GUI.SelectDropDownList("entity-language")
            .WithItems(languageItems)
            .Select(languageItems[(int)_language])
            .OnItemSelected(_ =>
            {
                if (_suppress || _languageSelect.SelectedItem?.Value is not SourceLanguage language
                    || language == _language)
                {
                    return;
                }

                _language = language;

                // 用户没改过示例时，跟着语言换成对应示例，避免拿 C# 示例去解析 TS/Java
                if (IsSampleCode(_code))
                {
                    _code = SampleFor(_direction, _language);
                    _suppress = true;
                    GUI.Text(_sourceInput, _code);
                    _suppress = false;
                }

                _rootType = null;
                ApplyDefaultNaming();

                // 正向时这行虽然隐藏，也要同步，否则切到反向时会拿到上一个语言的选项
                RefreshAnnotationItems();
                SaveState();
                Refresh();
            });

        _classNameInput = GUI.SingleLineTextInput("entity-classname")
            .HideCommandBar()
            .Text(_className)
            .OnTextChanged(text =>
            {
                if (_suppress)
                {
                    return;
                }

                _className = text;
                SaveState();
                Render();
            });

        _sourceInput = GUI.MultiLineTextInput("entity-source")
            .Title("实体类源码")
            .NeverWrap()
            .OnTextChanged(text =>
            {
                if (_suppress)
                {
                    return;
                }

                _code = text;
                SaveState();
                Refresh();
            });

        _outputInput = GUI.MultiLineTextInput("entity-output")
            .Title("JSON 输出")
            .ReadOnly()
            .NeverWrap()
            .Extendable();

        _statusInput = GUI.SingleLineTextInput("entity-status")
            .Title("诊断")
            .ReadOnly()
            .HideCommandBar();

        _rootSelect = GUI.SelectDropDownList("entity-root")
            .WithItems(Array.Empty<IUIDropDownListItem>())
            .OnItemSelected(_ =>
            {
                if (_suppress || _rootSelect.SelectedItem?.Value is not string name)
                {
                    return;
                }

                _rootType = name;
                SaveState();
                Render();
            });

        _keyQuoteSelect = QuoteSelect("entity-keyquote", _keyQuote, value =>
        {
            _keyQuote = value;
            SaveState();
            Render();
        });

        _valueQuoteSelect = QuoteSelect("entity-valuequote", _valueQuote, value =>
        {
            _valueQuote = value;
            SaveState();
            Render();
        });

        IUIDropDownListItem[] namingItems =
        [
            GUI.Item("camelCase（默认）", FieldNamingPolicy.CamelCase),
            GUI.Item("保持原名", FieldNamingPolicy.Keep),
            GUI.Item("snake_case", FieldNamingPolicy.SnakeCase),
            GUI.Item("PascalCase", FieldNamingPolicy.PascalCase),
            GUI.Item("kebab-case", FieldNamingPolicy.KebabCase)
        ];

        _namingSelect = GUI.SelectDropDownList("entity-naming")
            .WithItems(namingItems)
            .Select(namingItems[0])
            .OnItemSelected(_ =>
            {
                if (_suppress || _namingSelect.SelectedItem?.Value is not FieldNamingPolicy policy)
                {
                    return;
                }

                _naming = policy;
                _namingTouched = true;
                SaveState();
                Render();
            });

        if (!AnnotationItems(_language).Any(i => Equals(i.Value, _annotation)))
        {
            _annotation = DefaultAnnotation(_language);
        }

        IUIDropDownListItem[] annotationItems = AnnotationItems(_language);

        _annotationSelect = GUI.SelectDropDownList("entity-annotation")
            .WithItems(annotationItems)
            .Select(annotationItems.First(i => Equals(i.Value, _annotation)))
            .OnItemSelected(_ =>
            {
                if (_suppress || _annotationSelect.SelectedItem?.Value is not AnnotationStyle style)
                {
                    return;
                }

                _annotation = style;
                SaveState();
                Render();
            });

        IUIDropDownListItem[] indentItems = [.. IndentLabels.Select((label, index) => GUI.Item(label, index))];

        _indentSelect = GUI.SelectDropDownList("entity-indent")
            .WithItems(indentItems)
            .Select(indentItems[0])
            .OnItemSelected(_ =>
            {
                if (_suppress || _indentSelect.SelectedItem?.Value is not int index)
                {
                    return;
                }

                _indentIndex = index;
                SaveState();
                Render();
            });

        _arrayInput = GUI.NumberInput("entity-array", 0, 5, 1)
            .HideCommandBar()
            .Value(_arrayItems)
            .OnValueChanged(value =>
            {
                if (_suppress)
                {
                    return;
                }

                _arrayItems = (int)Math.Clamp(Math.Round(value), 0, 5);
                SaveState();
                Render();
            });

        _depthInput = GUI.NumberInput("entity-depth", 1, 10, 1)
            .HideCommandBar()
            .Value(_maxDepth)
            .OnValueChanged(value =>
            {
                if (_suppress)
                {
                    return;
                }

                _maxDepth = (int)Math.Clamp(Math.Round(value), 1, 10);
                SaveState();
                Render();
            });

        _wrapInput = GUI.SingleLineTextInput("entity-wrap")
            .HideCommandBar()
            .Text(_wrapName)
            .OnTextChanged(text =>
            {
                if (_suppress)
                {
                    return;
                }

                _wrapName = text;
                SaveState();
                Render();
            });

        _includeNullsSwitch = Switch("entity-null", _includeNulls, value =>
        {
            _includeNulls = value;
            SaveState();
            Render();
        });

        _sortSwitch = Switch("entity-sort", _sortFields, value =>
        {
            _sortFields = value;
            SaveState();
            Render();
        });

        _initialSwitch = Switch("entity-initial", _useInitials, value =>
        {
            _useInitials = value;
            SaveState();
            Render();
        });

        _rootArraySwitch = Switch("entity-rootarray", _rootArray, value =>
        {
            _rootArray = value;
            SaveState();
            Render();
        });
    }

    private static IUISetting Row(string id, string title, IUIElement control)
    {
        return GUI.Setting(id)
            .Title(title)
            .InteractiveElement(control);
    }

    private IUISelectDropDownList QuoteSelect(string id, char current, Action<char> apply)
    {
        IUIDropDownListItem[] items =
        [
            GUI.Item("双引号  \"", '"'),
            GUI.Item("单引号  '", '\'')
        ];

        IUISelectDropDownList select = GUI.SelectDropDownList(id)
            .WithItems(items)
            .Select(items[current == '"' ? 0 : 1]);

        select.OnItemSelected(_ =>
        {
            if (_suppress || select.SelectedItem?.Value is not char value)
            {
                return;
            }

            apply(value);
        });

        return select;
    }

    private IUISwitch Switch(string id, bool isOn, Action<bool> apply)
    {
        IUISwitch toggle = isOn ? GUI.Switch(id).On() : GUI.Switch(id).Off();
        toggle.OnToggle(_ =>
        {
            if (_suppress)
            {
                return;
            }

            apply(toggle.IsOn);
        });

        return toggle;
    }

    // ---------------------------------------------------------------- 解析与渲染

    /// <summary>重新解析源码，同步根类型下拉，然后渲染。</summary>
    private void Refresh()
    {
        if (_direction == Direction.EntityToJson)
        {
            _model = EntityParser.Parse(_language, _code);
            SyncRootItems();
        }

        Render();
    }

    private void SyncRootItems()
    {
        IReadOnlyList<EntityType> classes = _model.Classes;
        IUIDropDownListItem[] items = [.. classes.Select(t => GUI.Item(t.Name, t.Name))];

        _suppress = true;
        try
        {
            if (items.Length > 0)
            {
                GUI.WithItems(_rootSelect, items);
            }

            EntityType? chosen = _model.FindClass(_rootType);
            _rootType = chosen?.Name;

            if (chosen is not null)
            {
                GUI.Select(_rootSelect, items.First(i => Equals(i.Value, chosen.Name)));
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>只按当前选项重新渲染，不重新解析。</summary>
    private void Render()
    {
        if (_direction == Direction.JsonToEntity)
        {
            RenderJsonToEntity();
            return;
        }

        if (ShowDirectionHint(forward: true))
        {
            return;
        }

        var options = new JsonRenderOptions
        {
            KeyQuote = _keyQuote,
            ValueQuote = _valueQuote,
            IndentUnit = IndentUnits[_indentIndex],
            IncludeNulls = _includeNulls,
            ArrayItemCount = _arrayItems,
            MaxDepth = _maxDepth,
            SortFields = _sortFields,
            UseInitialValues = _useInitials,
            NamingPolicy = _naming,
            WrapRootName = string.IsNullOrWhiteSpace(_wrapName) ? null : _wrapName.Trim(),
            RootArray = _rootArray
        };

        JsonRenderResult result = JsonSampleGenerator.Generate(_model, _rootType, options);

        GUI.Text(
            _outputInput,
            HasError(result.Diagnostics) ? BuildErrorText(result.Diagnostics) : result.Json);
        GUI.Text(_statusInput, BuildStatus(_model.Classes.Count, result.FieldCount, result.Diagnostics));
    }

    /// <summary>方向选错时给出明确提示，而不是只报「没有可以导出的类」。</summary>
    private bool ShowDirectionHint(bool forward)
    {
        if (forward)
        {
            if (_model.Classes.Count == 0 && LooksLikeJson(_code))
            {
                GUI.Text(_outputInput, BuildHintText(
                    "输入看起来是 JSON。请把「转换方向」切到「JSON → 实体类」。"));
                GUI.Text(_statusInput, "类型 0 · 字段 0");
                return true;
            }

            return false;
        }

        if (!LooksLikeJson(_code) && LooksLikeEntityCode(_code))
        {
            GUI.Text(_outputInput, BuildHintText(
                "输入看起来是实体类代码。请把「转换方向」切到「实体类 → JSON」。"));
            GUI.Text(_statusInput, "类型 0 · 字段 0");
            return true;
        }

        return false;
    }

    private string BuildHintText(string hint)
    {
        string direction = _direction == Direction.EntityToJson ? "实体类 → JSON" : "JSON → 实体类";
        return string.Join(
            Environment.NewLine,
            "出错了：方向可能选错了。",
            hint,
            $"当前方向：{direction}；语言：{EntityParser.DisplayName(_language)}");
    }

    private static bool HasError(IReadOnlyList<Diagnostic> diagnostics)
        => diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>把错误写进输出框：标题一行，下面是最多 6 条带行号的明细。</summary>
    private static string BuildErrorText(IReadOnlyList<Diagnostic> diagnostics)
    {
        List<string> errors = [.. diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.Line > 0 ? $"第 {d.Line} 行：{d.Message}" : d.Message)];

        var builder = new StringBuilder();
        builder.AppendLine("出错了：");

        foreach (string message in errors.Take(6))
        {
            builder.Append("• ").AppendLine(message);
        }

        if (errors.Count > 6)
        {
            builder.Append("…共 ").Append(errors.Count).AppendLine(" 条错误");
        }

        return builder.ToString().TrimEnd();
    }

    private static bool LooksLikeJson(string text)
    {
        string trimmed = text.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static bool LooksLikeEntityCode(string text)
        => text.Contains("class ", StringComparison.Ordinal)
           || text.Contains("interface ", StringComparison.Ordinal)
           || text.Contains("record ", StringComparison.Ordinal)
           || text.Contains("enum ", StringComparison.Ordinal);

    private void RenderJsonToEntity()
    {
        if (ShowDirectionHint(forward: false))
        {
            return;
        }

        string indent = IndentUnits[_indentIndex];

        var options = new JsonEntityOptions
        {
            TargetLanguage = _language,
            RootClassName = string.IsNullOrWhiteSpace(_className) ? "Root" : _className,
            Naming = _naming,
            IndentUnit = string.IsNullOrEmpty(indent) ? "    " : indent,
            AnnotateRenamed = true,
            AnnotationStyle = _annotation
        };

        JsonEntityResult result = JsonEntityGenerator.Generate(_code, options);

        GUI.Text(
            _outputInput,
            HasError(result.Diagnostics) ? BuildErrorText(result.Diagnostics) : result.Code);
        GUI.Text(_statusInput, BuildStatus(result.TypeCount, result.FieldCount, result.Diagnostics));
    }

    private static string BuildStatus(int typeCount, int fieldCount, IReadOnlyList<Diagnostic> diagnostics)
    {
        var warnings = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .Select(d => d.Message)
            .ToList();

        string status = $"类型 {typeCount} · 字段 {fieldCount}";

        if (warnings.Count > 0)
        {
            status += $" · 警告 {warnings.Count}：{string.Join("；", warnings.Take(2))}";
            if (warnings.Count > 2)
            {
                status += "…";
            }
        }

        return status;
    }

    /// <summary>按方向切换标题与选项显隐（反向只需要命名、缩进、类名）。</summary>
    private void UpdateDirectionUi()
    {
        bool forward = _direction == Direction.EntityToJson;

        Show(_rootRow, forward);
        Show(_classNameRow, !forward);
        Show(_annotationRow, !forward);
        Show(_keyQuoteRow, forward);
        Show(_valueQuoteRow, forward);
        Show(_arrayRow, forward);
        Show(_depthRow, forward);
        Show(_wrapRow, forward);
        Show(_nullRow, forward);
        Show(_sortRow, forward);
        Show(_initialRow, forward);
        Show(_rootArrayRow, forward);

        GUI.Title(_sourceInput, forward ? "实体类源码" : "JSON 输入");
        GUI.Title(_outputInput, forward ? "JSON 输出" : "实体代码");
        GUI.Title(_languageRow, forward ? "源语言" : "目标语言");
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

    private void ApplyDefaultNaming()
    {
        if (_namingTouched)
        {
            return;
        }

        _naming = DefaultNaming(_direction, _language);
        _suppress = true;
        SelectByValue(_namingSelect, _naming);
        _suppress = false;
    }

    // ---------------------------------------------------------------- 状态同步

    private void SyncControlsFromState()
    {
        bool previous = _suppress;
        _suppress = true;

        try
        {
            GUI.Text(_sourceInput, _code);
            GUI.Text(_wrapInput, _wrapName);
            GUI.Text(_classNameInput, _className);
            GUI.Value(_arrayInput, _arrayItems);
            GUI.Value(_depthInput, _maxDepth);
            GUI.Text(_statusInput, string.Empty);

            SelectByValue(_directionSelect, _direction);
            SelectByValue(_languageSelect, _language);
            SelectByValue(_namingSelect, _naming);
            SelectByValue(_annotationSelect, _annotation);
            SelectByValue(_indentSelect, _indentIndex);
            SelectByValue(_keyQuoteSelect, _keyQuote);
            SelectByValue(_valueQuoteSelect, _valueQuote);
        }
        finally
        {
            _suppress = previous;
        }
    }

    private static void SelectByValue<T>(IUISelectDropDownList select, T value)
        where T : notnull
    {
        // 下拉项在创建时已经按固定顺序放好，这里按 Value 找回来再选中
        // （DevToys 的 Select(index) 在首次渲染时可能落到别的项上）。
        foreach (IUIDropDownListItem item in select.Items ?? [])
        {
            if (Equals(item.Value, value))
            {
                GUI.Select(select, item);
                return;
            }
        }
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

            if (state.Direction is Direction direction) _direction = direction;
            if (state.Language is SourceLanguage language) _language = language;
            if (state.Code is not null) _code = state.Code;
            else _code = SampleFor(_direction, _language);
            if (state.ClassName is not null) _className = state.ClassName;
            if (state.NamingTouched is bool touched) _namingTouched = touched;
            if (state.RootType is not null) _rootType = state.RootType;
            if (state.KeyQuote is '"' or '\'') _keyQuote = state.KeyQuote.Value;
            if (state.ValueQuote is '"' or '\'') _valueQuote = state.ValueQuote.Value;
            if (state.Naming is FieldNamingPolicy naming) _naming = naming;
            if (!_namingTouched) _naming = DefaultNaming(_direction, _language);
            if (state.Annotation is AnnotationStyle annotation) _annotation = annotation;
            if (!IsAnnotationValid(_language, _annotation)) _annotation = DefaultAnnotation(_language);
            if (state.IndentIndex is int indent) _indentIndex = Math.Clamp(indent, 0, IndentUnits.Length - 1);
            if (state.ArrayItems is int arrayItems) _arrayItems = Math.Clamp(arrayItems, 0, 5);
            if (state.MaxDepth is int depth) _maxDepth = Math.Clamp(depth, 1, 10);
            if (state.IncludeNulls is bool includeNulls) _includeNulls = includeNulls;
            if (state.SortFields is bool sort) _sortFields = sort;
            if (state.UseInitialValues is bool initials) _useInitials = initials;
            if (state.RootArray is bool rootArray) _rootArray = rootArray;
            if (state.WrapName is not null) _wrapName = state.WrapName;
        }
        catch
        {
            // 设置损坏时退回默认值
        }
    }

    private void SaveState()
    {
        try
        {
            _settingsProvider.SetSetting(
                StateSetting,
                JsonSerializer.Serialize(new PersistedState
                {
                    Direction = _direction,
                    Language = _language,
                    Code = _code,
                    RootType = _rootType,
                    ClassName = _className,
                    KeyQuote = _keyQuote,
                    ValueQuote = _valueQuote,
                    Naming = _naming,
                    NamingTouched = _namingTouched,
                    Annotation = _annotation,
                    IndentIndex = _indentIndex,
                    ArrayItems = _arrayItems,
                    MaxDepth = _maxDepth,
                    IncludeNulls = _includeNulls,
                    SortFields = _sortFields,
                    UseInitialValues = _useInitials,
                    RootArray = _rootArray,
                    WrapName = _wrapName
                }));
        }
        catch
        {
            // 持久化失败不影响使用
        }
    }

    private static string SampleFor(Direction direction, SourceLanguage language)
    {
        if (direction == Direction.JsonToEntity)
        {
            return JsonSample;
        }

        return language switch
        {
            SourceLanguage.TypeScript => TypeScriptSample,
            SourceLanguage.Java => JavaSample,
            _ => CSharpSample
        };
    }

    /// <summary>
    /// 正向：C# 实体习惯 PascalCase → camelCase；TS 的属性名就是 JSON 键，保持原名。
    /// 反向：C# 属性 → PascalCase，Java 字段 → camelCase（都会按需生成序列化注解）。
    /// </summary>
    private static FieldNamingPolicy DefaultNaming(Direction direction, SourceLanguage language)
    {
        if (direction == Direction.JsonToEntity)
        {
            return language == SourceLanguage.CSharp ? FieldNamingPolicy.PascalCase : FieldNamingPolicy.CamelCase;
        }

        return language == SourceLanguage.TypeScript ? FieldNamingPolicy.Keep : FieldNamingPolicy.CamelCase;
    }

    private static IUIDropDownListItem[] AnnotationItems(SourceLanguage language) => language switch
    {
        SourceLanguage.CSharp =>
        [
            GUI.Item("System.Text.Json（默认）", AnnotationStyle.SystemTextJson),
            GUI.Item("Newtonsoft.Json", AnnotationStyle.NewtonsoftJson),
            GUI.Item("SqlSugar（SugarColumn）", AnnotationStyle.SqlSugar),
            GUI.Item("不生成注解（保留原键名）", AnnotationStyle.None)
        ],
        SourceLanguage.Java =>
        [
            GUI.Item("Jackson（默认）", AnnotationStyle.Jackson),
            GUI.Item("Gson", AnnotationStyle.Gson),
            GUI.Item("fastjson", AnnotationStyle.FastJson),
            GUI.Item("MyBatis-Plus（TableField）", AnnotationStyle.MyBatisPlus),
            GUI.Item("不生成注解（保留原键名）", AnnotationStyle.None)
        ],
        _ =>
        [
            GUI.Item("不生成注解（TS 无注解）", AnnotationStyle.None)
        ]
    };

    private static AnnotationStyle DefaultAnnotation(SourceLanguage language) => language switch
    {
        SourceLanguage.Java => AnnotationStyle.Jackson,
        SourceLanguage.TypeScript => AnnotationStyle.None,
        _ => AnnotationStyle.SystemTextJson
    };

    private static bool IsAnnotationValid(SourceLanguage language, AnnotationStyle style) => style switch
    {
        AnnotationStyle.None => true,
        AnnotationStyle.SystemTextJson or AnnotationStyle.NewtonsoftJson or AnnotationStyle.SqlSugar
            => language == SourceLanguage.CSharp,
        _ => language == SourceLanguage.Java
    };

    /// <summary>按目标语言重建注解下拉；当前值不适用时退回该语言的默认注解。</summary>
    private void RefreshAnnotationItems()
    {
        IUIDropDownListItem[] items = AnnotationItems(_language);

        if (!IsAnnotationValid(_language, _annotation))
        {
            _annotation = DefaultAnnotation(_language);
        }

        bool previous = _suppress;
        _suppress = true;

        try
        {
            GUI.WithItems(_annotationSelect, items);
            GUI.Select(_annotationSelect, items.First(i => Equals(i.Value, _annotation)));
        }
        finally
        {
            _suppress = previous;
        }
    }

    private static bool IsSampleCode(string code)
    {
        static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();
        string normalized = Normalize(code);
        return normalized == Normalize(CSharpSample)
               || normalized == Normalize(TypeScriptSample)
               || normalized == Normalize(JavaSample)
               || normalized == Normalize(JsonSample);
    }

    private sealed class PersistedState
    {
        public Direction? Direction { get; set; }

        public SourceLanguage? Language { get; set; }

        public string? Code { get; set; }

        public string? ClassName { get; set; }

        public string? RootType { get; set; }

        public char? KeyQuote { get; set; }

        public char? ValueQuote { get; set; }

        public FieldNamingPolicy? Naming { get; set; }

        public bool? NamingTouched { get; set; }

        public AnnotationStyle? Annotation { get; set; }

        public int? IndentIndex { get; set; }

        public int? ArrayItems { get; set; }

        public int? MaxDepth { get; set; }

        public bool? IncludeNulls { get; set; }

        public bool? SortFields { get; set; }

        public bool? UseInitialValues { get; set; }

        public bool? RootArray { get; set; }

        public string? WrapName { get; set; }
    }
}

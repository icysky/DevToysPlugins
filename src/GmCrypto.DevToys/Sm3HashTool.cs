using System.ComponentModel.Composition;
using System.Text.Json;
using DevToys.Api;
using GmCrypto.DevToys.Core;
using Sm3 = GM.Core.SM3.SM3;
using static DevToys.Api.GUI;

namespace GmCrypto.DevToys;

[Export(typeof(IGuiTool))]
[Name("GmSm3Hash")]
[ToolDisplayInformation(
    // U+E611 = ic_fluent_fingerprint_20_regular（已确认存在于 FluentSystemIcons）
    IconFontName = "FluentSystemIcons",
    IconGlyph = '\uE611',
    GroupName = PredefinedCommonToolGroupNames.EncodersDecoders,
    ResourceManagerAssemblyIdentifier = nameof(GmCryptoResourceAssemblyIdentifier),
    ResourceManagerBaseName = "GmCrypto.DevToys.Strings",
    ShortDisplayTitleResourceName = "Sm3ShortDisplayTitle",
    LongDisplayTitleResourceName = "Sm3LongDisplayTitle",
    DescriptionResourceName = "Sm3Description",
    AccessibleNameResourceName = "Sm3AccessibleName",
    SearchKeywordsResourceName = "Sm3SearchKeywords")]
internal sealed class Sm3HashTool : IGuiTool
{
    private static readonly SettingDefinition<string> StateSetting =
        new("GmCrypto.Sm3.State", string.Empty);

    private readonly IFileStorage _fileStorage;

    private readonly IClipboard _clipboard;

    private readonly ISettingsProvider _settingsProvider;

    private readonly bool _isLoading;

    // ---------------- 状态 ----------------
    private string _encoding = TextEncodings.Utf8;
    private bool _inputIsHex;
    private string _inputText = string.Empty;
    private string _outputText = string.Empty;

    // ---------------- 控件 ----------------
    private readonly IUIInfoBar _infoBar;

    private readonly IUISelectDropDownList _encodingSelect;

    private readonly IUISwitch _hexSwitch;

    private readonly IUIMultiLineTextInput _inputBox;

    private readonly IUIMultiLineTextInput _outputBox;

    private readonly IUIButton _runButton;

    private readonly IUIButton _copyButton;

    private readonly IUIButton _clearButton;

    private readonly IUIButton _fileButton;

    private readonly IUILabel _fileLabel;

    private readonly IUIProgressBar _fileProgress;

    private UIToolView? _view;

    public UIToolView View => _view ??= BuildView();

    [ImportingConstructor]
    public Sm3HashTool(
        IFileStorage fileStorage,
        IClipboard clipboard,
        ISettingsProvider settingsProvider)
    {
        _fileStorage = fileStorage;
        _clipboard = clipboard;
        _settingsProvider = settingsProvider;

        LoadState();
        _isLoading = true;

        _infoBar = GUI.InfoBar("gmc-sm3-info").Closable();

        IUIDropDownListItem[] encodingItems = UiKit.Items(
            ("UTF-8", TextEncodings.Utf8),
            ("GB18030", TextEncodings.Gb18030),
            ("ASCII", TextEncodings.Ascii));

        _encodingSelect = GUI.SelectDropDownList("gmc-sm3-encoding")
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

        _hexSwitch = GUI.Switch("gmc-sm3-hex");
        UiKit.SetSwitchState(_hexSwitch, _inputIsHex);
        _hexSwitch.OnToggle(on =>
        {
            _inputIsHex = on;
            SaveState();
        });

        _inputBox = GUI.MultiLineTextInput("gmc-sm3-input")
            .Title("输入")
            .HideCommandBar()
            .Text(_inputText)
            .OnTextChanged(text =>
            {
                _inputText = text;
                SaveState();
            });

        _outputBox = GUI.MultiLineTextInput("gmc-sm3-output")
            .Title("SM3 摘要（64 位小写 Hex）")
            .HideCommandBar()
            .ReadOnly();

        _runButton = GUI.Button("gmc-sm3-run")
            .AccentAppearance()
            .Text("计算 SM3")
            .OnClick(RunAsync);

        _copyButton = GUI.Button("gmc-sm3-copy")
            .Text("复制结果")
            .OnClick(CopyOutputAsync);

        _clearButton = GUI.Button("gmc-sm3-clear")
            .Text("清空")
            .OnClick(ClearFields);

        _fileButton = GUI.Button("gmc-sm3-file")
            .Text("选择文件并计算 SM3")
            .OnClick(HashFileAsync);

        _fileLabel = GUI.Label().Text("未选择文件").WrapIfNeeded();
        _fileProgress = GUI.ProgressBar("gmc-sm3-file-progress");

        // 控件文本必须在视图构建后写入
        _view = BuildView();
        GUI.Text(_inputBox, _inputText);
        GUI.Text(_outputBox, string.IsNullOrEmpty(_outputText)
            ? "尚未计算。输入文本或选择文件后开始。"
            : _outputText);

        _isLoading = false;
    }

    public void OnDataReceived(string dataTypeName, object? parsedData)
    {
    }

    // ---------------------------------------------------------------- 视图

    private UIToolView BuildView()
    {
        IUIElement parametersCard = UiKit.Card(
            "设置",
            UiKit.LabeledField("文本编码", _encodingSelect),
            GUI.Setting("gmc-sm3-setting-hex")
                .Title("输入按 Hex 解析")
                .Description("勾选后输入框按十六进制字节解读，适合对二进制数据做摘要")
                .InteractiveElement(_hexSwitch));

        IUIElement inputCard = UiKit.Card(
            "输入",
            _inputBox,
            _fileButton,
            _fileLabel,
            _fileProgress);

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

    // ---------------------------------------------------------------- 执行

    private async ValueTask RunAsync()
    {
        GUI.Text(_runButton, "计算中…");
        GUI.Disable(_runButton);
        GUI.Close(_infoBar);

        try
        {
            string result = await Task.Run(() =>
            {
                byte[] bytes = _inputIsHex
                    ? HexCodec.Parse(_inputText, "输入")
                    : TextEncodings.Resolve(_encoding).GetBytes(_inputText);

                return HexCodec.ToHex(Sm3.CreateDefault().ComputeHash(bytes));
            });

            ShowResult(result, "SM3 摘要计算完成");
        }
        catch (Exception ex)
        {
            ShowError("SM3 摘要计算失败", ex);
        }
        finally
        {
            GUI.Text(_runButton, "计算 SM3");
            GUI.Enable(_runButton);
        }
    }

    private async ValueTask HashFileAsync()
    {
        GUI.Close(_infoBar);
        GUI.Progress(_fileProgress, 0);

        // 注意：不能传 "*"（DevToys 会拼出非法过滤器 "*|*.|" 导致任何文件都选不中），
        // 全部文件必须用 "*.*"。
        SandboxedFileReader? file = await _fileStorage.PickOpenFileAsync("*.*");
        if (file is null)
        {
            return;
        }

        try
        {
            GUI.Text(_fileLabel, $"正在计算：{file.FileName}");
            GUI.Disable(_fileButton);

            using (file)
            {
                using Stream stream = await file.GetNewAccessToFileContentAsync(CancellationToken.None);

                long? totalLength = null;
                try
                {
                    if (stream.CanSeek && stream.Length > 0)
                    {
                        totalLength = stream.Length;
                    }
                }
                catch
                {
                    totalLength = null;
                }

                var progress = new Progress<double>(percent => GUI.Progress(_fileProgress, percent));
                string hex = await Task.Run(() => Sm3FileHash.Compute(stream, totalLength, progress));

                GUI.Text(_fileLabel, $"文件：{file.FileName}");
                ShowResult(hex, "文件 SM3 计算完成");
            }
        }
        catch (Exception ex)
        {
            GUI.Text(_fileLabel, "文件读取失败");
            ShowError("文件 SM3 计算失败", ex);
        }
        finally
        {
            GUI.Progress(_fileProgress, 0);
            GUI.Enable(_fileButton);
        }
    }

    private async ValueTask CopyOutputAsync()
    {
        if (string.IsNullOrEmpty(_outputText))
        {
            GUI.Title(_infoBar, "没有可复制的内容");
            GUI.Description(_infoBar, "先计算一次摘要。");
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
        GUI.Progress(_fileProgress, 0);
        GUI.Text(_fileLabel, "未选择文件");
        SaveState();
    }

    private void ShowResult(string result, string title)
    {
        _outputText = result;
        SaveState();
        GUI.Text(_outputBox, result);
        GUI.Title(_infoBar, title);
        GUI.Description(_infoBar, result);
        GUI.Success(_infoBar);
        GUI.Open(_infoBar);
    }

    private void ShowError(string title, Exception ex)
    {
        GUI.Title(_infoBar, title);
        GUI.Description(_infoBar, ex.Message);
        GUI.Error(_infoBar);
        GUI.Open(_infoBar);
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

            if (state.Encoding is not null) _encoding = state.Encoding;
            if (state.InputIsHex is not null) _inputIsHex = state.InputIsHex.Value;
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
                Encoding = _encoding,
                InputIsHex = _inputIsHex,
                Input = _inputText,
                Output = _outputText
            };

            _settingsProvider.SetSetting(StateSetting, JsonSerializer.Serialize(state));
        }
        catch
        {
            // 设置写入失败不影响摘要计算。
        }
    }

    private sealed class PersistedState
    {
        public string? Encoding { get; set; }
        public bool? InputIsHex { get; set; }
        public string? Input { get; set; }
        public string? Output { get; set; }
    }
}

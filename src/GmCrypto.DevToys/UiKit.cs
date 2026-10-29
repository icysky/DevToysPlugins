using DevToys.Api;
using static DevToys.Api.GUI;

namespace GmCrypto.DevToys;

/// <summary>四个工具共用的小型 UI 构件封装（只做布局与控件创建，不持有状态）。</summary>
internal static class UiKit
{
    public static IUIElement Card(string title, params IUIElement[] children)
        => GUI.Card(title, GUI.Stack().Vertical().MediumSpacing().WithChildren(children));

    public static IUIElement LabeledField(string label, IUIElement element)
        => GUI.Stack().Vertical().SmallSpacing().WithChildren(GUI.Label().Text(label), element);

    public static IUIElement FieldWithButton(string label, IUIElement input, IUIElement button)
        => GUI.Stack().Vertical().SmallSpacing().WithChildren(
            GUI.Label().Text(label),
            GUI.Grid()
                .Columns(GUI.Fraction(1), UIGridLength.Auto)
                .ColumnSmallSpacing()
                .Cells(
                    GUI.Cell(0, 0, 1, 1, input),
                    GUI.Cell(0, 1, 1, 1, button)));

    /// <summary>单行输入框：只负责把输入回调出去，持久化由调用方在 onChanged 里做。</summary>
    public static IUISingleLineTextInput TextBox(string id, string value, Action<string> onChanged)
        => GUI.SingleLineTextInput(id)
            .HideCommandBar()
            .Text(value)
            .OnTextChanged(text => onChanged(text));

    public static IUIDropDownListItem[] Items(params (string Label, string Value)[] definitions)
        => Array.ConvertAll(definitions, definition => GUI.Item(definition.Label, definition.Value));

    public static int IndexOf(IUIDropDownListItem[] items, string value)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].Value is string itemValue
                && string.Equals(itemValue, value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    public static void SetSwitchState(IUISwitch toggle, bool isOn)
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

    /// <summary>
    /// 只改可见性，绝不替换控件实例（DevToys 的 Blazor 组件只在初始化时订阅一次）。
    /// </summary>
    public static void Show(IUIElement element, bool visible)
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

    /// <summary>左侧参数 / 右侧输入，输出区横跨两列。</summary>
    public static IUIElement Layout(IUIElement parameters, IUIElement input, IUIElement output)
        => GUI.Grid()
            .Columns(GUI.Fraction(1), GUI.Fraction(1))
            .Rows(UIGridLength.Auto, UIGridLength.Auto)
            .ColumnLargeSpacing()
            .RowLargeSpacing()
            .Cells(
                GUI.Cell(0, 0, 1, 1, parameters),
                GUI.Cell(0, 1, 1, 1, input),
                GUI.Cell(1, 0, 1, 2, output));
}

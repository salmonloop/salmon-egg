using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UnoAcpClient.Presentation.ViewModels.Chat;

/// <summary>
/// 配置选项 ViewModel，用于在 UI 中展示和编辑会话配置选项。
/// 对应 ACP 协议中的 configOptions 字段。
/// </summary>
public partial class ConfigOptionViewModel : ObservableObject
{
    /// <summary>
    /// 配置选项的唯一标识符
    /// </summary>
    [ObservableProperty]
    private string _id = string.Empty;

    /// <summary>
    /// 配置选项的显示名称
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 配置选项的描述
    /// </summary>
    [ObservableProperty]
    private string? _description;

    /// <summary>
    /// 配置选项的当前值
    /// </summary>
    [ObservableProperty]
    private object? _value;

    /// <summary>
    /// 配置选项的值类型（string, number, boolean, array 等）
    /// </summary>
    [ObservableProperty]
    private string _valueType = "string";

    /// <summary>
    /// 可选值的列表（用于下拉选择）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<OptionValueViewModel> _options = new();

    /// <summary>
    /// 是否为必填项
    /// </summary>
    [ObservableProperty]
    private bool _isRequired;

    /// <summary>
    /// 选中的选项值（用于下拉选择）
    /// </summary>
    [ObservableProperty]
    private OptionValueViewModel? _selectedOption;

    /// <summary>
    /// 输入框的文本值（用于文本输入）
    /// </summary>
    [ObservableProperty]
    private string _textValue = string.Empty;

    /// <summary>
    /// 布尔值（用于开关选项）
    /// </summary>
    [ObservableProperty]
    private bool _boolValue;

    /// <summary>
    /// 数字值（用于数字输入）
    /// </summary>
    [ObservableProperty]
    private double _numberValue;

    /// <summary>
    /// 判断是否有可选值列表
    /// </summary>
    public bool HasOptions => Options.Count > 0;

    /// <summary>
    /// 判断是否为布尔类型
    /// </summary>
    public bool IsBoolType => ValueType == "boolean";

    /// <summary>
    /// 判断是否为数字类型
    /// </summary>
    public bool IsNumberType => ValueType == "number" || ValueType == "integer";

    /// <summary>
    /// 判断是否为字符串类型
    /// </summary>
    public bool IsStringType => ValueType == "string" && !HasOptions;

    /// <summary>
    /// 判断是否为选择类型
    /// </summary>
    public bool IsSelectType => HasOptions;

    /// <summary>
    /// 获取显示用的值文本
    /// </summary>
    public string DisplayValue
    {
        get
        {
            if (Value == null) return "未设置";

            return Value switch
            {
                string s => s,
                bool b => b ? "是" : "否",
                int i => i.ToString(),
                double d => d.ToString("F2"),
                JsonElement je => je.ValueKind switch
                {
                    JsonValueKind.String => je.GetString() ?? "",
                    JsonValueKind.Number => je.GetDouble().ToString("F2"),
                    JsonValueKind.True => "是",
                    JsonValueKind.False => "否",
                    JsonValueKind.Array => "[数组]",
                    JsonValueKind.Object => "{对象}",
                    _ => Value?.ToString() ?? "未设置"
                },
                _ => Value?.ToString() ?? "未设置"
            };
        }
    }

    /// <summary>
    /// 从 JSON 字典创建 ConfigOptionViewModel
    /// </summary>
    public static ConfigOptionViewModel CreateFromJson(string id, object value)
    {
        var viewModel = new ConfigOptionViewModel
        {
            Id = id,
            Name = FormatName(id),
            Value = value
        };

        if (value is JsonElement je)
        {
            viewModel.ValueType = je.ValueKind switch
            {
                JsonValueKind.String => "string",
                JsonValueKind.Number => "number",
                JsonValueKind.True or JsonValueKind.False => "boolean",
                JsonValueKind.Array => "array",
                JsonValueKind.Object => "object",
                _ => "unknown"
            };

            switch (je.ValueKind)
            {
                case JsonValueKind.String:
                    viewModel.TextValue = je.GetString() ?? string.Empty;
                    break;
                case JsonValueKind.Number:
                    viewModel.NumberValue = je.GetDouble();
                    break;
                case JsonValueKind.True:
                    viewModel.BoolValue = true;
                    break;
                case JsonValueKind.False:
                    viewModel.BoolValue = false;
                    break;
            }
        }
        else if (value is string s)
        {
            viewModel.ValueType = "string";
            viewModel.TextValue = s;
        }
        else if (value is bool b)
        {
            viewModel.ValueType = "boolean";
            viewModel.BoolValue = b;
        }
        else if (value is int or double or float or decimal)
        {
            viewModel.ValueType = "number";
            viewModel.NumberValue = Convert.ToDouble(value);
        }

        return viewModel;
    }

    private static string FormatName(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < id.Length; i++)
        {
            var c = id[i];
            if (i > 0 && (char.IsUpper(c) || c == '_'))
            {
                result.Append(' ');
            }
            if (c != '_')
            {
                result.Append(char.ToUpper(c));
            }
        }
        return result.ToString();
    }
}

/// <summary>
/// 配置选项的可选值 ViewModel
/// </summary>
public partial class OptionValueViewModel : ObservableObject
{
    /// <summary>
    /// 选项值
    /// </summary>
    [ObservableProperty]
    private string _value = string.Empty;

    /// <summary>
    /// 选项显示名称
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 选项描述
    /// </summary>
    [ObservableProperty]
    private string? _description;
}

/// <summary>
/// 配置选项定义（用于解析 Agent 返回的选项定义）
/// </summary>
public class ConfigOptionDefinition
{
    /// <summary>
    /// 配置选项名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 配置选项描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 配置选项类型
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// 是否必填
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// 默认值
    /// </summary>
    public object? Default { get; set; }

    /// <summary>
    /// 可选值列表
    /// </summary>
    public List<OptionDefinition>? Options { get; set; }
}

/// <summary>
/// 选项定义
/// </summary>
public class OptionDefinition
{
    /// <summary>
    /// 选项值
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// 选项名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 选项描述
    /// </summary>
    public string? Description { get; set; }
}

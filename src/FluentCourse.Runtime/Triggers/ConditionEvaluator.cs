using System.Globalization;
using System.Text.RegularExpressions;
using FluentCourse.Runtime.Player;
using Microsoft.UI.Xaml;

namespace FluentCourse.Runtime.Triggers;

/// <summary>
/// 触发器 condition 字段的求值器。docs/fcourse-format.md 只声明了该字段(默认 null),
/// 未定义具体语法;这里按"目标元素属性(或播放器变量)与字面值比较"的语义实现:
/// <code>
///   "ElementName.Property == 值"   元素属性比较(反射读取)
///   "$变量名 >= 3"                  播放器变量比较
/// </code>
/// 支持运算符:== != &gt; &gt;= &lt; &lt;=。两侧能解析为数字时按数值比较,否则按
/// 字符串(忽略大小写)比较;无法解析/不支持的写法输出 Debug 警告并放行(返回 true),
/// 保证旧课件行为不变。
/// </summary>
internal static class ConditionEvaluator
{
    private static readonly Regex Comparison =
        new(@"^(.+?)\s*(==|!=|>=|<=|>|<)\s*(.+)$", RegexOptions.Compiled);

    public static bool Evaluate(string? condition, FrameworkElement root, PagePlayer player)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;

        var m = Comparison.Match(condition.Trim());
        if (!m.Success)
        {
            Warn($"无法解析条件 '{condition}',已放行");
            return true;
        }

        var leftRaw = m.Groups[1].Value.Trim();
        var op = m.Groups[2].Value;
        var rightRaw = m.Groups[3].Value.Trim().Trim('\'', '"', '“', '”');

        if (!TryResolveOperand(leftRaw, root, player, out var leftVal))
        {
            Warn($"条件左操作数 '{leftRaw}' 无法解析(条件 '{condition}'),已放行");
            return true;
        }

        // 双方都能转数字 → 数值比较
        if (TryToDouble(leftVal, out var ld) &&
            double.TryParse(rightRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var rd))
        {
            return op switch
            {
                "==" => ld == rd,
                "!=" => ld != rd,
                ">"  => ld > rd,
                ">=" => ld >= rd,
                "<"  => ld < rd,
                "<=" => ld <= rd,
                _    => true
            };
        }

        // 否则按字符串比较(仅支持 == / !=,其余运算符不适用于字符串)
        var ls = Convert.ToString(leftVal, CultureInfo.InvariantCulture) ?? "";
        switch (op)
        {
            case "==":
                return string.Equals(ls, rightRaw, StringComparison.OrdinalIgnoreCase);
            case "!=":
                return !string.Equals(ls, rightRaw, StringComparison.OrdinalIgnoreCase);
            default:
                Warn($"运算符 '{op}' 不支持字符串比较(条件 '{condition}'),已放行");
                return true;
        }
    }

    /// <summary>解析左操作数:$变量 → 播放器变量;Element.Property → 反射读元素属性。</summary>
    private static bool TryResolveOperand(string raw, FrameworkElement root, PagePlayer player, out object? value)
    {
        value = null;

        // $变量名
        if (raw.StartsWith('$'))
        {
            var name = raw[1..];
            if (player.Variables.TryGetValue(name, out var v)) { value = v; return true; }
            // 未赋值的变量按默认 0/false/空 处理,视为可比较的空值
            value = "";
            return true;
        }

        // ElementName.Property
        var dot = raw.LastIndexOf('.');
        if (dot > 0 && dot < raw.Length - 1)
        {
            var elementName = raw[..dot];
            var propertyName = raw[(dot + 1)..];
            if (root.FindName(elementName) is not FrameworkElement element) return false;
            var prop = element.GetType().GetProperty(propertyName);
            if (prop is null) return false;
            try
            {
                value = prop.GetValue(element);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 裸名字 → 也按播放器变量处理
        if (player.Variables.TryGetValue(raw, out var bare)) { value = bare; return true; }
        return false;
    }

    private static bool TryToDouble(object? value, out double result)
    {
        switch (value)
        {
            case null: result = 0; return false;
            case double d: result = d; return true;
            case float f: result = f; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case bool b: result = b ? 1 : 0; return true;
            case string s:
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
            default:
                try
                {
                    result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    result = 0;
                    return false;
                }
        }
    }

    private static void Warn(string message) =>
        System.Diagnostics.Debug.WriteLine($"[ConditionEvaluator] {message}。");
}

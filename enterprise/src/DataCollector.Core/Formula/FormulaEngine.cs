using System.Globalization;
using DataCollector.Contracts;

namespace DataCollector.Core.Formula;

public sealed class FormulaEngine
{
    private static readonly string[] SupportedVariableNames =
    [
        "开机时间",
        "加工时间",
        "等待时间",
        "待机时间",
        "关机时间",
        "报警时间",
        "急停时间",
        "通信中断时间",
        "已观测时间",
        "日历天时间",
    ];

    private static readonly Dictionary<char, int> OperatorPrecedence = new()
    {
        ['+'] = 1,
        ['-'] = 1,
        ['*'] = 2,
        ['/'] = 2,
    };

    public double Evaluate(string expression, IReadOnlyDictionary<string, double> variables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        ArgumentNullException.ThrowIfNull(variables);

        var rpn = ToReversePolishNotation(expression);
        var stack = new Stack<double>();

        foreach (var token in rpn)
        {
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                stack.Push(number);
                continue;
            }

            if (token.Length == 1 && OperatorPrecedence.ContainsKey(token[0]))
            {
                if (stack.Count < 2)
                {
                    throw new InvalidOperationException($"公式 \"{expression}\" 语法无效。");
                }

                var right = stack.Pop();
                var left = stack.Pop();
                stack.Push(ApplyOperator(token[0], left, right));
                continue;
            }

            if (!variables.TryGetValue(token, out var variableValue))
            {
                throw new InvalidOperationException($"公式变量 \"{token}\" 未定义。");
            }

            stack.Push(variableValue);
        }

        if (stack.Count != 1)
        {
            throw new InvalidOperationException($"公式 \"{expression}\" 无法被正确计算。");
        }

        return Math.Round(stack.Pop(), 2, MidpointRounding.AwayFromZero);
    }

    public IReadOnlyDictionary<string, double> BuildVariableMap(DailyMetricsSnapshot metrics)
    {
        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["开机时间"] = metrics.PowerOnMinutes,
            ["加工时间"] = metrics.ProcessingMinutes,
            ["等待时间"] = metrics.WaitingMinutes + metrics.StandbyMinutes,
            ["待机时间"] = metrics.StandbyMinutes,
            ["关机时间"] = metrics.PowerOffMinutes,
            ["报警时间"] = metrics.AlarmMinutes,
            ["急停时间"] = metrics.EmergencyMinutes,
            ["通信中断时间"] = metrics.CommunicationInterruptedMinutes,
            ["已观测时间"] = metrics.ObservedMinutes,
            ["日历天时间"] = 1440d,
        };
    }

    public IReadOnlyList<string> GetSupportedVariableNames()
    {
        return SupportedVariableNames;
    }

    private static IEnumerable<string> ToReversePolishNotation(string expression)
    {
        var output = new List<string>();
        var operators = new Stack<char>();
        var index = 0;

        while (index < expression.Length)
        {
            var current = expression[index];

            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                var start = index;
                index++;
                while (index < expression.Length && (char.IsLetterOrDigit(expression[index]) || expression[index] == '_'))
                {
                    index++;
                }

                output.Add(expression[start..index]);
                continue;
            }

            if (char.IsDigit(current) || current == '.')
            {
                var start = index;
                index++;
                while (index < expression.Length && (char.IsDigit(expression[index]) || expression[index] == '.'))
                {
                    index++;
                }

                output.Add(expression[start..index]);
                continue;
            }

            if (OperatorPrecedence.ContainsKey(current))
            {
                while (operators.Count > 0 &&
                       OperatorPrecedence.TryGetValue(operators.Peek(), out var precedence) &&
                       precedence >= OperatorPrecedence[current])
                {
                    output.Add(operators.Pop().ToString());
                }

                operators.Push(current);
                index++;
                continue;
            }

            if (current == '(')
            {
                operators.Push(current);
                index++;
                continue;
            }

            if (current == ')')
            {
                while (operators.Count > 0 && operators.Peek() != '(')
                {
                    output.Add(operators.Pop().ToString());
                }

                if (operators.Count == 0 || operators.Pop() != '(')
                {
                    throw new InvalidOperationException($"公式 \"{expression}\" 括号不匹配。");
                }

                index++;
                continue;
            }

            throw new InvalidOperationException($"公式包含不支持的字符 \"{current}\"。");
        }

        while (operators.Count > 0)
        {
            var symbol = operators.Pop();
            if (symbol is '(' or ')')
            {
                throw new InvalidOperationException($"公式 \"{expression}\" 括号不匹配。");
            }

            output.Add(symbol.ToString());
        }

        return output;
    }

    private static double ApplyOperator(char operatorToken, double left, double right)
    {
        return operatorToken switch
        {
            '+' => left + right,
            '-' => left - right,
            '*' => left * right,
            '/' => Math.Abs(right) < double.Epsilon ? 0d : left / right,
            _ => throw new InvalidOperationException($"不支持的运算符 \"{operatorToken}\"。"),
        };
    }
}

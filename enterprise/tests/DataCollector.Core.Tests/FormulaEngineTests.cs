using DataCollector.Contracts;
using DataCollector.Core;
using DataCollector.Core.Formula;

namespace DataCollector.Core.Tests;

public sealed class FormulaEngineTests
{
    private readonly FormulaEngine _formulaEngine = new();

    [Fact]
    public void Evaluate_ShouldComputeConfiguredExpression()
    {
        var variables = _formulaEngine.BuildVariableMap(new DailyMetricsSnapshot
        {
            PowerOnMinutes = 600,
            ProcessingMinutes = 450,
            WaitingMinutes = 30,
            StandbyMinutes = 120,
            PowerOffMinutes = 240,
            AlarmMinutes = 0,
            EmergencyMinutes = 0,
            CommunicationInterruptedMinutes = 0,
            ObservedMinutes = 840,
        });

        var powerOnRate = _formulaEngine.Evaluate("(开机时间 / 已观测时间) * 100", variables);
        var utilizationRate = _formulaEngine.Evaluate("(加工时间 / 开机时间) * 100", variables);

        Assert.Equal(71.43, powerOnRate);
        Assert.Equal(75.00, utilizationRate);
    }

    [Fact]
    public void Evaluate_ShouldReturnZeroWhenDivideByZero()
    {
        var value = _formulaEngine.Evaluate("加工时间 / 开机时间 * 100", new Dictionary<string, double>
        {
            ["加工时间"] = 120,
            ["开机时间"] = 0,
        });

        Assert.Equal(0, value);
    }
}

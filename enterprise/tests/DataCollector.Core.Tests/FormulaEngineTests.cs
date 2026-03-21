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

        var powerOnRate = _formulaEngine.Evaluate("(power_on_minutes / observed_minutes) * 100", variables);
        var utilizationRate = _formulaEngine.Evaluate("(processing_minutes / power_on_minutes) * 100", variables);

        Assert.Equal(71.43, powerOnRate);
        Assert.Equal(75.00, utilizationRate);
    }

    [Fact]
    public void Evaluate_ShouldReturnZeroWhenDivideByZero()
    {
        var value = _formulaEngine.Evaluate("processing_minutes / power_on_minutes * 100", new Dictionary<string, double>
        {
            ["processing_minutes"] = 120,
            ["power_on_minutes"] = 0,
        });

        Assert.Equal(0, value);
    }
}

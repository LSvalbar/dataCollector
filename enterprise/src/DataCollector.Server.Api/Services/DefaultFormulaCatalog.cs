using DataCollector.Server.Api.Persistence;

namespace DataCollector.Server.Api.Services;

internal static class DefaultFormulaCatalog
{
    public const string PowerOnRateCode = "power_on_rate";
    public const string UtilizationRateCode = "utilization_rate";
    public const string PowerOnRateDisplayName = "开机率";
    public const string UtilizationRateDisplayName = "利用率";
    public const string PowerOnRateDescription = "默认按当天已观测时长计算开机率。";
    public const string UtilizationRateDescription = "默认按开机时间中的加工占比计算利用率。";
    public const string PowerOnRateExpression = "(开机时间 / 已观测时间) * 100";
    public const string UtilizationRateExpression = "(加工时间 / 开机时间) * 100";

    public static IReadOnlyList<FormulaEntity> CreateEntities(DateTimeOffset now)
    {
        return
        [
            CreatePowerOnRate(now),
            CreateUtilizationRate(now),
        ];
    }

    public static FormulaEntity CreatePowerOnRate(DateTimeOffset now)
    {
        return new FormulaEntity
        {
            Code = PowerOnRateCode,
            DisplayName = PowerOnRateDisplayName,
            Description = PowerOnRateDescription,
            Expression = PowerOnRateExpression,
            ResultUnit = "%",
            UpdatedAt = now,
            UpdatedBy = "system",
        };
    }

    public static FormulaEntity CreateUtilizationRate(DateTimeOffset now)
    {
        return new FormulaEntity
        {
            Code = UtilizationRateCode,
            DisplayName = UtilizationRateDisplayName,
            Description = UtilizationRateDescription,
            Expression = UtilizationRateExpression,
            ResultUnit = "%",
            UpdatedAt = now,
            UpdatedBy = "system",
        };
    }
}

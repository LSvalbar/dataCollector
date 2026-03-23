using DataCollector.Server.Api.Persistence;
using System.Globalization;

namespace DataCollector.Server.Api.Services;

internal static class DefaultFormulaCatalog
{
    public static readonly string[] BaseVisibleOptions =
    [
        "开机时间",
        "加工时间",
        "待机时间",
        "关机时间",
    ];

    public const string PowerOnRateCode = "power_on_rate";
    public const string UtilizationRateCode = "utilization_rate";
    public const string PowerOnRateDisplayName = "开机率";
    public const string UtilizationRateDisplayName = "利用率";
    public const string PowerOnRateDescription = "默认按开机时间 / 制式工时(小时) × 系数 计算开机率，界面按百分比显示。";
    public const string UtilizationRateDescription = "默认按选定时间项 / 制式工时(小时) × 系数 计算利用率，界面按百分比显示。";
    public const string DefaultPowerOnVariable = "开机时间";
    public const string DefaultUtilizationVariable = "加工时间";
    public const double DefaultStandardWorkHours = 10d;
    public const double DefaultCoefficient = 1d;
    public static readonly string PowerOnRateExpression = BuildExpression(DefaultPowerOnVariable, DefaultStandardWorkHours, DefaultCoefficient);
    public static readonly string UtilizationRateExpression = BuildExpression(DefaultUtilizationVariable, DefaultStandardWorkHours, DefaultCoefficient);

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
            PrimaryVariable = DefaultPowerOnVariable,
            StandardWorkHours = DefaultStandardWorkHours,
            Coefficient = DefaultCoefficient,
            VisibleOptionsCsv = string.Join(",", BaseVisibleOptions),
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
            PrimaryVariable = DefaultUtilizationVariable,
            StandardWorkHours = DefaultStandardWorkHours,
            Coefficient = DefaultCoefficient,
            VisibleOptionsCsv = string.Join(",", BaseVisibleOptions),
            ResultUnit = "%",
            UpdatedAt = now,
            UpdatedBy = "system",
        };
    }

    public static string BuildExpression(string primaryVariable, double standardWorkHours, double coefficient)
    {
        return $"(({primaryVariable} / ({standardWorkHours.ToString("0.####", CultureInfo.InvariantCulture)} * 60)) * {coefficient.ToString("0.####", CultureInfo.InvariantCulture)})";
    }
}

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using DataCollector.Contracts;

namespace DataCollector.Desktop.Wpf;

public partial class FormulaConfigWindow : Window
{
    private readonly IReadOnlyList<FormulaVariableOptionDto> _allOptions;
    private readonly ObservableCollection<FormulaVariableOptionDto> _visibleOptions;

    public FormulaConfigWindow(
        IReadOnlyList<FormulaVariableOptionDto> allOptions,
        IReadOnlyCollection<string> visibleOptions,
        FormulaSelection powerOnSelection,
        FormulaSelection utilizationSelection)
    {
        InitializeComponent();
        WindowLayoutHelper.EnableResponsiveSizing(this, 0.74, 0.82);

        _allOptions = allOptions
            .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _visibleOptions = new ObservableCollection<FormulaVariableOptionDto>(
            _allOptions.Where(option => visibleOptions.Contains(option.VariableName, StringComparer.OrdinalIgnoreCase)));

        if (_visibleOptions.Count == 0)
        {
            foreach (var option in _allOptions.Take(4))
            {
                _visibleOptions.Add(option);
            }
        }

        PowerOnMetricComboBox.ItemsSource = _visibleOptions;
        UtilizationMetricComboBox.ItemsSource = _visibleOptions;

        PowerOnMetricComboBox.SelectedValue = powerOnSelection.PrimaryVariable;
        UtilizationMetricComboBox.SelectedValue = utilizationSelection.PrimaryVariable;
        PowerOnStandardHoursTextBox.Text = powerOnSelection.StandardWorkHours.ToString("0.##", CultureInfo.InvariantCulture);
        UtilizationStandardHoursTextBox.Text = utilizationSelection.StandardWorkHours.ToString("0.##", CultureInfo.InvariantCulture);
        PowerOnCoefficientTextBox.Text = powerOnSelection.Coefficient.ToString("0.##", CultureInfo.InvariantCulture);
        UtilizationCoefficientTextBox.Text = utilizationSelection.Coefficient.ToString("0.##", CultureInfo.InvariantCulture);

        PowerOnMetricComboBox.SelectionChanged += (_, _) => UpdatePreview();
        UtilizationMetricComboBox.SelectionChanged += (_, _) => UpdatePreview();
        PowerOnStandardHoursTextBox.TextChanged += (_, _) => UpdatePreview();
        UtilizationStandardHoursTextBox.TextChanged += (_, _) => UpdatePreview();
        PowerOnCoefficientTextBox.TextChanged += (_, _) => UpdatePreview();
        UtilizationCoefficientTextBox.TextChanged += (_, _) => UpdatePreview();
        UpdateVisibleOptionsSummary();
        UpdatePreview();
    }

    public FormulaSelection? PowerOnSelection { get; private set; }

    public FormulaSelection? UtilizationSelection { get; private set; }

    public IReadOnlyList<string> VisibleOptions =>
        _visibleOptions.Select(option => option.VariableName).ToArray();

    private void UpdatePreview()
    {
        PowerOnPreviewTextBlock.Text = $"当前公式：{BuildPreview(PowerOnMetricComboBox.SelectedValue as string, PowerOnStandardHoursTextBox.Text, PowerOnCoefficientTextBox.Text)}";
        UtilizationPreviewTextBlock.Text = $"当前公式：{BuildPreview(UtilizationMetricComboBox.SelectedValue as string, UtilizationStandardHoursTextBox.Text, UtilizationCoefficientTextBox.Text)}";
    }

    private static string BuildPreview(string? variableName, string standardHoursText, string coefficientText)
    {
        if (string.IsNullOrWhiteSpace(variableName) ||
            !double.TryParse(standardHoursText, NumberStyles.Float, CultureInfo.InvariantCulture, out var standardHours) ||
            !double.TryParse(coefficientText, NumberStyles.Float, CultureInfo.InvariantCulture, out var coefficient) ||
            standardHours <= 0 ||
            coefficient <= 0)
        {
            return "-";
        }

        return $"{variableName} / 制式工时({standardHours:0.##}小时) × 系数({coefficient:0.##}) × 100";
    }

    private void AddOptionButton_Click(object sender, RoutedEventArgs e)
    {
        var candidates = _allOptions
            .Where(option => _visibleOptions.All(visible => !visible.VariableName.Equals(option.VariableName, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (candidates.Length == 0)
        {
            MessageBox.Show(this, "所有可用时间项都已经加入下拉选项。", "无需添加", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new FormulaOptionPickerWindow(candidates) { Owner = this };
        if (window.ShowDialog() != true || window.SelectedOption is null)
        {
            return;
        }

        _visibleOptions.Add(window.SelectedOption);
        OnVisibleOptionsChanged();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (PowerOnMetricComboBox.SelectedValue is not string powerOnVariable ||
            UtilizationMetricComboBox.SelectedValue is not string utilizationVariable)
        {
            MessageBox.Show(this, "请先从下拉列表中选择时间项。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParsePositiveDouble(PowerOnStandardHoursTextBox.Text, out var powerOnStandardHours) ||
            !TryParsePositiveDouble(UtilizationStandardHoursTextBox.Text, out var utilizationStandardHours))
        {
            MessageBox.Show(this, "制式工时必须是大于 0 的数字。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParsePositiveDouble(PowerOnCoefficientTextBox.Text, out var powerOnCoefficient) ||
            !TryParsePositiveDouble(UtilizationCoefficientTextBox.Text, out var utilizationCoefficient))
        {
            MessageBox.Show(this, "系数必须是大于 0 的数字。", "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PowerOnSelection = new FormulaSelection(powerOnVariable, powerOnStandardHours, powerOnCoefficient);
        UtilizationSelection = new FormulaSelection(utilizationVariable, utilizationStandardHours, utilizationCoefficient);
        DialogResult = true;
        Close();
    }

    private static bool TryParsePositiveDouble(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnVisibleOptionsChanged()
    {
        PowerOnMetricComboBox.Items.Refresh();
        UtilizationMetricComboBox.Items.Refresh();
        PowerOnMetricComboBox.SelectedValue ??= _visibleOptions.FirstOrDefault()?.VariableName;
        UtilizationMetricComboBox.SelectedValue ??= _visibleOptions.FirstOrDefault()?.VariableName;
        UpdateVisibleOptionsSummary();
        UpdatePreview();
    }

    private void UpdateVisibleOptionsSummary()
    {
        VisibleOptionsSummaryTextBlock.Text = _visibleOptions.Count == 0
            ? "当前未显示任何时间项"
            : $"当前下拉选项：{string.Join("、", _visibleOptions.Select(option => option.DisplayName))}";
    }
}

public sealed record FormulaSelection(string PrimaryVariable, double StandardWorkHours, double Coefficient);

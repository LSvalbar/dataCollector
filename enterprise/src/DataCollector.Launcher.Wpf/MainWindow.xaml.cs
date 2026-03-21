using System.Diagnostics;
using System.IO;
using System.Windows;

namespace DataCollector.Launcher.Wpf;

public partial class MainWindow : Window
{
    private readonly string _runtimeRoot;
    private readonly string _settingsPath;

    public MainWindow()
    {
        InitializeComponent();
        WindowLayoutHelper.EnableResponsiveSizing(this);
        _runtimeRoot = AppContext.BaseDirectory;
        _settingsPath = Path.Combine(_runtimeRoot, "launcher.settings.json");
        RuntimeRootTextBlock.Text = _runtimeRoot;

        var settings = LauncherSettings.Load(_settingsPath);
        ServerBaseUrlTextBox.Text = settings.ServerBaseUrl;
        AgentNodeNameTextBox.Text = settings.AgentNodeName;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        MessageBox.Show(this, "启动器设置已保存。", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void StartServerButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        StartComponent("server", "DataCollector.Server.Api.exe", null);
    }

    private void StartClientButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        StartComponent(
            "client",
            "DataCollector.Desktop.Wpf.exe",
            new Dictionary<string, string>
            {
                ["DATACOLLECTOR_API_URL"] = ServerBaseUrlTextBox.Text.Trim(),
            });
    }

    private void StartAgentButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        StartComponent(
            "agent",
            "DataCollector.Agent.Worker.exe",
            new Dictionary<string, string>
            {
                ["Agent__ServerBaseUrl"] = ServerBaseUrlTextBox.Text.Trim(),
                ["Agent__AgentNodeName"] = AgentNodeNameTextBox.Text.Trim(),
            });
    }

    private void StartAllButton_Click(object sender, RoutedEventArgs e)
    {
        StartServerButton_Click(sender, e);
        StartClientButton_Click(sender, e);
        StartAgentButton_Click(sender, e);
    }

    private void StartComponent(string folderName, string executableName, IReadOnlyDictionary<string, string>? environmentVariables)
    {
        var workingDirectory = Path.Combine(_runtimeRoot, folderName);
        var executablePath = Path.Combine(workingDirectory, executableName);
        if (!File.Exists(executablePath))
        {
            MessageBox.Show(this, $"未找到 {executablePath}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
        };

        if (environmentVariables is not null)
        {
            startInfo.UseShellExecute = false;
            foreach (var pair in environmentVariables)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        Process.Start(startInfo);
    }

    private void SaveSettings()
    {
        var settings = new LauncherSettings
        {
            ServerBaseUrl = string.IsNullOrWhiteSpace(ServerBaseUrlTextBox.Text) ? "http://localhost:5180" : ServerBaseUrlTextBox.Text.Trim(),
            AgentNodeName = string.IsNullOrWhiteSpace(AgentNodeNameTextBox.Text) ? "W01-Agent" : AgentNodeNameTextBox.Text.Trim(),
        };
        settings.Save(_settingsPath);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

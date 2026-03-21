using System.Windows;

namespace DataCollector.Launcher.Wpf;

internal static class WindowLayoutHelper
{
    public static void EnableResponsiveSizing(Window window, double widthRatio = 0.88, double heightRatio = 0.88)
    {
        void Apply()
        {
            var workArea = SystemParameters.WorkArea;
            var maxWidth = Math.Max(520, Math.Floor(workArea.Width * widthRatio));
            var maxHeight = Math.Max(360, Math.Floor(workArea.Height * heightRatio));

            window.MaxWidth = workArea.Width;
            window.MaxHeight = workArea.Height;

            if (HasSize(window.MinWidth))
            {
                window.MinWidth = Math.Min(window.MinWidth, maxWidth);
            }

            if (HasSize(window.MinHeight))
            {
                window.MinHeight = Math.Min(window.MinHeight, maxHeight);
            }

            if (HasSize(window.Width))
            {
                window.Width = Math.Min(window.Width, maxWidth);
            }

            if (HasSize(window.Height))
            {
                window.Height = Math.Min(window.Height, maxHeight);
            }
        }

        if (window.IsLoaded)
        {
            Apply();
            return;
        }

        window.SourceInitialized += (_, _) => Apply();
    }

    private static bool HasSize(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }
}

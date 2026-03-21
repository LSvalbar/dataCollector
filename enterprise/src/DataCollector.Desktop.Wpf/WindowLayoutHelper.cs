using System.Windows;

namespace DataCollector.Desktop.Wpf;

internal static class WindowLayoutHelper
{
    public static void EnableResponsiveSizing(
        Window window,
        double widthRatio = 0.94,
        double heightRatio = 0.92,
        bool maximizeWhenConstrained = false)
    {
        void Apply()
        {
            var workArea = SystemParameters.WorkArea;
            var maxWidth = Math.Max(560, Math.Floor(workArea.Width * widthRatio));
            var maxHeight = Math.Max(420, Math.Floor(workArea.Height * heightRatio));
            var widthConstrained = HasSize(window.Width) && window.Width > maxWidth;
            var heightConstrained = HasSize(window.Height) && window.Height > maxHeight;

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

            if (maximizeWhenConstrained &&
                window.ResizeMode != ResizeMode.NoResize &&
                (widthConstrained || heightConstrained))
            {
                window.WindowState = WindowState.Maximized;
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

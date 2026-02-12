using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using PipeForge.Core.Models;

namespace PipeForge.GUI.Converters;

public class StatusToColorConverter : IValueConverter
{
    public static readonly StatusToColorConverter Instance = new();

    // Catppuccin Mocha palette
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#A6E3A1"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F38BA8"));
    private static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#F9E2AF"));
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#89B4FA"));
    private static readonly IBrush Gray = new SolidColorBrush(Color.Parse("#6C7086"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            StepStatus.Success => Green,
            StepStatus.Failed => Red,
            StepStatus.Running => Blue,
            StepStatus.Skipped => Gray,
            StepStatus.Pending => Gray,

            PipelineRunStatus.Success => Green,
            PipelineRunStatus.Failed => Red,
            PipelineRunStatus.Running => Blue,
            PipelineRunStatus.Paused => Yellow,
            PipelineRunStatus.Cancelled => Gray,
            PipelineRunStatus.Pending => Gray,

            _ => Gray
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

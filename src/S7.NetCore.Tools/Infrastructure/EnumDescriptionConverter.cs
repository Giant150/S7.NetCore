using System.Globalization;
using System.Windows.Data;
using S7.NetCore.Tools.Models;

namespace S7.NetCore.Tools.Infrastructure;

public sealed class EnumDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            S7SessionState state => Texts.Describe(state),
            S7Area area => Texts.Describe(area),
            S7DataType dataType => Texts.Describe(dataType),
            S7CpuType cpuType => Texts.Describe(cpuType),
            S7ErrorCategory category => Texts.Describe(category),
            S7OperationStage stage => Texts.Describe(stage),
            S7LogLevel level => Texts.Describe(level),
            OperationCategory category => Texts.Describe(category),
            LogKind kind => Texts.Describe(kind),
            S7ReconnectEventKind kind => Texts.Describe(kind),
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
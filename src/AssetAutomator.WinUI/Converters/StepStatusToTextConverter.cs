using System;
using AssetAutomator.Core.Models;
using Microsoft.UI.Xaml.Data;

namespace AssetAutomator.WinUI.Converters;

/// <summary>
/// Maps a <see cref="NodeStatus"/> enum value (or its string name) to a
/// human-readable badge text label (e.g. "⏳ Đang chạy...").
/// Used by the 5-step accordion sidebar in GeminiPage.
/// </summary>
public class StepStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        NodeStatus status = value switch
        {
            NodeStatus ns => ns,
            string s when Enum.TryParse<NodeStatus>(s, ignoreCase: true, out var parsed) => parsed,
            _ => NodeStatus.Idle
        };

        return status switch
        {
            NodeStatus.Running => "⏳ Đang chạy...",
            NodeStatus.Success => "✔️ Hoàn thành",
            NodeStatus.Failed  => "❌ Lỗi",
            _ => "⚪ Chờ"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

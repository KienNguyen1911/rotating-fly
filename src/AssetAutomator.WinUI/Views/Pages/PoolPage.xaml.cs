using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AssetAutomator.Core.Models;
using AssetAutomator.WinUI.ViewModels;

namespace AssetAutomator.WinUI.Views.Pages;

public sealed partial class PoolPage : Page
{
    public PoolViewModel ViewModel { get; }

    public PoolPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<PoolViewModel>();
        DataContext = ViewModel;
    }

    private void BtnOpenImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is BatchImageItem item)
        {
            ViewModel.SelectedItem = item;
            ViewModel.StatusText = $"Đang mở file: {item.ImagePath}";
        }
    }

    private void BtnCancelRequest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is BatchImageItem item)
        {
            ViewModel.ImageItems.Remove(item);
            ViewModel.StatusText = $"Đã xóa yêu cầu: {item.Prompt}";
        }
    }
}
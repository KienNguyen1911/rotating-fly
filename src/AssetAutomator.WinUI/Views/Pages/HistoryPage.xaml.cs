using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using AssetAutomator.WinUI.ViewModels;

namespace AssetAutomator.WinUI.Views.Pages;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<HistoryViewModel>();
        DataContext = ViewModel;
    }
}
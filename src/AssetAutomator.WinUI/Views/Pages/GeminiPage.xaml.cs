using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using AssetAutomator.WinUI.ViewModels;

namespace AssetAutomator.WinUI.Views.Pages;

public sealed partial class GeminiPage : Page
{
    public GeminiViewModel ViewModel { get; }

    public GeminiPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<GeminiViewModel>();
        DataContext = ViewModel;
    }
}
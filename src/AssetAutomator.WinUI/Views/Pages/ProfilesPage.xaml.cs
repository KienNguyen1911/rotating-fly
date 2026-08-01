using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using AssetAutomator.WinUI.ViewModels;

namespace AssetAutomator.WinUI.Views.Pages;

public sealed partial class ProfilesPage : Page
{
    public ProfilesViewModel ViewModel { get; }

    public ProfilesPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<ProfilesViewModel>();
        DataContext = ViewModel;
    }
}
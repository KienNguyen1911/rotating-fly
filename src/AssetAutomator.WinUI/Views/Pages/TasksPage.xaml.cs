using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AssetAutomator.Core.Models;
using AssetAutomator.WinUI.ViewModels;

namespace AssetAutomator.WinUI.Views.Pages;

public sealed partial class TasksPage : Page
{
    public TasksViewModel ViewModel { get; }

    public TasksPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<TasksViewModel>();
        DataContext = ViewModel;
    }

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.FilterVideoUrl = string.Empty;
        ViewModel.FilterLanguage = string.Empty;
        ViewModel.FilterVoiceId = string.Empty;
        ViewModel.FilterStepT = false;
        ViewModel.FilterStepR = false;
        ViewModel.FilterStepW = false;
        ViewModel.FilterStepV = false;
        ViewModel.FilterStepS = false;
        ViewModel.FilterStepG = false;
    }

    private void BtnRunSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AutomationTask task)
        {
            ViewModel.SelectedTask = task;
            ViewModel.StartTaskCommand.Execute(null);
        }
    }

    private void BtnViewLog_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AutomationTask task)
        {
            ViewModel.SelectedTask = task;
            ViewModel.StatusMessage = $"[Log #{task.Id}]: {task.Logs}";
        }
    }

    private void BtnOpenAssets_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AutomationTask task)
        {
            ViewModel.StatusMessage = $"Mở thư mục assets cho Task #{task.Id}";
        }
    }

    private void BtnDeleteSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is AutomationTask task)
        {
            ViewModel.Tasks.Remove(task);
        }
    }
}
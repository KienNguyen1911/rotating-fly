using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Application.Services;

namespace AssetAutomator.WinUI.Views.Dialogs;

public sealed partial class LicenseDialog : ContentDialog
{
    private readonly LicenseService? _licenseService;
    private readonly IConfigService? _configService;

    public bool IsVerifiedSuccessfully { get; private set; }

    public LicenseDialog(LicenseService? licenseService = null, IConfigService? configService = null)
    {
        InitializeComponent();
        _licenseService = licenseService;
        _configService = configService;

        PrimaryButtonClick += ContentDialog_PrimaryButtonClick;
        SecondaryButtonClick += ContentDialog_SecondaryButtonClick;

        LoadData();
    }

    private void LoadData()
    {
        if (_configService != null)
        {
            TxtLicenseKey.Text = _configService.LoadSettings().LicenseKey;
        }
        UpdateStatusUI();
    }

    private void UpdateStatusUI()
    {
        if (_licenseService == null) return;

        var token = _licenseService.CurrentToken;
        if (token != null && !string.IsNullOrWhiteSpace(token.LicenseKey) && token.DeviceId == _licenseService.GetDeviceId())
        {
            int daysLeft = Math.Max(0, (int)(token.ExpiredAt - DateTime.UtcNow).TotalDays);
            InfoStatus.Severity = InfoBarSeverity.Success;
            InfoStatus.Title = $"Đã Kích Hoạt ({token.Status})";
            InfoStatus.Message = $"Hạn sử dụng: {token.ExpiredAt.ToLocalTime():dd/MM/yyyy HH:mm} (Còn {daysLeft} ngày).";
            BtnDeactivate.Visibility = Visibility.Visible;
            PrimaryButtonText = "Xác nhận lại";
        }
        else
        {
            InfoStatus.Severity = InfoBarSeverity.Warning;
            InfoStatus.Title = "Chưa Kích Hoạt";
            InfoStatus.Message = "Vui lòng nhập License Key hợp lệ để bắt đầu sử dụng.";
            BtnDeactivate.Visibility = Visibility.Collapsed;
            PrimaryButtonText = "Kích hoạt";
        }
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true; // Prevent auto-close while performing async request
        string licenseKey = TxtLicenseKey.Text.Trim();
        if (string.IsNullOrWhiteSpace(licenseKey) || _licenseService == null || _configService == null) return;

        string serverUrl = _configService.LoadSettings().LicenseServerUrl;
        IsPrimaryButtonEnabled = false;

        try
        {
            var response = await _licenseService.ActivateAsync(licenseKey, serverUrl);
            if (response.Success)
            {
                IsVerifiedSuccessfully = true;
                UpdateStatusUI();
                Hide(); // Close dialog on success
            }
            else
            {
                InfoStatus.Severity = InfoBarSeverity.Error;
                InfoStatus.Title = "Kích hoạt thất bại";
                InfoStatus.Message = response.Message;
            }
        }
        catch (Exception ex)
        {
            InfoStatus.Severity = InfoBarSeverity.Error;
            InfoStatus.Title = "Lỗi kích hoạt";
            InfoStatus.Message = ex.Message;
        }
        finally
        {
            IsPrimaryButtonEnabled = true;
        }
    }

    private async void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        string licenseKey = TxtLicenseKey.Text.Trim();
        if (string.IsNullOrWhiteSpace(licenseKey) || _licenseService == null || _configService == null) return;

        string serverUrl = _configService.LoadSettings().LicenseServerUrl;
        IsSecondaryButtonEnabled = false;

        try
        {
            var response = await _licenseService.TransferAsync(licenseKey, serverUrl);
            if (response.Success)
            {
                IsVerifiedSuccessfully = true;
                UpdateStatusUI();
                Hide();
            }
            else
            {
                InfoStatus.Severity = InfoBarSeverity.Error;
                InfoStatus.Title = "Chuyển máy thất bại";
                InfoStatus.Message = response.Message;
            }
        }
        catch (Exception ex)
        {
            InfoStatus.Severity = InfoBarSeverity.Error;
            InfoStatus.Title = "Lỗi chuyển máy";
            InfoStatus.Message = ex.Message;
        }
        finally
        {
            IsSecondaryButtonEnabled = true;
        }
    }

    private async void BtnDeactivate_Click(object sender, RoutedEventArgs e)
    {
        if (_licenseService != null)
        {
            var response = await _licenseService.DeactivateAsync();
            UpdateStatusUI();
        }
    }
}

using System;
using System.Windows;
using System.Windows.Media;
using AssetAutomator.Services;

namespace AssetAutomator.Windows
{
    public partial class LicenseWindow : Window
    {
        private readonly LicenseService _licenseService;

        public bool IsVerifiedSuccessfully { get; private set; }

        public LicenseWindow(LicenseService licenseService)
        {
            InitializeComponent();
            _licenseService = licenseService;

            LoadData();
        }

        private void LoadData()
        {
            TxtLicenseKey.Text = ConfigService.CurrentSettings.LicenseKey;
            UpdateStatusUI();
        }

        private void UpdateStatusUI()
        {
            var token = _licenseService.CurrentToken;
            if (token != null && !string.IsNullOrWhiteSpace(token.LicenseKey) && token.DeviceId == _licenseService.GetDeviceId())
            {
                int daysLeft = Math.Max(0, (int)(token.ExpiredAt - DateTime.UtcNow).TotalDays);
                TxtStatusHeader.Text = $"Trạng thái: Đã Kích Hoạt ({token.Status})";
                TxtStatusHeader.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                TxtStatusDetails.Text = $"Hạn sử dụng: {token.ExpiredAt.ToLocalTime():dd/MM/yyyy HH:mm} (Còn {daysLeft} ngày).";

                BtnDeactivate.Visibility = Visibility.Visible;
                BtnActivate.Content = "Xác nhận lại";
            }
            else
            {
                TxtStatusHeader.Text = "Trạng thái: Chưa kích hoạt";
                TxtStatusHeader.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                TxtStatusDetails.Text = "Vui lòng nhập License Key hợp lệ để bắt đầu sử dụng ứng dụng.";

                BtnDeactivate.Visibility = Visibility.Collapsed;
                BtnActivate.Content = "Kích hoạt";
            }
        }

        private async void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            string serverUrl = ConfigService.CurrentSettings.LicenseServerUrl.Trim();
            string licenseKey = TxtLicenseKey.Text.Trim();

            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                MessageBox.Show("Vui lòng nhập License Key.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnActivate.IsEnabled = false;
            TxtStatusHeader.Text = "Đang kết nối tới máy chủ...";
            TxtStatusHeader.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));

            try
            {
                var response = await _licenseService.ActivateAsync(licenseKey, serverUrl);
                if (response.Success)
                {
                    IsVerifiedSuccessfully = true;
                    MessageBox.Show("Kích hoạt bản quyền thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    UpdateStatusUI();
                }
                else
                {
                    MessageBox.Show($"Kích hoạt thất bại: {response.Message}", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatusUI();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi trong quá trình kích hoạt: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusUI();
            }
            finally
            {
                BtnActivate.IsEnabled = true;
            }
        }

        private async void BtnDeactivate_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Bạn có chắc chắn muốn hủy kích hoạt bản quyền trên thiết bị này không?", 
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            BtnDeactivate.IsEnabled = false;
            try
            {
                var response = await _licenseService.DeactivateAsync();
                if (response.Success)
                {
                    MessageBox.Show("Đã hủy kích hoạt bản quyền trên thiết bị này.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    UpdateStatusUI();
                }
                else
                {
                    MessageBox.Show($"Hủy kích hoạt thất bại: {response.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                BtnDeactivate.IsEnabled = true;
            }
        }

        private async void BtnTransfer_Click(object sender, RoutedEventArgs e)
        {
            string serverUrl = ConfigService.CurrentSettings.LicenseServerUrl.Trim();
            string licenseKey = TxtLicenseKey.Text.Trim();

            if (string.IsNullOrWhiteSpace(licenseKey))
            {
                MessageBox.Show("Vui lòng nhập License Key trước khi thực hiện chuyển máy.", "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show("Chuyển bản quyền sẽ hủy phiên làm việc trên thiết bị cũ và gán cho thiết bị hiện tại. Tiếp tục?",
                "Xác nhận chuyển thiết bị", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            BtnTransfer.IsEnabled = false;
            try
            {
                var response = await _licenseService.TransferAsync(licenseKey, serverUrl);
                if (response.Success)
                {
                    IsVerifiedSuccessfully = true;
                    MessageBox.Show("Chuyển thiết bị thành công! Bản quyền đã được liên kết với máy này.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    UpdateStatusUI();
                }
                else
                {
                    MessageBox.Show($"Chuyển thiết bị thất bại: {response.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                BtnTransfer.IsEnabled = true;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

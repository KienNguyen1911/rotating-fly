using System.Windows;

namespace AssetAutomator.Windows
{
    /// <summary>
    /// Interaction logic for NewProjectWindow.xaml
    /// </summary>
    public partial class NewProjectWindow : Window
    {
        public string ProjectName { get; private set; } = string.Empty;

        public NewProjectWindow(string defaultName = "")
        {
            InitializeComponent();
            if (!string.IsNullOrWhiteSpace(defaultName))
            {
                TxtProjectName.Text = defaultName;
            }
            else
            {
                TxtProjectName.Text = $"Project_{System.DateTime.Now:yyyyMMdd_HHmmss}";
            }
            TxtProjectName.Focus();
            TxtProjectName.SelectAll();
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtProjectName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Vui lòng nhập tên dự án!", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ProjectName = name;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

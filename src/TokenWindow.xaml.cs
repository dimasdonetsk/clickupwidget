using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ClickUpWidget
{
    /// <summary>Вікно, куди вставляють особистий API-токен ClickUp.</summary>
    public partial class TokenWindow : Window
    {
        private readonly Func<string, Task<string?>> _connect;

        /// <param name="connect">Перевіряє токен; повертає текст помилки або null, якщо все добре.</param>
        public TokenWindow(Func<string, Task<string?>> connect)
        {
            InitializeComponent();
            _connect = connect;
            Loaded += (_, _) => { TokenBox.Focus(); Activate(); };
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            string token = TokenBox.Text.Trim();
            if (token.Length == 0)
            {
                ShowError("Встав токен (він починається з pk_).");
                return;
            }

            SaveBtn.IsEnabled = false;
            BusyText.Visibility = Visibility.Visible;
            ErrorText.Visibility = Visibility.Collapsed;

            string? error = await _connect(token);

            BusyText.Visibility = Visibility.Collapsed;
            SaveBtn.IsEnabled = true;
            if (error == null) Close();
            else ShowError(error);
        }

        private void ShowError(string text)
        {
            ErrorText.Text = text;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void OpenTokenPage(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("https://app.clickup.com/settings/apps") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write("Open: " + ex.Message); }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                try { DragMove(); } catch { }
        }
    }
}

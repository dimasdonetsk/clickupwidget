using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClickUpWidget.Mac
{
    /// <summary>Вікно, куди вставляють особистий API-токен ClickUp.</summary>
    public partial class TokenWindow : Window
    {
        private readonly Func<string, Task<string?>> _connect;

        public TokenWindow() : this(_ => Task.FromResult<string?>(null)) { }

        /// <param name="connect">Перевіряє токен; повертає текст помилки або null, якщо все добре.</param>
        public TokenWindow(Func<string, Task<string?>> connect)
        {
            InitializeComponent();
            _connect = connect;
            PrivacyText.Text = OperatingSystem.IsMacOS()
                ? "Віджету потрібен твій особистий API-токен. Він лишається тільки на цьому Маку — у Keychain (Зв'язці ключів)."
                : "Віджету потрібен твій особистий API-токен. Він лишається тільки на цьому комп'ютері.";
            Opened += (_, _) =>
            {
                MacNative.FloatOnAllSpaces(this);
                MacNative.ActivateApp();
                Activate();
                Dispatcher.UIThread.Post(() => TokenBox.Focus(), DispatcherPriority.Input);
            };
        }

        private async void Save_Click(object? sender, RoutedEventArgs e)
        {
            string token = (TokenBox.Text ?? "").Trim();
            if (token.Length == 0)
            {
                ShowError("Встав токен (він починається з pk_).");
                return;
            }

            SaveBtn.IsEnabled = false;
            BusyText.IsVisible = true;
            ErrorText.IsVisible = false;

            string? error = await _connect(token);

            BusyText.IsVisible = false;
            SaveBtn.IsEnabled = true;
            if (error == null) Close();
            else ShowError(error);
        }

        private void ShowError(string text)
        {
            ErrorText.Text = text;
            ErrorText.IsVisible = true;
        }

        private void OpenTokenPage(object? sender, RoutedEventArgs e) => MacNative.Open("https://app.clickup.com/settings/apps");

        private void Close_Click(object? sender, RoutedEventArgs e) => Close();

        private void Drag(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                (e.Source as Avalonia.Visual)?.FindAncestorOfType<Button>(true) == null)
                BeginMoveDrag(e);
        }
    }
}

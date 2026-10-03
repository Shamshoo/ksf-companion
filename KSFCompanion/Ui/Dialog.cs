using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace KsfCompanion.Ui
{
    /// <summary>A small dark message box in the app's style (Linux desktops have no shared one to borrow).</summary>
    static class Dialog
    {
        public static Task ShowAsync(string text) => OpenAsync(text, "OK", null);

        /// <summary>True when the first button was chosen.</summary>
        public static Task<bool> AskAsync(string text, string ok = "OK", string cancel = "Cancel") => OpenAsync(text, ok, cancel);

        static Task<bool> OpenAsync(string text, string ok, string cancel)
        {
            var result = new TaskCompletionSource<bool>();
            var chosen = false;
            var window = new Window
            {
                Title = Program.AppName,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = Resource<IBrush>("Bg1Brush"),
                Foreground = Resource<IBrush>("Text0Brush"),
                FontFamily = Resource<FontFamily>("BodyFont"),
                FontSize = 13,
                Topmost = true,
            };
            try { window.Icon = new WindowIcon(new Bitmap(AssetLoader.Open(new System.Uri("avares://KSFCompanion/Ui/Assets/icon-64.png")))); }
            catch (System.IO.IOException) { }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };
            if (cancel != null)
            {
                var no = new Button { Content = cancel, Theme = Resource<Avalonia.Styling.ControlTheme>("GhostButton"), MinWidth = 90 };
                no.Click += (s, e) => window.Close();
                buttons.Children.Add(no);
            }
            var yes = new Button { Content = ok, Theme = Resource<Avalonia.Styling.ControlTheme>("AccentButton"), MinWidth = 90 };
            yes.Click += (s, e) =>
            {
                chosen = true;
                window.Close();
            };
            buttons.Children.Add(yes);

            window.Content = new Border
            {
                Padding = new Thickness(22, 20),
                Child = new StackPanel
                {
                    Children =
                    {
                        new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 440, LineHeight = 19 },
                        buttons,
                    },
                },
            };
            window.Closed += (s, e) => result.TrySetResult(chosen);
            window.Show();
            window.Activate();
            return result.Task;
        }

        static T Resource<T>(string key) where T : class =>
            Application.Current != null && Application.Current.TryFindResource(key, out var value) ? value as T : null;
    }
}

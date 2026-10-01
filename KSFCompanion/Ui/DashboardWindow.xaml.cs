using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Screen = System.Windows.Forms.Screen;

namespace KsfCompanion.Ui
{
    public partial class DashboardWindow : Window
    {
        readonly DashboardViewModel vm;
        readonly DispatcherTimer toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };

        internal DashboardWindow(DashboardViewModel vm, KeyNames keys)
        {
            this.vm = vm;
            InitializeComponent();
            DataContext = vm;
            SetKeys(keys);
            // The binds page waits for the key you want: the next key, mouse button or wheel turn is it.
            PreviewKeyDown += OnCaptureKey;
            PreviewMouseDown += OnCaptureMouse;
            PreviewMouseWheel += OnCaptureWheel;
            Deactivated += (s, e) => vm.Binds.CancelCapture();

            // The window can be opened after the map (and its colours) arrived.
            Ambient.Opacity = vm.AmbientImage != null ? 1 : 0;
            vm.PropertyChanged += OnViewModelChanged;
            SizeChanged += (s, e) => Relayout(ActualWidth);
            // Showing, hiding or switching Simple/Advanced lays the dashboard out again.
            vm.Layout.PropertyChanged += (s, e) => Relayout(ActualWidth);
            StateChanged += (s, e) => OnStateChanged();
            toastTimer.Tick += (s, e) =>
            {
                toastTimer.Stop();
                ToastHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250)));
            };
        }

        /// <summary>Raised when the window is closed or moved so the owner can remember where it was.</summary>
        internal event Action PlacementChanged;
        internal event Action<bool> TopmostChanged;

        internal bool AllowClose { get; set; }

        void OnViewModelChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(DashboardViewModel.MapName):
                    Animate(HeroContent, OpacityProperty, 0, 1, 380);
                    HeroShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                        new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    break;
                case nameof(DashboardViewModel.MapImage):
                    if (vm.MapImage != null) Animate(HeroImage, OpacityProperty, 0, 1, 650);
                    break;
                case nameof(DashboardViewModel.AmbientImage):
                    Animate(Ambient, OpacityProperty, 0, vm.AmbientImage != null ? 1 : 0, 1400);
                    break;
                case nameof(DashboardViewModel.CelebrationId):
                    Celebrate();
                    break;
                case nameof(DashboardViewModel.HasStagesB):
                case nameof(DashboardViewModel.HasBonusesB):
                    Relayout(ActualWidth);
                    break;
                case nameof(DashboardViewModel.Toast):
                    if (string.IsNullOrEmpty(vm.Toast)) break;
                    ToastText.Text = vm.Toast;
                    Animate(ToastHost, OpacityProperty, ToastHost.Opacity, 1, 180);
                    toastTimer.Stop();
                    toastTimer.Start();
                    break;
            }
        }

        /// <summary>Pops the PB card over the map picture: in with a little overshoot, holds, then fades away.</summary>
        void Celebrate()
        {
            Celebration.Visibility = Visibility.Visible;
            Celebration.BeginAnimation(OpacityProperty, InHoldOut(0, 1));
            // The map name and buttons step aside meanwhile so the two texts don't overlap.
            var aside = InHoldOut(1, 0);
            aside.Completed += (s, e) => { if (Celebration.Opacity < 0.01) Celebration.Visibility = Visibility.Collapsed; };
            HeroContent.BeginAnimation(OpacityProperty, aside);

            var pop = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut } };
            CelebrationScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pop);
            CelebrationScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pop);
        }

        /// <summary>Goes from one value to the other in a quarter second, stays there about four seconds, then eases back.</summary>
        static DoubleAnimationUsingKeyFrames InHoldOut(double from, double to)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(4200))));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(4900))));
            return animation;
        }

        /// <summary>For --preview: the PB card fully shown, no animation.</summary>
        internal void ShowCelebrationStill()
        {
            Celebration.Visibility = Visibility.Visible;
            Celebration.Opacity = 1;
            HeroContent.Opacity = 0;
        }

        static void Animate(UIElement element, DependencyProperty property, double from, double to, int ms) =>
            element.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        /// <summary>
        /// Two columns when there is room and both sides have something on show, one scrolling column otherwise. On a
        /// big screen everything is scaled up (it's laid out for about 1500 px across), a bit more in the Simple view.
        /// </summary>
        internal void Relayout(double width)
        {
            // The title bar isn't scaled: it has the window's own width.
            PlayerChip.MaxWidth = width >= 1200 ? double.PositiveInfinity : 0;
            // Size (Customize): everything on the page drawn bigger or smaller - the Simple view a touch bigger.
            Zoom(NominatePage, vm.Layout.Scale);
            var zoom = vm.Layout.Scale * (vm.Layout.IsSimple ? 1.06 : 1);
            Zoom(Columns, zoom);
            width /= zoom;

            bool leftShown = vm.Layout.AnyShown(left: true), rightShown = vm.Layout.AnyShown(left: false);
            NothingShown.Visibility = leftShown || rightShown ? Visibility.Collapsed : Visibility.Visible;
            var wide = width >= 1060 && leftShown && rightShown;
            RightColumn.Width = wide ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(RightPanel, wide ? 1 : 0);
            Grid.SetRow(RightPanel, wide ? 0 : 1);
            RightPanel.Margin = wide ? new Thickness(16, 0, 0, 0) : new Thickness(0);
            // A single column on a very wide window stays a readable width, in the middle.
            const double readable = 1560;
            Columns.MaxWidth = wide ? double.PositiveInfinity : readable;
            if (!wide) width = Math.Min(width, readable + 40);

            var leftWidth = wide ? (width - 56) * 0.6 : width - 40;
            Tiles.Columns = leftWidth >= 720 ? 4 : 2;
            // Stages and bonuses side by side (S1-S8 | S9-S16) when there's room, otherwise one list each.
            var room = leftWidth >= 700;
            SideBySide(StageColumnB, StagesRight, room && vm.HasStagesB);
            SideBySide(BonusColumnB, BonusesRight, room && vm.HasBonusesB);
            Hero.Height = leftWidth >= 680 ? 300 : 230;
            MapTitle.FontSize = leftWidth >= 800 ? 46 : leftWidth >= 600 ? 36 : 28;
        }

        static void Zoom(FrameworkElement element, double zoom)
        {
            var scaled = Math.Abs(zoom - 1) > 0.01;
            element.LayoutTransform = scaled ? new System.Windows.Media.ScaleTransform(zoom, zoom) : null;
            // Text drawn at an odd scale looks better smooth than snapped to pixels.
            System.Windows.Media.TextOptions.SetTextFormattingMode(element,
                scaled ? System.Windows.Media.TextFormattingMode.Ideal : System.Windows.Media.TextFormattingMode.Display);
        }

        /// <summary>The second half of a list next to the first (in its own column) or under it.</summary>
        static void SideBySide(ColumnDefinition column, FrameworkElement secondHalf, bool sideBySide)
        {
            column.Width = sideBySide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(secondHalf, sideBySide ? 1 : 0);
            Grid.SetRow(secondHalf, sideBySide ? 0 : 1);
            secondHalf.Margin = sideBySide ? new Thickness(28, 0, 0, 0) : new Thickness(0, 1, 0, 0);
        }

        void OnStateChanged()
        {
            // A chrome-less window hangs over the screen edge when maximized; pull the content back in.
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
            PlacementChanged?.Invoke();
        }

        const int WM_SYSCOMMAND = 0x0112;
        const int KeepOnTopCommand = 0x1F10;
        IntPtr systemMenu;

        /// <summary>KSF Companion's own keys, shown at the bottom of the dashboard.</summary>
        internal void SetKeys(KeyNames keys)
        {
            SaveKey.Text = GameKeys.Label(keys.Save);
            CardKey.Text = GameKeys.Label(keys.Card);
            ListKey.Text = GameKeys.Label(keys.List);
        }

        static readonly System.Reflection.PropertyInfo ExtendedKey =
            typeof(KeyEventArgs).GetProperty("IsExtendedKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        void OnCaptureKey(object sender, KeyEventArgs e)
        {
            if (!vm.Binds.IsCapturing) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape)
            {
                vm.Binds.CancelCapture();
                return;
            }
            // The numpad without Num Lock sends the same keys as the arrows, Home, End..., only not "extended".
            var extended = ExtendedKey?.GetValue(e) is bool b && b;
            var name = GameKeyOf(key, extended);
            if (name == null)
            {
                vm.Toast = key == Key.OemTilde ? "That's the console key - pick another" : "That key can't be bound - pick another";
                vm.Binds.CancelCapture();
                return;
            }
            vm.Binds.Capture(name);
        }

        void OnCaptureMouse(object sender, MouseButtonEventArgs e)
        {
            if (!vm.Binds.IsCapturing) return;
            string name = null;
            switch (e.ChangedButton)
            {
                case MouseButton.Right: name = "MOUSE2"; break;
                case MouseButton.Middle: name = "MOUSE3"; break;
                case MouseButton.XButton1: name = "MOUSE4"; break;
                case MouseButton.XButton2: name = "MOUSE5"; break;
            }
            if (name != null)
            {
                e.Handled = true;
                vm.Binds.Capture(name);
                return;
            }
            // A left click stops waiting; on the waiting row's own key button that's all it does.
            var row = (e.OriginalSource as FrameworkElement)?.DataContext as BindRow;
            var onWaitingButton = row != null && row.IsCapturing && InsideButton(e.OriginalSource as DependencyObject);
            vm.Binds.CancelCapture();
            if (onWaitingButton) e.Handled = true;
        }

        void OnCaptureWheel(object sender, MouseWheelEventArgs e)
        {
            if (!vm.Binds.IsCapturing) return;
            e.Handled = true;
            vm.Binds.Capture(e.Delta > 0 ? "MWHEELUP" : "MWHEELDOWN");
        }

        static bool InsideButton(DependencyObject d)
        {
            for (; d != null; d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is Button) return true;
            return false;
        }

        /// <summary>The Source engine's name for a key ("r", "SHIFT", "KP_END"), or null for keys that can't be bound here.</summary>
        static string GameKeyOf(Key key, bool extended)
        {
            if (key >= Key.A && key <= Key.Z) return ((char)('a' + (key - Key.A))).ToString();
            if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
            if (key >= Key.F1 && key <= Key.F12) return "F" + (key - Key.F1 + 1).ToString(CultureInfo.InvariantCulture);
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return new[] { "KP_INS", "KP_END", "KP_DOWNARROW", "KP_PGDN", "KP_LEFTARROW", "KP_5", "KP_RIGHTARROW", "KP_HOME", "KP_UPARROW", "KP_PGUP" }[key - Key.NumPad0];
            switch (key)
            {
                case Key.Space: return "SPACE";
                case Key.Tab: return "TAB";
                case Key.Return: return extended ? "KP_ENTER" : "ENTER";
                case Key.Back: return "BACKSPACE";
                case Key.Capital: return "CAPSLOCK";
                case Key.Insert: return extended ? "INS" : "KP_INS";
                case Key.Delete: return extended ? "DEL" : "KP_DEL";
                case Key.Home: return extended ? "HOME" : "KP_HOME";
                case Key.End: return extended ? "END" : "KP_END";
                case Key.PageUp: return extended ? "PGUP" : "KP_PGUP";
                case Key.PageDown: return extended ? "PGDN" : "KP_PGDN";
                case Key.Up: return extended ? "UPARROW" : "KP_UPARROW";
                case Key.Down: return extended ? "DOWNARROW" : "KP_DOWNARROW";
                case Key.Left: return extended ? "LEFTARROW" : "KP_LEFTARROW";
                case Key.Right: return extended ? "RIGHTARROW" : "KP_RIGHTARROW";
                case Key.Clear: return "KP_5";
                case Key.LeftShift: return "SHIFT";
                case Key.RightShift: return "RSHIFT";
                case Key.LeftCtrl: return "CTRL";
                case Key.RightCtrl: return "RCTRL";
                case Key.LeftAlt: return "ALT";
                case Key.RightAlt: return "RALT";
                case Key.Divide: return "KP_SLASH";
                case Key.Multiply: return "KP_MULTIPLY";
                case Key.Subtract: return "KP_MINUS";
                case Key.Add: return "KP_PLUS";
                case Key.Decimal: return "KP_DEL";
                case Key.Pause: return "PAUSE";
                case Key.Scroll: return "SCROLLLOCK";
                case Key.NumLock: return "NUMLOCK";
                case Key.OemSemicolon: return "SEMICOLON";
                case Key.OemQuotes: return "'";
                case Key.OemComma: return ",";
                case Key.OemMinus: return "-";
                case Key.OemPeriod: return ".";
                case Key.OemQuestion: return "/";
                case Key.OemPlus: return "=";
                case Key.OemOpenBrackets: return "[";
                case Key.OemCloseBrackets: return "]";
            }
            return null;
        }

        /// <summary>Adds "Keep on top" to the menu you get by right-clicking the title bar.</summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            systemMenu = NativeMethods.GetSystemMenu(hwnd, false);
            NativeMethods.AppendMenu(systemMenu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(systemMenu, NativeMethods.MF_STRING, KeepOnTopCommand, "Keep on top");
            UpdateKeepOnTopCheck();
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == KeepOnTopCommand)
            {
                SetTopmost(!Topmost);
                handled = true;
            }
            return IntPtr.Zero;
        }

        internal void SetTopmost(bool on)
        {
            if (Topmost == on) return;
            Topmost = on;
            UpdateKeepOnTopCheck();
            TopmostChanged?.Invoke(on);
        }

        void UpdateKeepOnTopCheck()
        {
            if (systemMenu != IntPtr.Zero)
                NativeMethods.CheckMenuItem(systemMenu, KeepOnTopCommand, Topmost ? NativeMethods.MF_CHECKED : NativeMethods.MF_UNCHECKED);
        }

        void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        void OnMaximize(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        void OnClose(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            PlacementChanged?.Invoke();
            if (!AllowClose)
            {
                // Closing only hides it; KSF Companion keeps running in the tray.
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            PlacementChanged?.Invoke();
        }

        // Set while a maximized dashboard is standing in as a plain window that fills the screen (see ShowWithoutFocus).
        Rect? maximizeWhenActivated;

        /// <summary>
        /// Shows the dashboard without taking focus from the game. Windows won't show a maximized window without
        /// activating it, so a maximized dashboard opens covering its monitor's work area instead and becomes
        /// properly maximized the first time you click into it.
        /// </summary>
        internal void ShowWithoutFocus()
        {
            ShowActivated = false;
            if (WindowState == WindowState.Maximized)
            {
                var normal = new Rect(Left, Top, Width, Height);
                var center = new System.Drawing.Point((int)(normal.Left + normal.Width / 2), (int)(normal.Top + normal.Height / 2));
                var area = (Screen.AllScreens.FirstOrDefault(s => s.Bounds.Contains(center)) ?? Screen.PrimaryScreen).WorkingArea;
                double scale;
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96.0;
                WindowState = WindowState.Normal;
                maximizeWhenActivated = normal;
                Left = area.Left / scale;
                Top = area.Top / scale;
                Width = area.Width / scale;
                Height = area.Height / scale;
            }
            Show();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (!(maximizeWhenActivated is Rect normal)) return;
            maximizeWhenActivated = null;
            Left = normal.Left;
            Top = normal.Top;
            Width = normal.Width;
            Height = normal.Height;
            WindowState = WindowState.Maximized;
        }

        /// <summary>"left,top,width,height,maximized" in device-independent pixels.</summary>
        internal string Placement
        {
            get
            {
                if (maximizeWhenActivated is Rect waiting)
                    return string.Format(CultureInfo.InvariantCulture, "{0:0},{1:0},{2:0},{3:0},1", waiting.Left, waiting.Top, waiting.Width, waiting.Height);
                var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                if (r.IsEmpty || double.IsNaN(r.Left)) return null;
                return string.Format(CultureInfo.InvariantCulture, "{0:0},{1:0},{2:0},{3:0},{4}", r.Left, r.Top, r.Width, r.Height,
                    WindowState == WindowState.Maximized ? 1 : 0);
            }
        }

        /// <summary>Puts the window where it was last time, or on the second monitor the first time.</summary>
        internal void ApplyPlacement(string saved)
        {
            var parts = (saved ?? "").Split(',');
            if (parts.Length == 5 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var left) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var top) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var height) &&
                Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new System.Drawing.Rectangle((int)left + 40, (int)top + 10, 120, 40))))
            {
                Left = left;
                Top = top;
                Width = Math.Max(MinWidth, width);
                Height = Math.Max(MinHeight, height);
                if (parts[4] == "1") WindowState = WindowState.Maximized;
                return;
            }

            // First run: the biggest monitor that isn't the one the game runs on.
            var screen = Screen.AllScreens.Where(s => !s.Primary).OrderByDescending(s => s.Bounds.Width * s.Bounds.Height).FirstOrDefault()
                         ?? Screen.PrimaryScreen;
            var area = screen.WorkingArea;
            Width = Math.Min(1440, area.Width * 0.9);
            Height = Math.Min(940, area.Height * 0.9);
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        }

        /// <summary>True when the window sits on a monitor other than the main one (where the game runs).</summary>
        internal bool IsOnSecondaryScreen
        {
            get
            {
                var center = new System.Drawing.Point((int)(Left + Width / 2), (int)(Top + Height / 2));
                var screen = Screen.AllScreens.FirstOrDefault(s => s.Bounds.Contains(center));
                return screen != null && !screen.Primary;
            }
        }
    }
}

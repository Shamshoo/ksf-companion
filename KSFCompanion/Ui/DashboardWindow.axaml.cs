using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace KsfCompanion.Ui
{
    public partial class DashboardWindow : Window
    {
        readonly DashboardViewModel vm;
        readonly DispatcherTimer toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };

        // Named parts of the window (found once the XAML is loaded).
        Border Root, Ambient, HeroImage, Toast;
        Panel HeroContent, Hero, Celebration;
        StackPanel CelebrationContent, PlayerChip, RightPanel, NothingShown;
        TextBlock ToastText, MapTitle, SaveKey, CardKey, ListKey;
        Grid Columns, StagesGrid, BonusesGrid, TitleBar;
        ItemsControl StagesRight, BonusesRight;
        UniformGrid Tiles;
        LayoutTransformControl ColumnsZoom, NominateZoom;
        PathIcon MaxIcon;
        MenuItem KeepOnTopMenuItem;

        // For the XAML loader and designer only.
        public DashboardWindow() : this(new DashboardViewModel(), null) { }

        internal DashboardWindow(DashboardViewModel vm, KeyNames keys)
        {
            this.vm = vm;
            AvaloniaXamlLoader.Load(this);
            FindParts();
            DataContext = vm;
            if (keys != null) SetKeys(keys);

            // The binds page waits for the key you want: the next key, mouse button or wheel turn is it.
            AddHandler(KeyDownEvent, OnCaptureKey, RoutingStrategies.Tunnel);
            AddHandler(PointerPressedEvent, OnCaptureMouse, RoutingStrategies.Tunnel);
            AddHandler(PointerWheelChangedEvent, OnCaptureWheel, RoutingStrategies.Tunnel);
            AddHandler(KeyDownEvent, OnOwnCommandKey, RoutingStrategies.Tunnel);
            // Remembers whether the mouse button is down (sliders save their value once it's let go).
            AddHandler(PointerPressedEvent, (s, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) IsLeftButtonDown = true; }, RoutingStrategies.Tunnel, true);
            AddHandler(PointerReleasedEvent, (s, e) => IsLeftButtonDown = false, RoutingStrategies.Tunnel, true);
            AddHandler(PointerCaptureLostEvent, (s, e) => IsLeftButtonDown = false, RoutingStrategies.Tunnel, true);
            Deactivated += (s, e) =>
            {
                vm.Binds.CancelCapture();
                IsLeftButtonDown = false;
            };

            // The window can be opened after the map (and its colours) arrived.
            Ambient.Opacity = vm.AmbientImage != null ? 1 : 0;
            vm.PropertyChanged += OnViewModelChanged;
            SizeChanged += (s, e) => Relayout(Bounds.Width);
            // Showing, hiding or switching Simple/Advanced lays the dashboard out again.
            vm.Layout.PropertyChanged += (s, e) => Relayout(Bounds.Width);
            PropertyChanged += (s, e) =>
            {
                if (e.Property == WindowStateProperty) OnStateChanged();
            };
            PositionChanged += (s, e) => PlacementChanged?.Invoke();
            toastTimer.Tick += (s, e) =>
            {
                toastTimer.Stop();
                Toast.Opacity = 0;
            };
            SetUpChrome();
        }

        void FindParts()
        {
            T Part<T>(string name) where T : Control => this.FindControl<T>(name) ?? throw new InvalidOperationException("missing " + name);
            Root = Part<Border>("Root");
            Ambient = Part<Border>("Ambient");
            HeroImage = Part<Border>("HeroImage");
            Toast = Part<Border>("Toast");
            HeroContent = Part<Panel>("HeroContent");
            Hero = Part<Panel>("Hero");
            Celebration = Part<Panel>("Celebration");
            CelebrationContent = Part<StackPanel>("CelebrationContent");
            PlayerChip = Part<StackPanel>("PlayerChip");
            RightPanel = Part<StackPanel>("RightPanel");
            NothingShown = Part<StackPanel>("NothingShown");
            ToastText = Part<TextBlock>("ToastText");
            MapTitle = Part<TextBlock>("MapTitle");
            SaveKey = Part<TextBlock>("SaveKey");
            CardKey = Part<TextBlock>("CardKey");
            ListKey = Part<TextBlock>("ListKey");
            Columns = Part<Grid>("Columns");
            StagesGrid = Part<Grid>("StagesGrid");
            BonusesGrid = Part<Grid>("BonusesGrid");
            TitleBar = Part<Grid>("TitleBar");
            StagesRight = Part<ItemsControl>("StagesRight");
            BonusesRight = Part<ItemsControl>("BonusesRight");
            Tiles = Part<UniformGrid>("Tiles");
            ColumnsZoom = Part<LayoutTransformControl>("ColumnsZoom");
            NominateZoom = Part<LayoutTransformControl>("NominateZoom");
            MaxIcon = Part<PathIcon>("MaxIcon");
            KeepOnTopMenuItem = this.FindControl<MenuItem>("KeepOnTopMenuItem");
        }

        /// <summary>Raised when the window is closed or moved so the owner can remember where it was.</summary>
        internal event Action PlacementChanged;
        internal event Action<bool> TopmostChanged;

        internal bool AllowClose { get; set; }

        /// <summary>Whether the left mouse button is held down in the window right now (a slider being dragged).</summary>
        internal bool IsLeftButtonDown { get; private set; }

        void OnViewModelChanged(object sender, PropertyChangedEventArgs e)
        {
            // Only looks: whatever goes wrong here must never stop the data that's coming in.
            try { React(e.PropertyName); }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                Program.Trace($"dashboard ({e.PropertyName}): {ex.Message}");
            }
        }

        void React(string property)
        {
            switch (property)
            {
                case nameof(DashboardViewModel.MapName):
                    _ = Animate(HeroContent, OpacityProperty, 0, 1, 380);
                    // Transforms are animated through the control that has them.
                    _ = Animate(HeroContent, TranslateTransform.YProperty, 12, 0, 420);
                    break;
                case nameof(DashboardViewModel.MapImage):
                    if (vm.MapImage != null) _ = Animate(HeroImage, OpacityProperty, 0, 1, 650);
                    break;
                case nameof(DashboardViewModel.AmbientImage):
                    var to = vm.AmbientImage != null ? 1 : 0;
                    var from = Ambient.Opacity;
                    Ambient.Opacity = to;
                    _ = Animate(Ambient, OpacityProperty, vm.AmbientImage != null ? 0 : from, to, 1400);
                    break;
                case nameof(DashboardViewModel.CelebrationId):
                    Celebrate();
                    break;
                case nameof(DashboardViewModel.HasStagesB):
                case nameof(DashboardViewModel.HasBonusesB):
                    Relayout(Bounds.Width);
                    break;
                case nameof(DashboardViewModel.Toast):
                    if (string.IsNullOrEmpty(vm.Toast)) break;
                    ToastText.Text = vm.Toast;
                    Toast.Opacity = 1;
                    toastTimer.Stop();
                    toastTimer.Start();
                    break;
            }
        }

        /// <summary>Pops the PB card over the map picture: in with a little overshoot, holds, then fades away.</summary>
        async void Celebrate()
        {
            Celebration.IsVisible = true;
            // The map name and buttons step aside meanwhile so the two texts don't overlap.
            var shown = InHoldOut(0, 1).RunAsync(Celebration);
            var aside = InHoldOut(1, 0).RunAsync(HeroContent);
            var pop = new BackEaseOut();
            _ = Animate(CelebrationContent, ScaleTransform.ScaleXProperty, 0.82, 1, 520, pop);
            _ = Animate(CelebrationContent, ScaleTransform.ScaleYProperty, 0.82, 1, 520, pop);
            await Task.WhenAll(shown, aside);
            if (Celebration.Opacity < 0.01) Celebration.IsVisible = false;
        }

        /// <summary>Goes from one value to the other in a quarter second, stays there about four seconds, then eases back.</summary>
        static Animation InHoldOut(double from, double to) => new Animation
        {
            Duration = TimeSpan.FromMilliseconds(4900),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, from) } },
                new KeyFrame { Cue = new Cue(260 / 4900.0), Setters = { new Setter(OpacityProperty, to) } },
                new KeyFrame { Cue = new Cue(4200 / 4900.0), Setters = { new Setter(OpacityProperty, to) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, from) } },
            },
        };

        /// <summary>For --preview: the PB card fully shown, no animation.</summary>
        internal void ShowCelebrationStill()
        {
            Celebration.IsVisible = true;
            Celebration.Opacity = 1;
            HeroContent.Opacity = 0;
        }

        static Task Animate(Animatable target, AvaloniaProperty property, double from, double to, int ms, Easing easing = null) =>
            new Animation
            {
                Duration = TimeSpan.FromMilliseconds(ms),
                Easing = easing ?? new CubicEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
                },
            }.RunAsync(target);

        /// <summary>
        /// Two columns when there is room and both sides have something on show, one scrolling column otherwise. On a
        /// big screen everything is scaled up (it's laid out for about 1500 px across), a bit more in the Simple view.
        /// </summary>
        internal void Relayout(double width)
        {
            if (width <= 0) return;
            // The title bar isn't scaled: it has the window's own width.
            PlayerChip.IsVisible = width >= 1200;
            // Size (Customize): everything on the page drawn bigger or smaller - the Simple view a touch bigger.
            Zoom(NominateZoom, vm.Layout.Scale);
            var zoom = vm.Layout.Scale * (vm.Layout.IsSimple ? 1.06 : 1);
            Zoom(ColumnsZoom, zoom);
            width /= zoom;

            bool leftShown = vm.Layout.AnyShown(left: true), rightShown = vm.Layout.AnyShown(left: false);
            NothingShown.IsVisible = !(leftShown || rightShown);
            var wide = width >= 1060 && leftShown && rightShown;
            Columns.ColumnDefinitions[1].Width = wide ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
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
            SideBySide(StagesGrid, StagesRight, room && vm.HasStagesB);
            SideBySide(BonusesGrid, BonusesRight, room && vm.HasBonusesB);
            Hero.Height = leftWidth >= 680 ? 300 : 230;
            MapTitle.FontSize = leftWidth >= 800 ? 46 : leftWidth >= 600 ? 36 : 28;
        }

        static void Zoom(LayoutTransformControl host, double zoom)
        {
            var scaled = Math.Abs(zoom - 1) > 0.01;
            host.LayoutTransform = scaled ? new ScaleTransform(zoom, zoom) : null;
        }

        /// <summary>The second half of a list next to the first (in its own column) or under it.</summary>
        static void SideBySide(Grid grid, Control secondHalf, bool sideBySide)
        {
            grid.ColumnDefinitions[1].Width = sideBySide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(secondHalf, sideBySide ? 1 : 0);
            Grid.SetRow(secondHalf, sideBySide ? 0 : 1);
            secondHalf.Margin = sideBySide ? new Thickness(28, 0, 0, 0) : new Thickness(0, 1, 0, 0);
        }

        void OnStateChanged()
        {
            MaxIcon.Data = (Geometry)this.FindResource(WindowState == WindowState.Maximized ? "IconRestore" : "IconMaximize");
            // Maximized, the edges belong to the screen: no frame line.
            Root.BorderThickness = WindowState == WindowState.Maximized ? new Thickness(0) : new Thickness(1);
            PlacementChanged?.Invoke();
        }

        /// <summary>KSF Companion's own keys, shown at the bottom of the dashboard.</summary>
        internal void SetKeys(KeyNames keys)
        {
            SaveKey.Text = GameKeys.Label(keys.Save);
            CardKey.Text = GameKeys.Label(keys.Card);
            ListKey.Text = GameKeys.Label(keys.List);
        }

        // ----- the window's own frame: drag the title bar to move, the edges to resize -----

        void SetUpChrome()
        {
            TitleBar.PointerPressed += (s, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2)
                {
                    ToggleMaximized();
                    return;
                }
                BeginMoveDrag(e);
            };
            this.FindControl<Button>("MinButton").Click += (s, e) => WindowState = WindowState.Minimized;
            this.FindControl<Button>("MaxButton").Click += (s, e) => ToggleMaximized();
            this.FindControl<Button>("CloseButton").Click += (s, e) => Close();
            foreach (var (name, edge) in new[]
            {
                ("GripN", WindowEdge.North), ("GripS", WindowEdge.South), ("GripW", WindowEdge.West), ("GripE", WindowEdge.East),
                ("GripNW", WindowEdge.NorthWest), ("GripNE", WindowEdge.NorthEast), ("GripSW", WindowEdge.SouthWest), ("GripSE", WindowEdge.SouthEast),
            })
            {
                var grip = this.FindControl<Border>(name);
                grip.PointerPressed += (s, e) =>
                {
                    if (WindowState != WindowState.Normal || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                    BeginResizeDrag(edge, e);
                    e.Handled = true;
                };
                // Maximized there's nothing to resize.
                PropertyChanged += (s, e) => { if (e.Property == WindowStateProperty) grip.IsVisible = WindowState == WindowState.Normal; };
            }
            if (KeepOnTopMenuItem != null)
                KeepOnTopMenuItem.Click += (s, e) => SetTopmost(!Topmost);
        }

        void ToggleMaximized() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        internal void SetTopmost(bool on)
        {
            if (KeepOnTopMenuItem != null) KeepOnTopMenuItem.IsChecked = on;
            if (Topmost == on) return;
            Topmost = on;
            TopmostChanged?.Invoke(on);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
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

        // ----- the binds page waiting for a key -----

        void OnCaptureKey(object sender, KeyEventArgs e)
        {
            if (!vm.Binds.IsCapturing) return;
            e.Handled = true;
            if (e.Key == Key.Escape)
            {
                vm.Binds.CancelCapture();
                return;
            }
            var name = GameKeyOf(e.Key, e.PhysicalKey);
            if (name == null)
            {
                vm.Toast = e.Key == Key.OemTilde || e.PhysicalKey == PhysicalKey.Backquote ? "That's the console key - pick another" : "That key can't be bound - pick another";
                vm.Binds.CancelCapture();
                return;
            }
            vm.Binds.Capture(name);
        }

        void OnCaptureMouse(object sender, PointerPressedEventArgs e)
        {
            if (!vm.Binds.IsCapturing) return;
            string name = null;
            switch (e.GetCurrentPoint(this).Properties.PointerUpdateKind)
            {
                case PointerUpdateKind.RightButtonPressed: name = "MOUSE2"; break;
                case PointerUpdateKind.MiddleButtonPressed: name = "MOUSE3"; break;
                case PointerUpdateKind.XButton1Pressed: name = "MOUSE4"; break;
                case PointerUpdateKind.XButton2Pressed: name = "MOUSE5"; break;
            }
            if (name != null)
            {
                e.Handled = true;
                vm.Binds.Capture(name);
                return;
            }
            // A left click stops waiting; on the waiting row's own key button that's all it does.
            var source = e.Source as Control;
            var row = source?.DataContext as BindRow;
            var onWaitingButton = row != null && row.IsCapturing && InsideButton(source);
            vm.Binds.CancelCapture();
            if (onWaitingButton) e.Handled = true;
        }

        void OnCaptureWheel(object sender, PointerWheelEventArgs e)
        {
            if (!vm.Binds.IsCapturing) return;
            e.Handled = true;
            vm.Binds.Capture(e.Delta.Y > 0 ? "MWHEELUP" : "MWHEELDOWN");
        }

        /// <summary>Enter in the "any other command" box adds it.</summary>
        void OnOwnCommandKey(object sender, KeyEventArgs e)
        {
            if (vm.Binds.IsCapturing || e.Key != Key.Enter || !(e.Source is TextBox box) || !box.Classes.Contains("ownBox")) return;
            e.Handled = true;
            if (vm.Binds.AddOwnCommand.CanExecute(null)) vm.Binds.AddOwnCommand.Execute(null);
        }

        static bool InsideButton(Visual v)
        {
            for (; v != null; v = v.GetVisualParent())
                if (v is Button) return true;
            return false;
        }

        static bool IsNumPad(PhysicalKey key) => key >= PhysicalKey.NumLock && key <= PhysicalKey.NumPadSubtract || key == PhysicalKey.NumPadEnter
            || key == PhysicalKey.NumPadEqual || key == PhysicalKey.NumPadComma || key == PhysicalKey.NumPadDecimal;

        /// <summary>The Source engine's name for a key ("r", "SHIFT", "KP_END"), or null for keys that can't be bound here.</summary>
        static string GameKeyOf(Key key, PhysicalKey physical)
        {
            // The numpad without Num Lock sends the same keys as the arrows, Home, End...: where the key sits tells them apart.
            var pad = IsNumPad(physical);
            switch (physical)
            {
                case PhysicalKey.NumPad0: return "KP_INS";
                case PhysicalKey.NumPad1: return "KP_END";
                case PhysicalKey.NumPad2: return "KP_DOWNARROW";
                case PhysicalKey.NumPad3: return "KP_PGDN";
                case PhysicalKey.NumPad4: return "KP_LEFTARROW";
                case PhysicalKey.NumPad5: return "KP_5";
                case PhysicalKey.NumPad6: return "KP_RIGHTARROW";
                case PhysicalKey.NumPad7: return "KP_HOME";
                case PhysicalKey.NumPad8: return "KP_UPARROW";
                case PhysicalKey.NumPad9: return "KP_PGUP";
                case PhysicalKey.NumPadDecimal: return "KP_DEL";
                case PhysicalKey.NumPadEnter: return "KP_ENTER";
                case PhysicalKey.NumPadDivide: return "KP_SLASH";
                case PhysicalKey.NumPadMultiply: return "KP_MULTIPLY";
                case PhysicalKey.NumPadSubtract: return "KP_MINUS";
                case PhysicalKey.NumPadAdd: return "KP_PLUS";
            }
            if (key >= Key.A && key <= Key.Z) return ((char)('a' + (key - Key.A))).ToString();
            if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
            if (key >= Key.F1 && key <= Key.F12) return "F" + (key - Key.F1 + 1).ToString(CultureInfo.InvariantCulture);
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return new[] { "KP_INS", "KP_END", "KP_DOWNARROW", "KP_PGDN", "KP_LEFTARROW", "KP_5", "KP_RIGHTARROW", "KP_HOME", "KP_UPARROW", "KP_PGUP" }[key - Key.NumPad0];
            switch (key)
            {
                case Key.Space: return "SPACE";
                case Key.Tab: return "TAB";
                case Key.Enter: return pad ? "KP_ENTER" : "ENTER";
                case Key.Back: return "BACKSPACE";
                case Key.CapsLock: return "CAPSLOCK";
                case Key.Insert: return pad ? "KP_INS" : "INS";
                case Key.Delete: return pad ? "KP_DEL" : "DEL";
                case Key.Home: return pad ? "KP_HOME" : "HOME";
                case Key.End: return pad ? "KP_END" : "END";
                case Key.PageUp: return pad ? "KP_PGUP" : "PGUP";
                case Key.PageDown: return pad ? "KP_PGDN" : "PGDN";
                case Key.Up: return pad ? "KP_UPARROW" : "UPARROW";
                case Key.Down: return pad ? "KP_DOWNARROW" : "DOWNARROW";
                case Key.Left: return pad ? "KP_LEFTARROW" : "LEFTARROW";
                case Key.Right: return pad ? "KP_RIGHTARROW" : "RIGHTARROW";
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
            // Keyboard layouts can report these by character only: go by where the key sits.
            switch (physical)
            {
                case PhysicalKey.Semicolon: return "SEMICOLON";
                case PhysicalKey.Quote: return "'";
                case PhysicalKey.Comma: return ",";
                case PhysicalKey.Minus: return "-";
                case PhysicalKey.Period: return ".";
                case PhysicalKey.Slash: return "/";
                case PhysicalKey.Equal: return "=";
                case PhysicalKey.BracketLeft: return "[";
                case PhysicalKey.BracketRight: return "]";
            }
            return null;
        }

        // ----- where the window goes -----

        /// <summary>
        /// Shows the dashboard without taking focus from the game.
        /// </summary>
        internal void ShowWithoutFocus()
        {
            ShowActivated = false;
            Show();
            ShowActivated = true;
        }

        /// <summary>"left,top,width,height,maximized": the position in screen pixels, the size in the window's own units.</summary>
        internal string Placement
        {
            get
            {
                var size = WindowState == WindowState.Normal ? new Size(Width, Height) : restoreSize;
                var position = WindowState == WindowState.Normal ? Position : restorePosition;
                if (double.IsNaN(size.Width) || size.Width <= 0) return null;
                return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:0},{3:0},{4}", position.X, position.Y, size.Width, size.Height,
                    WindowState == WindowState.Maximized ? 1 : 0);
            }
        }

        // Where the window goes back to when it's no longer maximized.
        Size restoreSize = new Size(1440, 940);
        PixelPoint restorePosition;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (WindowState != WindowState.Normal) return;
            if (change.Property == WidthProperty || change.Property == HeightProperty || change.Property == ClientSizeProperty)
                restoreSize = new Size(Width, Height);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            restorePosition = Position;
            PositionChanged += (s, args) => { if (WindowState == WindowState.Normal) restorePosition = Position; };
        }

        /// <summary>Puts the window where it was last time, or on the second monitor the first time.</summary>
        internal void ApplyPlacement(string saved)
        {
            var parts = (saved ?? "").Split(',');
            var screens = Screens.All;
            if (parts.Length == 5 &&
                int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var left) &&
                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var top) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var height) &&
                screens.Any(s => s.WorkingArea.Intersects(new PixelRect(left + 40, top + 10, 120, 40))))
            {
                Position = restorePosition = new PixelPoint(left, top);
                Width = Math.Max(MinWidth, width);
                Height = Math.Max(MinHeight, height);
                restoreSize = new Size(Width, Height);
                if (parts[4] == "1") WindowState = WindowState.Maximized;
                return;
            }

            // First run: the biggest monitor that isn't the one the game runs on.
            var screen = screens.Where(s => !s.IsPrimary).OrderByDescending(s => s.Bounds.Width * s.Bounds.Height).FirstOrDefault()
                         ?? Screens.Primary ?? screens.FirstOrDefault();
            if (screen == null) return;
            var area = screen.WorkingArea;
            var scale = screen.Scaling;
            Width = Math.Min(1440, area.Width / scale * 0.9);
            Height = Math.Min(940, area.Height / scale * 0.9);
            restoreSize = new Size(Width, Height);
            Position = restorePosition = new PixelPoint(area.X + (int)((area.Width - Width * scale) / 2), area.Y + (int)((area.Height - Height * scale) / 2));
        }

        /// <summary>True when the window sits on a monitor other than the main one (where the game runs).</summary>
        internal bool IsOnSecondaryScreen
        {
            get
            {
                var center = new PixelPoint(Position.X + (int)(Width / 2), Position.Y + (int)(Height / 2));
                var screen = Screens.All.FirstOrDefault(s => s.Bounds.Contains(center));
                return screen != null && !screen.IsPrimary;
            }
        }
    }
}

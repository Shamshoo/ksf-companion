using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KsfCompanion
{
    /// <summary>
    /// The tray icon, made from the PNGs rendered from Ui\Assets\icon.svg (picked to match the system's small-icon size).
    /// </summary>
    static class AppIcon
    {
        static readonly int[] Sizes = { 16, 20, 24, 32 };
        static Icon icon;

        public static Icon Get() => icon ??= Create();

        static Icon Create()
        {
            var wanted = SystemInformation.SmallIconSize.Width;
            var size = Sizes.FirstOrDefault(s => s >= wanted);
            if (size == 0) size = Sizes.Last();

            var resource = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/KSFCompanion;component/Ui/Assets/icon-{size}.png"));
            using (var stream = resource.Stream)
            using (var bitmap = new Bitmap(stream))
            {
                var handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { NativeMethods.DestroyIcon(handle); }
            }
        }
    }
}

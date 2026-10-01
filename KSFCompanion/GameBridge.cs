using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace KsfCompanion
{
    enum LinkFormat { Raw, CommandLine }

    enum SendResult { NoWindow, Timeout, Declined, Accepted }

    /// <summary>
    /// Hands console commands to the running CS:S window with WM_COPYDATA, the same channel Valve's launcher
    /// uses for "-hijack". This never reads or writes game memory.
    /// </summary>
    static class GameBridge
    {
        static readonly string[] ProcessNames = { "cstrike_win64", "cstrike" };

        public static int FindGameProcessId()
        {
            // Lets the app be exercised against a stand-in process without touching a real running game.
            var testProcess = Environment.GetEnvironmentVariable("KSFC_GAME_PROCESS");
            var names = string.IsNullOrEmpty(testProcess) ? ProcessNames : new[] { testProcess };

            foreach (var name in names)
            {
                var processes = Process.GetProcessesByName(name);
                try
                {
                    if (processes.Length > 0) return processes[0].Id;
                }
                finally
                {
                    foreach (var p in processes) p.Dispose();
                }
            }
            return 0;
        }

        public static IntPtr FindGameWindow(int processId)
        {
            var found = IntPtr.Zero;
            var className = new StringBuilder(64);
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != processId) return true;
                className.Clear();
                NativeMethods.GetClassName(hwnd, className, className.Capacity);
                if (className.ToString() != "Valve001") return true;
                found = hwnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        public static string Wrap(string command, LinkFormat format)
        {
            if (format == LinkFormat.Raw) return command;
            var parts = command.Split(';').Select(c => c.Trim()).Where(c => c.Length > 0).Select(c => "+" + c);
            return "-hijack " + string.Join(" ", parts);
        }

        public static SendResult Send(IntPtr hwnd, string text, uint timeoutMs = 1500)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return SendResult.NoWindow;

            var bytes = Encoding.ASCII.GetBytes(text + "\0");
            var buffer = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
                var data = new NativeMethods.COPYDATASTRUCT { dwData = IntPtr.Zero, cbData = bytes.Length, lpData = buffer };
                var ok = NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WM_COPYDATA, IntPtr.Zero, ref data,
                    NativeMethods.SMTO_ABORTIFHUNG, timeoutMs, out var handled);
                if (ok == IntPtr.Zero) return SendResult.Timeout;
                return handled != IntPtr.Zero ? SendResult.Accepted : SendResult.Declined;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}

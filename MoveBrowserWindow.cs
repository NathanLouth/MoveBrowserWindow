using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MoveBrowserWindow
{
    class Program
    {
        public class WindowHelper
        {
            [DllImport("user32.dll")]
            public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

            [DllImport("user32.dll")]
            public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

            public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

            [DllImport("user32.dll")]
            public static extern int GetWindowTextLength(IntPtr hWnd);

            [DllImport("user32.dll")]
            public static extern bool IsWindowVisible(IntPtr hWnd);
        }

        public class KeyboardSimulator
        {
            [DllImport("user32.dll")]
            public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

            private const byte VK_F11 = 0x7A;
            private const byte VK_LWIN = 0x5B;
            private const byte VK_UP = 0x26;

            private const uint KEYEVENTF_KEYDOWN = 0x0000;
            private const uint KEYEVENTF_KEYUP = 0x0002;

            public static void PressF11() => SimulateKeyPress(VK_F11);

            public static void PressWinUP()
            {
                keybd_event(VK_LWIN, 0, KEYEVENTF_KEYDOWN, 0);
                Thread.Sleep(50);

                keybd_event(VK_UP, 0, KEYEVENTF_KEYDOWN, 0);
                Thread.Sleep(50);
                keybd_event(VK_UP, 0, KEYEVENTF_KEYUP, 0);
                Thread.Sleep(50);

                keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, 0);
            }

            private static void SimulateKeyPress(byte key)
            {
                keybd_event(key, 0, KEYEVENTF_KEYDOWN, 0);
                Thread.Sleep(100);
                keybd_event(key, 0, KEYEVENTF_KEYUP, 0);
            }
        }

        static void Main(string[] args)
        {
            if (args.Length < 3 || (args.Length - 1) % 2 != 0)
            {
                Console.WriteLine("Usage: MoveBrowserWindow.exe <delayInSeconds> <URL1> <fullscreenFlag1> <URL2> <fullscreenFlag2> ...");
                return;
            }

            if (!int.TryParse(args[0], out int delayInSeconds) || delayInSeconds < 0)
            {
                Console.WriteLine("Invalid delay time. Please provide a non-negative integer.");
                return;
            }

            var urlActions = new List<(string url, int f11Flag)>();
            for (int i = 1; i < args.Length; i += 2)
            {
                string url = args[i];
                if (!int.TryParse(args[i + 1], out int flag) || (flag != 0 && flag != 1))
                {
                    Console.WriteLine($"Invalid F11 flag for URL '{url}'. Use 1 to enable fullscreen, 0 to disable.");
                    return;
                }
                urlActions.Add((url, flag));
            }

            Console.WriteLine($"Waiting for {delayInSeconds} seconds before starting...");
            Thread.Sleep(delayInSeconds * 1000);

            int monitorIndex = 0;
            foreach (var (url, f11Flag) in urlActions)
            {
                var initialWindows = GetOpenWindows();

                Console.WriteLine($"Opening {url}...");
                Process.Start("chrome.exe", $"--new-window {url}");
                Thread.Sleep(5000);

                var newWindows = GetOpenWindows();
                var newWindowHandles = newWindows.Keys.Except(initialWindows.Keys).ToList();

                if (newWindowHandles.Count == 1)
                {
                    MoveWindowToMonitor(newWindowHandles[0], monitorIndex + 1);
                }
                else
                {
                    Console.WriteLine($"Error: Multiple or no new windows found after opening {url}");
                }

                KeyboardSimulator.PressWinUP();

                if (f11Flag == 1)
                {
                    Console.WriteLine($"Applying F11 to {url}...");
                    KeyboardSimulator.PressF11();

                }

                monitorIndex++;
            }
        }

        static Dictionary<string, IntPtr> GetOpenWindows()
        {
            var windows = new Dictionary<string, IntPtr>();
            WindowHelper.EnumWindows((hWnd, lParam) =>
            {
                if (WindowHelper.IsWindowVisible(hWnd))
                {
                    var length = WindowHelper.GetWindowTextLength(hWnd);
                    if (length > 0)
                    {
                        var title = new StringBuilder(length + 1);
                        WindowHelper.GetWindowText(hWnd, title, title.Capacity);
                        if (!string.IsNullOrEmpty(title.ToString().Trim()))
                        {
                            windows[title.ToString()] = hWnd;
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        static void MoveWindowToMonitor(string windowTitle, int monitorIndex)
        {
            var screens = Screen.AllScreens;
            if (monitorIndex < 1 || monitorIndex > screens.Length)
            {
                Console.WriteLine($"Invalid monitor number. There are {screens.Length} monitors.");
                return;
            }

            var targetScreen = screens[monitorIndex - 1];
            var bounds = targetScreen.Bounds;
            var windows = GetOpenWindows();
            var window = windows.FirstOrDefault(w => w.Key.Contains(windowTitle));

            if (window.Equals(default(KeyValuePair<string, IntPtr>)))
            {
                Console.WriteLine($"No window found matching '{windowTitle}'");
                return;
            }

            bool success = WindowHelper.MoveWindow(window.Value, bounds.X, bounds.Y, bounds.Width, bounds.Height, true);
            Console.WriteLine(success
                ? $"Moved '{windowTitle}' to monitor {monitorIndex} at {bounds.X},{bounds.Y} with size {bounds.Width}x{bounds.Height}"
                : "Failed to move the window.");
        }
    }
}

using System;
using System.IO;
using System.Text.Json;
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
        public class Config
        {
            public int DelayInSeconds { get; set; }
            public List<UrlAction> UrlActions { get; set; }
        }

        public class UrlAction
        {
            public string Url { get; set; }
            public int Fullscreen { get; set; }
        }

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
            string configPath;

            if (args.Length == 0)
            {
                configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            }
            else
            {
                configPath = args[0];
            }

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Config file not found: {configPath}");
                return;
            }

            Config config;
            try
            {
                string json = File.ReadAllText(configPath);
                config = System.Text.Json.JsonSerializer.Deserialize<Config>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read or parse config file: {ex.Message}");
                return;
            }

            if (config == null || config.UrlActions == null || config.DelayInSeconds < 0)
            {
                Console.WriteLine("Invalid config structure.");
                return;
            }

            Console.WriteLine($"Waiting for {config.DelayInSeconds} seconds before starting...");
            Thread.Sleep(config.DelayInSeconds * 1000);

            int monitorIndex = 0;
            foreach (var action in config.UrlActions)
            {
                var initialWindows = GetOpenWindows();

                Console.WriteLine($"Opening {action.Url}...");
                Process.Start("chrome.exe", $"--new-window {action.Url}");
                Thread.Sleep(5000);

                var newWindows = GetOpenWindows();
                var newWindowHandles = newWindows.Keys.Except(initialWindows.Keys).ToList();

                if (newWindowHandles.Count == 1)
                {
                    MoveWindowToMonitor(newWindowHandles[0], monitorIndex + 1);
                }
                else
                {
                    Console.WriteLine($"Error: Multiple or no new windows found after opening {action.Url}");
                }

                KeyboardSimulator.PressWinUP();

                if (action.Fullscreen == 1)  // changed here
                {
                    Console.WriteLine($"Applying F11 to {action.Url}...");
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

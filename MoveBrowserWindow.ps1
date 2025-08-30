$urls = @("http://example.com", "http://example.com", "http://example.com")

# Add necessary types for window manipulation
Add-Type -AssemblyName System.Windows.Forms

# Define helper class to interact with windows
Add-Type @"
using System;
using System.Runtime.InteropServices;

public class WindowHelper {
    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError=true)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
}
"@

# Define keyboard simulation for F11 press
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Threading;

public class KeyboardSimulator {
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);
    
    public static void PressF11() {
        const byte VK_F11 = 0x7A;
        const uint KEYEVENTF_KEYDOWN = 0x0000;
        const uint KEYEVENTF_KEYUP = 0x0002;

        // Simulate F11 key press
        keybd_event(VK_F11, 0, KEYEVENTF_KEYDOWN, 0);
        Thread.Sleep(100);
        keybd_event(VK_F11, 0, KEYEVENTF_KEYUP, 0);
    }
}
"@

# Function to get currently open windows
function Get-OpenWindows {
    $signature = @'
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
'@

    Add-Type -MemberDefinition $signature -Name Win32Enum -Namespace Win32 -ErrorAction SilentlyContinue

    $windows = @{}

    # Callback to retrieve window titles
    $callback = {
        param ($hWnd, $lParam)
        if ([Win32.Win32Enum]::IsWindowVisible($hWnd)) {
            $length = [Win32.Win32Enum]::GetWindowTextLength($hWnd)
            if ($length -gt 0) {
                $builder = New-Object System.Text.StringBuilder -ArgumentList ($length + 1)
                [Win32.Win32Enum]::GetWindowText($hWnd, $builder, $builder.Capacity) | Out-Null
                $title = $builder.ToString().Trim()
                if ($title.Length -gt 0) {
                    $windows[$title] = $hWnd
                }
            }
        }
        return $true
    }

    $enumProc = [Win32.Win32Enum+EnumWindowsProc]$callback
    [Win32.Win32Enum]::EnumWindows($enumProc, [IntPtr]::Zero) | Out-Null

    return $windows
}

# Function to move window to a specific monitor
function Move-WindowToMonitor {
    param (
        [Parameter(Mandatory=$true)]
        [string]$WindowTitle,

        [Parameter(Mandatory=$true)]
        [int]$MonitorNumber,  # 1-based index

        [Parameter(Mandatory=$false)]
        [bool]$ConsoleLog=$false
    )

    $windows = Get-OpenWindows

    # Find the matching window by title
    $matchingWindows = $windows.Keys | Where-Object { $_ -like "*$WindowTitle*" }

    # Handle different cases based on number of matched windows
    if ($matchingWindows.Count -eq 0) {
        Write-Host "No window found matching '$WindowTitle'" -ForegroundColor Red
        return
    }

    if ($matchingWindows.Count -gt 1) {
        Write-Host "Multiple windows matched:" -ForegroundColor Yellow
        $matchingWindows | ForEach-Object { Write-Host " - $_" }
        Write-Host "Please provide a more specific title." -ForegroundColor Yellow
        return
    }

    $hwnd = $windows[$matchingWindows]

    # Get monitor details
    $screens = [System.Windows.Forms.Screen]::AllScreens
    if ($MonitorNumber -lt 1 -or $MonitorNumber -gt $screens.Count) {
        Write-Host "Invalid monitor number. There are $($screens.Count) monitors." -ForegroundColor Red
        return
    }

    $targetScreen = $screens[$MonitorNumber - 1]
    $bounds = $targetScreen.Bounds
    $x = $bounds.X
    $y = $bounds.Y
    $width = $bounds.Width
    $height = $bounds.Height

    # Attempt to move the window
    $success = [WindowHelper]::MoveWindow($hwnd, $x, $y, $width, $height, $true)

    if ($ConsoleLog) {
        if ($success) {
            Write-Host "Moved '$WindowTitle' to monitor $MonitorNumber at ($x, $y) with size ${width}x$height" -ForegroundColor Green
        } else {
            Write-Host "Failed to move the window." -ForegroundColor Red
        }
    }
}

# Main logic to open URLs and move windows
$tmpMonitor = 1
foreach ($url in $urls) {
    $initialWindows = Get-OpenWindows

    # Open the URL in Chrome and wait for the window to appear
    Start-Process "chrome.exe" -ArgumentList "--new-window", $url
    Start-Sleep -Seconds 5

    # Simulate pressing F11 (fullscreen)
    [KeyboardSimulator]::PressF11()

    # Get the newly opened windows and determine which one is new
    $newWindows = Get-OpenWindows
    $addedWindows = $newWindows.Keys | Where-Object { -not $initialWindows.ContainsKey($_) }

    if ($addedWindows.Count -eq 1) {
        Move-WindowToMonitor -WindowTitle $addedWindows -MonitorNumber $tmpMonitor -ConsoleLog $true
    } else {
        Write-Host "Multiple or no new windows found after opening $url" -ForegroundColor Red
    }

    # Increment monitor for next window
    $tmpMonitor++
}
<#
.SYNOPSIS
    Simulates user activity in Visual Studio at random intervals.
.DESCRIPTION
    This script mimics user actions like scrolling, navigating, moving cursor,
    and changing tabs to simulate activity in Visual Studio.
.PARAMETER MinInterval
    Minimum seconds between actions (default: 2)
.PARAMETER MaxInterval
    Maximum seconds between actions (default: 10)
.PARAMETER Duration
    Total duration in minutes to run the script (default: 60)
.EXAMPLE
    .\SimulateActivity.ps1 -MinInterval 3 -MaxInterval 15 -Duration 30
#>

param(
    [int]$MinInterval = 2,
    [int]$MaxInterval = 10,
    [int]$Duration = 60
)

# Add required assemblies
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName Microsoft.VisualBasic

# Add user32.dll for mouse operations
Add-Type @"
using System;
using System.Runtime.InteropServices;

public class MouseSimulator {
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, int dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    public struct POINT {
        public int X;
        public int Y;
    }

    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const int WHEEL_DELTA = 120;
}
"@

# Action definitions
$actions = @(
    @{ Name = "ScrollDown"; Weight = 15 },
    @{ Name = "ScrollUp"; Weight = 15 },
    @{ Name = "NextTab"; Weight = 10 },
    @{ Name = "PrevTab"; Weight = 10 },
    @{ Name = "MoveCursorDown"; Weight = 10 },
    @{ Name = "MoveCursorUp"; Weight = 10 },
    @{ Name = "MoveCursorLeft"; Weight = 5 },
    @{ Name = "MoveCursorRight"; Weight = 5 },
    @{ Name = "GoToDefinition"; Weight = 3 },
    @{ Name = "GoBack"; Weight = 3 },
    @{ Name = "GoForward"; Weight = 3 },
    @{ Name = "SolutionExplorer"; Weight = 3 },
    @{ Name = "ErrorList"; Weight = 2 },
    @{ Name = "FindInFiles"; Weight = 2 },
    @{ Name = "MoveMouse"; Weight = 4 }
)

# Build weighted action list
$weightedActions = @()
foreach ($action in $actions) {
    for ($i = 0; $i -lt $action.Weight; $i++) {
        $weightedActions += $action.Name
    }
}

function Get-RandomInterval {
    return Get-Random -Minimum $MinInterval -Maximum ($MaxInterval + 1)
}

function Get-ForegroundWindowTitle {
    $handle = [MouseSimulator]::GetForegroundWindow()
    $sb = New-Object System.Text.StringBuilder 256
    [void][MouseSimulator]::GetWindowText($handle, $sb, 256)
    return $sb.ToString()
}

function Send-Keys {
    param([string]$Keys)
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
}

function Move-MouseRandomly {
    $point = New-Object MouseSimulator+POINT
    [MouseSimulator]::GetCursorPos([ref]$point) | Out-Null

    $offsetX = Get-Random -Minimum -100 -Maximum 100
    $offsetY = Get-Random -Minimum -100 -Maximum 100

    $newX = [Math]::Max(0, [Math]::Min([System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width, $point.X + $offsetX))
    $newY = [Math]::Max(0, [Math]::Min([System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Height, $point.Y + $offsetY))

    # Smooth movement
    $steps = 10
    for ($i = 1; $i -le $steps; $i++) {
        $currentX = $point.X + (($newX - $point.X) * $i / $steps)
        $currentY = $point.Y + (($newY - $point.Y) * $i / $steps)
        [MouseSimulator]::SetCursorPos([int]$currentX, [int]$currentY) | Out-Null
        Start-Sleep -Milliseconds 20
    }
}

function Invoke-Scroll {
    param([int]$Direction) # 1 = up, -1 = down
    $wheelAmount = $Direction * [MouseSimulator]::WHEEL_DELTA * (Get-Random -Minimum 1 -Maximum 4)
    [MouseSimulator]::mouse_event([MouseSimulator]::MOUSEEVENTF_WHEEL, 0, 0, $wheelAmount, 0)
}

function Invoke-Action {
    param([string]$ActionName)

    switch ($ActionName) {
        "ScrollDown" {
            Invoke-Scroll -Direction -1
            Write-Host "  -> Scrolled down" -ForegroundColor Gray
        }
        "ScrollUp" {
            Invoke-Scroll -Direction 1
            Write-Host "  -> Scrolled up" -ForegroundColor Gray
        }
        "NextTab" {
            Send-Keys "^{TAB}"
            Write-Host "  -> Switched to next tab" -ForegroundColor Gray
        }
        "PrevTab" {
            Send-Keys "^+{TAB}"
            Write-Host "  -> Switched to previous tab" -ForegroundColor Gray
        }
        "MoveCursorDown" {
            $lines = Get-Random -Minimum 1 -Maximum 10
            for ($i = 0; $i -lt $lines; $i++) {
                Send-Keys "{DOWN}"
                Start-Sleep -Milliseconds 50
            }
            Write-Host "  -> Moved cursor down $lines lines" -ForegroundColor Gray
        }
        "MoveCursorUp" {
            $lines = Get-Random -Minimum 1 -Maximum 10
            for ($i = 0; $i -lt $lines; $i++) {
                Send-Keys "{UP}"
                Start-Sleep -Milliseconds 50
            }
            Write-Host "  -> Moved cursor up $lines lines" -ForegroundColor Gray
        }
        "MoveCursorLeft" {
            $chars = Get-Random -Minimum 1 -Maximum 5
            for ($i = 0; $i -lt $chars; $i++) {
                Send-Keys "{LEFT}"
                Start-Sleep -Milliseconds 30
            }
            Write-Host "  -> Moved cursor left $chars chars" -ForegroundColor Gray
        }
        "MoveCursorRight" {
            $chars = Get-Random -Minimum 1 -Maximum 5
            for ($i = 0; $i -lt $chars; $i++) {
                Send-Keys "{RIGHT}"
                Start-Sleep -Milliseconds 30
            }
            Write-Host "  -> Moved cursor right $chars chars" -ForegroundColor Gray
        }
        "GoToDefinition" {
            Send-Keys "{F12}"
            Write-Host "  -> Go to Definition (F12)" -ForegroundColor Gray
        }
        "GoBack" {
            Send-Keys "^{-}"
            Write-Host "  -> Navigate back (Ctrl+-)" -ForegroundColor Gray
        }
        "GoForward" {
            Send-Keys "^+{-}"
            Write-Host "  -> Navigate forward (Ctrl+Shift+-)" -ForegroundColor Gray
        }
        "SolutionExplorer" {
            Send-Keys "^%l"
            Write-Host "  -> Opened Solution Explorer (Ctrl+Alt+L)" -ForegroundColor Gray
        }
        "ErrorList" {
            Send-Keys "^\e"
            Write-Host "  -> Opened Error List (Ctrl+\, E)" -ForegroundColor Gray
        }
        "FindInFiles" {
            Send-Keys "^+f"
            Start-Sleep -Milliseconds 500
            Send-Keys "{ESCAPE}"
            Write-Host "  -> Opened Find in Files (Ctrl+Shift+F)" -ForegroundColor Gray
        }
        "MoveMouse" {
            Move-MouseRandomly
            Write-Host "  -> Moved mouse cursor" -ForegroundColor Gray
        }
    }
}

# Main execution
Clear-Host
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Visual Studio Activity Simulator" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Interval: $MinInterval - $MaxInterval seconds"
Write-Host "  Duration: $Duration minutes"
Write-Host ""
Write-Host "Press Ctrl+C to stop the script" -ForegroundColor Yellow
Write-Host ""
Write-Host "Starting in 5 seconds... Switch to Visual Studio now!" -ForegroundColor Green
Start-Sleep -Seconds 5

$endTime = (Get-Date).AddMinutes($Duration)
$actionCount = 0

Write-Host ""
Write-Host "Simulation started at $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor Green
Write-Host "Will run until $(Get-Date $endTime -Format 'HH:mm:ss')" -ForegroundColor Green
Write-Host ""

try {
    while ((Get-Date) -lt $endTime) {
        # Check if Visual Studio is in foreground (optional warning)
        $windowTitle = Get-ForegroundWindowTitle
        $isVS = $windowTitle -match "Visual Studio" -or $windowTitle -match "\.cs" -or $windowTitle -match "\.csproj"

        if (-not $isVS) {
            Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Warning: Visual Studio not in foreground" -ForegroundColor Yellow
        }

        # Select and perform random action
        $selectedAction = $weightedActions | Get-Random
        $actionCount++

        Write-Host "[$(Get-Date -Format 'HH:mm:ss')] Action #$actionCount : $selectedAction" -ForegroundColor Cyan

        Invoke-Action -ActionName $selectedAction

        # Wait for random interval
        $interval = Get-RandomInterval
        Write-Host "  -> Waiting $interval seconds..." -ForegroundColor DarkGray
        Start-Sleep -Seconds $interval
    }
}
catch {
    Write-Host ""
    Write-Host "Error occurred: $_" -ForegroundColor Red
}
finally {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Simulation ended at $(Get-Date -Format 'HH:mm:ss')" -ForegroundColor Green
    Write-Host "Total actions performed: $actionCount" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
}

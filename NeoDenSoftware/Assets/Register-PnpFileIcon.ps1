# Associates a custom Explorer icon with .pnp project files (NeoDenSoftware's "Save Project"
# format) for the CURRENT USER ONLY (HKEY_CURRENT_USER - no admin rights needed, no other user
# account on this machine is affected).
#
# What this does NOT do: it does not set a default "open" action for double-clicking a .pnp file
# - only the icon shown in Explorer. Double-clicking a .pnp file will still prompt "How do you
# want to open this file?" unless you separately choose a program.
#
# Run this yourself (e.g. right-click > Run with PowerShell, or `powershell -ExecutionPolicy
# Bypass -File Register-PnpFileIcon.ps1`) - it isn't run automatically, since it edits the
# registry.

$ErrorActionPreference = "Stop"

$iconPath = Join-Path $PSScriptRoot "PnpFile.ico"
if (-not (Test-Path $iconPath)) {
    throw "Icon not found at $iconPath - run Build-PnpIcon.ps1 first."
}

$progId = "NeoDenSoftware.PnpProject"

New-Item -Path "HKCU:\Software\Classes\$progId" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\$progId" -Name "(Default)" -Value "NeoDen Project File"

New-Item -Path "HKCU:\Software\Classes\$progId\DefaultIcon" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\$progId\DefaultIcon" -Name "(Default)" -Value "$iconPath,0"

New-Item -Path "HKCU:\Software\Classes\.pnp" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\.pnp" -Name "(Default)" -Value $progId

# Tell Explorer to refresh its icon cache/associations now, instead of waiting for a restart.
Add-Type -Namespace Shell32 -Name NativeMethods -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
"@
$SHCNE_ASSOCCHANGED = 0x08000000
$SHCNF_IDLIST = 0x0000
[Shell32.NativeMethods]::SHChangeNotify($SHCNE_ASSOCCHANGED, $SHCNF_IDLIST, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Output "Registered '$progId' as the icon handler for .pnp, using $iconPath"
Write-Output "If existing .pnp files don't update immediately, restarting Explorer (or signing out/in) refreshes the icon cache."

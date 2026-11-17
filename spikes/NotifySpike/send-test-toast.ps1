<#
.SYNOPSIS  Shows a test toast from Windows PowerShell's own AUMID (run with powershell.exe 5.1 or pwsh? -> use powershell.exe: WinRT projection).
  Usage: powershell.exe -NoProfile -ExecutionPolicy Bypass -File send-test-toast.ps1 -Text "NothingShell spike test 12:00:00 x" [-Suppress]
  -Suppress sets SuppressPopup (no banner; goes straight to the Action Center). Test toasts only.
#>
param([string]$Text, [switch]$Suppress)
[Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime] | Out-Null
$x = New-Object Windows.Data.Xml.Dom.XmlDocument
$x.LoadXml("<toast><visual><binding template=`"ToastGeneric`"><text>$Text</text><text>body line two</text></binding></visual></toast>")
$t = [Windows.UI.Notifications.ToastNotification]::new($x)
$t.Tag = 'nsspike'; $t.Group = 'nsspike'
if ($Suppress) { $t.SuppressPopup = $true }
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe').Show($t)
"shown at $(Get-Date -Format HH:mm:ss.fff)"

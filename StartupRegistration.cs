using System.IO;
using System.Security;
using Microsoft.Win32;

namespace DesktopCarousel;

/// <summary>注册登录自启动（HKCU Run，无需管理员权限，删除即撤销）。</summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopCarousel";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string command && command.Length > 0;
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException("无法打开启动项注册表键");
        if (enabled)
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定程序路径");
            key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }

    /// <summary>命令行入口，供安装脚本和验证使用：--startup enable|disable|status。</summary>
    public static bool TryHandleCommand(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length < 2 || !string.Equals(args[0], "--startup", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            switch (args[1].ToLowerInvariant())
            {
                case "enable":
                    SetEnabled(true);
                    break;
                case "disable":
                    SetEnabled(false);
                    break;
                case "status":
                    exitCode = IsEnabled() ? 0 : 1;
                    break;
                default:
                    exitCode = 2;
                    break;
            }
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            exitCode = 3;
        }

        return true;
    }
}

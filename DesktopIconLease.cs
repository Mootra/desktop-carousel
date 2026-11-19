using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace DesktopCarousel;

internal sealed class DesktopIconLease(DesktopSurface surface, Process restorer) : IDisposable
{
    private const string RestoreMode = "--restore-desktop-icons";
    private const char Armed = 'H';
    private const char Disarmed = 'D';
    private bool _hidden;

    public DesktopSurface Surface { get; } = surface;
    public bool RestorerRunning => !restorer.HasExited;

    public static async Task<DesktopIconLease?> StartAsync(DesktopSurface surface, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "DesktopCarousel.dll"));
        }
        info.ArgumentList.Add(RestoreMode);
        info.ArgumentList.Add(surface.Icons.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(surface.ExplorerProcessId.ToString(CultureInfo.InvariantCulture));

        Process? process = null;
        try
        {
            process = Process.Start(info);
            if (process is not null && await process.StandardOutput.ReadLineAsync(cancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(4), cancellationToken) == "ready")
            {
                var lease = new DesktopIconLease(surface, process);
                process = null;
                return lease;
            }
        }
        catch (Exception error) when (error is Win32Exception or IOException or TimeoutException or OperationCanceledException)
        {
            // The desktop remains visible if its recovery process cannot start.
        }
        finally
        {
            if (process is not null)
            {
                ClosePipe(process);
            }
        }
        return null;
    }

    public bool Hide()
    {
        if (!RestorerRunning)
        {
            return false;
        }
        if (!_hidden)
        {
            try
            {
                restorer.StandardInput.Write(Armed);
                restorer.StandardInput.Flush();
            }
            catch (IOException)
            {
                return false;
            }
        }
        _hidden |= DesktopShell.SetIconVisibility(Surface, false);
        return _hidden;
    }

    public void Dispose()
    {
        if (_hidden && !DesktopShell.SetIconVisibility(Surface, true))
        {
            ClosePipe(restorer, disarm: false);
            return;
        }
        ClosePipe(restorer);
    }

    public static bool RunRestorer(string[] args)
    {
        if (args is not [RestoreMode, var iconArgument, var processArgument] ||
            !nint.TryParse(iconArgument, CultureInfo.InvariantCulture, out nint icons) ||
            !uint.TryParse(processArgument, CultureInfo.InvariantCulture, out uint processId))
        {
            return false;
        }

        // The OS closes the inherited pipe on termination, including a forced exit.
        Console.WriteLine("ready");
        if (Console.Read() == Armed && Console.Read() != Disarmed)
        {
            DesktopShell.SetIconVisibility(new DesktopSurface(0, icons, processId), true);
        }
        return true;
    }

    private static void ClosePipe(Process process, bool disarm = true)
    {
        try
        {
            var input = process.StandardInput;
            if (disarm)
            {
                input.Write(Disarmed);
                input.Flush();
            }
            input.Close();
        }
        catch (IOException)
        {
            // The recovery process has already closed its end of the pipe.
        }
        finally
        {
            process.Dispose();
        }
    }
}

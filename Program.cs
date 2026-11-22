using System.Runtime.CompilerServices;

namespace DesktopCarousel;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => StartupRegistration.TryHandleCommand(args, out int exitCode)
        ? exitCode
        : DesktopIconLease.RunRestorer(args) ? 0 : RunWidget();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunWidget()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}

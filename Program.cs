using System.Runtime.CompilerServices;

namespace DesktopCarousel;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => DesktopIconLease.RunRestorer(args) ? 0 : RunWidget();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunWidget()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}

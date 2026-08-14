using System.Windows;

namespace DvsViewer;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? file = null;
        if (args.Length > 0)
        {
            if (args[0].Equals("open", StringComparison.OrdinalIgnoreCase))
                file = args.Length > 1 ? args[1] : null;
            else
                file = args[0];
        }

        var app = new App();
        var window = new MainWindow(file);
        app.Run(window);
        return 0;
    }
}
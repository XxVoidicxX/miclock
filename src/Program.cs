namespace MicLock;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\MicLock.SingleInstance", out bool first);
        if (!first) return;

        ApplicationConfiguration.Initialize();

        if (args.Length == 2 && args[0] == "--selftest")
        {
            var app = new TrayApp();
            File.WriteAllText(args[1], app.SelfTest());
            app.Shutdown();
            return;
        }

        Application.Run(new TrayApp());
    }
}

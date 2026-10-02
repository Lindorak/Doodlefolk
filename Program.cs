namespace StickFight;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "StickFight.SingleInstance", out bool first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        using var app = new App(args);
        Application.Run(app);
        World.Log("message loop ended");
    }
}

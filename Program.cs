namespace StickFight;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "StickFight.SingleInstance", out bool first);
        // Started by an install or an update: wait for the old copy to finish closing.
        if (!first && args.Contains("--wait"))
        {
            try { first = mutex.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { first = true; }
        }
        if (args.Contains("--uninstall"))
        {
            ApplicationConfiguration.Initialize();
            if (!first) { MessageBox.Show("Please quit StickFight first (Studio → Settings → Quit), then try again.", "StickFight"); return; }
            App.Uninstall();
            return;
        }
        if (!first) return;
        ApplicationConfiguration.Initialize();
        using var app = new App(args);
        Application.Run(app);
        World.Log("message loop ended");
    }
}

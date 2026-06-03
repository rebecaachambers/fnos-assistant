using System.Windows;

namespace FnosAssistant;

public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        // Clean temp files on exit
        try
        {
            var tempPaths = new[]
            {
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FnosAssistant"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "FnosAssistant")
            };
            foreach (var path in tempPaths)
            {
                if (System.IO.Directory.Exists(path))
                    System.IO.Directory.Delete(path, true);
            }
        }
        catch { }

        base.OnExit(e);
    }
}

namespace MSlide;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        // Files dropped onto mSlide.exe itself arrive as command-line arguments.
        if (args.Length > 0)
            form.Shown += (_, _) => form.AddFiles(args, insertAt: 0);
        Application.Run(form);
    }
}

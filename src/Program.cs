using System;
using System.Threading;
using System.Windows.Forms;

namespace CodingEffect;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--bench")
        {
            ApplicationConfiguration.Initialize();
            double intensity = args.Length >= 3 && double.TryParse(args[2], out var v) ? v : 1.0;
            Bench.Run(args[1], intensity);
            return;
        }

        using var mutex = new Mutex(true, "CodingEffect_SingleInstance", out bool created);
        if (!created) return; // 二重起動しない

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}

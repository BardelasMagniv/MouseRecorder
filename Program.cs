using System;
using System.Windows.Forms;

namespace MouseRecorder
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Quick smoke test helper: dotnet run -- test-csv
            if (args.Length == 1 && args[0] == "test-csv")
            {
                var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MouseRecorder_test");
                System.IO.Directory.CreateDirectory(folder);
                var tmp = System.IO.Path.Combine(folder, $"session_passive_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid()}.tmp");
                var exporter = new CsvExporter(tmp);
                var meta = new SessionMetadata
                {
                    SessionId = Guid.NewGuid(),
                    SessionStartUtc = DateTimeOffset.UtcNow,
                    RecordingMode = "Passive",
                    IsPassive = true,
                    ScreenWidth = 1920,
                    ScreenHeight = 1080,
                    WindowLeft = 0,
                    WindowTop = 0,
                    WindowWidth = 800,
                    WindowHeight = 600,
                    DpiX = 96,
                    DpiY = 96
                };
                exporter.WriteSessionMetadata(meta);
                exporter.AppendEvents(new[] { new MouseEvent { EventType = "test", TsMonotonicUs = 123, TsUtcIso = DateTimeOffset.UtcNow.ToString("o") } });
                exporter.FinalizeAndClose();
                Console.WriteLine($"Wrote: {System.IO.Path.ChangeExtension(tmp, ".csv")}\n");
                Console.WriteLine(System.IO.File.ReadAllText(System.IO.Path.ChangeExtension(tmp, ".csv")));
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}

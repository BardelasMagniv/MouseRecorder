using System.Windows.Forms;

namespace MouseRecorder
{
    internal interface ISpawner
    {
        // spawn with optional safe client rectangle (coordinates relative to playArea client)
        SpawnResult Spawn(Panel playArea, Button target, System.Drawing.Rectangle? safeClientRect = null);
    }
}
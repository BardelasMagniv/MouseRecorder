using System.Windows.Forms;

namespace MouseRecorder
{
    internal interface ISpawner
    {
        SpawnResult Spawn(Panel playArea, Button target);
    }
}

using System.Collections.Generic;
using System.Windows.Forms;

namespace Viggi_Windows_Slideshow
{
    public static class Constants
    {
        //TODO: Figure out a good way to use this for switch checking (i.e. ensure in help dialog)
        public static Dictionary<Keys, string> DefinedActionKeys = new Dictionary<Keys, string>
        {
            [Keys.Return] = "Open",
            [Keys.O] = "Open",
            [Keys.Left] = "Previous",
            [Keys.Up] = "Previous",
            [Keys.B] = "Previous",
            [Keys.Right] = "Next",
            [Keys.Down] = "Next",
            [Keys.N] = "Next",
            [Keys.D] = "Debug",
            [Keys.I] = "Info",
            [Keys.F] = "Filename",
            [Keys.H] = "Help",
            [Keys.S] = "Settings",
            [Keys.R] = "Random",
            [Keys.M] = "Mute",
            [Keys.Q] = "Quit",
            [Keys.D0] = "Interval",
            [Keys.D1] = "Interval",
            [Keys.D2] = "Interval",
            [Keys.D3] = "Interval",
            [Keys.D4] = "Interval",
            [Keys.D5] = "Interval",
            [Keys.D6] = "Interval",
            [Keys.D7] = "Interval",
            [Keys.D8] = "Interval",
            [Keys.D9] = "Interval"
        };
    }
}

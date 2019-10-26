using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Viggi_Windows_Slideshow
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length > 0)
            {
                if (args[0].ToLower().Trim().Substring(0, 2) == "/s") //show
                {
                    //show the screen saver
                    ShowScreenSaver();
                    Application.Run();
                }
                else if (args[0].ToLower().Trim().Substring(0, 2) == "/p") //preview
                {
                    //preview the screen saver
                    //args[1] is the handle to the preview window

                    Application.Run(new MainForm(new IntPtr(long.Parse(args[1]))));
                }
                else if (args[0].ToLower().Trim().Substring(0, 2) == "/c") //configure
                {
                    //configure the screen saver
                    Application.Run(new ConfigureForm());
                }
                else
                {
                    // an argument was passed, but it wasn't /s, /p, or /c
                    //show the screen saver anyway
                    ShowScreenSaver();
                    Application.Run();
                }
            }
            else
            {
                //no arguments were passed (we should probably show the screen saver)
                ShowScreenSaver();
                Application.Run();
            }
        }

        static void ShowScreenSaver()
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                MainForm screensaver = new MainForm(screen.Bounds);
                screensaver.Show();
            }
        }
    }
}

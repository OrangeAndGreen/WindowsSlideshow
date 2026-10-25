using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Win32;

namespace Viggi_Windows_Slideshow
{
    public class ConfigFile
    {
        public const string ConfigPath = "ViggiSlideshowSettings.txt";
        public const int DefaultIntervalMS = 5000;
        private const string DefaultPicDirectory = @"E:\Pictures\For Slideshow";
        private const string RegistryKey = @"SOFTWARE\ViggiSlideshow";

        private const string DirectoryString = "Directory";
        private const string LabelString = "Label";
        private const string RandomString = "Random";
        private const string InfoString = "Info";
        private const string IntervalString = "Interval";
        private const string MuteVideosString = "Mute";
        private const string VideosOnlyString = "VideosOnly";

        public string Path { get; private set; }

        public string PicDirectory { get; set; }
        public bool LabelPics { get; set; }
        public bool Info { get; set; }
        public bool RandomOrder { get; set; }
        public int IntervalMS { get; set; }
        public bool MuteVideos { get; set; }
        public bool VideosOnly { get; set; }

        public static ConfigFile Load(string path)
        {
            return new ConfigFile(path);
        }

        public ConfigFile() : this(null)
        { }

        public ConfigFile(string path)
        {
            LoadFromFile(path);
        }

        private void Reset()
        {
            Path = "";
            PicDirectory = DefaultPicDirectory;
            LabelPics = false;
            Info = false;
            RandomOrder = true;
            IntervalMS = DefaultIntervalMS;
            MuteVideos = false;
            VideosOnly = false;
        }

        public void LoadFromFile(string path)
        {
            Reset();

            if (path != null)
            {
                Path = path;
            }

            try
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKey);
                if (key != null)
                {
                    PicDirectory = (string)key.GetValue(DirectoryString);
                    LabelPics = (string)key.GetValue(LabelString) == "True";
                    Info = (string)key.GetValue(InfoString) == "True";
                    RandomOrder = (string)key.GetValue(RandomString) == "True";
                    IntervalMS = (int)key.GetValue(IntervalString);
                    MuteVideos = (string)key.GetValue(MuteVideosString) == "True";
                    VideosOnly = (string)key.GetValue(VideosOnlyString) == "True";
                }

                //using (StreamReader reader = new StreamReader(path))
                //{
                //    string[] lines = reader.ReadToEnd().Split('\n');
                //    foreach (string line in lines)
                //    {
                //        int splitter = line.IndexOf('=');
                //        if (splitter > 0)
                //        {
                //            string setting = line.Substring(0, splitter).Trim();
                //            string val = line.Substring(splitter + 1).Trim();

                //            if (setting == DirectoryString)
                //            {
                //                PicDirectory = val;
                //            }
                //            else if (setting == LabelString)
                //            {
                //                LabelPics = val.ToLower() == "true" || val == "1";
                //            }
                //            else if (setting == RandomString)
                //            {
                //                RandomOrder = val.ToLower() == "true" || val == "1";
                //            }
                //            else if (setting == IntervalString)
                //            {
                //                int interval = DefaultIntervalMS;
                //                int.TryParse(val, out interval);
                //                IntervalMS = interval;
                //            }
                //        }
                //    }
                //}
            }
            catch (Exception)
            {
                Console.WriteLine("Could not read config file: " + this.Path);
            }
        }

        public void Save()
        {
            RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKey);
            key.SetValue(DirectoryString, PicDirectory);
            key.SetValue(LabelString, LabelPics);
            key.SetValue(InfoString, Info);
            key.SetValue(RandomString, RandomOrder);
            key.SetValue(IntervalString, IntervalMS);
            key.SetValue(MuteVideosString, MuteVideos);
            key.SetValue(VideosOnlyString, VideosOnly);

            //using (StreamWriter writer = new StreamWriter(Path))
            //{
            //    writer.WriteLine(string.Format("{0}={1}", DirectoryString, PicDirectory));
            //    writer.WriteLine(string.Format("{0}={1}", LabelString, LabelPics));
            //    writer.WriteLine(string.Format("{0}={1}", RandomString, RandomOrder));
            //    writer.WriteLine(string.Format("{0}={1}", IntervalString, IntervalMS));
            //}
        }
    }
}

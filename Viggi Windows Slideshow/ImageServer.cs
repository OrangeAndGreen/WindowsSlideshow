using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Viggi_Windows_Slideshow
{
    public class ImageServer
    {
        static ImageServer mServer = null;

        string mDirectory { get; set; }
        bool mRandomOrder { get; set; }

        Thread mThread = null;
        bool mQuitter = false;
        Random mRandom = new Random(DateTime.Now.Millisecond);
        int mLastIndex = -1;

        List<string> mImages = new List<string>();
        List<string> mVideos = new List<string>();
        List<string> mDirectoriesToSearch = new List<string>();

        private ImageServer()
        {
            mThread = new Thread(WorkerLoop);
            mThread.Start();
        }

        private void WorkerLoop()
        {
            //Load images in the background
            string lastDirectory = null;
            while(true)
            {
                if (mQuitter)
                {
                    break;
                }
                else if (mDirectory == null)
                {
                    Thread.Sleep(100);
                }
                else
                {
                    if (mDirectory != lastDirectory)
                    {
                        mImages.Clear();
                        mVideos.Clear();
                        mDirectoriesToSearch.Clear();
                        mDirectoriesToSearch.Add(mDirectory);
                        lastDirectory = mDirectory;
                    }

                    if(mDirectoriesToSearch.Count == 0)
                    {
                        Thread.Sleep(250);
                    }
                    else
                    {
                        int directoryIndex = mRandomOrder ? mRandom.Next(mDirectoriesToSearch.Count) : 0;
                        string directory = mDirectoriesToSearch[directoryIndex];
                        mDirectoriesToSearch.RemoveAt(directoryIndex);
                        Console.WriteLine($"{DateTime.Now}: Loading directory {directory}");

                        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                        {
                            bool isVideo = IsVideo(file);
                            if (IsImage(file) || isVideo)
                                mImages.Add(file);

                            if (isVideo)
                                mVideos.Add(file);
                        }

                        foreach (string subDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                        {
                            mDirectoriesToSearch.Add(subDirectory);
                        }
                    }
                }
            }
        }

        public static void SetDirectory(string directory, bool randomOrder)
        {
            if(mServer == null)
            {
                mServer = new ImageServer();
            }

            mServer.mDirectory = directory;
            mServer.mRandomOrder = randomOrder;
        }

        public static int NumImages => mServer.mImages.Count;
        public static int NumVideos => mServer.mVideos.Count;

        public static int GetNextFileIndex(bool videosOnly)
        {
            if(mServer == null)
            {
                return -1;
            }

            List<string> source = videosOnly ? mServer.mVideos : mServer.mImages;

            while(source.Count == 0)
            {
                Thread.Sleep(10);
            }

            int index = mServer.mRandomOrder ? mServer.mRandom.Next(source.Count) : ((mServer.mLastIndex + 1) % source.Count);
            mServer.mLastIndex = index;
            return index;
        }

        public static string ImageForIndex(int index, bool videosOnly)
        {
            List<string> source = videosOnly ? mServer.mVideos : mServer.mImages;
            return source[index % source.Count];
        }

        public static void Shutdown()
        {
            if (mServer != null)
            {
                mServer.mQuitter = true;

                mServer.mThread.Join();
                mServer.mThread = null;

                mServer = null;
            }
        }

        private static bool IsImage(string path)
        {
            string extension = path.Substring(path.LastIndexOf('.') + 1).ToLower();
            return extension == "jpg" || extension == "jpeg" || extension == "bmp" || extension == "png" || extension == "gif";
        }

        private static bool IsVideo(string path)
        {
            string extension = path.Substring(path.LastIndexOf('.') + 1).ToLower();
            return extension == "mpg" || extension == "mpeg" || extension == "mov" || extension == "mp4" || extension == "avi";
        }
    }
}

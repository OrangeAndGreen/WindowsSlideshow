using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Text.RegularExpressions;
using System.Drawing.Imaging;
using System.Diagnostics;

namespace Viggi_Windows_Slideshow
{
    public partial class MainForm : Form
    {
        private const int WakeMovementPixels = 20;
        private const int HistoryLength = 1000;

        private ConfigFile mConfig = null;
        private List<string> mImages = null;
        private int mPicIndex = 0;
        private Image mCurImage = null;

        private bool mPlayingVideo = false;

        private System.Timers.Timer mTimer = null;
        private Random mRandom = null;

        private string mMessage = null;
        private string mMessageLock = "messageLock";
        private int mLabelCorner = 0;

        private bool mShowDebugInfo = false;
        private bool mShowHelp = false;

        private List<int> mHistory = new List<int>();
        private int mHistoryIndex = -1;

        private bool IsPreviewMode = false;
        Point OriginalLocation = new Point(int.MaxValue, int.MaxValue);

        private string mErrorMessages = "";

        #region Preview API's

        [DllImport("user32.dll")]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out Rectangle lpRect);

        #endregion

        #region Constructors

        public MainForm(Rectangle Bounds)
        {
            InitializeComponent();
            this.Bounds = Bounds;
            this.mainImage.Bounds = Bounds;
            Cursor.Hide();

            RunScreensaver();
        }

        //This constructor is the handle to the select screen saver dialog preview window. It is used when in preview mode (/p)
        public MainForm(IntPtr PreviewHandle)
        {
            InitializeComponent();

            //set the preview window as the parent of this window
            SetParent(this.Handle, PreviewHandle);

            //make this a child window, so when the select screensaver dialog closes, this will also close
            SetWindowLong(this.Handle, -16, new IntPtr(GetWindowLong(this.Handle, -16) | 0x40000000));

            //set our window's size to the size of our window's new parent
            Rectangle ParentRect;
            GetClientRect(PreviewHandle, out ParentRect);
            this.Size = ParentRect.Size;
            this.mainImage.Size = ParentRect.Size;

            //set our location at (0, 0)
            this.Location = new Point(0, 0);

            IsPreviewMode = true;

            RunScreensaver();
        }

        #endregion

        private void RunScreensaver()
        {
            mConfig = ConfigFile.Load(ConfigFile.ConfigPath);

            //Build the image list
            mImages = new List<string>();
            foreach (string file in Directory.EnumerateFiles(mConfig.PicDirectory, "*", SearchOption.AllDirectories))
            {
                if(IsImage(file) || IsVideo(file))
                    mImages.Add(file);
            }

            //Prepare the timer
            mTimer = new System.Timers.Timer(mConfig.IntervalMS);
            mTimer.Elapsed += OnTimedEvent;
            mTimer.AutoReset = true;
            mTimer.Enabled = true;
            
            LoadNextImage(1);
        }

        private void OnTimedEvent(Object source, System.Timers.ElapsedEventArgs e)
        {
            //Console.WriteLine("The Elapsed event was raised at {0}", e.SignalTime);

            LoadNextImage(1);
        }

        private void UpdateImageIndex(int direction)
        {
            try
            {
                //First, update the index in the history (if necessary)
                if (direction > 0)
                {
                    //Go forward
                    if (mHistoryIndex >= 0)
                        mHistoryIndex--; //might go to -1, meaning we're out of the history
                    //else we're not in the history
                }
                else if (direction < 0)
                {
                    //Go back
                    //The first time we go back we'll jump twice to go back
                    if (mHistoryIndex < 0)
                        mHistoryIndex++;
                    mHistoryIndex++;

                    //Don't let the history index get larger than the history
                    if (mHistoryIndex >= mHistory.Count)
                        mHistoryIndex = mHistory.Count - 1;
                }

                if (mHistoryIndex < 0)
                {
                    //Load a new image (increment or random)
                    if (mConfig.RandomOrder && mImages.Count > 0)
                    {
                        if (mRandom == null)
                        {
                            mRandom = new Random();
                        }

                        mPicIndex = mRandom.Next(0, mImages.Count - 1);
                    }
                    else
                    {
                        mPicIndex++;
                    }

                    //Update the history buffer
                    mHistory.Insert(0, mPicIndex);
                    if (mHistory.Count > HistoryLength)
                    {
                        //Remove the last entry to stay within the desired history length
                        mHistory.RemoveAt(mHistory.Count - 1);
                    }
                    SaveHistory();
                }
                else
                {
                    //Go to the image in the history
                    mPicIndex = mHistory[mHistoryIndex];
                }

                if (mPicIndex >= mImages.Count)
                {
                    mPicIndex = 0;
                }
            }
            catch (Exception e)
            {
                LogError("Error in UpdateImageIndex", e);
                Finish();
            }
        }

        private void LoadNextImage(int direction)
        {
            try
            {
                //Reset the timer
                mTimer.Interval = mConfig.IntervalMS;

                if (direction != 0)
                {
                    mPlayingVideo = false;
                    UpdateImageIndex(direction);

                    mLabelCorner = (mLabelCorner + 1) % 4;
                }

                if (mPicIndex < mImages.Count)
                {
                    mCurImage = null;

                    if (IsImage(mImages[mPicIndex]))
                    {
                        SetVisibilityForImage();

                        try
                        {
                            mCurImage = Image.FromFile(mImages[mPicIndex]);

                            int rotation = GetOrientationFromImage(mImages[mPicIndex]);
                            switch (rotation)
                            {
                                case 3:
                                    mCurImage.RotateFlip(RotateFlipType.Rotate180FlipNone);
                                    break;
                                case 6:
                                    mCurImage.RotateFlip(RotateFlipType.Rotate90FlipNone);
                                    break;
                                case 8:
                                    mCurImage.RotateFlip(RotateFlipType.Rotate270FlipNone);
                                    break;
                            }

                            mainImage.Image = mCurImage;

                            mTimer.Enabled = true;
                        }
                        catch (Exception)
                        {
                            lock (mMessageLock)
                            {
                                mMessage = "*** ERROR LOADING IMAGE *** " + mImages[mPicIndex];
                                LogError(mMessage);
                            }
                        }
                    }
                    else if(IsVideo(mImages[mPicIndex]) && !mPlayingVideo)
                    {
                        mPlayingVideo = true;
                        PlayVideo();
                    }

                    UpdateMessages();
                }
                else
                {
                    LogError("No images available");
                    Finish();
                }
            }
            catch (Exception e)
            {
                LogError("Error in LoadNextImage", e);
                Finish();
            }
        }

        public void SetVisibilityForImage()
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action(SetVisibilityForImage));
                return;
            }

            mediaPlayer.Visible = false;
            mainImage.Visible = true;
        }

        public void PlayVideo()
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action(PlayVideo));
                return;
            }

            mediaPlayer.Visible = true;
            mainImage.Visible = false;

            mTimer.Enabled = false;

            mediaPlayer.URL = mImages[mPicIndex];
        }

        public void UpdateMessages()
        {
            if (IsPreviewMode)
            {
                messageLabel.Visible = false;
                infoLabel.Visible = false;
                return;
            }

            if (InvokeRequired)
            {
                this.Invoke(new Action(UpdateMessages));
                return;
            }

            try
            {
                //Build the info string
                string info = "";

                if (mConfig.LabelPics || mConfig.Info)
                    info += mImages[mPicIndex];

                if (mConfig.Info)
                {
                    if (info.Length > 0)
                        info += "\n\n";
                    info += GetFileInfo(mImages[mPicIndex], mCurImage);
                }

                if (mShowDebugInfo)
                {
                    if (info.Length > 0)
                        info += "\n\n";
                    info += GetDebugString(GetOrientationFromImage(mImages[mPicIndex]));
                }

                if (mShowHelp)
                {
                    if (info.Length > 0)
                        info += "\n\n";
                    info += GetHelpString();
                }

                lock (mMessageLock)
                {
                    //Show and update the info label if necessary
                    infoLabel.Visible = info.Length > 0;
                    if (info.Length > 0)
                        infoLabel.Text = info;

                    //Show and update the message label if necessary
                    messageLabel.Visible = mMessage != null;
                    if (mMessage != null)
                    {
                        messageLabel.Text = mMessage;
                        mMessage = null;
                    }
                }

                ChangeLabelLocation();
            }
            catch (Exception e)
            {
                LogError("Error in UpdateMessages", e);
                Finish();
            }
        }

        private void ChangeLabelLocation()
        {
            if (InvokeRequired)
            {
                this.Invoke(new Action(ChangeLabelLocation));
                return;
            }

            try
            {
                //Move the label to a different corner each time
                int padding = 10;
                switch (mLabelCorner)
                {
                    case 1:
                        //Bottom-left
                        infoLabel.Bounds = new Rectangle(this.Bounds.Left + padding, this.Bounds.Bottom - infoLabel.Height - padding, infoLabel.Width, infoLabel.Height);
                        break;
                    case 2:
                        //Bottom-right
                        infoLabel.Bounds = new Rectangle(this.Bounds.Right - infoLabel.Width - padding, this.Bounds.Bottom - infoLabel.Height - padding, infoLabel.Width, infoLabel.Height);
                        break;
                    case 3:
                        //Top-right
                        infoLabel.Bounds = new Rectangle(this.Bounds.Right - infoLabel.Width - padding, this.Bounds.Top + padding, infoLabel.Width, infoLabel.Height);
                        break;
                    default:
                        //Top-left
                        infoLabel.Bounds = new Rectangle(this.Bounds.Left + padding, this.Bounds.Top + padding, infoLabel.Width, infoLabel.Height);
                        break;
                }
            }
            catch (Exception e)
            {
                LogError("Error in ChangeLabelLocation", e);
                Finish();
            }
        }

        private string GetDebugString(int rotation)
        {
            return "Debug Info:\n"
                 + string.Format("Pic: index {0} of {1}\n", mPicIndex, mImages.Count)
                 + string.Format("History: index {0} of {1}\n", mHistoryIndex, mHistory.Count)
                 + string.Format("Interval: {0} ms\n", mConfig.IntervalMS)
                 + string.Format("Rotation: {0}", rotation);
        }

        private string GetHelpString()
        {
            return "Keyboard Commands:\n"
                 + "Left/Up/B:         Go to previous image\n"
                 + "Right/Down/N: Go to next image\n"
                 + "F:                       Show/hide filename\n"
                 + "I:                        Show/hide image info\n"
                 + "D:                      Show/hide debug info\n"
                 + "H:                      Show/hide help\n"
                 + "Return:              Exit and open folder for current image in Windows Explorer\n"
                 + "Numbers:           Change image interval\n"
                 + "                               1-5: 1-5 seconds\n"
                 + "                               6/7/8/9: 10/20/30/60 seconds\n"
                 + "                               0: Default interval";
        }

        private string GetFileInfo(string path, Image image)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                
                string sizeString = "";
                if (image != null)
                {
                    sizeString = string.Format("{0} x {1} ", image.Width, image.Height);
                }

                return string.Format("Size:                 {0}({1:0.0} kB)\n", sizeString, (double)info.Length / 1024)
                     + string.Format("Taken:             {0}\n", GetDateTakenFromImage(path))
                     + string.Format("Camera:           {0}\n", GetCameraInfoFromImage(path))
                     + string.Format("Pics in Folder:  {0}", info.Directory.EnumerateFiles().Count());
            }
            catch (Exception e)
            {
                LogError("Error getting file info", e);
                return "(error) " + e.Message;
            }
        }

        #region Image Info Methods

        //Came from here:
        //http://www.d2soft.com/Blog/Post.aspx?id=156

        private static Regex r = new Regex(":");

        public string GetDateTakenFromImage(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (Image myImage = Image.FromStream(fs, false, false))
                {
                    PropertyItem propItem = myImage.GetPropertyItem(36867);
                    string dateTaken = r.Replace(Encoding.UTF8.GetString(propItem.Value), "-", 2);

                    dateTaken = RemoveTrailingZeroByte(dateTaken);

                    return dateTaken.Trim();
                }
            }
            catch (Exception e)
            {
                LogError("Error getting image date", e);
                return "?";
            }
        }

        public string GetCameraInfoFromImage(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (Image myImage = Image.FromStream(fs, false, false))
                {

                    PropertyItem propItem = myImage.GetPropertyItem(0x010F);
                    string cameraBrand = RemoveTrailingZeroByte(r.Replace(Encoding.UTF8.GetString(propItem.Value), "-", 2).Trim());

                    propItem = myImage.GetPropertyItem(0x0110);
                    string cameraModel = RemoveTrailingZeroByte(r.Replace(Encoding.UTF8.GetString(propItem.Value), "-", 2).Trim());
                    return string.Format("{0}: {1}", cameraBrand, cameraModel);
                }
            }
            catch (Exception e)
            {
                LogError("Error getting camera info from image", e);
                return "?";
            }
        }

        public int GetOrientationFromImage(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (Image myImage = Image.FromStream(fs, false, false))
                {
                    foreach (PropertyItem prop in myImage.PropertyItems)
                    {
                        //Link for orientation info:
                        //http://www.impulseadventure.com/photo/exif-orientation.html
                        //1 = Normal orientation
                        //3 = Need to rotate 180
                        //6 = Need to rotate 90 clockwise
                        //8 = Need to rotate 90 counter-clockwise

                        if (prop.Id == 0x112 || prop.Id == 0x5029)
                        {
                            int code = BitConverter.ToInt16(prop.Value, 0);

                            return code;
                        }
                    }
                }

                return -1;
            }
            catch (Exception e)
            {
                LogError("Error getting orientation info from image", e);
                return -2;
            }
        }

        private static string RemoveTrailingZeroByte(string str)
        {
            if (str[str.Length - 1] == 0)
                str = str.Substring(0, str.Length - 1);

            return str;
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
        
        #endregion

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            HandleKeyPress(e.KeyCode);
        }

        private void mediaPlayer_KeyDownEvent(object sender, AxWMPLib._WMPOCXEvents_KeyDownEvent e)
        {
            HandleKeyPress((Keys)e.nKeyCode);
        }

        private void HandleKeyPress(Keys keyCode)
        {
            //mMessage = string.Format("Key pressed: {0}", e.KeyCode);
            try
            {
                int nextDirection = 0;
                switch (keyCode)
                {
                    case Keys.Return:
                        {
                            //Open Explorer at current image and exit
                            FileInfo info = new FileInfo(mImages[mPicIndex]);
                            Process.Start(info.DirectoryName);

                            if (!IsPreviewMode)
                            {
                                Finish();
                            }
                            break;
                        }
                    case Keys.Left:
                    case Keys.Up:
                    case Keys.B:
                        {
                            //Go Back
                            nextDirection = -1;
                            break;
                        }
                    case Keys.Right:
                    case Keys.Down:
                    case Keys.N:
                        {
                            //Go Forward
                            nextDirection = 1;
                            break;
                        }
                    case Keys.D:
                        {
                            //Toggle Debug Info
                            mShowDebugInfo = !mShowDebugInfo;
                            break;
                        }
                    case Keys.I:
                        {
                            //Toggle Image Info
                            mConfig.Info = !mConfig.Info;
                            mConfig.Save();
                            break;
                        }
                    case Keys.F:
                        {
                            //Toggle Labelling
                            mConfig.LabelPics = !mConfig.LabelPics;
                            mConfig.Save();
                            break;
                        }
                    case Keys.H:
                        {
                            mShowHelp = !mShowHelp;
                            break;
                        }
                    case Keys.R:
                        {
                            //Toggle Random
                            mConfig.RandomOrder = !mConfig.RandomOrder;
                            mConfig.Save();
                            break;
                        }
                    case Keys.Q:
                        {
                            Finish();
                            break;
                        }
                    case Keys.D0:
                    case Keys.D1:
                    case Keys.D2:
                    case Keys.D3:
                    case Keys.D4:
                    case Keys.D5:
                    case Keys.D6:
                    case Keys.D7:
                    case Keys.D8:
                    case Keys.D9:
                        {
                            //Interval Shortcuts
                            ChangeInterval(keyCode);
                            mConfig.Save();
                            break;
                        }
                    default:
                        {
                            //if (!IsPreviewMode)
                            //{
                            //    Finish();
                            //}
                            break;
                        }
                }

                LoadNextImage(nextDirection);
            }
            catch (Exception e2)
            {
                LogError("Error in MainForm_KeyDown", e2);
                Finish();
            }
        }

        private void ChangeInterval(Keys shortcut)
        {
            string defaultString = "";
            switch (shortcut)
            {
                case Keys.D1:
                    {
                        mConfig.IntervalMS = 1000;
                        break;
                    }
                case Keys.D2:
                    {
                        mConfig.IntervalMS = 2000;
                        break;
                    }
                case Keys.D3:
                    {
                        mConfig.IntervalMS = 3000;
                        break;
                    }
                case Keys.D4:
                    {
                        mConfig.IntervalMS = 4000;
                        break;
                    }
                case Keys.D5:
                    {
                        mConfig.IntervalMS = 5000;
                        break;
                    }
                case Keys.D6:
                    {
                        mConfig.IntervalMS = 10000;
                        break;
                    }
                case Keys.D7:
                    {
                        mConfig.IntervalMS = 20000;
                        break;
                    }
                case Keys.D8:
                    {
                        mConfig.IntervalMS = 30000;
                        break;
                    }
                case Keys.D9:
                    {
                        mConfig.IntervalMS = 60000;
                        break;
                    }
                case Keys.D0:
                default:
                    {
                        mConfig.IntervalMS = ConfigFile.DefaultIntervalMS;
                        defaultString = " (default)";
                        break;
                    }
            }

            lock (mMessageLock)
            {
                mMessage = string.Format("Interval: {0} ms{1}", mConfig.IntervalMS, defaultString);
            }
        }

        private void SaveHistory()
        {
            try
            {
                if (mHistory.Count > 0)
                {
                    using (TextWriter writer = new StreamWriter(@"C:\Users\dave\Desktop\screensaver history.txt"))
                    {
                        foreach (int fileIndex in mHistory)
                        {
                            writer.WriteLine(mImages[fileIndex]);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogError("SaveHistory", e);
            }
        }

        private void LogError(string message)
        {
            mErrorMessages += DateTime.Now.ToString() + ": " + message + "\n";
        }

        private void LogError(string message, Exception e)
        {
            LogError(message + ": " + e.Message);
        }

        private void mediaPlayer_PlayStateChange(object sender, AxWMPLib._WMPOCXEvents_PlayStateChangeEvent e)
        {
            //The other possible states are defined here:
            //https://msdn.microsoft.com/en-us/library/windows/desktop/dd562460(v=vs.85).aspx

            if (e.newState == 1)
            {
                LoadNextImage(1);
            }
        }

        #region Exit Points

        private void MainForm_Click(object sender, EventArgs e)
        {
            if (!IsPreviewMode)
            {
                Finish();
            }
        }

        private void MainForm_MouseMove(object sender, MouseEventArgs e)
        {
            HandleMouseMove(e.Location);
        }

        private void mediaPlayer_ClickEvent(object sender, AxWMPLib._WMPOCXEvents_ClickEvent e)
        {
            if (!IsPreviewMode)
            {
                Finish();
            }
        }

        

        private void mediaPlayer_MouseMoveEvent(object sender, AxWMPLib._WMPOCXEvents_MouseMoveEvent e)
        {
            HandleMouseMove(new Point(e.fX, e.fY));
        }

        private void HandleMouseMove(Point location)
        {
            if (!IsPreviewMode)
            {
                //see if originallocation has been set
                if (OriginalLocation.X == int.MaxValue && OriginalLocation.Y == int.MaxValue)
                {
                    OriginalLocation = location;
                }
                //see if the mouse has moved more than WakeMovementPixels pixels in any direction. If it has, close the application.
                if (Math.Abs(location.X - OriginalLocation.X) > WakeMovementPixels || Math.Abs(location.Y - OriginalLocation.Y) > WakeMovementPixels)
                {
                    Finish();
                }
            }
        }

        private void Finish()
        {
            try
            {
                if (mErrorMessages.Length > 0)
                {
                    using (TextWriter writer = new StreamWriter(@"C:\Users\dave\Desktop\screensaver error.txt",true))
                    {
                        writer.WriteLine(mErrorMessages);
                    }
                }
            }
            catch (Exception) { }

            Application.Exit();
        }

        #endregion
    }
}

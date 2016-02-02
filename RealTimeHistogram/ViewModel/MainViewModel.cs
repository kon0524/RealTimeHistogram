using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace RealTimeHistogram.ViewModel
{
    class MainViewModel : ViewModelBase
    {
        // キャプチャイメージ
        private BitmapImage captureImage;
        public BitmapImage CaptureImage
        {
            get { return captureImage; }
            set
            {
                captureImage = value;
                NotifyPropertyChanged("CaptureImage");
            }
        }

        // スクリーンインデックス
        private int screenIndex;
        public int ScreenIndex
        {
            get { return screenIndex; }
            set
            {
                if (value < Screen.AllScreens.Length)
                {
                    screenIndex = value;
                    position = Screen.AllScreens[screenIndex].Bounds;
                    NotifyPropertyChanged("ScreenIndex");
                    NotifyPropertyChanged("PositionX");
                    NotifyPropertyChanged("PositionY");
                    NotifyPropertyChanged("Width");
                }
            }
        }

        // Y軸スケール
        public int ScaleY { get; set; }

        // キャプチャ位置
        private Rectangle position;

        public int PositionX
        {
            get { return position.X - Screen.AllScreens[ScreenIndex].Bounds.X; }
            set
            {
                if (0 <= value && value < Screen.AllScreens[ScreenIndex].Bounds.Width)
                {
                    position.X = value + Screen.AllScreens[ScreenIndex].Bounds.X;
                    position.Width = Screen.AllScreens[ScreenIndex].Bounds.Width - value;
                    position.Height = position.Width / 2;
                    NotifyPropertyChanged("PositionX");
                    NotifyPropertyChanged("Width");
                }
            }
        }

        public int PositionY
        {
            get { return position.Y; }
            set
            {
                if (0 <= value && value < Screen.AllScreens[ScreenIndex].Bounds.Height)
                {
                    position.Y = value;
                    if (position.Y + position.Height <= Screen.AllScreens[ScreenIndex].Bounds.Height)
                    {
                        // Heightは変更しない
                    }
                    else
                    {
                        position.Height = Screen.AllScreens[ScreenIndex].Bounds.Height - position.Y;
                        position.Width = position.Height * 2;
                        NotifyPropertyChanged("Width");
                    }
                    NotifyPropertyChanged("PositionY");
                }
            }
        }

        public int Width
        {
            get { return position.Width; }
            set
            {
                if (0 < value && value <= Screen.AllScreens[ScreenIndex].Bounds.Width - (position.X - Screen.AllScreens[ScreenIndex].Bounds.X))
                {
                    position.Width = value;
                    position.Height = position.Width / 2;
                    NotifyPropertyChanged("Width");
                }
            }
        }

        // 削除
        public ICommand Start { get; set; }

        // コピー
        public ICommand Stop { get; set; }

        // Windowタイトル
        public string WindowTitle { get; private set; }

        // 実行状態
        private bool isExecuting;

        // チャート
        private Chart chart;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="chart"></param>
        public MainViewModel(Chart chart)
        {
            this.chart = chart;

            WindowTitle = "RealTimeHistogram " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion;
            isExecuting = false;
            ScreenIndex = 0;
            ScaleY = 0;
            position = Screen.AllScreens[ScreenIndex].Bounds;
            if (position.Width > position.Height * 2)
            {
                position.Width = position.Height * 2;
            }
            else
            {
                position.Height = position.Width / 2;
            }

            Start = new DelegateCommand(startExecute, canStartExecute);
            Stop = new DelegateCommand(stopExecute, canStopExecute);
        }

        /// <summary>
        /// スタートボタンの実行可否
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>
        private bool canStartExecute(object parameter)
        {
            return !isExecuting;
        }

        /// <summary>
        /// スタートボタン押下
        /// </summary>
        /// <param name="parameter"></param>
        private void startExecute(object parameter)
        {
            isExecuting = true;
            Task.Run(() => 
            {
                Stopwatch sw = new Stopwatch();
                Bitmap screen;
                Graphics g;
                UInt32[] histo;

                while (isExecuting)
                {
                    sw.Start();
                    // スクリーンキャプチャ(70ms)
                    screen = new Bitmap(position.Width, position.Height, PixelFormat.Format32bppArgb);
                    g = Graphics.FromImage(screen);
                    g.CopyFromScreen(position.X, position.Y, 0, 0, screen.Size);
                    g.Dispose();

                    // 輝度計算(25ms)
                    histo = new UInt32[256];
                    BitmapData data = screen.LockBits(new Rectangle(0, 0, screen.Width, screen.Height), ImageLockMode.ReadOnly, screen.PixelFormat);
                    byte[] buf = new byte[data.Stride * screen.Height];
                    Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                    for (int i = 0; i < buf.Length; i += 4)
                    {
                        byte grey = (byte)(0.299 * buf[i] + 0.587 * buf[i + 1] + 0.114 * buf[i + 2]);
                        histo[grey]++;
                    }
                    screen.UnlockBits(data);

                    // チャート表示(0ms)
                    App.Current.Dispatcher.Invoke(() => 
                    {
                        chart.ChartAreas.Clear();
                        chart.ChartAreas.Add("ChartArea1");
                        Series series = new Series();
                        series.ChartType = SeriesChartType.Column;
                        series.MarkerStyle = MarkerStyle.None;
                        for (int i = 0; i < histo.Length; i++)
                        {
                            series.Points.AddXY((double)i, (double)histo[i]);
                        }
                        chart.Series.Clear();
                        chart.Series.Add(series);

                        // X軸の設定
                        chart.ChartAreas["ChartArea1"].AxisX.Minimum = 0;
                        chart.ChartAreas["ChartArea1"].AxisX.Maximum = 255;
                        chart.ChartAreas["ChartArea1"].AxisX.MajorGrid.Enabled = false;

                        // Y軸の設定
                        if (ScaleY != 0)
                        {
                            chart.ChartAreas["ChartArea1"].AxisY.Minimum = 0;
                            chart.ChartAreas["ChartArea1"].AxisY.Maximum = ScaleY;
                        }
                        chart.ChartAreas["ChartArea1"].AxisY.MajorGrid.Enabled = false;
                        chart.ChartAreas["ChartArea1"].AxisY.Enabled = AxisEnabled.False;
                    });

                    sw.Stop();
                    sw.Reset();

                    using (MemoryStream ms = new MemoryStream())
                    {
                        screen.Save(ms, ImageFormat.Bmp);
                        ms.Seek(0, SeekOrigin.Begin);

                        BitmapImage img = new BitmapImage();
                        img.BeginInit();
                        img.CacheOption = BitmapCacheOption.OnLoad;
                        img.StreamSource = ms;
                        img.EndInit();
                        img.Freeze();

                        CaptureImage = img;
                    }
                    screen.Dispose();
                    Thread.Sleep(100);
                }
            });
        }

        /// <summary>
        /// ストップボタンの実行可否
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>
        private bool canStopExecute(object parameter)
        {
            return isExecuting;
        }

        /// <summary>
        /// ストップボタン押下
        /// </summary>
        /// <param name="parameter"></param>
        private void stopExecute(object parameter)
        {
            isExecuting = false;
        }
    }
}

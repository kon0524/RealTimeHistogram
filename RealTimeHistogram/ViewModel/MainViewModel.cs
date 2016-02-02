using RealTimeHistogram.Model;
using System;
using System.Collections.ObjectModel;
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

        // プロセスリスト
        public ObservableCollection<Process> Processes { get; set; }

        // 選択中プロセス
        private Process selectedProcess;
        public Process SelectedProcess 
        {
            get { return selectedProcess; }
            set
            {
                selectedProcess = value;
                selectedWindowRect = wm.GetWindowRectangle(selectedProcess);
                OffsetX = OffsetY = 0;
                Width = selectedWindowRect.Width;
                NotifyPropertyChanged("SelectedProcess");
            }
        }

        // Y軸スケール
        private int scaleY;
        public int ScaleY {
            get { return scaleY; }
            set
            {
                if (value >= 0)
                {
                    scaleY = value;
                    NotifyPropertyChanged("ScaleY");
                }
            }
        }

        // OffsetX
        private int offsetX;
        public int OffsetX
        {
            get { return offsetX; }
            set
            {
                if (0 <= value && value < selectedWindowRect.Width)
                {
                    offsetX = value;
                    UpdatePosition();
                    NotifyPropertyChanged("OffsetX");
                }
            }
        }

        // OffsetY
        private int offsetY;
        public int OffsetY
        {
            get { return offsetY; }
            set
            {
                if (0 <= value && value < selectedWindowRect.Height)
                {
                    offsetY = value;
                    UpdatePosition();
                    NotifyPropertyChanged("OffsetY");
                }
            }
        }

        // Width
        private int width;
        public int Width
        {
            get { return width; }
            set
            {
                if (0 < value && value <= selectedWindowRect.Width)
                {
                    width = value;
                    UpdatePosition();
                    NotifyPropertyChanged("Width");
                }
            }
        }

        // 削除
        public ICommand Start { get; set; }

        // コピー
        public ICommand Stop { get; set; }

        // リフレッシュ
        public ICommand Refresh { get; set; }

        // Windowタイトル
        public string WindowTitle { get; private set; }

        // 実行状態
        private bool isExecuting;

        // チャート
        private Chart chart;

        // WindowManager
        private WindowManager wm;

        // 選択中Windowの位置
        private Rectangle selectedWindowRect;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="chart"></param>
        public MainViewModel(Chart chart)
        {
            this.chart = chart;

            WindowTitle = "RealTimeHistogram " + FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion;
            isExecuting = false;
            OffsetX = offsetY = Width = 0;
            ScaleY = 0;
            Processes = new ObservableCollection<Process>();
            wm = new WindowManager();
            foreach (Process p in wm.GetProcessesWithMainWindowHandle())
            {
                Processes.Add(p);
            }
            SelectedProcess = Processes[0];

            Start = new DelegateCommand(startExecute, canStartExecute);
            Stop = new DelegateCommand(stopExecute, canStopExecute);
            Refresh = new DelegateCommand(refreshExecute, null);
        }

        private void UpdatePosition()
        {
            // OffsetXの確認
            if (OffsetX >= selectedWindowRect.Width)
            {
                OffsetX = selectedWindowRect.Width - 1;
            }

            // OffsetYの確認
            if (OffsetY >= selectedWindowRect.Height)
            {
                OffsetY = selectedWindowRect.Height - 1;
            }

            // Widthの確認
            if (OffsetX + Width > selectedWindowRect.Width)
            {
                Width = selectedWindowRect.Width - OffsetX;
            }
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
                    selectedWindowRect = wm.GetWindowRectangle(SelectedProcess);
                    if (selectedWindowRect == Rectangle.Empty)
                    {
                        isExecuting = false;
                        App.Current.Dispatcher.Invoke(() => 
                        {
                            RaiseCanExecuteChanged();
                        });
                        
                        break;
                    }
                    UpdatePosition();
                    Rectangle captureRect = new Rectangle(selectedWindowRect.X + OffsetX, selectedWindowRect.Y + OffsetY, Width, Width / 2);
                    screen = new Bitmap(captureRect.Width, captureRect.Height, PixelFormat.Format32bppArgb);
                    g = Graphics.FromImage(screen);
                    g.CopyFromScreen(captureRect.X, captureRect.Y, 0, 0, screen.Size);
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

        /// <summary>
        /// リフレッシュボタン押下
        /// </summary>
        /// <param name="parameter"></param>
        private void refreshExecute(object parameter)
        {
            Processes.Clear();
            foreach (Process p in wm.GetProcessesWithMainWindowHandle())
            {
                Processes.Add(p);
            }
            SelectedProcess = Processes[0];
            selectedWindowRect = wm.GetWindowRectangle(SelectedProcess);
            UpdatePosition();
        }
    }
}

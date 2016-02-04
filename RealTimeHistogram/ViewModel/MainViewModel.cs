using RealTimeHistogram.Model;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms.DataVisualization.Charting;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace RealTimeHistogram.ViewModel
{
    public class MainViewModel : ViewModelBase
    {
        #region Properties
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
                NotifyPropertyChanged("SelectedProcess");

                // Windowを変更したらOffsetを初期化する
                selectedWindowRect = wm.GetWindowRectangle(selectedProcess);
                OffsetX = OffsetY = 0;
                Width = selectedWindowRect.Width;
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
        #endregion

        #region Fields
        // 実行状態
        private bool isExecuting;

        // チャート
        private Chart chart;

        // WindowManager
        private WindowManager wm;

        // 選択中Windowの位置
        private Rectangle selectedWindowRect;

        // キャプチャ画像表示Window
        private CaptureImageWindow captureImageWindow;
        #endregion

        #region Constructor
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="chart"></param>
        public MainViewModel(Chart chart)
        {
            this.chart = chart;

            // 各種初期化
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
            Refresh = new DelegateCommand(refreshExecute, canRefreshExecute);

            // MainWindowが閉じられたら子Windowも閉じるようにする
            App.Current.MainWindow.Closing += new CancelEventHandler((obj, args) => 
            {
                if (captureImageWindow != null) captureImageWindow.Close();
            });

            App.Current.MainWindow.PreviewDragOver += new DragEventHandler(previewDragOver);
            App.Current.MainWindow.Drop += new DragEventHandler(drop);
        }
        #endregion

        #region Methods
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
        /// ヒストグラムを計算する
        /// </summary>
        /// <param name="image">対象画像</param>
        /// <returns></returns>
        private UInt32[] calcHistogram(Bitmap image)
        {
            if (image == null) return null;

            UInt32[] histogram = new UInt32[256];

            BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, image.PixelFormat);
            byte[] buf = new byte[data.Stride * image.Height];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            for (int i = 0; i < buf.Length; i += 4)
            {
                byte grey = (byte)(0.299 * buf[i] + 0.587 * buf[i + 1] + 0.114 * buf[i + 2]);
                histogram[grey]++;
            }
            image.UnlockBits(data);

            return histogram;
        }

        /// <summary>
        /// BitmapをBitmapImageに変換する
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        private BitmapImage getBitmapImage(Bitmap image)
        {
            if (image == null) return null;

            BitmapImage bImage = new BitmapImage();
            using (MemoryStream ms = new MemoryStream())
            {
                image.Save(ms, ImageFormat.Bmp);
                ms.Seek(0, SeekOrigin.Begin);
                bImage.BeginInit();
                bImage.CacheOption = BitmapCacheOption.OnLoad;
                bImage.StreamSource = ms;
                bImage.EndInit();
                bImage.Freeze();
            }

            return bImage;
        }
        #endregion

        #region Commands
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
                Bitmap screen;
                Graphics g;
                UInt32[] histo;

                while (isExecuting)
                {
                    // スクリーンキャプチャ(70ms)
                    selectedWindowRect = wm.GetWindowRectangle(SelectedProcess);
                    if (selectedWindowRect == Rectangle.Empty)
                    {
                        isExecuting = false;
                        App.Current.Dispatcher.Invoke(() => 
                        {
                            captureImageWindow.Close();
                            captureImageWindow = null;
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
                    histo = calcHistogram(screen);

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

                    // キャプチャ画像表示
                    CaptureImage = getBitmapImage(screen);
                    screen.Dispose();

                    Thread.Sleep(100);
                }
            });

            captureImageWindow = new CaptureImageWindow(this);
            captureImageWindow.Show();
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
            captureImageWindow.Close();
            captureImageWindow = null;
        }

        /// <summary>
        /// リフレッシュボタンの実行可否
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>
        private bool canRefreshExecute(object parameter)
        {
            return !isExecuting;
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
        #endregion

        #region Events
        /// <summary>
        /// DragOverイベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void previewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop, true)) e.Effects = DragDropEffects.Copy;
            else e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// Dropイベント
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void drop(object sender, DragEventArgs e)
        {
            string[] images = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (images == null) return;

            // キャプチャ中なら停止する
            if (captureImageWindow != null)
            {
                captureImageWindow.Close();
                captureImageWindow = null;
            }
            if (isExecuting)
            {
                isExecuting = false;
            }

            // ヒストグラムを計算
            Bitmap image = new Bitmap(images[0]);
            UInt32[] histogram = calcHistogram(image);

            // ヒストグラム表示
            chart.ChartAreas.Clear();
            chart.ChartAreas.Add("ChartArea1");
            Series series = new Series();
            series.ChartType = SeriesChartType.Column;
            series.MarkerStyle = MarkerStyle.None;
            for (int i = 0; i < histogram.Length; i++)
            {
                series.Points.AddXY((double)i, (double)histogram[i]);
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

            // 画像を表示
            CaptureImage = getBitmapImage(image);
            image.Dispose();

            // 子Windows表示
            captureImageWindow = new CaptureImageWindow(this);
            captureImageWindow.Show();
        }
        #endregion
    }
}

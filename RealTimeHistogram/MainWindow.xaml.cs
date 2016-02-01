using RealTimeHistogram.ViewModel;
using System.Windows;
using System.Windows.Forms.DataVisualization.Charting;

namespace RealTimeHistogram
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = new MainViewModel((Chart)graph.Child);
        }
    }
}

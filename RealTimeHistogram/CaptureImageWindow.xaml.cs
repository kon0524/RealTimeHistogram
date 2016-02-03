using RealTimeHistogram.ViewModel;
using System.Windows;

namespace RealTimeHistogram
{
    /// <summary>
    /// CaptureImageWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class CaptureImageWindow : Window
    {
        public CaptureImageWindow(MainViewModel mainVM)
        {
            InitializeComponent();
            this.DataContext = mainVM;
        }
    }
}

using RealTimeHistogram.ViewModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.DataVisualization.Charting;

namespace RealTimeHistogram
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel vm;
        public MainWindow()
        {
            InitializeComponent();
            vm = new MainViewModel((Chart)graph.Child);
            this.DataContext = vm;
        }

        /// <summary>
        /// マウスホイール(VMに通知する方法がわからないのでコードビハインドに。。)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TextBox_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            TextBox textbox = sender as TextBox;
            int offset = (e.Delta > 0) ? 4 : -4;

            if (textbox == null) return;
            switch (textbox.Name)
            {
                case "offsetX":
                    vm.PositionX += offset;
                    break;
                case "offsetY":
                    vm.PositionY += offset;
                    break;
                case "Width":
                    vm.Width += offset;
                    break;
                case "scaleY":
                    vm.ScaleY += offset * 100;
                    break;
                default:
                    break;
            }
        }
    }
}

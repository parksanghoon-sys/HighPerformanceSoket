using System.Windows;
using Hps.Sample.Dashboard.ViewModels;

namespace Hps.Sample.Dashboard
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new DashboardViewModel();
        }
    }
}

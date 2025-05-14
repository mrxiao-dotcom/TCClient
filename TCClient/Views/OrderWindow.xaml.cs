using System.Windows;
using TCClient.ViewModels;

namespace TCClient.Views
{
    public partial class OrderWindow : Window
    {
        public OrderWindow()
        {
            InitializeComponent();
            DataContext = new OrderViewModel();
        }
    }
} 
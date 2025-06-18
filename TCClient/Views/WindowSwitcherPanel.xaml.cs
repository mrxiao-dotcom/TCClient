using System.Windows.Controls;
using TCClient.ViewModels;

namespace TCClient.Views
{
    /// <summary>
    /// 窗口切换面板
    /// </summary>
    public partial class WindowSwitcherPanel : UserControl
    {
        public WindowSwitcherPanel()
        {
            InitializeComponent();
        }
        
        public WindowSwitcherPanel(WindowSwitcherViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }
    }
} 
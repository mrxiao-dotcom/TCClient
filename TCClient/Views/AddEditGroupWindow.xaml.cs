using System.Linq;
using System.Windows;
using TCClient.Models;
using TCClient.ViewModels;

namespace TCClient.Views
{
    public partial class AddEditGroupWindow : Window
    {
        private AddEditGroupViewModel ViewModel => (AddEditGroupViewModel)DataContext;

        public AddEditGroupWindow(AddEditGroupViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose += () => this.DialogResult = true;
            
            // 处理DataGrid选择变化
            CandidateDataGrid.SelectionChanged += (s, e) =>
            {
                ViewModel.SelectedCandidates.Clear();
                foreach (CandidateSymbol item in CandidateDataGrid.SelectedItems)
                {
                    ViewModel.SelectedCandidates.Add(item);
                }
            };
        }
    }
} 
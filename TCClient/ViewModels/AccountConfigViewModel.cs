using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TCClient.Commands;
using TCClient.Models;
using TCClient.Services;
using TCClient.Utils;
using TCClient.Views;

namespace TCClient.ViewModels
{
    public class DialogResultEventArgs : EventArgs
    {
        public bool Result { get; }

        public DialogResultEventArgs(bool result)
        {
            Result = result;
        }
    }

    public class AccountConfigViewModel : ViewModelBase
    {
        private readonly IUserService _userService;
        private readonly IMessageService _messageService;
        private readonly IDatabaseService _databaseService;
        private ObservableCollection<TradingAccount> _accounts;
        private TradingAccount _selectedAccount;
        private bool _isLoading;
        private string _statusMessage;

        public ObservableCollection<TradingAccount> Accounts
        {
            get => _accounts;
            set
            {
                _accounts = value;
                OnPropertyChanged();
            }
        }

        public TradingAccount SelectedAccount
        {
            get => _selectedAccount;
            set
            {
                if (_selectedAccount != value)
                {
                    _selectedAccount = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public ICommand AddAccountCommand { get; }
        public ICommand EditAccountCommand { get; }
        public ICommand DeleteAccountCommand { get; }
        public ICommand SetDefaultAccountCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public event EventHandler<DialogResultEventArgs> CloseWindow;

        public AccountConfigViewModel(
            IUserService userService,
            IMessageService messageService,
            IDatabaseService databaseService)
        {
            _userService = userService;
            _messageService = messageService;
            _databaseService = databaseService;

            Accounts = new ObservableCollection<TradingAccount>();
            AddAccountCommand = new RelayCommand(ShowAddAccountWindow);
            EditAccountCommand = new RelayCommand(EditAccount);
            DeleteAccountCommand = new RelayCommand(DeleteAccount);
            SetDefaultAccountCommand = new RelayCommand(SetDefaultAccount);
            RefreshCommand = new RelayCommand(async () => await LoadAccountsAsync());
            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(Cancel);

            _ = LoadAccountsAsync();
        }

        private async Task LoadAccountsAsync()
        {
            try
            {
                IsLoading = true;
                var accounts = await _userService.GetTradingAccountsAsync();
                Accounts.Clear();
                foreach (var account in accounts)
                {
                    Accounts.Add(account);
                }
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage($"加载账户列表失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ShowAddAccountWindow()
        {
            var window = new AddAccountWindow
            {
                Owner = Application.Current.MainWindow
            };

            if (window.ShowDialog() == true)
            {
                // 重新加载账户列表
                _ = LoadAccountsAsync();
            }
        }

        private void EditAccount()
        {
            if (SelectedAccount == null) return;

            var window = new AddAccountWindow
            {
                Owner = Application.Current.MainWindow
            };

            // 设置编辑模式
            var viewModel = window.ViewModel;
            viewModel.SetEditMode(SelectedAccount);

            if (window.ShowDialog() == true)
            {
                // 重新加载账户列表
                _ = LoadAccountsAsync();
            }
        }

        private bool CanDeleteAccount()
        {
            return SelectedAccount != null;
        }

        private async void DeleteAccount()
        {
            if (SelectedAccount == null) return;

            var result = _messageService.ShowMessage(
                $"确定要删除账户 {SelectedAccount.AccountName} 吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _userService.DeleteTradingAccountAsync(SelectedAccount.Id);
                    Accounts.Remove(SelectedAccount);
                    SelectedAccount = null;
                }
                catch (Exception ex)
                {
                    _messageService.ShowMessage(
                        $"删除账户失败：{ex.Message}",
                        "错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void SetDefaultAccount()
        {
            // Implementation needed
        }

        private void Save()
        {
            try
            {
                // 关闭窗口，因为账户的保存已经在AddAccountWindow中完成
                CloseWindow?.Invoke(this, new DialogResultEventArgs(true));
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"保存账户配置失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Cancel()
        {
            CloseWindow?.Invoke(this, new DialogResultEventArgs(false));
        }
    }
} 
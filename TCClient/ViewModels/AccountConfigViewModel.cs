using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TCClient.Commands;
using TCClient.Models;
using TCClient.Services;
using TCClient.Utils;

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
        private readonly ObservableCollection<TradingAccount> _accounts;
        private TradingAccount _selectedAccount;
        private bool _isLoading;

        public AccountConfigViewModel()
        {
            _userService = ServiceLocator.GetService<IUserService>();
            _messageService = ServiceLocator.GetService<IMessageService>();
            _accounts = new ObservableCollection<TradingAccount>();

            // 初始化命令
            AddAccountCommand = new RelayCommand(AddAccount);
            DeleteAccountCommand = new RelayCommand(DeleteAccount, CanDeleteAccount);
            SaveCommand = new RelayCommand(async () => await SaveAsync(), CanSave);
            CancelCommand = new RelayCommand(Cancel);

            // 加载账户列表
            _ = LoadAccountsAsync();
        }

        public ObservableCollection<TradingAccount> Accounts => _accounts;

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

        public ICommand AddAccountCommand { get; }
        public ICommand DeleteAccountCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public event EventHandler<DialogResultEventArgs> CloseWindow;

        private async Task LoadAccountsAsync()
        {
            try
            {
                IsLoading = true;
                var accounts = await _userService.GetTradingAccountsAsync();
                _accounts.Clear();
                foreach (var account in accounts)
                {
                    _accounts.Add(account);
                }
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage(
                    $"加载账户列表失败：{ex.Message}",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void AddAccount()
        {
            var newAccount = new TradingAccount
            {
                AccountName = "新账户",
                BinanceAccountId = "",
                ApiKey = "",
                ApiSecret = "",
                ApiPassphrase = "",
                Equity = 0,
                InitialEquity = 0,
                OpportunityCount = 10,
                Status = 1, // 1-启用
                IsActive = 1,
                CreateTime = DateTime.Now,
                UpdateTime = DateTime.Now,
                IsDefault = false
            };

            _accounts.Add(newAccount);
            SelectedAccount = newAccount;
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
                    _accounts.Remove(SelectedAccount);
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

        private bool CanSave()
        {
            return true;
        }

        private async Task SaveAsync()
        {
            try
            {
                foreach (var account in _accounts)
                {
                    if (account.Id == 0)
                    {
                        await _userService.CreateTradingAccountAsync(account);
                    }
                    else
                    {
                        await _userService.UpdateTradingAccountAsync(account);
                    }
                }

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
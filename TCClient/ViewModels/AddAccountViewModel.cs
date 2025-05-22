using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using TCClient.Commands;
using TCClient.Models;
using TCClient.Services;
using TCClient.Utils;
using System.IO;
using System.ComponentModel;
using System.Threading.Tasks;

namespace TCClient.ViewModels
{
    public class AddAccountViewModel : INotifyPropertyChanged
    {
        private readonly IUserService _userService;
        private readonly IMessageService _messageService;
        private readonly IDatabaseService _databaseService;
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "TCClient_Account.log");

        private static void LogToFile(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, logMessage);
            }
            catch
            {
                // 忽略日志写入失败
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<DialogResultEventArgs> CloseWindow;

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private bool _isEditMode;
        public bool IsEditMode
        {
            get => _isEditMode;
            set
            {
                if (_isEditMode != value)
                {
                    _isEditMode = value;
                    OnPropertyChanged(nameof(IsEditMode));
                    OnPropertyChanged(nameof(WindowTitle));
                }
            }
        }

        public string WindowTitle => IsEditMode ? "编辑账户" : "添加账户";

        private TradingAccount _account;
        public TradingAccount Account
        {
            get => _account;
            set
            {
                _account = value;
                OnPropertyChanged(nameof(Account));
                OnPropertyChanged(nameof(AccountName));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(ApiKey));
                OnPropertyChanged(nameof(ApiSecret));
                OnPropertyChanged(nameof(ApiPassphrase));
                OnPropertyChanged(nameof(IsDefaultAccount));
            }
        }

        public string AccountName
        {
            get => Account?.AccountName;
            set
            {
                if (Account != null)
                {
                    Account.AccountName = value;
                    OnPropertyChanged(nameof(AccountName));
                }
            }
        }

        public string Description
        {
            get => Account?.Description;
            set
            {
                if (Account != null)
                {
                    Account.Description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }
        }

        public string ApiKey
        {
            get => Account?.ApiKey;
            set
            {
                if (Account != null)
                {
                    Account.ApiKey = value;
                    OnPropertyChanged(nameof(ApiKey));
                }
            }
        }

        private string _apiSecret;
        public string ApiSecret
        {
            get => Account?.ApiSecret ?? _apiSecret;
            set
            {
                if (Account != null)
                {
                    Account.ApiSecret = value;
                    OnPropertyChanged(nameof(ApiSecret));
                }
                else
                {
                    _apiSecret = value;
                    OnPropertyChanged(nameof(ApiSecret));
                }
            }
        }

        private string _apiPassphrase;
        public string ApiPassphrase
        {
            get => Account?.ApiPassphrase ?? _apiPassphrase;
            set
            {
                if (Account != null)
                {
                    Account.ApiPassphrase = value;
                    OnPropertyChanged(nameof(ApiPassphrase));
                }
                else
                {
                    _apiPassphrase = value;
                    OnPropertyChanged(nameof(ApiPassphrase));
                }
            }
        }

        public bool IsDefaultAccount
        {
            get => Account?.IsDefaultAccount ?? false;
            set
            {
                if (Account != null)
                {
                    Account.IsDefaultAccount = value;
                    OnPropertyChanged(nameof(IsDefaultAccount));
                }
            }
        }

        private decimal _initialEquity;
        public decimal InitialEquity
        {
            get => Account?.InitialEquity ?? _initialEquity;
            set
            {
                if (Account != null)
                {
                    Account.InitialEquity = value;
                    OnPropertyChanged(nameof(InitialEquity));
                }
                else
                {
                    _initialEquity = value;
                    OnPropertyChanged(nameof(InitialEquity));
                }
            }
        }

        private int _opportunityCount;
        public int OpportunityCount
        {
            get => Account?.OpportunityCount ?? _opportunityCount;
            set
            {
                if (Account != null)
                {
                    Account.OpportunityCount = value;
                    OnPropertyChanged(nameof(OpportunityCount));
                }
                else
                {
                    _opportunityCount = value;
                    OnPropertyChanged(nameof(OpportunityCount));
                }
            }
        }

        private string _binanceAccountId;
        public string BinanceAccountId
        {
            get => Account?.BinanceAccountId ?? _binanceAccountId;
            set
            {
                if (Account != null)
                {
                    Account.BinanceAccountId = value;
                    OnPropertyChanged(nameof(BinanceAccountId));
                }
                else
                {
                    _binanceAccountId = value;
                    OnPropertyChanged(nameof(BinanceAccountId));
                }
            }
        }

        private bool _isDefault;
        public bool IsDefault
        {
            get => Account?.IsDefault == 1 || _isDefault;
            set
            {
                if (Account != null)
                {
                    Account.IsDefault = value ? 1 : 0;
                    Account.IsDefaultAccount = value;
                    OnPropertyChanged(nameof(IsDefault));
                }
                else
                {
                    _isDefault = value;
                    OnPropertyChanged(nameof(IsDefault));
                }
            }
        }

        public AddAccountViewModel(IUserService userService, IMessageService messageService, IDatabaseService databaseService)
        {
            _userService = userService;
            _messageService = messageService;
            _databaseService = databaseService;

            Account = new TradingAccount();
            _initialEquity = 10000;
            _opportunityCount = 10;
            _isDefault = false;

            SaveCommand = new RelayCommand(async () => await SaveAsync(), () => CanSave(null));
            CancelCommand = new RelayCommand(async () => await CancelAsync());
        }

        public void SetEditMode(TradingAccount account)
        {
            Account = account;
            IsEditMode = true;
            OnPropertyChanged(nameof(InitialEquity));
            OnPropertyChanged(nameof(OpportunityCount));
            OnPropertyChanged(nameof(BinanceAccountId));
            OnPropertyChanged(nameof(IsDefault));
        }

        private bool CanSave(object parameter)
        {
            // 检查必填字段
            if (string.IsNullOrWhiteSpace(AccountName))
            {
                return false;
            }

            // 检查数值字段
            if (InitialEquity <= 0)
            {
                return false;
            }

            if (OpportunityCount <= 0)
            {
                return false;
            }

            // 如果是编辑模式，只需要检查账户名称和数值字段
            if (IsEditMode)
            {
                return true;
            }

            // 如果是新增模式，需要检查所有必填字段
            return !string.IsNullOrWhiteSpace(ApiKey) &&
                   !string.IsNullOrWhiteSpace(ApiSecret) &&
                   !string.IsNullOrWhiteSpace(BinanceAccountId);
        }

        private async Task SaveAsync()
        {
            try
            {
                // 确保更新密码框的值
                if (Account != null)
                {
                    Account.ApiSecret = ApiSecret;
                    Account.ApiPassphrase = ApiPassphrase;
                }

                if (IsEditMode)
                {
                    // 更新现有账户
                    Account.UpdateTime = DateTime.Now;
                    await _userService.UpdateTradingAccountAsync(Account);
                    _messageService.ShowMessage("账户更新成功！", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    // 添加新账户
                    Account.CreateTime = DateTime.Now;
                    Account.UpdateTime = DateTime.Now;
                    await _userService.CreateTradingAccountAsync(Account);
                    _messageService.ShowMessage("账户添加成功！", "成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CloseWindow?.Invoke(this, new DialogResultEventArgs(true));
                });
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage($"保存账户失败：{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task CancelAsync()
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CloseWindow?.Invoke(this, new DialogResultEventArgs(false));
                });
            }
            catch (Exception ex)
            {
                _messageService.ShowMessage($"取消操作失败：{ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 
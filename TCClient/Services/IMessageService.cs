using System.Windows;

namespace TCClient.Services
{
    public interface IMessageService
    {
        MessageBoxResult ShowMessage(string message, string title, MessageBoxButton buttons, MessageBoxImage icon);
    }
} 
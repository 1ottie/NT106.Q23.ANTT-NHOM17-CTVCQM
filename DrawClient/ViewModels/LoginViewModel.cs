using System;
using System.Windows.Input;

namespace DrawClient.ViewModels
{
    public class LoginViewModel
    {
        public string Username { get; set; }
        public string ServerIp { get; set; }
        public string Port { get; set; }

        public ICommand ConnectCommand { get; }
        public Action GoToLobby { get; set; }

        public LoginViewModel()
        {
            ConnectCommand = new RelayCommand(ExecuteConnect);
        }

        private void ExecuteConnect(object obj)
        {
            // Viết code kiểm tra IP, Port ở đây

            // Chuyển trang sang sảnh chờ
            GoToLobby?.Invoke();
        }
    }
}
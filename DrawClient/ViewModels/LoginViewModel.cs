using System;
using System.Windows;
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
            ServerIp = "127.0.0.1";
            Port = "5000";
            Username = "aa";
        }

        private void ExecuteConnect(object obj)
        {
            int portNum = int.Parse(Port);

            bool isConnected = ClientSocket.Instance.Connect(ServerIp, portNum);

            if (isConnected)
            {
                MessageBox.Show("Kết nối thành công tới " + ServerIp);
                GoToLobby?.Invoke();
            }
            else
            {
                MessageBox.Show("Kết nối thất bại!");
            }

        }
    }
}
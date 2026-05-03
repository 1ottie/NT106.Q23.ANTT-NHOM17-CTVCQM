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

        // Thêm 2 biến static này để truyền IP/Port của Master sang cho LobbyViewModel
        public static string CurrentMasterIp { get; set; } = "127.0.0.1";
        public static int CurrentMasterPort { get; set; } = 5000;

        //lưu token
        public static string Token { get; set; }

        public LoginViewModel()
        {
            ConnectCommand = new RelayCommand(ExecuteConnect);
            ServerIp = "127.0.0.1";
            Port = "5000";
            Username = "aa";
        }

        private void ExecuteConnect(object obj)
        {
            // Lưu lại IP và Port của Master Server
            CurrentMasterIp = ServerIp;
            CurrentMasterPort = int.Parse(Port);

            // Bỏ qua kết nối TCP lúc này. Chỉ cần chuyển sang Lobby. 
            // Việc kết nối mạng thực sự sẽ diễn ra khi người dùng chọn phòng.
            GoToLobby?.Invoke();
        }
    }
}
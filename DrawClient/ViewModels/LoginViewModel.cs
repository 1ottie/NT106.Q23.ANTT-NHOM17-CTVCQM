using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using DrawClient.Models;

namespace DrawClient.ViewModels
{
    public class LoginViewModel
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string ServerIp { get; set; }
        public string Port { get; set; }
        public ICommand ConnectCommand { get; }
        public Action GoToLobby { get; set; }

        public static string CurrentMasterIp { get; set; } = "127.0.0.1";
        public static int CurrentMasterPort { get; set; } = 5000;
        public static string Token { get; set; }
        public static int CurrentUserId { get; set; }

        // SỬA TRIỆT ĐỂ TẠI ĐÂY: Khởi tạo sẵn giá trị mặc định là "tula"
        // Như vậy dù bạn có test bỏ qua Đăng nhập, nó vẫn sẽ gửi tên "tula" thay vì rỗng.
        public static string CurrentUsername { get; set; } = "tula";

        private readonly HttpClient _httpClient = new HttpClient();

        public LoginViewModel()
        {
            ConnectCommand = new RelayCommand(async (obj) => await ExecuteConnect());
            ServerIp = "127.0.0.1";
            Port = "5000";

            // Đặt UI cũng hiển thị mặc định tên bạn
            Username = "tula";
            Password = "123456";
        }

        private async Task ExecuteConnect()
        {
            try
            {
                var loginReq = new
                {
                    username = Username,
                    password = Password
                };

                string json = JsonSerializer.Serialize(loginReq);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("http://localhost:5274/api/auth/login", content);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show("Login thất bại");
                    return;
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<LoginResponse>(responseJson, options);

                if (result == null || result.user == null) return;

                // Lưu dữ liệu Auth
                Token = result.token;
                CurrentUserId = result.user.user_id;

                // Ghi đè tên mới nếu người dùng thực sự có nhập ở màn hình Đăng nhập
                CurrentUsername = string.IsNullOrWhiteSpace(Username) ? "tula" : Username;

                // Gán cho socket
                ClientSocket.Instance.CurrentUserId = CurrentUserId;
                ClientSocket.Instance.CurrentUsername = CurrentUsername;

                GoToLobby?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Login error: " + ex.Message);
            }
        }
    }
}
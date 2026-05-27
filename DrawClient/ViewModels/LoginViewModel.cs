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

        public static string CurrentMasterIp { get; set; } = AppConfig.MasterServerIp;
        public static int CurrentMasterPort { get; set; } = AppConfig.MasterServerPort;
        public static string Token { get; set; }
        public static int CurrentUserId { get; set; }

        public static string CurrentUsername { get; set; } = string.Empty;

        private readonly HttpClient _httpClient = new HttpClient();

        public LoginViewModel()
        {
            ConnectCommand = new RelayCommand(async (obj) => await ExecuteConnect());
            ServerIp = AppConfig.MasterServerIp;
            Port = "5274";

            Username = string.Empty;
            Password = string.Empty;
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

                var response = await _httpClient.PostAsync($"http://{ServerIp}:{Port}/api/auth/login", content);
                string responseJson = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show("Login thất bại");
                    return;
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = JsonSerializer.Deserialize<LoginResponse>(responseJson, options);

                if (result == null || result.user == null) return;

                if (string.IsNullOrEmpty(result.token))
                {
                    MessageBox.Show("Login thất bại: Server không trả về token.");
                    return;
                }

                // Lưu dữ liệu Auth
                Token = result.token;
                CurrentUserId = result.user.user_id;
                CurrentUsername = result.user.username ?? Username;

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
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

        public static string CurrentMasterIp { get; set; }
            = "127.0.0.1";

        public static int CurrentMasterPort { get; set; }
            = 5000;

        public static string Token { get; set; }

        public static int CurrentUserId { get; set; }

        private readonly HttpClient _httpClient =
            new HttpClient();

        public LoginViewModel()
        {
            ConnectCommand =
                new RelayCommand(
                    async (obj) => await ExecuteConnect());

            ServerIp = "127.0.0.1";

            Port = "5000";

            Username = "aa";

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

                string json =
                    JsonSerializer.Serialize(loginReq);

                var content =
                    new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json");

                var response =
                    await _httpClient.PostAsync(
                        "http://localhost:5274/api/auth/login",
                        content);

                string responseJson =
                    await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine(
                    "[LOGIN RESPONSE]");

                System.Diagnostics.Debug.WriteLine(
                    responseJson);

                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show(
                        "Login thất bại");

                    return;
                }

                var options =
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                var result =
                    JsonSerializer.Deserialize<LoginResponse>(
                        responseJson,
                        options);

                if (result == null)
                {
                    MessageBox.Show(
                        "Deserialize fail");

                    return;
                }

                if (result.user == null)
                {
                    MessageBox.Show(
                        "User data null");

                    return;
                }

                // Lưu token
                Token = result.token;

                // Lưu user id
                CurrentUserId =
                    result.user.user_id;

                // Gán cho socket
                ClientSocket.Instance.CurrentUserId =
                    result.user.user_id;

                System.Diagnostics.Debug.WriteLine(
                    "[AFTER SET USER ID] = "
                    + CurrentUserId);

                System.Diagnostics.Debug.WriteLine(
                    "[LOGIN USER ID] = "
                    + result.user.user_id);

                System.Diagnostics.Debug.WriteLine(
                    "[STATIC USER ID] = "
                    + CurrentUserId);

                System.Diagnostics.Debug.WriteLine(
                    "[SOCKET USER ID] = "
                    + ClientSocket.Instance.CurrentUserId);

                // Lưu thông tin Master
                CurrentMasterIp = ServerIp;

                CurrentMasterPort =
                    int.Parse(Port);

                MessageBox.Show(
                    "LOGIN USER ID = "
                    + CurrentUserId);
                // Chuyển sang Lobby
                GoToLobby?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Login error: "
                    + ex.Message);
            }
        }
    }
}
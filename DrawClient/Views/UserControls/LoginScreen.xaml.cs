using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DrawClient.ViewModels;

namespace DrawClient.Views
{
    public partial class LoginScreen : UserControl
    {
        private bool isLoginMode = true;
        private static readonly HttpClient httpClient = new HttpClient();

        // LƯU Ý: Đổi cổng 5274 thành cổng Web API của bạn
        private readonly string ApiBaseUrl = "http://localhost:5274/api/auth";

        public LoginScreen()
        {
            InitializeComponent();
        }

        private void ToggleMode_Click(object sender, RoutedEventArgs e)
        {
            isLoginMode = !isLoginMode;
            txtMessage.Visibility = Visibility.Collapsed;

            if (isLoginMode)
            {
                txtTitle.Text = "Sign in to CollabDraw";
                btnSubmit.Content = "Login";
                lblEmail.Visibility = Visibility.Collapsed;
                txtEmail.Visibility = Visibility.Collapsed;
                txtToggleMode.Text = "Don't have an account? Register";
            }
            else
            {
                txtTitle.Text = "Create an Account";
                btnSubmit.Content = "Register";
                lblEmail.Visibility = Visibility.Visible;
                txtEmail.Visibility = Visibility.Visible;
                txtToggleMode.Text = "Already have an account? Sign in";
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;
            string email = txtEmail.Text.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowMessage("Username and Password are required.", true);
                return;
            }

            btnSubmit.IsEnabled = false;

            try
            {
                string endpoint = isLoginMode ? "/login" : "/register";
                object payload;

                if (isLoginMode)
                {
                    payload = new { username = username, password = password };
                }
                else
                {
                    if (string.IsNullOrEmpty(email))
                    {
                        ShowMessage("Email is required for registration.", true);
                        btnSubmit.IsEnabled = true;
                        return;
                    }
                    payload = new { username = username, password = password, email = email };
                }

                string jsonPayload = JsonSerializer.Serialize(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await httpClient.PostAsync($"{ApiBaseUrl}{endpoint}", content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    if (isLoginMode)
                    {
                        using (JsonDocument doc = JsonDocument.Parse(responseBody))
                        {
                            string token = doc.RootElement.GetProperty("token").GetString();

                            // LƯU TOKEN VÀO VÍ ĐỂ CÁC MÀN HÌNH KHÁC DÙNG
                            LoginViewModel.Token = token;

                            // CHUYỂN MÀN HÌNH SAU KHI ĐĂNG NHẬP THÀNH CÔNG
                            // Lấy ViewModel đang được gán cho LoginScreen (được gán ở MainWindow)
                            var viewModel = this.DataContext as LoginViewModel;
                            if (viewModel != null && viewModel.GoToLobby != null)
                            {
                                viewModel.GoToLobby.Invoke(); // Thực thi lệnh chuyển trang
                            }
                            else
                            {
                                ShowMessage("Lỗi cấu hình ViewModel: Không thể chuyển trang.", true);
                            }
                        }
                    }
                    else
                    {
                        ShowMessage("Registration successful! You can now log in.", false);
                        ToggleMode_Click(null, null);
                    }
                }
                else
                {
                    string errorMsg = "An error occurred.";
                    try
                    {
                        using (JsonDocument doc = JsonDocument.Parse(responseBody))
                        {
                            if (doc.RootElement.TryGetProperty("message", out JsonElement msgElement))
                            {
                                errorMsg = msgElement.GetString();
                            }
                        }
                    }
                    catch { errorMsg = responseBody; }

                    ShowMessage(errorMsg, true);
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"Connection failed: {ex.Message}", true);
            }
            finally
            {
                btnSubmit.IsEnabled = true;
            }
        }

        private void ShowMessage(string message, bool isError)
        {
            txtMessage.Text = message;
            txtMessage.Foreground = isError ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red)
                                            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            txtMessage.Visibility = Visibility.Visible;
        }
    }
}
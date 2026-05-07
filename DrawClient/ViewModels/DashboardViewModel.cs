using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Windows;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DrawClient.ViewModels
{
    // Model Room để hứng dữ liệu từ API
    public class Room
    {
        public int room_id { get; set; }
        public string room_name { get; set; }
        public int max_users { get; set; }
        public bool is_private { get; set; }
        public int player_count { get; set; } // Thêm thuộc tính này để hứng dữ liệu từ API
        public NodeInfo node { get; set; }

        public string Id => room_id.ToString();
        public string Name => room_name;
        public string Host => "Admin";

        // Trả về player_count thực tế thay vì số 1
        public int PlayerCount => player_count;

        public string HostAvatar => "R";
        public string PlayerCountText => $"{PlayerCount} players";
    }

    public class NodeInfo
    {
        public string ip { get; set; }
        public int port { get; set; }
    }

    public class LobbyViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<Room> _rooms;
        public ObservableCollection<Room> Rooms
        {
            get => _rooms;
            set { _rooms = value; OnPropertyChanged(); }
        }

        private readonly HttpClient _httpClient = new HttpClient();
        private const string BaseUrl = "http://localhost:5274/api/room"; // URL API của bạn

        // --- CÁC BIẾN BINDING CHO GIAO DIỆN MỚI ---
        private string _newRoomName = "My Awesome Room";
        public string NewRoomName { get => _newRoomName; set { _newRoomName = value; OnPropertyChanged(); } }

        private string _newRoomPassword = "";
        public string NewRoomPassword { get => _newRoomPassword; set { _newRoomPassword = value; OnPropertyChanged(); } }

        private bool _newRoomIsPrivate = false;
        public bool NewRoomIsPrivate { get => _newRoomIsPrivate; set { _newRoomIsPrivate = value; OnPropertyChanged(); } }

        private string _joinRoomIdStr = "";
        public string JoinRoomIdStr { get => _joinRoomIdStr; set { _joinRoomIdStr = value; OnPropertyChanged(); } }

        private string _joinRoomPassword = "";
        public string JoinRoomPassword { get => _joinRoomPassword; set { _joinRoomPassword = value; OnPropertyChanged(); } }
        // ------------------------------------------

        public ICommand JoinRoomCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand JoinManualCommand { get; }
        public ICommand CreateRoomCommand { get; }
        public ICommand RefreshCommand { get; }

        private bool _isProfilePopoverVisible;
        public bool IsProfilePopoverVisible
        {
            get => _isProfilePopoverVisible;
            set
            {
                _isProfilePopoverVisible = value;
                OnPropertyChanged();
            }
        }

        // ĐÃ SỬA: Thêm tham số thứ 3 (string password) vào Action để truyền sang Canvas
        public Action<string, string, string> GoToCanvas { get; set; }

        public LobbyViewModel()
        {
            Rooms = new ObservableCollection<Room>();
            JoinRoomCommand = new RelayCommand(ExecuteJoinRoomList);
            JoinManualCommand = new RelayCommand(ExecuteJoinRoomManual);
            CreateRoomCommand = new RelayCommand(async (obj) => await ExecuteCreateRoom());
            RefreshCommand = new RelayCommand(async (obj) => await LoadRooms());
            LogoutCommand = new RelayCommand(ExecuteLogout);

            // Load danh sách phòng ngay khi mở App
            _ = LoadRooms();
        }

        private void SetAuthHeader()
        {
            if (!string.IsNullOrEmpty(LoginViewModel.Token))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", LoginViewModel.Token);
            }
        }

        public async Task LoadRooms()
        {
            try
            {
                SetAuthHeader();

                var response = await _httpClient.GetAsync($"{BaseUrl}/list");
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var rooms = JsonSerializer.Deserialize<List<Room>>(jsonResponse, options);

                    if (rooms != null)
                    {
                        Rooms = new ObservableCollection<Room>(rooms);
                        ActiveRoomsCount = Rooms.Count;
                        TotalPlayers = Rooms.Sum(r => r.PlayerCount);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể lấy danh sách phòng: " + ex.Message);
            }
        }

        private async Task ExecuteCreateRoom()
        {
            try
            {
                var newRoomReq = new
                {
                    room_name = NewRoomName,
                    is_private = NewRoomIsPrivate,
                    password = string.IsNullOrWhiteSpace(NewRoomPassword) ? null : NewRoomPassword,
                    node_id = 1,
                    max_users = 10
                };

                string jsonString = JsonSerializer.Serialize(newRoomReq);
                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

                SetAuthHeader();

                var response = await _httpClient.PostAsync($"{BaseUrl}/create", content);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    int createdRoomId = 0;

                    using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                    {
                        createdRoomId = doc.RootElement.GetProperty("room_id").GetInt32();
                    }

                    // Gọi JoinApi luôn để tự động kết nối TCP và nhảy thẳng vào phòng
                    await CallJoinApi(createdRoomId, newRoomReq.password);

                    await LoadRooms();
                }
                else
                {
                    string err = await response.Content.ReadAsStringAsync();
                    MessageBox.Show("Lỗi từ server: " + err);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi tạo phòng: " + ex.Message);
            }
        }

        private async void ExecuteJoinRoomManual(object obj)
        {
            if (!int.TryParse(JoinRoomIdStr, out int roomId))
            {
                MessageBox.Show("Room ID phải là một số hợp lệ!");
                return;
            }

            string pass = string.IsNullOrWhiteSpace(JoinRoomPassword) ? null : JoinRoomPassword;
            await CallJoinApi(roomId, pass);
        }

        private async void ExecuteJoinRoomList(object obj)
        {
            if (obj is Room selectedRoom)
            {
                if (selectedRoom.is_private)
                {
                    MessageBox.Show($"Phòng này là Private. Vui lòng nhập Room ID ({selectedRoom.room_id}) và Mật khẩu ở khung bên phải để tham gia.");
                    JoinRoomIdStr = selectedRoom.room_id.ToString();
                }
                else
                {
                    await CallJoinApi(selectedRoom.room_id, null);
                }
            }
        }

        private async Task CallJoinApi(int roomId, string password)
        {
            try
            {
                var joinReq = new { room_id = roomId, password = password };

                string jsonString = JsonSerializer.Serialize(joinReq);
                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

                SetAuthHeader();

                var response = await _httpClient.PostAsync($"{BaseUrl}/join", content);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<Room>(jsonResponse, options);

                    System.Diagnostics.Debug.WriteLine(
                        "[LOGIN STATIC USER ID] = "
                        + LoginViewModel.CurrentUserId);

                    System.Diagnostics.Debug.WriteLine(
                        "[SOCKET USER ID BEFORE] = "
                        + ClientSocket.Instance.CurrentUserId);
                    
                    ClientSocket.Instance.CurrentUserId = LoginViewModel.CurrentUserId;

                    System.Diagnostics.Debug.WriteLine(
                        "[SOCKET USER ID AFTER] = "
                        + ClientSocket.Instance.CurrentUserId);

                    bool connected = ClientSocket.Instance.Connect(result.node.ip, result.node.port);
                    if (connected)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "[BEFORE SEND JOIN] USER ID = "
                            + ClientSocket.Instance.CurrentUserId);
                        // Gói tin JOIN bây giờ gửi kèm cả roomId và userId
                        ClientSocket.Instance.Send(new
                        {
                            type = "JOIN",
                            roomId = result.Id,
                            userId = ClientSocket.Instance.CurrentUserId
                        });

                        // Truyền thêm biến password vào hàm này
                        GoToCanvas?.Invoke(result.Id, result.room_name, password);
                    }
                }
                else
                {
                    string err = await response.Content.ReadAsStringAsync();
                    MessageBox.Show("Không thể tham gia phòng: " + err);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi hệ thống: " + ex.Message);
            }
        }
        private void ExecuteLogout(object obj)
        {
            Application.Current.Shutdown();
        }

        // --- CÁC HÀM XỬ LÝ SỰ KIỆN GIAO DIỆN  ---
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // --- THÔNG KÊ SERVER ---
        private int _activeRoomsCount = 0;
        public int ActiveRoomsCount
        {
            get => _activeRoomsCount;
            set { _activeRoomsCount = value; OnPropertyChanged(); }
        }

        private int _totalPlayers = 0;
        public int TotalPlayers
        {
            get => _totalPlayers;
            set { _totalPlayers = value; OnPropertyChanged(); }
        }
        // -----------------------
    }
}
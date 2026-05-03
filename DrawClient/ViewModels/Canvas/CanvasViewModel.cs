using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Text.Json;
using System.Windows.Media;
using DrawClient.Models;
using System.Windows;
using System;

namespace DrawClient.ViewModels
{
    public class UserParticipant
    {
        public string Initials { get; set; }
        public string ColorHex { get; set; }
    }

    public class ChatMessage
    {
        public string User { get; set; }
        public string Message { get; set; }
        public string Time { get; set; }
    }

    public class CanvasViewModel : INotifyPropertyChanged
    {
        private string _roomName;
        public string RoomName
        {
            get => _roomName;
            set { _roomName = value; OnPropertyChanged(); }
        }

        private string _roomId;
        public string RoomId
        {
            get => _roomId;
            set { _roomId = value; OnPropertyChanged(); }
        }

        private string _roomPassword;
        public string RoomPassword
        {
            get => _roomPassword;
            set { _roomPassword = value; OnPropertyChanged(); }
        }

        private void InitSocketListener()
        {
            ClientSocket.Instance.OnMessageReceived += (msg) =>
            {
                try
                {
                    // ĐÃ SỬA: Thêm options để bỏ qua các trường không tồn tại trong DrawMessage của Client
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var draw = JsonSerializer.Deserialize<DrawMessage>(msg, options);
                    if (draw == null) return;

                    if (draw.type == "DRAW")
                    {
                        var p1 = new Point(draw.x1, draw.y1);
                        var p2 = new Point(draw.x2, draw.y2);

                        OnLineReceived?.Invoke(p1, p2, draw.color, draw.thickness);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Parse error: " + ex.Message);
                }
            };
        }

        public ICommand LeaveRoomCommand { get; }
        public ICommand ShowRoomInfoCommand { get; }
        public Action GoBackToLobby { get; set; }

        public ObservableCollection<UserParticipant> Users { get; set; }
        public ObservableCollection<string> NetworkLogs { get; set; }
        public ObservableCollection<ChatMessage> ChatMessages { get; set; }

        private bool _isSidebarOpen = true;
        public bool IsSidebarOpen
        {
            get => _isSidebarOpen;
            set { _isSidebarOpen = value; OnPropertyChanged(); }
        }

        private string _currentColor = "#000000";
        public string CurrentColor
        {
            get => _currentColor;
            set { _currentColor = value; OnPropertyChanged(); }
        }

        private double _currentThickness = 2.0;
        public double CurrentThickness
        {
            get => _currentThickness;
            set { _currentThickness = value; OnPropertyChanged(); }
        }

        public event Action<Point, Point, string, double> OnLineReceived;

        public CanvasViewModel(string roomName, string roomId, string password = "")
        {
            RoomName = roomName;
            RoomId = roomId;
            RoomPassword = string.IsNullOrEmpty(password) ? "Không có mật khẩu" : password;

            InitSocketListener();

            LeaveRoomCommand = new RelayCommand(ExecuteLeaveRoom);
            ShowRoomInfoCommand = new RelayCommand(ExecuteShowRoomInfo);

            Users = new ObservableCollection<UserParticipant>
            {
                new UserParticipant { Initials = "JD", ColorHex = "#1A73E8" }
            };

            NetworkLogs = new ObservableCollection<string>
            {
                $"Joined Room: {roomName}",
                $"ID: {roomId}",
                $"Password: {RoomPassword}"
            };

            ChatMessages = new ObservableCollection<ChatMessage>();
        }

        public void SendDrawData(Point p1, Point p2)
        {
            var msg = new DrawMessage
            {
                type = "DRAW",
                roomId = this.RoomId,
                x1 = p1.X,
                y1 = p1.Y,
                x2 = p2.X,
                y2 = p2.Y,
                color = this.CurrentColor,
                thickness = this.CurrentThickness
            };
            ClientSocket.Instance.Send(msg);
        }

        private void ExecuteShowRoomInfo(object obj)
        {
            MessageBox.Show($"Room ID: {RoomId}\nPassword: {RoomPassword}", "Thông tin phòng", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExecuteLeaveRoom(object obj)
        {
            var leaveMsg = new DrawMessage { type = "LEAVE", roomId = RoomId };
            ClientSocket.Instance.Send(leaveMsg);
            GoBackToLobby?.Invoke();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
using DrawClient.Models;
using DrawClient.ViewModels.Canvas;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Ink;


namespace DrawClient.ViewModels
{
    public class UserParticipant
    {
        public string Initials { get; set; } = "";
        public string ColorHex { get; set; } = "";
    }

    public class ChatMessage
    {
        public string User { get; set; } = "";
        public string Message { get; set; } = "";
        public string Time { get; set; } = "";
    }

    public class CanvasViewModel : INotifyPropertyChanged
    {
        public ToolbarViewModel Toolbar { get; set; } = new ToolbarViewModel();

        public Action<Point, Point, string, double> OnLineReceived;
        public Action OnCanvasCleared;
        public Action GoBackToLobby;

        private bool _isCleanedUp = false;

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

        private bool _isColorMenuOpen;
        public bool IsColorMenuOpen
        {
            get => _isColorMenuOpen;
            set { _isColorMenuOpen = value; OnPropertyChanged(); }
        }

        private bool _isPenMenuOpen;
        public bool IsPenMenuOpen
        {
            get => _isPenMenuOpen;
            set { _isPenMenuOpen = value; OnPropertyChanged(); }
        }

        private string _currentPenType = "Brush";
        public string CurrentPenType
        {
            get => _currentPenType;
            set { _currentPenType = value; OnPropertyChanged(); }
        }

        private InkCanvasEditingMode _currentEditingMode = InkCanvasEditingMode.Ink;
        public InkCanvasEditingMode CurrentEditingMode
        {
            get => _currentEditingMode;
            set { _currentEditingMode = value; OnPropertyChanged(); }
        }

        private bool _isSidebarOpen = true;
        public GridLength RightSidebarWidth =>
            _isSidebarOpen
                ? new GridLength(320)
                : new GridLength(0);

        private bool _isProfilePopoverVisible;
        public bool IsProfilePopoverVisible
        {
            get => _isProfilePopoverVisible;
            set { _isProfilePopoverVisible = value; OnPropertyChanged(); }
        }

        private string _currentColor = "#000000";
        public string CurrentColor
        {
            get => _currentColor;
            set
            {
                _currentColor = value;
                OnPropertyChanged();
            }
        }

        private double _currentThickness = 4.0;
        public double CurrentThickness
        {
            get => _currentThickness;
            set
            {
                _currentThickness = value;
                OnPropertyChanged();
            }
        }

        private double _penThickness = 4.0;
        public double PenThickness
        {
            get => _penThickness;
            set
            {
                _penThickness = value;
                OnPropertyChanged();

                if (SelectedTool?.ToLower() == "pen" || SelectedTool?.ToLower() == "pencil" || SelectedTool?.ToLower() == "highlighter")
                {
                    CurrentThickness = value;
                }
            }
        }

        private double _eraserThickness = 20.0;
        public double EraserThickness
        {
            get => _eraserThickness;
            set
            {
                _eraserThickness = value;
                OnPropertyChanged();

                if (SelectedTool?.ToLower() == "eraser")
                {
                    CurrentThickness = value;
                }
            }
        }

        private string _currentUserInitials;
        public string CurrentUserInitials
        {
            get => _currentUserInitials;
            set { _currentUserInitials = value; OnPropertyChanged(); }
        }

        private string _selectedTool = "pen";
        public string SelectedTool
        {
            get => _selectedTool;
            set
            {
                _selectedTool = value;
                OnPropertyChanged();

                if (_selectedTool?.ToLower() == "eraser")
                {
                    CurrentThickness = EraserThickness;
                }
                else
                {
                    CurrentThickness = PenThickness;
                }
            }
        }

        private string _previousColor = "#000000";

        public ICommand LeaveRoomCommand { get; }
        public ICommand ShowRoomInfoCommand { get; }
        public ICommand ToggleSidebarCommand { get; }
        public ICommand ToggleProfilePopoverCommand { get; }
        public ICommand AccountManagerCommand { get; }
        public ICommand SelectToolCommand { get; }
        public ICommand ChooseColorCommand { get; }
        public ICommand ClearCanvasCommand { get; }
        public ICommand ToggleColorMenuCommand { get; }
        public ICommand TogglePenMenuCommand { get; }
        public ICommand ChangeColorCommand { get; }
        public ICommand ChangePenTypeCommand { get; }
        public ICommand ChangeThicknessCommand { get; }

        public ObservableCollection<UserParticipant> Users { get; set; }
        public ObservableCollection<string> NetworkLogs { get; set; }
        public ObservableCollection<ChatMessage> ChatMessages { get; set; }

        private bool _socketInitialized = false;

        public CanvasViewModel(string roomName, string roomId, string password = "")
        {
            _roomName = roomName;
            _roomId = roomId;
            _roomPassword = string.IsNullOrEmpty(password)
                ? "Không có mật khẩu"
                : password;

            Toolbar.ToolSelected += (sender, toolType) =>
            {
                ExecuteSelectTool(toolType);
            };

            InitSocketListener();

            LeaveRoomCommand = new RelayCommand(ExecuteLeaveRoom);
            ShowRoomInfoCommand = new RelayCommand(ExecuteShowRoomInfo);

            ToggleSidebarCommand = new RelayCommand(_ =>
            {
                _isSidebarOpen = !_isSidebarOpen;
                OnPropertyChanged(nameof(RightSidebarWidth));
            });

            ToggleProfilePopoverCommand = new RelayCommand(_ =>
            {
                IsProfilePopoverVisible = !IsProfilePopoverVisible;
            });

            AccountManagerCommand = new RelayCommand(_ =>
            {
                MessageBox.Show(
                    "Open Account Manager",
                    "Account",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                IsProfilePopoverVisible = false;
            });

            SelectToolCommand = new RelayCommand(ExecuteSelectTool);

            ChooseColorCommand = new RelayCommand(param =>
            {
                if (param != null)
                {
                    CurrentColor = param.ToString();

                    SelectedTool = "pen";
                    CurrentEditingMode = InkCanvasEditingMode.Ink;

                    if (CurrentColor != "#FFFFFF")
                    {
                        _previousColor = CurrentColor;
                    }
                }
            });

            ClearCanvasCommand = new RelayCommand(ExecuteClearCanvas);

            string safeUsername =
                LoginViewModel.CurrentUsername
                ?? ClientSocket.Instance.CurrentUsername
                ?? "U";

            CurrentUserInitials = GetInitials(safeUsername);

            Users = new ObservableCollection<UserParticipant>
            {
                new UserParticipant
                {
                    Initials = CurrentUserInitials,
                    ColorHex = "#1A73E8"
                }
            };

            NetworkLogs = new ObservableCollection<string>
            {
                $"Joined Room: {roomName}",
                $"ID: {roomId}",
                $"Password: {RoomPassword}"
            };

            ChatMessages = new ObservableCollection<ChatMessage>();

            ToggleColorMenuCommand = new RelayCommand(o =>
            {
                IsColorMenuOpen = !IsColorMenuOpen;

                if (IsColorMenuOpen)
                    IsPenMenuOpen = false;
            });

            TogglePenMenuCommand = new RelayCommand(o =>
            {
                IsPenMenuOpen = !IsPenMenuOpen;

                if (IsPenMenuOpen)
                    IsColorMenuOpen = false;
            });

            ChangeColorCommand = new RelayCommand(colorHex =>
            {
                if (colorHex is string hex)
                {
                    CurrentColor = hex;
                    Toolbar.CurrentColor = hex;

                    SelectedTool = "pen";
                    Toolbar.IsPencilSelected = true;  
                    Toolbar.IsEraserSelected = false;
                    CurrentEditingMode = InkCanvasEditingMode.Ink;
                }
            });

            ChangePenTypeCommand = new RelayCommand(penType =>
            {
                if (penType is string type)
                {
                    CurrentPenType = type;
                    Toolbar.CurrentPenType = type;

                    SelectedTool = "pen";
                    Toolbar.IsPencilSelected = true;   // Bật bút
                    Toolbar.IsEraserSelected = false;  // Tắt tẩy

                    CurrentEditingMode = InkCanvasEditingMode.Ink;
                    IsPenMenuOpen = false;
                }
            });

            ChangeThicknessCommand = new RelayCommand(thickness =>
            {
                if (double.TryParse(thickness.ToString(), out double t))
                {
                    if (SelectedTool?.ToLower() == "eraser")
                    {
                        EraserThickness = t;
                        Toolbar.EraserSize = t;
                    }
                    else
                    {
                        PenThickness = t;
                        Toolbar.PencilSize = t;
                    }
                }
            });
        }

        private void ExecuteSelectTool(object obj)
        {
            string tool = obj?.ToString()?.ToLower() ?? "pen";
            // Ngăn không cho các event cập nhật thuộc tính ghi đè lên SelectedTool
            if (tool == "sizechanged" || tool == "colorchanged" || tool == "pentypechanged")
            {
                return;
            }
            if (tool == "color")
            {
                IsColorMenuOpen = !IsColorMenuOpen;

                if (IsColorMenuOpen)
                    IsPenMenuOpen = false;

                return;
            }

            if (SelectedTool == tool)
            {
                IsColorMenuOpen = false;
                IsPenMenuOpen = false;

                if (tool == "select")
                {
                    CurrentEditingMode = InkCanvasEditingMode.Select;
                }

                return;
            }

            SelectedTool = tool;
            IsColorMenuOpen = false;
            IsPenMenuOpen = false; ;

            switch (tool)
            {
                case "select":
                    CurrentEditingMode = InkCanvasEditingMode.Select;
                    break;

                case "pencil":
                case "pen":
                    CurrentEditingMode = InkCanvasEditingMode.Ink;
                    Toolbar.IsPencilSelected = true;   
                    Toolbar.IsEraserSelected = false;

                    if (CurrentColor == "#FFFFFF")
                    {
                        CurrentColor = _previousColor;
                        Toolbar.CurrentColor = _previousColor;
                    }
                    break;

                case "eraser":
                    CurrentEditingMode = InkCanvasEditingMode.EraseByPoint;
                    Toolbar.IsEraserSelected = true;   
                    Toolbar.IsPencilSelected = false;
                    if (CurrentColor != "#FFFFFF")
                    {
                        _previousColor = CurrentColor;
                    }
                    break;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void InitSocketListener()
        {
            if (_socketInitialized) return;

            _socketInitialized = true;

            ClientSocket.Instance.OnMessageReceived += HandleSocketMessage;
        }

        private void HandleSocketMessage(string msg)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(msg))
                {
                    if (!doc.RootElement.TryGetProperty("type", out JsonElement typeElement))
                        return;

                    string type = typeElement.GetString();

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    var draw = JsonSerializer.Deserialize<DrawMessage>(msg, options);
                    if (draw == null)
                        return;

                    // FIX DOUBLE DRAW
                    if (draw.userId == ClientSocket.Instance.CurrentUserId)
                        return;

                    switch (type)
                    {
                        case "DRAW":
                            OnLineReceived?.Invoke(
                                new Point(draw.x1, draw.y1),
                                new Point(draw.x2, draw.y2),
                                draw.color,
                                draw.thickness);
                            break;

                        case "ERASE":
                            OnLineReceived?.Invoke(
                                new Point(draw.x1, draw.y1),
                                new Point(draw.x2, draw.y2),
                                "#ERASE",
                                draw.thickness);
                            break;

                        case "CLEAR":
                            OnCanvasCleared?.Invoke();
                            break;

                        case "LEAVE":
                            // optional handle
                            break;

                        default:
                            Console.WriteLine($"Unknown type: {type}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Parse error: " + ex.Message);
            }
        }

        public void SendDrawData(Point p1, Point p2)
        {
            string safeUsername =
                LoginViewModel.CurrentUsername
                ?? ClientSocket.Instance.CurrentUsername
                ?? "Unknown";

            var msg = new DrawMessage
            {
                type = SelectedTool?.ToLower() == "eraser" ? "ERASE" : "DRAW",
                roomId = RoomId,
                userId = ClientSocket.Instance.CurrentUserId,
                username = safeUsername,
                x1 = p1.X,
                y1 = p1.Y,
                x2 = p2.X,
                y2 = p2.Y,
                color = CurrentColor,
                thickness = Toolbar.CurrentThickness
            };

            ClientSocket.Instance.Send(msg);
        }

        private void ExecuteClearCanvas(object obj)
        {
            string safeUsername =
                LoginViewModel.CurrentUsername
                ?? ClientSocket.Instance.CurrentUsername
                ?? "Unknown";

            var msg = new DrawMessage
            {
                type = "CLEAR",
                roomId = RoomId,
                userId = ClientSocket.Instance.CurrentUserId,
                username = safeUsername,
            };

            ClientSocket.Instance.Send(msg);

            OnCanvasCleared?.Invoke();
        }

        private void ExecuteShowRoomInfo(object obj)
        {
            MessageBox.Show(
                $"Room ID: {RoomId}\nPassword: {RoomPassword}",
                "Thông tin phòng",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ExecuteLeaveRoom(object obj)
        {
            string safeUsername =
                LoginViewModel.CurrentUsername
                ?? ClientSocket.Instance.CurrentUsername
                ?? "Unknown";

            var leaveMsg = new DrawMessage
            {
                type = "LEAVE",
                roomId = RoomId,
                userId = ClientSocket.Instance.CurrentUserId,
                username = safeUsername,
            };

            ClientSocket.Instance.Send(leaveMsg);

            GoBackToLobby?.Invoke();
        }

        private string GetInitials(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return "U";

            string initials = "";

            string[] parts = username.Trim().Split(' ');

            foreach (string part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    initials += char.ToUpper(part[0]);
                }
            }

            return initials.Length > 2
                ? initials.Substring(0, 2)
                : initials;
        }

        public void Cleanup()
        {
            if (_isCleanedUp)
                return;

            _isCleanedUp = true;

            ClientSocket.Instance.OnMessageReceived -= HandleSocketMessage;
        }
    }
}
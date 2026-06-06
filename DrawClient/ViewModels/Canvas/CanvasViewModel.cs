using DrawClient.Models;
using DrawClient.Views;
using DrawClient.Services;
using DrawClient.ViewModels.Canvas;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace DrawClient.ViewModels
{
    public class UserParticipant
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Initials { get; set; } = "";
        public string ColorHex { get; set; } = "";
    }

    public class CanvasViewModel : INotifyPropertyChanged
    {
        public ToolbarViewModel Toolbar { get; set; } = new ToolbarViewModel();

        public Action<Point, Point, string, double, string, bool, string> OnLineReceived;
        public Action<string> OnSelectionTransformedReceived; // Thêm dòng này để View lắng nghe
        public Action<Point, Point, double> OnEraseReceived;
        public Action<Point, string, double, string, int> OnLaserReceived;
        public Action<DrawMessage> OnShapeReceived;
        public Action<DrawMessage> OnTextReceived;
        public Action<DrawMessage> OnDeleteTextReceived;
        public Action OnCanvasCleared;
        public Action GoBackToLobby;
        public UndoRedoManager UndoRedoManager { get; private set; } = new UndoRedoManager();
        public event Action OnUndoRedo;
        public event Action<DrawAction, bool> OnTransformUndoRedo;
        public Action GoToLogin { get; set; }
        private bool _isCleanedUp = false;
        // Replay/Play events - View subscribes to render each step
        public event Action<DrawMessage> OnReplayDraw;
        public event Action<DrawMessage> OnReplayErase;
        public event Action<DrawMessage> OnReplayShape;
        public event Action<DrawMessage> OnReplayText;
        public event Action<string> OnReplayUndo;   // actionId to undo
        public event Action<string> OnReplayRedo;   // actionId to redo
        public event Action OnReplayClear;
        public event Action OnReplayFinished;

        private List<DrawMessage> _rawHistory = new List<DrawMessage>();
        // Dùng để throttle UpdateHistoryUI: chỉ cập nhật khi groupId của DRAW thay đổi
        private string _lastReceivedDrawGroupId = null;
        private CancellationTokenSource _playCts;


        #region Properties
        private string _roomName;
        public string RoomName { get => _roomName; set { _roomName = value; OnPropertyChanged(); } }

        private string _roomId;
        public string RoomId { get => _roomId; set { _roomId = value; OnPropertyChanged(); } }

        private string _roomPassword;
        public string RoomPassword { get => _roomPassword; set { _roomPassword = value; OnPropertyChanged(); } }


        private bool _isColorMenuOpen;
        public bool IsColorMenuOpen { get => _isColorMenuOpen; set { _isColorMenuOpen = value; OnPropertyChanged(); } }

        private bool _isPenMenuOpen;
        public bool IsPenMenuOpen { get => _isPenMenuOpen; set { _isPenMenuOpen = value; OnPropertyChanged(); } }

        private string _currentPenType = "Brush";
        public string CurrentPenType { get => _currentPenType; set { _currentPenType = value; OnPropertyChanged(); } }

        private InkCanvasEditingMode _currentEditingMode = InkCanvasEditingMode.Select;
        public InkCanvasEditingMode CurrentEditingMode { get => _currentEditingMode; set { _currentEditingMode = value; OnPropertyChanged(); } }

        private bool _isSidebarOpen = false;
        public GridLength RightSidebarWidth => _isSidebarOpen ? new GridLength(320) : new GridLength(0);

        private bool _isProfilePopoverVisible;
        public bool IsProfilePopoverVisible { get => _isProfilePopoverVisible; set { _isProfilePopoverVisible = value; OnPropertyChanged(); } }

        private string _currentColor = "#000000";
        public string CurrentColor { get => _currentColor; set { _currentColor = value; OnPropertyChanged(); } }

        // Stroke group ID — shared by all segments drawn in a single MouseDown→MouseUp drag
        public string CurrentStrokeGroupId { get; set; }

        public void BeginStroke()
        {
            long id = System.Threading.Interlocked.Increment(ref _strokeGroupCounter);
            CurrentStrokeGroupId = $"G_{Environment.MachineName}_{id}_{DateTime.UtcNow.Ticks}";
        }

        public void EndStroke()
        {
            CurrentStrokeGroupId = null;
        }

        private static long _strokeGroupCounter = 0;

        private double _penThickness = 1.0;
        public double PenThickness
        {
            get => _penThickness;
            set
            {
                _penThickness = value;
                OnPropertyChanged();
                if (IsPenTool(Toolbar.CurrentPenType)) Toolbar.CurrentThickness = value;
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
                if (SelectedTool?.ToLower() == "eraser") Toolbar.CurrentThickness = value;
            }
        }

        private string _currentUserInitials;
        public string CurrentUserInitials { get => _currentUserInitials; set { _currentUserInitials = value; OnPropertyChanged(); } }

        private string _selectedTool = "select";
        public string SelectedTool
        {
            get => _selectedTool;
            set
            {
                _selectedTool = value;
                OnPropertyChanged();
                Toolbar.CurrentThickness = (_selectedTool?.ToLower() == "eraser") ? EraserThickness : PenThickness;
            }
        }

        private string _currentShape = "rectangle"; // Mặc định là hình chữ nhật
        public string CurrentShape
        {
            get => _currentShape;
            set { _currentShape = value; OnPropertyChanged(); }
        }


        private bool _isOcrToastVisible = false;

        public bool IsOcrToastVisible
        {
            get => _isOcrToastVisible;
            set { _isOcrToastVisible = value; OnPropertyChanged(); }
        }

        private bool _canUndo = false;
        public bool CanUndo
        {
            get => _canUndo;
            set { _canUndo = value; OnPropertyChanged(); }
        }

        private bool _canRedo = false;
        public bool CanRedo
        {
            get => _canRedo;
            set { _canRedo = value; OnPropertyChanged(); }
        }

        private string _historyInfo = "History: 0 Undo | 0 Redo";
        public string HistoryInfo
        {
            get => _historyInfo;
            set { _historyInfo = value; OnPropertyChanged(); }
        }

        private bool _isPlaying = false;
        public bool IsPlaying
        {
            get => _isPlaying;
            set { _isPlaying = value; OnPropertyChanged(); }
        }

        private double _playProgress = 0;
        public double PlayProgress
        {
            get => _playProgress;
            set { _playProgress = value; OnPropertyChanged(); }
        }

        private string _playProgressText = "0%";
        public string PlayProgressText
        {
            get => _playProgressText;
            set { _playProgressText = value; OnPropertyChanged(); }
        }

        #endregion

        #region Commands
        public ICommand LeaveRoomCommand { get; }
        public ICommand ShowRoomInfoCommand { get; }
        public ICommand ToggleSidebarCommand { get; }
        public ICommand ToggleProfilePopoverCommand { get; }
        public ICommand AccountManagerCommand { get; }
        public ICommand SelectToolCommand { get; }
        public ICommand ChooseColorCommand { get; }
        public ICommand ChooseTextColorCommand { get; }
        public ICommand ClearCanvasCommand { get; }
        public ICommand ToggleColorMenuCommand { get; }
        public ICommand TogglePenMenuCommand { get; }
        public ICommand ChangeColorCommand { get; }
        public ICommand ChangePenTypeCommand { get; }
        public ICommand ChangeThicknessCommand { get; }
        public ICommand ChangeShapeCommand { get; } // Lệnh đổi hình dạng

        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand PlayCommand { get; }
        public ICommand LogoutCommand { get; }

        public ICommand SendChatMessageCommand { get; }

        #endregion
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
                string username = LoginViewModel.CurrentUsername
                    ?? ClientSocket.Instance.CurrentUsername
                    ?? "Unknown";
                int userId = LoginViewModel.CurrentUserId > 0
                    ? LoginViewModel.CurrentUserId
                    : ClientSocket.Instance.CurrentUserId;

                MessageBox.Show(
                    $"Username: {username}\nUser ID: {userId}",
                    "Account Info",
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

                }
            });

            ChooseTextColorCommand = new RelayCommand(_ =>
            {
                var dlg = new System.Windows.Forms.ColorDialog();
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var c = dlg.Color;
                    Toolbar.CurrentTextColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                }
            });

            ClearCanvasCommand = new RelayCommand(ExecuteClearCanvas);
            //logout
            LogoutCommand = new RelayCommand(ExecuteLogout);
            // Undo/Redo
            UpdateHistoryUI();

            UndoCommand = new RelayCommand(_ => ExecuteUndo(), _ => CanUndo);
            RedoCommand = new RelayCommand(_ => ExecuteRedo(), _ => CanRedo);
            ClearHistoryCommand = new RelayCommand(_ => ExecuteClearHistory());
            PlayCommand = new RelayCommand(_ => TogglePlay());
            LogoutCommand = new RelayCommand(_ => ExecuteLogout());

            string safeUsername =
                LoginViewModel.CurrentUsername
                ?? ClientSocket.Instance.CurrentUsername
                ?? "U";

            CurrentUserInitials = GetInitials(safeUsername);

            Users = new ObservableCollection<UserParticipant>
            {
                new UserParticipant
                {
                    UserId = ClientSocket.Instance.CurrentUserId,
                    Username = safeUsername,
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

                    var shapes = new[] { "rectangle", "circle", "triangle", "line", "square", "ellipse" };
                    if (shapes.Any(s => string.Equals(s, type, StringComparison.OrdinalIgnoreCase)))
                    {
                        CurrentShape = type;
                        SelectedTool = "shape";
                        CurrentEditingMode = InkCanvasEditingMode.None;
                    }
                    else if (string.Equals(type, "laser", StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedTool = "pen";
                        Toolbar.IsPencilSelected = false;
                        Toolbar.IsEraserSelected = false;
                        CurrentEditingMode = InkCanvasEditingMode.None;
                    }
                    else
                    {
                        SelectedTool = "pen";
                        Toolbar.IsPencilSelected = true;
                        Toolbar.IsEraserSelected = false;
                        CurrentEditingMode = InkCanvasEditingMode.Ink;
                    }
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
            ChangeShapeCommand = new RelayCommand(param =>
            {
                if (param != null)
                {
                    CurrentShape = param.ToString();
                    SelectedTool = "shape"; // Tự động chuyển sang mode hình dạng
                    CurrentEditingMode = InkCanvasEditingMode.None;
                }
            });

            SendChatMessageCommand = new RelayCommand(_ => ExecuteSendChatMessage());
        }

        private void ExecuteSelectTool(object obj)
        {
            string tool = obj?.ToString()?.ToLower() ?? "pen";
            // Ngăn không cho các event cập nhật thuộc tính ghi đè lên SelectedTool
            if (tool == "sizechanged" || tool == "pentypechanged")
                return;

            if (tool == "colorchanged")
            {
                // Đồng bộ màu ngược lại CanvasViewModel để không bị reset khi switch tool
                CurrentColor = Toolbar.CurrentColor;
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
                if (tool == "ocr")
                {
                    ShowOcrToastTemporarily();
                }
                return;
            }

            SelectedTool = tool;
            IsColorMenuOpen = false;
            IsPenMenuOpen = false; ;

            if (tool.ToLowerInvariant() != "ocr")
            {
                IsOcrToastVisible = false;
                _ocrToastToken++;
            }

            switch (tool)
            {
                case "select":
                    CurrentEditingMode = InkCanvasEditingMode.Select;
                    Toolbar.IsPencilSelected = false;
                    Toolbar.IsEraserSelected = false;
                    Toolbar.IsShapeSelected = false;
                    Toolbar.IsTextSelected = false;
                    break;

                case "pencil":
                case "pen":
                    if (string.Equals(Toolbar.CurrentPenType, "laser", StringComparison.OrdinalIgnoreCase))
                    {
                        CurrentEditingMode = InkCanvasEditingMode.None;
                        Toolbar.IsPencilSelected = false;
                    }
                    else
                    {
                        CurrentEditingMode = InkCanvasEditingMode.Ink;
                        Toolbar.IsPencilSelected = true;
                    }
                    Toolbar.IsEraserSelected = false;
                    Toolbar.IsShapeSelected = false;
                    Toolbar.IsTextSelected = false;
                    Toolbar.CurrentColor = CurrentColor;
                    Toolbar.CurrentThickness = Toolbar.PencilSize;
                    if (string.IsNullOrEmpty(Toolbar.CurrentPenType) || IsShapeTool(Toolbar.CurrentPenType))
                    {
                        Toolbar.CurrentPenType = "brush";
                    }
                    break;

                case "eraser":
                    CurrentEditingMode = InkCanvasEditingMode.EraseByPoint;
                    Toolbar.IsEraserSelected = true;
                    Toolbar.IsPencilSelected = false;
                    Toolbar.IsShapeSelected = false;
                    Toolbar.IsTextSelected = false;
                    Toolbar.CurrentPenType = "eraser";
                    Toolbar.CurrentThickness = Toolbar.EraserSize;
                    break;
                case "shape":
                    CurrentEditingMode = InkCanvasEditingMode.None;
                    Toolbar.IsPencilSelected = false;
                    Toolbar.IsEraserSelected = false;
                    Toolbar.IsShapeSelected = true;
                    Toolbar.IsTextSelected = false;
                    Toolbar.CurrentThickness = Toolbar.CurrentShapeThickness;

                    var shapes = new System.Collections.Generic.List<string> { "square", "circle", "triangle", "line", "rectangle", "ellipse" };
                    if (!shapes.Contains(Toolbar.CurrentPenType?.ToLowerInvariant()))
                    {
                        Toolbar.CurrentPenType = "rectangle";
                    }
                    break;
                case "text":
                    CurrentEditingMode = InkCanvasEditingMode.None;
                    Toolbar.IsPencilSelected = false;
                    Toolbar.IsEraserSelected = false;
                    Toolbar.IsShapeSelected = false;
                    Toolbar.IsTextSelected = true;
                    break;

                case "ocr":
                    CurrentEditingMode = InkCanvasEditingMode.None;

                    if (Toolbar != null)
                    {
                        Toolbar.IsPencilSelected = false;
                        Toolbar.IsShapeSelected = false;
                        Toolbar.IsEraserSelected = false;
                        Toolbar.IsTextSelected = false;
                    }

                    ShowOcrToastTemporarily();
                    break;
            }
        }
        private bool IsShapeTool(string tool)
        {
            var shapes = new System.Collections.Generic.List<string> { "square", "rectangle", "circle", "ellipse", "triangle", "line" };
            return shapes.Contains(tool?.ToLowerInvariant());
        }

        private int _ocrToastToken = 0;

        private void ShowOcrToastTemporarily()
        {
            int currentToken = ++_ocrToastToken;

            IsOcrToastVisible = false;

            System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (currentToken != _ocrToastToken) return;
                IsOcrToastVisible = true;
                await Task.Delay(1500);

                if (currentToken == _ocrToastToken)
                {
                    IsOcrToastVisible = false;
                }
            }, System.Windows.Threading.DispatcherPriority.ContextIdle);
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

            ClientSocket.Instance.OnMessageReceived -= HandleSocketMessage;
            ClientSocket.Instance.OnMessageReceived += HandleSocketMessage;

            ClientSocket.Instance.OnConnectionLost -= HandleConnectionLost;
            ClientSocket.Instance.OnConnectionLost += HandleConnectionLost;

            // Nếu HISTORY đã đến trước khi Canvas UI được set up (race condition lúc vào phòng),
            // defer việc replay đến ApplicationIdle để đảm bảo Canvas_DataContextChanged đã chạy
            // và OnLineReceived đã được subscribe trước khi HISTORY bắt đầu dispatch các DRAW events.
            string pendingHistory = ClientSocket.Instance.LastHistoryJson;
            if (!string.IsNullOrEmpty(pendingHistory))
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Task.Run(() => HandleSocketMessage(pendingHistory));
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }

        private void ExecuteLogout(object obj)
        {
            try
            {
                // Ngắt kết nối socket giống Dashboard
                ClientSocket.Instance.Disconnect();
            }
            catch { }

            // Xóa sạch Session đăng nhập hệ thống công khai
            LoginViewModel.Token = null;
            LoginViewModel.CurrentUserId = 0;
            LoginViewModel.CurrentUsername = null;

            if (ClientSocket.Instance != null)
            {
                ClientSocket.Instance.CurrentUserId = 0;
                ClientSocket.Instance.CurrentUsername = string.Empty;
            }

            // Ẩn popup đi
            IsProfilePopoverVisible = false;

            // Điều hướng ứng dụng quay trở về màn hình LoginScreen bằng Main Thread UI
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow != null)
                {
                    Application.Current.MainWindow.Content = new LoginScreen();
                }
            });
        }
        private void HandleConnectionLost(ConnectionLostArgs args)
        {
            InvokeUI(() =>
            {
                MessageBox.Show("Mất kết nối tới máy chủ vẽ.\nBạn sẽ được đưa về màn hình chờ.",
                    "Mất kết nối", MessageBoxButton.OK, MessageBoxImage.Warning);
                Cleanup();
                GoBackToLobby?.Invoke();
            });
        }

        private readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private void HandleSocketMessage(string msg)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(msg))
                {
                    if (!doc.RootElement.TryGetProperty("type", out var typeEl))
                        return;

                    string type = typeEl.GetString();
                    if (string.IsNullOrEmpty(type)) return;

                    // HISTORY
                    if (type == "HISTORY")
                    {
                        if (!doc.RootElement.TryGetProperty("actions", out var actions))
                            return;

                        _rawHistory.Clear();
                        foreach (var item in actions.EnumerateArray())
                        {
                            var h = JsonSerializer.Deserialize<DrawMessage>(item.GetRawText(), _jsonOptions);
                            if (h != null) _rawHistory.Add(h);
                        }

                        // Snapshot để tránh race condition khi socket thread modify _rawHistory
                        // trong lúc InvokeUI lambda đang iterate trên UI thread
                        var historySnapshot = _rawHistory.ToList();

                        // Tính tập hợp message active (không bị UNDO) trực tiếp từ raw history
                        // — không bị giới hạn MAX_HISTORY của UndoRedoManager
                        var activeMessages = ComputeActiveMessages(historySnapshot);

                        // Cập nhật UndoRedoManager để tính năng Undo/Redo hoạt động sau khi vào phòng
                        UndoRedoManager.Clear();
                        foreach (var draw in historySnapshot)
                        {
                            if (draw.type == "DRAW" || draw.type == "ERASE" || draw.type == "SHAPE" || draw.type == "TEXT")
                            {
                                var action = new DrawAction(
                                    draw.type,
                                    new Point(draw.x1, draw.y1),
                                    new Point(draw.x2, draw.y2),
                                    draw.color,
                                    draw.thickness,
                                    draw.userId,
                                    draw.username,
                                    RoomId)
                                {
                                    penType = draw.penType,
                                    ShapeType = draw.shapeType,
                                    Text = draw.text,
                                    FontSize = draw.fontSize,
                                    FontFamily = draw.fontFamily,
                                    StrokeGroupId = draw.strokeGroupId
                                };
                                if (!string.IsNullOrEmpty(draw.actionId))
                                    action.Id = draw.actionId;
                                // Khôi phục AffectedActionIds cho ERASE-text (dùng text field làm carrier)
                                if (draw.type == "ERASE" && !string.IsNullOrEmpty(draw.text))
                                    action.AffectedActionIds = new List<string> { draw.text };
                                UndoRedoManager.AddAction(action);
                                draw.actionId = action.Id;
                            }
                            else if (draw.type == "UNDO")
                            {
                                if (!string.IsNullOrEmpty(draw.strokeGroupId))
                                    UndoRedoManager.UndoByGroupId(draw.strokeGroupId);
                                else
                                    UndoRedoManager.Undo(draw.userId);
                            }
                            else if (draw.type == "REDO")
                            {
                                if (!string.IsNullOrEmpty(draw.strokeGroupId))
                                    UndoRedoManager.RedoByGroupId(draw.strokeGroupId);
                                else
                                    UndoRedoManager.Redo(draw.userId);
                            }
                            else if (draw.type == "CLEAR") UndoRedoManager.Clear();
                        }

                        InvokeUI(() =>
                        {
                            OnCanvasCleared?.Invoke();
                            // Replay active draws AND TRANSFORM_SELECTIONs theo đúng thứ tự thời gian.
                            // TRANSFORM_SELECTION dùng chỉ số stroke tại thời điểm ghi — cần theo dõi
                            // danh sách stroke hiện tại để kiểm tra index có còn hợp lệ không.
                            var activeSet = new HashSet<DrawMessage>(activeMessages);
                            // Tracks stroke-adding messages in canvas order (mirrors MyCanvas.Strokes).
                            // Mỗi strokeGroupId DRAW chỉ đếm 1 entry vì DrawNetworkLine gom các segment
                            // vào 1 stroke — khớp với native InkCanvas stroke để TRANSFORM index đúng.
                            var currentStrokeList = new List<DrawMessage>();
                            var seenDrawGroups = new HashSet<string>();

                            foreach (var h in historySnapshot)
                            {
                                if (activeSet.Contains(h))
                                {
                                    DispatchDraw(h);
                                    if (h.type == "SHAPE")
                                    {
                                        currentStrokeList.Add(h);
                                    }
                                    else if (h.type == "DRAW")
                                    {
                                        string gid = !string.IsNullOrEmpty(h.strokeGroupId) ? h.strokeGroupId : h.actionId;
                                        if (string.IsNullOrEmpty(gid) || seenDrawGroups.Add(gid))
                                            currentStrokeList.Add(h);
                                    }
                                }
                                else if (h.type == "DELETE_TEXT")
                                {
                                    // DELETE_TEXT luôn active — dispatch để xóa TextBlock cũ khỏi canvas
                                    DispatchDraw(h);
                                }
                                else if (h.type == "TRANSFORM_SELECTION" && !string.IsNullOrEmpty(h.text))
                                {
                                    bool indexSafe = true;
                                    try
                                    {
                                        var firstPart = h.text.Split('|')[0];
                                        foreach (var idxStr in firstPart.Split(','))
                                        {
                                            string trimmed = idxStr.Trim();
                                            if (string.IsNullOrEmpty(trimmed)) continue;

                                            if (trimmed.StartsWith("G:") || trimmed.StartsWith("T:"))
                                            {
                                                // GroupId/TextActionId-based: luôn an toàn
                                                continue;
                                            }
                                            else if (int.TryParse(trimmed, out int idx))
                                            {
                                                if (idx < 0 || idx >= currentStrokeList.Count ||
                                                    !activeSet.Contains(currentStrokeList[idx]))
                                                {
                                                    indexSafe = false;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    catch { indexSafe = false; }

                                    if (indexSafe)
                                    {
                                        var json = JsonSerializer.Serialize(new { type = "TRANSFORM_SELECTION", h.text, userId = -1 });
                                        OnSelectionTransformedReceived?.Invoke(json);
                                    }
                                }
                            }
                        });

                        UpdateHistoryUI();
                        return;
                    }

                    // CHAT_HISTORY
                    if (type == "CHAT_HISTORY")
                    {
                        if (!doc.RootElement.TryGetProperty("messages", out var messages))
                            return;

                        foreach (var item in messages.EnumerateArray())
                        {
                            var chat = JsonSerializer.Deserialize<DrawMessage>(item.GetRawText(), _jsonOptions);
                            if (chat == null) continue;
                            DispatchDraw(chat);
                        }

                        return;
                    }

                    //select
                    if (type == "TRANSFORM_SELECTION")
                    {
                        // Kiểm tra xem ID người gửi có phải là chính mình không, nếu phải thì bỏ qua
                        if (doc.RootElement.TryGetProperty("userId", out var userEl) &&
                            userEl.GetInt32() == ClientSocket.Instance.CurrentUserId)
                        {
                            return;
                        }

                        // Chuyển thẳng chuỗi json thô (biến msg) sang cho View (Canvas.xaml.cs) xử lý giao diện
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            OnSelectionTransformedReceived?.Invoke(msg); // Thay thế hoàn toàn thành biến 'msg'
                        });
                        return;
                    }

                    // NORMAL MESSAGE
                    var drawMsg = JsonSerializer.Deserialize<DrawMessage>(msg, _jsonOptions);
                    if (drawMsg == null) return;

                    switch (drawMsg.type)
                    {
                        case "DRAW":
                            // Own echoed DRAW messages: InkCanvas đã render native stroke rồi,
                            // không render lại qua DispatchDraw để tránh duplicate stroke.
                            if (drawMsg.userId != ClientSocket.Instance.CurrentUserId)
                            {
                                DispatchDraw(drawMsg);
                                var drawAction = new DrawAction(
                                    "DRAW",
                                    new Point(drawMsg.x1, drawMsg.y1),
                                    new Point(drawMsg.x2, drawMsg.y2),
                                    drawMsg.color,
                                    drawMsg.thickness,
                                    drawMsg.userId,
                                    drawMsg.username,
                                    RoomId)
                                {
                                    penType = drawMsg.penType,
                                    StrokeGroupId = drawMsg.strokeGroupId
                                };
                                if (!string.IsNullOrEmpty(drawMsg.actionId))
                                    drawAction.Id = drawMsg.actionId;
                                UndoRedoManager.AddAction(drawAction);
                                // Throttle: chỉ cập nhật UI khi bắt đầu một group mới,
                                // tránh gọi UpdateHistoryUI 50+ lần cho từng segment của một nét.
                                if (drawMsg.strokeGroupId != _lastReceivedDrawGroupId)
                                {
                                    _lastReceivedDrawGroupId = drawMsg.strokeGroupId;
                                    UpdateHistoryUI();
                                }
                            }
                            break;

                        case "ERASE":
                            DispatchDraw(drawMsg);
                            if (drawMsg.userId != ClientSocket.Instance.CurrentUserId)
                            {
                                var eraseAction = new DrawAction(
                                    "ERASE",
                                    new Point(drawMsg.x1, drawMsg.y1),
                                    new Point(drawMsg.x2, drawMsg.y2),
                                    "#ERASE",
                                    drawMsg.thickness,
                                    drawMsg.userId,
                                    drawMsg.username,
                                    RoomId)
                                {
                                    StrokeGroupId = drawMsg.strokeGroupId
                                };
                                if (!string.IsNullOrEmpty(drawMsg.actionId))
                                    eraseAction.Id = drawMsg.actionId;
                                // text field carries original text's actionId for precise TextBlock removal on undo/redo
                                if (!string.IsNullOrEmpty(drawMsg.text))
                                    eraseAction.AffectedActionIds = new List<string> { drawMsg.text };
                                UndoRedoManager.AddAction(eraseAction);
                                UpdateHistoryUI();
                            }
                            break;

                        case "LASER":
                            InvokeUI(() => OnLaserReceived?.Invoke(new Point(drawMsg.x1, drawMsg.y1), drawMsg.color, drawMsg.thickness, drawMsg.penType?.Trim(), drawMsg.userId));
                            break;

                        case "SHAPE":
                            // Không render cho own user vì shape đã được preview tại Canvas_MouseMove
                            if (drawMsg.userId != ClientSocket.Instance.CurrentUserId)
                            {
                                // Thêm vào UndoRedoManager TRƯỚC DispatchDraw để DrawShape có thể
                                // tìm action qua GetActionById và set _actionIdToStroke đúng.
                                var shapeAction = new DrawAction(
                                    "SHAPE",
                                    new Point(drawMsg.x1, drawMsg.y1),
                                    new Point(drawMsg.x2, drawMsg.y2),
                                    drawMsg.color,
                                    drawMsg.thickness,
                                    drawMsg.userId,
                                    drawMsg.username,
                                    RoomId)
                                {
                                    ShapeType = drawMsg.shapeType,
                                    StrokeGroupId = drawMsg.strokeGroupId
                                };
                                if (!string.IsNullOrEmpty(drawMsg.actionId))
                                    shapeAction.Id = drawMsg.actionId;
                                UndoRedoManager.AddAction(shapeAction);
                                DispatchDraw(drawMsg);
                                UpdateHistoryUI();
                            }
                            break;

                        case "TEXT":
                            // Thêm vào UndoRedoManager TRƯỚC DispatchDraw để DrawText có thể
                            // map _childToAction và _actionIdToChild đúng (giống SHAPE).
                            if (drawMsg.userId != ClientSocket.Instance.CurrentUserId)
                            {
                                var textAction = new DrawAction(
                                    "TEXT",
                                    new Point(drawMsg.x1, drawMsg.y1),
                                    new Point(drawMsg.x2, drawMsg.y2),
                                    drawMsg.color,
                                    0,
                                    drawMsg.userId,
                                    drawMsg.username,
                                    RoomId)
                                {
                                    Text = drawMsg.text,
                                    FontSize = drawMsg.fontSize,
                                    FontFamily = drawMsg.fontFamily,
                                    StrokeGroupId = drawMsg.strokeGroupId
                                };
                                if (!string.IsNullOrEmpty(drawMsg.actionId))
                                    textAction.Id = drawMsg.actionId;
                                UndoRedoManager.AddAction(textAction);
                                UpdateHistoryUI();
                            }
                            DispatchDraw(drawMsg);
                            break;

                        case "UNDO":
                            if (drawMsg.userId == ClientSocket.Instance.CurrentUserId)
                                return;
                            ApplyUndoFromNetwork(drawMsg.strokeGroupId, drawMsg.actionId, drawMsg.userId);
                            break;

                        case "REDO":
                            if (drawMsg.userId == ClientSocket.Instance.CurrentUserId)
                                return;
                            ApplyRedoFromNetwork(drawMsg.strokeGroupId, drawMsg.actionId, drawMsg.userId);
                            break;

                        case "CLEAR":
                            DispatchDraw(drawMsg);
                            UndoRedoManager.Clear();
                            break;

                        default:
                            DispatchDraw(drawMsg);
                            break;
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine("JSON parse error: " + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Socket parse error: " + ex);
            }
        }

        private void DispatchDraw(DrawMessage draw)
        {
            switch (draw.type)
            {
                case "JOIN":
                    // 1. In log thẳng ra màn hình Console của Visual Studio để xác nhận Client đã nhận được lệnh
                    Console.WriteLine($"[MẠNG - NHẬN LỆNH] '{draw.username}' (ID: {draw.userId}) vừa vào phòng.");

                    InvokeUI(() =>
                    {
                        // Nếu gói tin là của chính bản thân mình -> Bỏ qua
                        if (draw.userId == ClientSocket.Instance.CurrentUserId)
                        {
                            Console.WriteLine("-> Đây là ID của bản thân, bỏ qua.");
                            return;
                        }

                        string joinedUsername = string.IsNullOrEmpty(draw.username) ? $"User {draw.userId}" : draw.username;
                        string initials = GetInitials(joinedUsername);

                        // Kiểm tra xem user này đã có trong danh sách bên Sidebar chưa
                        if (!Users.Any(u => u.UserId == draw.userId))
                        {
                            Users.Add(new UserParticipant
                            {
                                UserId = draw.userId,
                                Username = joinedUsername,
                                Initials = initials,
                                ColorHex = "#4CAF50"
                            });

                            Console.WriteLine($"-> Đã thêm '{joinedUsername}' vào UI.");

                            // Thêm log chat hệ thống
                            if (NetworkLogs != null)
                            {
                                NetworkLogs.Add($"[MẠNG] {joinedUsername} đã vào phòng.");
                            }
                        }
                        else
                        {
                            Console.WriteLine("-> User này đã có trên UI, không thêm nữa.");
                        }
                    });
                    break;
                case "DRAW":
                    InvokeUI(() =>
                        OnLineReceived?.Invoke(
                            new Point(draw.x1, draw.y1),
                            new Point(draw.x2, draw.y2),
                            draw.color,
                            draw.thickness,
                            draw.penType,
                            draw.isHighlighter,
                            draw.strokeGroupId));
                    break;



                case "LASER":
                    InvokeUI(() =>
                        OnLaserReceived?.Invoke(
                            new Point(draw.x1, draw.y1),
                            draw.color,
                            draw.thickness,
                            draw.penType,
                            draw.userId));
                    break;

                // 
                case "UNDO":
                    ApplyUndoFromNetwork(draw.strokeGroupId, draw.actionId, draw.userId);
                    break;

                case "REDO":
                    ApplyRedoFromNetwork(draw.strokeGroupId, draw.actionId, draw.userId);
                    break;

                case "SHAPE":
                    InvokeUI(() => OnShapeReceived?.Invoke(draw));
                    break;

                case "TEXT":
                    InvokeUI(() => OnTextReceived?.Invoke(draw));
                    break;

                case "DELETE_TEXT":
                    InvokeUI(() => OnDeleteTextReceived?.Invoke(draw));
                    break;

                case "CLEAR":
                    InvokeUI(() => OnCanvasCleared?.Invoke());
                    break;

                case "TRANSFORM_SELECTION":
                    InvokeUI(() => OnSelectionTransformedReceived?.Invoke(draw.text));
                    break;

                case "LEAVE":
                    InvokeUI(() =>
                    {
                        string leftUsername = string.IsNullOrEmpty(draw.username) ? $"User {draw.userId}" : draw.username;

                        // Ghi nhận log mạng hiển thị lên góc màn hình phòng vẽ
                        NetworkLogs.Add($"[MẠNG] {leftUsername} đã rời phòng.");

                        // Tự động xóa user này ra khỏi danh sách Online Users hiển thị ở Sidebar bên phải
                        string targetInitials = GetInitials(leftUsername);
                        var userToRemove = Users.FirstOrDefault(u => u.Initials == targetInitials);
                        if (userToRemove != null)
                        {
                            Users.Remove(userToRemove);
                        }
                    });
                    break;

                case "CHAT":
                    InvokeUI(() =>
                    {
                        DateTime messageTime =
                            draw.timestamp == default
                                ? DateTime.Now
                                : draw.timestamp;

                        AddChatMessage(draw.username, draw.text, messageTime,
                            draw.userId == ClientSocket.Instance.CurrentUserId);
                    });
                    break;
                case "ERASE":
                    InvokeUI(() =>
                        OnEraseReceived?.Invoke(
                            new Point(draw.x1, draw.y1),
                            new Point(draw.x2, draw.y2),
                            draw.thickness));
                    break;
            }
        }

        private void InvokeUI(Action action)
        {
            Application.Current.Dispatcher.Invoke(action);
        }
        public void SendDrawData(Point p1, Point p2)
        {
            // 1. LASER POINTER: Không gửi laser lên server - chỉ là visual feedback tạm thời
            if (Toolbar.CurrentPenType?.ToLower() == "laser")
                return;

            // 2. Tránh gửi dữ liệu khi khoảng cách di chuyển quá nhỏ hoặc trùng nhau
            if (Math.Abs(p1.X - p2.X) < 0.5 && Math.Abs(p1.Y - p2.Y) < 0.5)
                return;

            bool isEraser = Toolbar.IsEraserSelected || SelectedTool?.ToLower() == "eraser";
            bool isShape = SelectedTool?.ToLower() == "shape";

            // Khởi tạo message chứa các thông tin cơ bản chung
            var msg = new DrawMessage
            {
                roomId = RoomId,
                userId = ClientSocket.Instance.CurrentUserId,
                username = ClientSocket.Instance.CurrentUsername,
                x1 = p1.X,
                y1 = p1.Y,
                x2 = p2.X,
                y2 = p2.Y,
                penType = Toolbar.CurrentPenType
            };

            // Đơn giản hóa cấu hình dữ liệu theo từng loại công cụ
            if (isEraser)
            {
                msg.type = "ERASE";
                msg.color = "#ERASE";
                msg.thickness = Toolbar.EraserSize;
                msg.isHighlighter = false;
            }
            else if (isShape)
            {
                msg.type = "SHAPE";
                msg.shapeType = CurrentShape;
                msg.color = Toolbar.CurrentColor;
                msg.thickness = Toolbar.CurrentThickness; // Đã sửa: Đồng bộ chính xác độ dày của Shape
                msg.isHighlighter = false;
            }
            else // Trường hợp DRAW thông thường
            {
                msg.type = "DRAW";
                msg.color = Toolbar.CurrentColor;
                msg.thickness = Toolbar.PencilSize;
                msg.penType = Toolbar.CurrentPenType;

                // Nếu là công cụ Highlighter (bút dạ quang), xử lý màu sắc đặc biệt
                if (Toolbar.CurrentPenType?.ToLower() == "highlighter")
                {
                    msg.color = "[HL]" + msg.color;
                    msg.isHighlighter = true;
                }
                else
                {
                    msg.isHighlighter = false;
                }
            }


            // 3. Thêm hành động vào Undo/Redo manager
            var drawAction = new DrawAction(
                msg.type,
                p1,
                p2,
                msg.color,
                msg.thickness,
                msg.userId,
                msg.username,
                RoomId
            )
            {
                penType = msg.penType,
                ShapeType = isShape ? CurrentShape : null,
                StrokeGroupId = CurrentStrokeGroupId
            };
            UndoRedoManager.AddAction(drawAction);
            UpdateHistoryUI();

            // Gắn actionId và strokeGroupId để client nhận có thể khớp khi UNDO/REDO
            msg.actionId = drawAction.Id;
            msg.strokeGroupId = drawAction.StrokeGroupId ?? drawAction.Id;

            // 4. Gửi dữ liệu đã xử lý nhất quán sang server qua Socket
            if (p1.X == p2.X && p1.Y == p2.Y) return;

            ClientSocket.Instance.Send(msg);
        }
        // childIndicesData: index của TextBlock trong Children.OfType<TextBlock>() (để sync di chuyển text)
        public void SendSelectionTransform(string indicesData, Rect oldBounds, Rect newBounds, string childIndicesData = "")
        {
            // Format: "{strokeIndices}|{oldBounds}|{newBounds}|{childIndices}"
            string transformData = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}|{1},{2},{3},{4}|{5},{6},{7},{8}|{9}",
                indicesData,
                oldBounds.X, oldBounds.Y, oldBounds.Width, oldBounds.Height,
                newBounds.X, newBounds.Y, newBounds.Width, newBounds.Height,
                childIndicesData ?? "");

            ClientSocket.Instance.Send(new DrawMessage
            {
                type = "TRANSFORM_SELECTION",
                roomId = RoomId,
                userId = ClientSocket.Instance.CurrentUserId,
                username = ClientSocket.Instance.CurrentUsername,
                text = transformData
            });
        }
        public DrawAction SendText(string text, Point p, double width, double height, double fontSize = 14, string fontFamily = null, string color = null, string strokeGroupId = null)
        {
            fontFamily = fontFamily ?? Toolbar.CurrentTextFont;
            color = color ?? Toolbar.CurrentTextColor;

            var textAction = new DrawAction(
                "TEXT",
                p,
                new Point(width, height),
                color,
                0,
                ClientSocket.Instance.CurrentUserId,
                ClientSocket.Instance.CurrentUsername,
                RoomId)
            {
                Text = text,
                FontSize = fontSize,
                FontFamily = fontFamily,
                StrokeGroupId = strokeGroupId
            };
            UndoRedoManager.AddAction(textAction);
            UpdateHistoryUI();

            var msg = new DrawMessage
            {
                type = "TEXT",
                roomId = this.RoomId,
                userId = ClientSocket.Instance.CurrentUserId,
                text = text,
                x1 = p.X,
                y1 = p.Y,
                x2 = width,
                y2 = height,
                fontSize = fontSize,
                fontFamily = fontFamily,
                color = color,
                actionId = textAction.Id,
                strokeGroupId = textAction.StrokeGroupId
            };

            ClientSocket.Instance.Send(msg);
            return textAction;
        }
        public void SendDeleteText(double x, double y, string textContent)
        {
            var msg = new DrawMessage
            {
                type = "DELETE_TEXT",
                roomId = this.RoomId,
                userId = ClientSocket.Instance.CurrentUserId,
                x1 = x,
                y1 = y,
                text = textContent
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
            UndoRedoManager.Clear();
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

            Cleanup();
            ClientSocket.Instance.Disconnect();

            GoBackToLobby?.Invoke();
        }

        private void ExecuteLogout()
        {
            // Gửi LEAVE trước khi đăng xuất
            string safeUsername = LoginViewModel.CurrentUsername
                ?? ClientSocket.Instance.CurrentUsername ?? "Unknown";
            try
            {
                ClientSocket.Instance.Send(new DrawMessage
                {
                    type = "LEAVE",
                    roomId = RoomId,
                    userId = ClientSocket.Instance.CurrentUserId,
                    username = safeUsername,
                });
            }
            catch { }

            Cleanup();

            try { ClientSocket.Instance.Disconnect(); } catch { }

            // Xóa thông tin đăng nhập
            LoginViewModel.CurrentUsername = null;

            IsProfilePopoverVisible = false;

            // Thông báo rời phòng cho người dùng
            MessageBox.Show(
                "Bạn đã đăng xuất thành công.\nMọi người trong phòng đã được thông báo bạn rời đi.",
                "Rời phòng",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Về màn hình đăng nhập — đảm bảo chạy trên UI thread
            if (GoToLogin == null)
            {
                System.Diagnostics.Debug.WriteLine("[CanvasViewModel] GoToLogin delegate is null — navigation skipped.");
                return;
            }
            Application.Current?.Dispatcher.Invoke(() => GoToLogin.Invoke());
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

        private bool IsPenTool(string tool)
        {
            if (string.IsNullOrWhiteSpace(tool))
                return false;

            string t = tool.Trim().ToLowerInvariant();

            return t == "pencil"
                || t == "brush"
                || t == "fountain"
                || t == "highlighter"
                || t == "laser";
        }

        private void ExecuteUndo()
        {
            int userId = ClientSocket.Instance.CurrentUserId;
            var undoneActions = UndoRedoManager.Undo(userId);
            if (undoneActions == null) return;

            if (undoneActions.Count > 0)
            {
                if (undoneActions[0].ActionType == "TRANSFORM")
                {
                    // Fire TRƯỚC OnUndoRedo để Canvas còn stroke ở vị trí cũ (sau di chuyển)
                    // → Canvas tính index và gửi reverse TRANSFORM_SELECTION cho các client khác
                    OnTransformUndoRedo?.Invoke(undoneActions[0], true);
                }
                else
                {
                    string groupId = undoneActions[0].StrokeGroupId ?? undoneActions[0].Id;
                    ClientSocket.Instance.Send(new DrawMessage
                    {
                        type = "UNDO",
                        roomId = RoomId,
                        userId = userId,
                        username = ClientSocket.Instance.CurrentUsername,
                        strokeGroupId = groupId
                    });
                }
            }
            OnUndoRedo?.Invoke();
            UpdateHistoryUI();
        }

        private void ExecuteRedo()
        {
            int userId = ClientSocket.Instance.CurrentUserId;
            var redoneActions = UndoRedoManager.Redo(userId);
            if (redoneActions == null) return;

            if (redoneActions.Count > 0)
            {
                if (redoneActions[0].ActionType == "TRANSFORM")
                {
                    // Fire TRƯỚC OnUndoRedo để Canvas tính index đúng rồi gửi reverse transform
                    OnTransformUndoRedo?.Invoke(redoneActions[0], false);
                }
                else
                {
                    string groupId = redoneActions[0].StrokeGroupId ?? redoneActions[0].Id;
                    ClientSocket.Instance.Send(new DrawMessage
                    {
                        type = "REDO",
                        roomId = RoomId,
                        userId = userId,
                        username = ClientSocket.Instance.CurrentUsername,
                        strokeGroupId = groupId
                    });
                }
            }
            OnUndoRedo?.Invoke();
            UpdateHistoryUI();
        }

        private void ApplyUndoFromNetwork(string strokeGroupId, string actionId, int userId)
        {
            InvokeUI(() =>
            {
                try
                {
                    bool anyUndone = false;

                    // Ưu tiên group-based undo (chính xác nhất)
                    if (!string.IsNullOrEmpty(strokeGroupId))
                        anyUndone = UndoRedoManager.UndoByGroupId(strokeGroupId);

                    // Fallback: stack-based undo theo userId (khi strokeGroupId không khớp)
                    if (!anyUndone)
                    {
                        var undoneList = UndoRedoManager.Undo(userId);
                        anyUndone = undoneList != null; // null = không có gì, empty = trimmed
                    }

                    if (anyUndone)
                        OnUndoRedo?.Invoke();
                }
                finally
                {
                    UpdateHistoryUI();
                }
            });
        }

        private void ApplyRedoFromNetwork(string strokeGroupId, string actionId, int userId)
        {
            InvokeUI(() =>
            {
                try
                {
                    bool anyRedone = false;

                    if (!string.IsNullOrEmpty(strokeGroupId))
                        anyRedone = UndoRedoManager.RedoByGroupId(strokeGroupId);

                    if (!anyRedone)
                    {
                        var redoneList = UndoRedoManager.Redo(userId);
                        anyRedone = redoneList != null;
                    }

                    if (anyRedone)
                        OnUndoRedo?.Invoke();
                }
                finally
                {
                    UpdateHistoryUI();
                }
            });
        }

        private void ExecuteClearHistory()
        {
            UndoRedoManager.Clear();
            UpdateHistoryUI();
        }

        private void TogglePlay()
        {
            if (IsPlaying)
                StopPlay();
            else
                _ = PlayHistory();
        }

        public void StopPlay()
        {
            _playCts?.Cancel();
            IsPlaying = false;
            PlayProgress = 0;
            PlayProgressText = "0%";
        }

        private async Task PlayHistory()
        {
            if (_rawHistory == null || _rawHistory.Count == 0)
            {
                MessageBox.Show("Không có lịch sử để phát lại!", "Play", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsPlaying = true;
            PlayProgress = 0;
            PlayProgressText = "0%";
            _playCts = new CancellationTokenSource();
            var token = _playCts.Token;

            // Clear canvas before replay
            OnCanvasCleared?.Invoke();

            // Build actionId -> DrawMessage map from history for undo/redo lookup
            var actionMap = new Dictionary<string, DrawMessage>();
            var undoStack = new Stack<string>(); // Stack of undone actionIds for redo tracking
            int currentIndex = 0;
            int total = _rawHistory.Count;

            try
            {
                foreach (var action in _rawHistory)
                {
                    token.ThrowIfCancellationRequested();

                    // Calculate delay based on action type
                    int delayMs = 50;
                    if (action.type == "DRAW" || action.type == "ERASE")
                        delayMs = 30;
                    else if (action.type == "SHAPE" || action.type == "TEXT" || action.type == "TRANSFORM_SELECTION")
                        delayMs = 100;
                    else if (action.type == "UNDO" || action.type == "REDO")
                        delayMs = 200;
                    else if (action.type == "CLEAR")
                        delayMs = 300;

                    // Process the action
                    switch (action.type)
                    {
                        case "DRAW":
                        case "ERASE":
                        case "SHAPE":
                        case "TEXT":
                            // Store with a generated actionId for undo/redo tracking
                            if (string.IsNullOrEmpty(action.actionId))
                                action.actionId = $"replay_{currentIndex}";
                            actionMap[action.actionId] = action;

                            if (action.type == "DRAW")
                                OnReplayDraw?.Invoke(action);
                            else if (action.type == "ERASE")
                                OnReplayErase?.Invoke(action);
                            else if (action.type == "SHAPE")
                                OnReplayShape?.Invoke(action);
                            else if (action.type == "TEXT")
                                OnReplayText?.Invoke(action);
                            break;

                        case "UNDO":
                            // Find the most recent non-undone action and undo it
                            for (int i = currentIndex - 1; i >= 0; i--)
                            {
                                var prev = _rawHistory[i];
                                if ((prev.type == "DRAW" || prev.type == "ERASE" ||
                                     prev.type == "SHAPE" || prev.type == "TEXT") &&
                                    !string.IsNullOrEmpty(prev.actionId) &&
                                    actionMap.ContainsKey(prev.actionId))
                                {
                                    OnReplayUndo?.Invoke(prev.actionId);
                                    undoStack.Push(prev.actionId); // Remember for redo
                                    actionMap.Remove(prev.actionId);
                                    break;
                                }
                            }
                            break;

                        case "REDO":
                            // Redo the most recently undone action
                            if (undoStack.Count > 0)
                            {
                                string redoActionId = undoStack.Pop();
                                OnReplayRedo?.Invoke(redoActionId);
                                // Find the original action data to restore in actionMap
                                for (int i = currentIndex - 1; i >= 0; i--)
                                {
                                    var prev = _rawHistory[i];
                                    if (prev.actionId == redoActionId)
                                    {
                                        actionMap[prev.actionId] = prev;
                                        break;
                                    }
                                }
                            }
                            break;

                        case "CLEAR":
                            OnReplayClear?.Invoke();
                            actionMap.Clear();
                            undoStack.Clear();
                            break;

                        case "TRANSFORM_SELECTION":
                            if (!string.IsNullOrEmpty(action.text))
                            {
                                string replayTransformJson = JsonSerializer.Serialize(
                                    new { type = "TRANSFORM_SELECTION", action.text, userId = -1 });
                                InvokeUI(() => OnSelectionTransformedReceived?.Invoke(replayTransformJson));
                            }
                            break;
                    }

                    currentIndex++;
                    PlayProgress = (double)currentIndex / total * 100;
                    PlayProgressText = $"{(int)PlayProgress}%";

                    await Task.Delay(delayMs, token);
                }

                PlayProgress = 100;
                PlayProgressText = "100%";
            }
            catch (OperationCanceledException)
            {
                // Play was stopped by user
            }
            finally
            {
                IsPlaying = false;
                OnReplayFinished?.Invoke();
            }
        }

        public void UpdateHistoryUI()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                int userId = ClientSocket.Instance.CurrentUserId;
                CanUndo = UndoRedoManager.CanUndo(userId);
                CanRedo = UndoRedoManager.CanRedo(userId);
                HistoryInfo = $"History: {UndoRedoManager.UndoCount} Undo | {UndoRedoManager.RedoCount} Redo";
                CommandManager.InvalidateRequerySuggested();
            });
        }
        public void Cleanup()
        {
            if (_isCleanedUp)
                return;

            _isCleanedUp = true;

            StopPlay();
            ClientSocket.Instance.OnMessageReceived -= HandleSocketMessage;
            ClientSocket.Instance.OnConnectionLost -= HandleConnectionLost;
        }

        private string _currentChatMessage;

        public string CurrentChatMessage
        {
            get => _currentChatMessage;
            set
            {
                _currentChatMessage = value;
                OnPropertyChanged();
            }
        }

        private void ExecuteSendChatMessage()
        {
            if (string.IsNullOrWhiteSpace(CurrentChatMessage))
                return;

            var now = DateTime.Now;
            var text = CurrentChatMessage.Trim();

            ClientSocket.Instance.Send(new DrawMessage
            {
                type = "CHAT",
                roomId = RoomId,
                userId = ClientSocket.Instance.CurrentUserId,
                username = ClientSocket.Instance.CurrentUsername,
                text = text,
                timestamp = now
            });

            // Server không echo lại sender, hiện tin nhắn ngay lập tức
            AddChatMessage(ClientSocket.Instance.CurrentUsername, text, now, isMine: true);

            CurrentChatMessage = "";
        }

        private void AddChatMessage(string user, string text, DateTime time, bool isMine)
        {
            bool showSeparator = ChatMessages.Count == 0
                || ChatMessages.Last().Timestamp.Date != time.Date
                || (time - ChatMessages.Last().Timestamp).TotalMinutes >= 15;

            ChatMessages.Add(new ChatMessage
            {
                User = user,
                Message = text,
                Timestamp = time,
                ShowSeparator = showSeparator,
                IsMine = isMine
            });
        }

        // Tính tập hợp DrawMessage đang "active" (không bị UNDO) từ raw history.
        // Không dùng UndoRedoManager nên không bị giới hạn MAX_HISTORY.
        private List<DrawMessage> ComputeActiveMessages(List<DrawMessage> rawHistory)
        {
            var drawMessages = new List<DrawMessage>();
            var activeSet = new HashSet<int>();
            // groupKey → list of indices in drawMessages
            var groupToIndices = new Dictionary<string, List<int>>();
            var undoStacks = new Dictionary<int, Stack<string>>();
            var redoStacks = new Dictionary<int, Stack<string>>();

            foreach (var draw in rawHistory)
            {
                if (draw.type == "DRAW" || draw.type == "ERASE" ||
                    draw.type == "SHAPE" || draw.type == "TEXT")
                {
                    int idx = drawMessages.Count;
                    drawMessages.Add(draw);
                    activeSet.Add(idx);

                    string groupKey = !string.IsNullOrEmpty(draw.strokeGroupId) ? draw.strokeGroupId
                                    : !string.IsNullOrEmpty(draw.actionId) ? draw.actionId
                                    : idx.ToString();

                    if (!groupToIndices.ContainsKey(groupKey))
                        groupToIndices[groupKey] = new List<int>();
                    groupToIndices[groupKey].Add(idx);

                    if (!undoStacks.ContainsKey(draw.userId))
                    {
                        undoStacks[draw.userId] = new Stack<string>();
                        redoStacks[draw.userId] = new Stack<string>();
                    }
                    if (undoStacks[draw.userId].Count == 0 || undoStacks[draw.userId].Peek() != groupKey)
                        undoStacks[draw.userId].Push(groupKey);
                    redoStacks[draw.userId].Clear();
                }
                else if (draw.type == "UNDO")
                {
                    string groupKey = PopGroupFromStack(undoStacks, draw.userId, draw.strokeGroupId);
                    if (groupKey != null)
                    {
                        if (groupToIndices.TryGetValue(groupKey, out var idxList))
                            foreach (int i in idxList) activeSet.Remove(i);
                        EnsureStack(redoStacks, draw.userId).Push(groupKey);
                    }
                }
                else if (draw.type == "REDO")
                {
                    string groupKey = PopGroupFromStack(redoStacks, draw.userId, draw.strokeGroupId);
                    if (groupKey != null)
                    {
                        if (groupToIndices.TryGetValue(groupKey, out var idxList))
                            foreach (int i in idxList) activeSet.Add(i);
                        EnsureStack(undoStacks, draw.userId).Push(groupKey);
                    }
                }
                else if (draw.type == "CLEAR")
                {
                    drawMessages.Clear();
                    activeSet.Clear();
                    groupToIndices.Clear();
                    undoStacks.Clear();
                    redoStacks.Clear();
                }
            }

            var result = new List<DrawMessage>();
            for (int i = 0; i < drawMessages.Count; i++)
                if (activeSet.Contains(i))
                    result.Add(drawMessages[i]);
            return result;
        }

        private static string PopGroupFromStack(Dictionary<int, Stack<string>> stacks, int userId, string preferredGroupId)
        {
            if (!stacks.TryGetValue(userId, out var stack) || stack.Count == 0)
                return null;

            if (!string.IsNullOrEmpty(preferredGroupId))
            {
                var temp = new Stack<string>();
                bool found = false;
                while (stack.Count > 0)
                {
                    string g = stack.Pop();
                    if (g == preferredGroupId && !found) { found = true; break; }
                    temp.Push(g);
                }
                while (temp.Count > 0) stack.Push(temp.Pop());
                // If not found, do NOT fall back to popping the top — skip this undo entirely.
                return found ? preferredGroupId : null;
            }

            return stack.Pop();
        }

        private static Stack<string> EnsureStack(Dictionary<int, Stack<string>> stacks, int userId)
        {
            if (!stacks.ContainsKey(userId))
                stacks[userId] = new Stack<string>();
            return stacks[userId];
        }
    }
}

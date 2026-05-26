using DrawClient.Models;
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

        public Action<Point, Point, string, double, string, bool> OnLineReceived;
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
        private bool _isCleanedUp = false;
        private bool _isApplyingRemoteUndoRedo = false;

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

        private string _previousColor = "#000000";

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
            // Undo/Redo
            UpdateHistoryUI();

            UndoCommand = new RelayCommand(_ => ExecuteUndo(), _ => CanUndo);
            RedoCommand = new RelayCommand(_ => ExecuteRedo(), _ => CanRedo);
            ClearHistoryCommand = new RelayCommand(_ => ExecuteClearHistory());
            PlayCommand = new RelayCommand(_ => TogglePlay());

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

                        // Store raw history for Play feature
                        _rawHistory.Clear();
                        foreach (var item in actions.EnumerateArray())
                        {
                            var h = JsonSerializer.Deserialize<DrawMessage>(item.GetRawText(), _jsonOptions);
                            if (h != null) _rawHistory.Add(h);
                        }

                        // Pass 1: Replay all actions in order to build correct canvas state
                        foreach (var item in actions.EnumerateArray())
                        {
                            var draw = JsonSerializer.Deserialize<DrawMessage>(item.GetRawText(), _jsonOptions);
                            if (draw == null) continue;

                            if (draw.type == "DRAW" || draw.type == "ERASE" || draw.type == "SHAPE" || draw.type == "TEXT")
                            {
                                // Add to undo manager (creates action with ID)
                                var action = new DrawAction(
                                    draw.type,
                                    new Point(draw.x1, draw.y1),
                                    new Point(draw.x2, draw.y2),
                                    draw.color,
                                    draw.thickness,
                                    draw.userId,
                                    draw.username,
                                    RoomId);
                                UndoRedoManager.AddAction(action);
                                // Store the actionId in the draw message for later reference
                                draw.actionId = action.Id;
                            }
                            else if (draw.type == "UNDO")
                            {
                                // Find the most recent non-undone action by this user and undo it
                                UndoRedoManager.Undo(draw.userId);
                            }
                            else if (draw.type == "REDO")
                            {
                                UndoRedoManager.Redo(draw.userId);
                            }
                            else if (draw.type == "CLEAR")
                            {
                                UndoRedoManager.Clear();
                            }
                        }

                        // Pass 2: Render only active (non-undone) actions on canvas
                        InvokeUI(() =>
                        {
                            OnCanvasCleared?.Invoke();
                            var activeActions = UndoRedoManager.GetAllActions();
                            foreach (var action in activeActions)
                            {
                                // Re-dispatch each active action to render on canvas
                                var redrawMsg = new DrawMessage
                                {
                                    type = action.ActionType,
                                    x1 = action.StartPoint.X,
                                    y1 = action.StartPoint.Y,
                                    x2 = action.EndPoint.X,
                                    y2 = action.EndPoint.Y,
                                    color = action.Color,
                                    thickness = action.Thickness,
                                    penType = action.penType,
                                    shapeType = action.ShapeType,
                                    text = action.Text,
                                    fontSize = action.FontSize
                                };
                                DispatchDraw(redrawMsg);
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
                            DispatchDraw(drawMsg);
                            if (drawMsg.userId != ClientSocket.Instance.CurrentUserId)
                            {
                                UndoRedoManager.AddAction(new DrawAction(
                                    "DRAW",
                                    new Point(drawMsg.x1, drawMsg.y1),
                                    new Point(drawMsg.x2, drawMsg.y2),
                                    drawMsg.color,
                                    drawMsg.thickness,
                                    drawMsg.userId,
                                    drawMsg.username,
                                    RoomId));
                            }
                            break;

                        //case "TRANSFORM_SELECTION":
                        //    Application.Current.Dispatcher.Invoke(() =>
                        //    {
                        //        // Kích hoạt sự kiện để View (Canvas.xaml.cs) nghe thấy và xử lý
                        //        OnSelectionTransformedReceived?.Invoke(msg);
                        //    });
                        //    break;

                        case "ERASE":
                            DispatchDraw(drawMsg);
                            if (drawMsg.userId != ClientSocket.Instance.CurrentUserId)
                            {
                                UndoRedoManager.AddAction(new DrawAction(
                                    "ERASE",
                                    new Point(drawMsg.x1, drawMsg.y1),
                                    new Point(drawMsg.x2, drawMsg.y2),
                                    "#ERASE",
                                    drawMsg.thickness,
                                    drawMsg.userId,
                                    drawMsg.username,
                                    RoomId));
                            }
                            break;

                        case "LASER":
                            InvokeUI(() => OnLaserReceived?.Invoke(new Point(drawMsg.x1, drawMsg.y1), drawMsg.color, drawMsg.thickness, drawMsg.penType?.Trim(), drawMsg.userId));
                            break;

                        case "SHAPE":
                            DispatchDraw(drawMsg);
                            if (drawMsg.userId != ClientSocket.Instance.CurrentUserId)
                            {
                                UndoRedoManager.AddAction(new DrawAction(
                                    "SHAPE",
                                    new Point(drawMsg.x1, drawMsg.y1),
                                    new Point(drawMsg.x2, drawMsg.y2),
                                    drawMsg.color,
                                    drawMsg.thickness,
                                    drawMsg.userId,
                                    drawMsg.username,
                                    RoomId));
                            }
                            break;

                        case "TEXT":
                            DispatchDraw(drawMsg);
                            if (drawMsg.userId != ClientSocket.Instance.CurrentUserId)
                            {
                                UndoRedoManager.AddAction(new DrawAction(
                                    "TEXT",
                                    new Point(drawMsg.x1, drawMsg.y1),
                                    new Point(drawMsg.x2, drawMsg.y2),
                                    drawMsg.color,
                                    drawMsg.thickness,
                                    drawMsg.userId,
                                    drawMsg.username,
                                    RoomId));
                            }
                            break;

                        case "UNDO":
                            if (drawMsg.userId == ClientSocket.Instance.CurrentUserId)
                                return;
                            ApplyUndoFromNetwork(drawMsg.actionId, drawMsg.userId);
                            break;

                        case "REDO":
                            if (drawMsg.userId == ClientSocket.Instance.CurrentUserId)
                                return;
                            ApplyRedoFromNetwork(drawMsg.actionId, drawMsg.userId);
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
                            draw.isHighlighter));
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
                    ApplyUndoFromNetwork(draw.actionId, draw.userId);
                    break;

                case "REDO":
                    ApplyRedoFromNetwork(draw.actionId, draw.userId);
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

                        bool showSeparator = false;

                        if (ChatMessages.Count == 0)
                        {
                            showSeparator = true;
                        }
                        else
                        {
                            var last = ChatMessages.Last();

                            bool differentDay =
                                last.Timestamp.Date != messageTime.Date;

                            bool over15Minutes =
                                (messageTime - last.Timestamp).TotalMinutes >= 15;

                            if (differentDay || over15Minutes)
                                showSeparator = true;
                        }

                        ChatMessages.Add(new ChatMessage
                        {
                            User = draw.username,
                            Message = draw.text,
                            Timestamp = messageTime,
                            ShowSeparator = showSeparator,
                            IsMine = draw.userId == ClientSocket.Instance.CurrentUserId
                        });
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


            // 3. Gom việc thêm hành động vào quản lý Undo/Redo ra ngoài khối điều kiện
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
                ShapeType = isShape ? CurrentShape : null
            };
            UndoRedoManager.AddAction(drawAction);

            // 4. Gửi dữ liệu đã xử lý nhất quán sang server qua Socket
            if (p1.X == p2.X && p1.Y == p2.Y) return;

            ClientSocket.Instance.Send(msg);
        }
        public void SendSelectionTransform(string indicesData, Rect oldBounds, Rect newBounds)
        {
            // Ép kiểu chuỗi dạng InvariantCulture để dùng dấu chấm (.) cho số thực, bất chấp máy dùng Win Tiếng Việt hay Tiếng Anh
            string transformData = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0}|{1},{2},{3},{4}|{5},{6},{7},{8}",
                indicesData,
                oldBounds.X, oldBounds.Y, oldBounds.Width, oldBounds.Height,
                newBounds.X, newBounds.Y, newBounds.Width, newBounds.Height);

            ClientSocket.Instance.Send(new DrawMessage
            {
                type = "TRANSFORM_SELECTION",
                roomId = RoomId,
                userId = ClientSocket.Instance.CurrentUserId,
                username = ClientSocket.Instance.CurrentUsername,
                text = transformData
            });
        }
        public void SendText(string text, Point p, double width, double height, double fontSize = 14)
        {
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
                color = Toolbar.CurrentColor
            };

            ClientSocket.Instance.Send(msg);
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
            if (UndoRedoManager.CanUndo(userId))
            {
                var undone = UndoRedoManager.Undo(userId);
                if (undone != null)
                {
                    // Send UNDO with specific actionId to server
                    ClientSocket.Instance.Send(new DrawMessage
                    {
                        type = "UNDO",
                        roomId = RoomId,
                        userId = userId,
                        username = ClientSocket.Instance.CurrentUsername,
                        actionId = undone.Id
                    });
                    OnUndoRedo?.Invoke();
                    UpdateHistoryUI();
                }
            }
        }

        private void ExecuteRedo()
        {
            int userId = ClientSocket.Instance.CurrentUserId;
            if (UndoRedoManager.CanRedo(userId))
            {
                var redone = UndoRedoManager.Redo(userId);
                if (redone != null)
                {
                    // Send REDO with specific actionId to server
                    ClientSocket.Instance.Send(new DrawMessage
                    {
                        type = "REDO",
                        roomId = RoomId,
                        userId = userId,
                        username = ClientSocket.Instance.CurrentUsername,
                        actionId = redone.Id
                    });
                    OnUndoRedo?.Invoke();
                    UpdateHistoryUI();
                }
            }
        }

        private void ApplyUndoFromNetwork(string actionId, int userId)
        {
            InvokeUI(() =>
            {
                _isApplyingRemoteUndoRedo = true;
                try
                {
                    DrawAction undone = null;
                    if (!string.IsNullOrEmpty(actionId))
                        undone = UndoRedoManager.UndoById(actionId);
                    else
                        undone = UndoRedoManager.Undo(userId);

                    if (undone != null)
                    {
                        OnUndoRedo?.Invoke();
                        Console.WriteLine($"[NETWORK] Undo action {undone.Id} by user {userId}");
                    }
                }
                finally
                {
                    _isApplyingRemoteUndoRedo = false;
                    UpdateHistoryUI();
                }
            });
        }

        private void ApplyRedoFromNetwork(string actionId, int userId)
        {
            InvokeUI(() =>
            {
                _isApplyingRemoteUndoRedo = true;
                try
                {
                    DrawAction redone = null;
                    if (!string.IsNullOrEmpty(actionId))
                        redone = UndoRedoManager.RedoById(actionId);
                    else
                        redone = UndoRedoManager.Redo(userId);

                    if (redone != null)
                    {
                        OnUndoRedo?.Invoke();
                        Console.WriteLine($"[NETWORK] Redo action {redone.Id} by user {userId}");
                    }
                }
                finally
                {
                    _isApplyingRemoteUndoRedo = false;
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
                    else if (action.type == "SHAPE" || action.type == "TEXT")
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

        private void UpdateHistoryUI()
        {
            int userId = ClientSocket.Instance.CurrentUserId;
            CanUndo = UndoRedoManager.CanUndo(userId);
            CanRedo = UndoRedoManager.CanRedo(userId);
            HistoryInfo = $"History: {UndoRedoManager.UndoCount} Undo | {UndoRedoManager.RedoCount} Redo";
        }
        public void Cleanup()
        {
            if (_isCleanedUp)
                return;

            _isCleanedUp = true;

            StopPlay();
            ClientSocket.Instance.OnMessageReceived -= HandleSocketMessage;
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

            ClientSocket.Instance.Send(new DrawMessage
            {
                type = "CHAT",
                roomId = RoomId,
                userId = ClientSocket.Instance.CurrentUserId,
                username = ClientSocket.Instance.CurrentUsername,
                text = CurrentChatMessage.Trim(),
                timestamp = DateTime.Now
            });

            CurrentChatMessage = "";
        }
    }
}

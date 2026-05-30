using DrawClient.Models;
using DrawClient.Services;
using DrawClient.ViewModels;
using DrawClient.Views.UserControls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

// For replay undo/redo tracking

namespace DrawClient.Views.UserControls
{
    public partial class Canvas : UserControl
    {
        private Point lastPoint;
        private bool isDrawing = false;
        private CanvasViewModel _viewModel;
        private Point _startPoint;
        private Stroke _currentTempStroke;
        private bool isShapeDrawing = false;
        // Lưu StrokeGroupId trước khi EndStroke() xóa nó, dùng cho StrokeCollected và eraser click.
        private string _lastStrokeGroupId;
        private System.Windows.Shapes.Rectangle _ocrSelectionRect;
        private Point _ocrStartPoint;
        private DispatcherTimer _laserTimer;
        // Laser visual trail
        private Polyline _currentLaserPolyline;
        private readonly TimeSpan _laserFadeDuration = TimeSpan.FromMilliseconds(1200);
        private readonly double _laserThickness = 8.0;
        // Quản lý Laser của các client khác: UserId -> (Đường Laser, Timer xóa mờ)
        private Dictionary<string, (Polyline Line, DispatcherTimer Timer)> _remoteLasers = new Dictionary<string, (Polyline, DispatcherTimer)>();
        //khai bái biến lưu vị trí cũ
        private Rect _oldSelectionBounds;

        // Maps StrokeGroupId -> native InkCanvas strokes (smooth, multi-point).
        // Used by RedrawAllFromActions to restore exact native strokes on undo/redo
        // instead of choppy 2-point segment reconstructions.
        private readonly Dictionary<string, List<Stroke>> _groupNativeStrokes = new Dictionary<string, List<Stroke>>();

        // Replay: maps actionId -> Stroke for undo/redo during playback
        private Dictionary<string, Stroke> _replayStrokeMap = new Dictionary<string, Stroke>();
        private Dictionary<string, DrawMessage> _replayActionMap = new Dictionary<string, DrawMessage>();
        private HashSet<string> _replayUndoneActions = new HashSet<string>();

        // Stroke/UIElement → DrawAction: dùng để tìm DrawAction từ stroke khi move
        private readonly Dictionary<Stroke, DrawAction> _strokeToAction = new Dictionary<Stroke, DrawAction>();
        private readonly Dictionary<UIElement, DrawAction> _childToAction = new Dictionary<UIElement, DrawAction>();
        // DrawAction.Id → Stroke/UIElement: dùng trong Phase 2 RedrawAllFromActions để apply TRANSFORM
        private readonly Dictionary<string, Stroke> _actionIdToStroke = new Dictionary<string, Stroke>();
        private readonly Dictionary<string, UIElement> _actionIdToChild = new Dictionary<string, UIElement>();
        // Lưu bản gốc (chưa transform) của native PEN strokes để replay transform đúng
        private readonly Dictionary<string, List<Stroke>> _groupNativeOriginals = new Dictionary<string, List<Stroke>>();
        // strokeGroupId → stroke nhận từ mạng; mỗi group tích lũy thành 1 stroke thay vì nhiều đoạn rời
        private readonly Dictionary<string, Stroke> _networkGroupStrokes = new Dictionary<string, Stroke>();
        // groupId → strokes trong lần redraw hiện tại (dùng Phase 2 apply TRANSFORM cho PEN)
        private Dictionary<string, List<Stroke>> _currentRedrawGroupStrokes = new Dictionary<string, List<Stroke>>();

        // Inline text editing state
        private Border _activeTextWrapper = null;
        private TextBox _activeTextBox = null;
        private TextBlock _editingExistingBlock = null;
        private Border _textFloatingToolbar = null;
        private Border _colorIndicatorBtn = null;
        private bool _colorPickerOpen = false;

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            // 1. Chủ động ép Popup đóng ngay lập tức để không bị kẹt giao diện
            ProfilePopover.IsOpen = false;

            // 2. Cập nhật lại biến Binding trong ViewModel (nếu có) để đồng bộ trạng thái công khai
            if (this.DataContext is CanvasViewModel vm)
            {
                // Nếu trong CanvasViewModel của bạn có thuộc tính công khai này, hãy uncomment dòng dưới:
                // vm.IsProfilePopoverVisible = false;
            }

            // 3. Dừng các Timer chạy ngầm của Canvas nếu có (Ví dụ laser timer tránh leak bộ nhớ)
            if (_laserTimer != null && _laserTimer.IsEnabled)
            {
                _laserTimer.Stop();
            }

            // 4. Xóa dữ liệu phiên đăng nhập cũ trong LoginViewModel tĩnh (static)
            LoginViewModel.Token = null;
            LoginViewModel.CurrentUserId = 0;
            LoginViewModel.CurrentUsername = string.Empty;

            // 5. Reset thông tin định danh trên Socket kết nối 
            if (ClientSocket.Instance != null)
            {
                ClientSocket.Instance.CurrentUserId = 0;
                ClientSocket.Instance.CurrentUsername = string.Empty;
                // Nếu hệ thống của bạn có hàm đóng hẳn kết nối phòng, hãy gọi ở đây:
                // ClientSocket.Instance.Disconnect(); 
            }

            // 6. Tìm MainWindow của ứng dụng và đổi Content về lại LoginScreen công khai
            Window mainWindow = Window.GetWindow(this);
            if (mainWindow != null)
            {
                mainWindow.Content = new LoginScreen();
            }
        }

        private void Logout_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ép ViewModel thực thi lệnh Logout ngay khi nhấn chuột xuống, 
            // trước khi InkCanvas làm mất focus và đóng Popup
            if (this.DataContext is CanvasViewModel vm && vm.LogoutCommand != null)
            {
                if (vm.LogoutCommand.CanExecute(null))
                {
                    vm.LogoutCommand.Execute(null);
                    e.Handled = true; // Đánh dấu đã xử lý để tránh lỗi double-click hoặc kẹt sự kiện
                }
            }
        }


        public Canvas()
        {
            InitializeComponent();

            this.DataContextChanged += Canvas_DataContextChanged;

            this.PreviewMouseDown += UserControl_PreviewMouseDown;
            //laser
            this.MyCanvas.MouseMove += InkCanvas_MouseMove;

            _laserTimer = new DispatcherTimer();
            _laserTimer.Interval = TimeSpan.FromSeconds(3);

            _laserTimer.Tick += (s, e) =>
            {
                _laserTimer.Stop();
                FadeOutLaser();
            };


            // FIX MEMORY LEAK
            this.Unloaded += Canvas_Unloaded;

            // Khởi tạo thuộc tính vẽ mặc định cho Canvas
            MyCanvas.DefaultDrawingAttributes = new DrawingAttributes
            {
                FitToCurve = true,
                IgnorePressure = true,
                Width = 2,
                Height = 2,
                Color = Colors.Black
            };
            // Đăng ký sự kiện thay đổi trên InkCanvas
            this.MyCanvas.SelectionChanged += MyCanvas_SelectionChanged;
            this.MyCanvas.SelectionMoved += MyCanvas_SelectionMoved;
            this.MyCanvas.SelectionResized += MyCanvas_SelectionResized;
            this.MyCanvas.SelectionMoving += MyCanvas_SelectionMoving;
            this.MyCanvas.StrokeCollected += MyCanvas_StrokeCollected;
        }
        private void MyCanvas_SelectionMoving(object sender, InkCanvasSelectionEditingEventArgs e)
        {
            // e.OldRectangle bao gồm cả strokes lẫn UIElement children (TextBlock) →
            // đúng hơn GetSelectedStrokes().GetBounds() khi chỉ có text được chọn.
            _oldSelectionBounds = e.OldRectangle;
        }

        private void MyCanvas_SelectionMoved(object sender, EventArgs e)
        {
            SyncSelectionTransform();
        }

        private void MyCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        {
            string groupId = _viewModel?.CurrentStrokeGroupId ?? _lastStrokeGroupId;
            if (string.IsNullOrEmpty(groupId))
            {
                // Nét đầu tiên: WPF InkCanvas stylus plugin init có thể khiến PreviewMouseDown
                // không nhận diện được InkCanvas → BeginStroke() bị bỏ qua → groupId null.
                // Tạo groupId ngay bây giờ để FallbackSyncStroke có thể sync đúng.
                if (_viewModel == null) return;
                _viewModel.BeginStroke();
                groupId = _viewModel.CurrentStrokeGroupId;
                _lastStrokeGroupId = groupId;
                _viewModel.EndStroke();
                if (string.IsNullOrEmpty(groupId)) return;
            }
            if (!_groupNativeStrokes.ContainsKey(groupId))
                _groupNativeStrokes[groupId] = new List<Stroke>();
            _groupNativeStrokes[groupId].Add(e.Stroke);

            // Lưu bản gốc chưa transform để dùng trong RedrawAllFromActions Phase 2
            if (!_groupNativeOriginals.ContainsKey(groupId))
                _groupNativeOriginals[groupId] = new List<Stroke>();
            _groupNativeOriginals[groupId].Add(e.Stroke.Clone());

            if (_viewModel != null)
            {
                var repAction = _viewModel.UndoRedoManager.GetAllActionsIncludingUndone()
                    .LastOrDefault(a => a.StrokeGroupId == groupId && a.ActionType == "DRAW");

                if (repAction != null)
                {
                    // Canvas_MouseMove đã sync các segment → chỉ map stroke
                    _strokeToAction[e.Stroke] = repAction;
                }
                else
                {
                    // Canvas_MouseMove không gửi được (thường xảy ra với nét đầu tiên do
                    // InkCanvas stylus plugin init) → sync toàn bộ stroke ngay bây giờ
                    FallbackSyncStroke(e.Stroke, groupId);

                    // Cập nhật mapping sau khi đã tạo DrawActions
                    repAction = _viewModel.UndoRedoManager.GetAllActionsIncludingUndone()
                        .LastOrDefault(a => a.StrokeGroupId == groupId && a.ActionType == "DRAW");
                    if (repAction != null)
                        _strokeToAction[e.Stroke] = repAction;
                }
            }
        }

        // Gửi toàn bộ stroke khi Canvas_MouseMove không sync được (nét đầu tiên)
        private void FallbackSyncStroke(Stroke stroke, string groupId)
        {
            if (_viewModel == null || stroke == null) return;
            var pts = stroke.StylusPoints;
            if (pts.Count < 2) return;

            string prev = _viewModel.CurrentStrokeGroupId;
            _viewModel.CurrentStrokeGroupId = groupId;
            try
            {
                for (int i = 0; i < pts.Count - 1; i++)
                    _viewModel.SendDrawData(new Point(pts[i].X, pts[i].Y),
                                            new Point(pts[i + 1].X, pts[i + 1].Y));
            }
            finally
            {
                _viewModel.CurrentStrokeGroupId = prev;
            }
        }

        private void Canvas_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.OnLineReceived -= DrawNetworkLine;
                _viewModel.OnEraseReceived -= EraseNetworkStroke;
                _viewModel.OnLaserReceived -= ShowRemoteLaser;
                _viewModel.OnCanvasCleared -= ClearLocalCanvas;
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _viewModel.OnShapeReceived -= DrawShape;
                _viewModel.OnTextReceived -= DrawText;
                _viewModel.OnDeleteTextReceived -= DeleteTextFromNetwork;
                _viewModel.OnSelectionTransformedReceived -= HandleRemoteSelectionTransform;
                _viewModel.OnUndoRedo -= RedrawAllFromActions;
                _viewModel.OnTransformUndoRedo -= HandleTransformUndoRedo;
                _viewModel.OnReplayDraw -= ReplayDraw;
                _viewModel.OnReplayErase -= ReplayErase;
                _viewModel.OnReplayShape -= ReplayShape;
                _viewModel.OnReplayText -= ReplayText;
                _viewModel.OnReplayUndo -= ReplayUndo;
                _viewModel.OnReplayRedo -= ReplayRedo;
                _viewModel.OnReplayClear -= ReplayClear;
                _viewModel.OnReplayFinished -= ReplayFinished;
                _viewModel.Cleanup();
            }
            _groupNativeStrokes.Clear();
            _groupNativeOriginals.Clear();
            _strokeToAction.Clear();
            _childToAction.Clear();
            _actionIdToStroke.Clear();
            _actionIdToChild.Clear();
            _networkGroupStrokes.Clear();
            if (_laserTimer != null && _laserTimer.IsEnabled)
            {
                _laserTimer.Stop();
            }
        }

        private void MyCanvas_SelectionChanged(object sender, EventArgs e)
        {
            var selectedStrokes = MyCanvas.GetSelectedStrokes();
            if (selectedStrokes != null && selectedStrokes.Count > 0)
            {
                // Ghi lại khung tọa độ gốc trước khi kéo đi
                _oldSelectionBounds = selectedStrokes.GetBounds();
            }
        }

        private void MyCanvas_SelectionResized(object sender, EventArgs e)
        {
            SyncSelectionTransform();
        }

        // Ghi nhận di chuyển như một TRANSFORM action có thể undo/redo
        private void SyncSelectionTransform()
        {
            if (_viewModel == null) return;

            var selectedStrokes = MyCanvas.GetSelectedStrokes();
            var selectedElems = MyCanvas.GetSelectedElements();
            bool hasStrokes = selectedStrokes != null && selectedStrokes.Count > 0;
            bool hasText = selectedElems != null && selectedElems.OfType<TextBlock>().Any();
            if (!hasStrokes && !hasText) return;

            // Tính newBounds bao gồm cả strokes lẫn TextBlocks đang được chọn
            Rect newBounds = hasStrokes ? selectedStrokes.GetBounds() : Rect.Empty;
            if (hasText && selectedElems != null)
            {
                foreach (UIElement el in selectedElems)
                {
                    if (el is TextBlock tb)
                    {
                        double l = InkCanvas.GetLeft(tb); if (double.IsNaN(l)) l = 0;
                        double t = InkCanvas.GetTop(tb);  if (double.IsNaN(t)) t = 0;
                        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        var tbRect = new Rect(l, t, tb.DesiredSize.Width, tb.DesiredSize.Height);
                        newBounds = newBounds.IsEmpty ? tbRect : Rect.Union(newBounds, tbRect);
                    }
                }
            }
            if (newBounds.IsEmpty) return;

            if (Math.Abs(newBounds.X - _oldSelectionBounds.X) > 0.1 ||
                Math.Abs(newBounds.Y - _oldSelectionBounds.Y) > 0.1 ||
                Math.Abs(newBounds.Width - _oldSelectionBounds.Width) > 0.1 ||
                Math.Abs(newBounds.Height - _oldSelectionBounds.Height) > 0.1)
            {
                // Thu thập các action/group bị ảnh hưởng để UndoRedoManager theo dõi
                var affectedActionIds = new List<string>();
                var affectedGroupIds = new List<string>();

                foreach (var stroke in selectedStrokes)
                {
                    if (_strokeToAction.TryGetValue(stroke, out var affectedAction))
                    {
                        if (affectedAction.ActionType == "DRAW" &&
                            !string.IsNullOrEmpty(affectedAction.StrokeGroupId) &&
                            _groupNativeOriginals.ContainsKey(affectedAction.StrokeGroupId))
                        {
                            if (!affectedGroupIds.Contains(affectedAction.StrokeGroupId))
                                affectedGroupIds.Add(affectedAction.StrokeGroupId);
                        }
                        else
                        {
                            if (!affectedActionIds.Contains(affectedAction.Id))
                                affectedActionIds.Add(affectedAction.Id);
                        }
                    }
                    else
                    {
                        // Stroke từ client khác nhận qua mạng → tra ngược _networkGroupStrokes
                        foreach (var kvp in _networkGroupStrokes)
                        {
                            if (kvp.Value == stroke)
                            {
                                if (!affectedGroupIds.Contains(kvp.Key))
                                    affectedGroupIds.Add(kvp.Key);
                                break;
                            }
                        }
                    }
                }

                var selectedChildren = MyCanvas.GetSelectedElements();
                if (selectedChildren != null)
                {
                    foreach (UIElement child in selectedChildren)
                    {
                        if (_childToAction.TryGetValue(child, out var affectedAction) &&
                            !affectedActionIds.Contains(affectedAction.Id))
                            affectedActionIds.Add(affectedAction.Id);
                    }
                }

                // Ghi TRANSFORM action vào UndoRedoManager (undo/redo sẽ xử lý)
                if (affectedActionIds.Count > 0 || affectedGroupIds.Count > 0)
                {
                    var transformAction = new DrawAction
                    {
                        Id = DrawAction.GenerateId(),
                        ActionType = "TRANSFORM",
                        UserId = ClientSocket.Instance.CurrentUserId,
                        Username = ClientSocket.Instance.CurrentUsername,
                        RoomId = _viewModel.RoomId,
                        StrokeGroupId = DrawAction.GenerateId(),
                        AffectedActionIds = affectedActionIds.Count > 0 ? affectedActionIds : null,
                        AffectedStrokeGroupIds = affectedGroupIds.Count > 0 ? affectedGroupIds : null,
                        TransformOldX = _oldSelectionBounds.X,
                        TransformOldY = _oldSelectionBounds.Y,
                        TransformOldW = _oldSelectionBounds.Width,
                        TransformOldH = _oldSelectionBounds.Height,
                        TransformNewX = newBounds.X,
                        TransformNewY = newBounds.Y,
                        TransformNewW = newBounds.Width,
                        TransformNewH = newBounds.Height
                    };
                    _viewModel.UndoRedoManager.AddAction(transformAction);
                    _viewModel.UpdateHistoryUI();
                }

                // Gửi network sync dùng "G:groupId" thay vì index —
                // tránh index sai khi các client vẽ đồng thời (index thay đổi theo thứ tự nhận).
                var selectedIdentifiers = new List<string>();
                foreach (var stroke in selectedStrokes)
                {
                    string groupId = null;
                    if (_strokeToAction.TryGetValue(stroke, out var act))
                        groupId = act.StrokeGroupId;
                    else
                    {
                        foreach (var kvp in _networkGroupStrokes)
                        {
                            if (kvp.Value == stroke) { groupId = kvp.Key; break; }
                        }
                    }

                    if (!string.IsNullOrEmpty(groupId))
                        selectedIdentifiers.Add("G:" + groupId);
                    else
                    {
                        // Fallback: dùng index (backward-compat với data cũ trong DB)
                        int idx = MyCanvas.Strokes.IndexOf(stroke);
                        if (idx >= 0) selectedIdentifiers.Add(idx.ToString());
                    }
                }

                // Thu thập index của TextBlock bị di chuyển
                var allTextBlocks = MyCanvas.Children.OfType<TextBlock>().ToList();
                var selectedChildIndices = new List<int>();
                var selElements = MyCanvas.GetSelectedElements();
                if (selElements != null)
                {
                    foreach (UIElement el in selElements)
                    {
                        if (el is TextBlock tb)
                        {
                            int ci = allTextBlocks.IndexOf(tb);
                            if (ci >= 0) selectedChildIndices.Add(ci);
                        }
                    }
                }

                if (selectedIdentifiers.Count > 0 || selectedChildIndices.Count > 0)
                {
                    string childData = selectedChildIndices.Count > 0
                        ? string.Join(",", selectedChildIndices) : "";
                    _viewModel.SendSelectionTransform(
                        string.Join(",", selectedIdentifiers), _oldSelectionBounds, newBounds, childData);
                }

                _oldSelectionBounds = newBounds;
            }
        }

        private void ChatMessages_CollectionChanged(
            object sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                ChatScrollViewer?.ScrollToEnd();
            }));
        }

        private void Canvas_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // NOTE: SelectionMoved/Resized/Changed are already registered in the constructor.
            // Do NOT re-register them here — that causes every event to fire twice.
            // 1. Unsubscribe VM cũ trước
            if (e.OldValue is CanvasViewModel oldVm)
            {
                oldVm.OnLineReceived -= DrawNetworkLine;
                oldVm.OnEraseReceived -= EraseNetworkStroke;
                oldVm.OnLaserReceived -= ShowRemoteLaser;
                oldVm.OnCanvasCleared -= ClearLocalCanvas;
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
                oldVm.OnShapeReceived -= DrawShape;
                oldVm.OnTextReceived -= DrawText;
                oldVm.OnDeleteTextReceived -= DeleteTextFromNetwork;
                oldVm.OnSelectionTransformedReceived -= HandleRemoteSelectionTransform;
                oldVm.OnTransformUndoRedo -= HandleTransformUndoRedo;

                if (oldVm.Toolbar != null)
                {
                    oldVm.Toolbar.PropertyChanged -= Toolbar_PropertyChanged;
                    oldVm.Toolbar.ToolSelected -= Toolbar_ToolSelected;
                }
            }

            // 2. Gán VM mới
            if (e.NewValue is CanvasViewModel newVm)
            {
                _viewModel = newVm;

                // 3. Subscribe đúng instance
                _viewModel.OnLineReceived += DrawNetworkLine;
                _viewModel.OnEraseReceived += EraseNetworkStroke;
                _viewModel.OnLaserReceived += ShowRemoteLaser;
                _viewModel.OnCanvasCleared += ClearLocalCanvas;
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                _viewModel.OnShapeReceived += DrawShape;
                _viewModel.OnTextReceived += DrawText;
                _viewModel.OnDeleteTextReceived += DeleteTextFromNetwork;
                _viewModel.ChatMessages.CollectionChanged += ChatMessages_CollectionChanged;
                // Đăng ký nhận dữ liệu từ ViewModel truyền xuống
                _viewModel.OnSelectionTransformedReceived += HandleRemoteSelectionTransform;


                if (_viewModel.Toolbar != null)
                {
                    _viewModel.Toolbar.PropertyChanged += Toolbar_PropertyChanged;
                    _viewModel.Toolbar.ToolSelected += Toolbar_ToolSelected;
                }

                UpdateCurrentDrawingAttributes(_viewModel);
                _viewModel.OnUndoRedo += RedrawAllFromActions;
                _viewModel.OnTransformUndoRedo += HandleTransformUndoRedo;

                // Replay/Play events
                _viewModel.OnReplayDraw += ReplayDraw;
                _viewModel.OnReplayErase += ReplayErase;
                _viewModel.OnReplayShape += ReplayShape;
                _viewModel.OnReplayText += ReplayText;
                _viewModel.OnReplayUndo += ReplayUndo;
                _viewModel.OnReplayRedo += ReplayRedo;
                _viewModel.OnReplayClear += ReplayClear;
                _viewModel.OnReplayFinished += ReplayFinished;

                // Nếu có DRAW actions đã đến trước khi Canvas được set up (OnLineReceived còn null),
                // redraw lại để các nét đó xuất hiện ngay thay vì chờ đến undo/redo.
                if (_viewModel.UndoRedoManager.GetAllActionsIncludingUndone().Any(a => !a.IsUndone))
                    RedrawAllFromActions();
            }
        }

        private void RedrawAllFromActions()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MyCanvas.Strokes.Clear();
                _strokeToAction.Clear();
                _childToAction.Clear();
                _actionIdToStroke.Clear();
                _actionIdToChild.Clear();
                // Xóa _networkGroupStrokes vì strokes được rebuild từ actions —
                // các entry cũ sẽ trỏ vào strokes không còn tồn tại nữa.
                _networkGroupStrokes.Clear();
                _currentRedrawGroupStrokes = new Dictionary<string, List<Stroke>>();

                var textBlocksToRemove = MyCanvas.Children.OfType<TextBlock>().ToList();
                foreach (var tb in textBlocksToRemove)
                    MyCanvas.Children.Remove(tb);

                if (_viewModel == null) return;
                // Lấy tất cả actions kể cả đã undone để apply TRANSFORM đúng thứ tự
                var allActions = _viewModel.UndoRedoManager.GetAllActionsIncludingUndone().ToList();

                // ── Phase 1: Vẽ tất cả action không-undone, không phải TRANSFORM ──────────
                var drawnNativeGroups = new HashSet<string>();
                foreach (var action in allActions)
                {
                    if (action.IsUndone || action.ActionType == "TRANSFORM") continue;

                    if (action.ActionType == "DRAW" &&
                        !string.IsNullOrEmpty(action.StrokeGroupId) &&
                        _groupNativeOriginals.TryGetValue(action.StrokeGroupId, out var originals))
                    {
                        // Dùng bản gốc chưa transform để Phase 2 apply đúng
                        if (drawnNativeGroups.Add(action.StrokeGroupId))
                        {
                            if (!_currentRedrawGroupStrokes.ContainsKey(action.StrokeGroupId))
                                _currentRedrawGroupStrokes[action.StrokeGroupId] = new List<Stroke>();
                            foreach (var orig in originals)
                            {
                                var clone = orig.Clone();
                                MyCanvas.Strokes.Add(clone);
                                _strokeToAction[clone] = action;
                                _actionIdToStroke[action.Id] = clone;
                                _currentRedrawGroupStrokes[action.StrokeGroupId].Add(clone);
                            }
                        }
                    }
                    else if (action.ActionType != "DRAW" ||
                             string.IsNullOrEmpty(action.StrokeGroupId) ||
                             !_groupNativeOriginals.ContainsKey(action.StrokeGroupId))
                    {
                        var s = DrawSingleAction(action);
                        if (s != null)
                        {
                            _strokeToAction[s] = action;
                            _actionIdToStroke[action.Id] = s;
                        }
                        // TEXT: DrawSingleAction đã tự set _childToAction và _actionIdToChild
                    }
                }

                // ── Phase 2: Apply tất cả TRANSFORM action không-undone theo thứ tự ────────
                foreach (var action in allActions)
                {
                    if (action.IsUndone || action.ActionType != "TRANSFORM") continue;
                    ApplyTransformActionToCanvas(action);
                }
            });
        }

        private void ApplyTransformActionToCanvas(DrawAction transformAction)
        {
            double oldW = transformAction.TransformOldW;
            double oldH = transformAction.TransformOldH;
            double scaleX = oldW > 1e-6 ? transformAction.TransformNewW / oldW : 1.0;
            double scaleY = oldH > 1e-6 ? transformAction.TransformNewH / oldH : 1.0;
            double offsetX = transformAction.TransformNewX - transformAction.TransformOldX * scaleX;
            double offsetY = transformAction.TransformNewY - transformAction.TransformOldY * scaleY;
            var matrix = new Matrix();
            matrix.Scale(scaleX, scaleY);
            matrix.Translate(offsetX, offsetY);

            // Apply cho SHAPE/TEXT (via actionId)
            if (transformAction.AffectedActionIds != null)
            {
                foreach (var affId in transformAction.AffectedActionIds)
                {
                    if (_actionIdToStroke.TryGetValue(affId, out var stroke))
                    {
                        var coll = new StrokeCollection { stroke };
                        coll.Transform(matrix, false);
                    }
                    else if (_actionIdToChild.TryGetValue(affId, out var child))
                    {
                        double l = InkCanvas.GetLeft(child);
                        double t = InkCanvas.GetTop(child);
                        if (double.IsNaN(l)) l = 0;
                        if (double.IsNaN(t)) t = 0;
                        InkCanvas.SetLeft(child, l * scaleX + offsetX);
                        InkCanvas.SetTop(child, t * scaleY + offsetY);
                    }
                }
            }

            // Apply cho native PEN strokes (via groupId)
            if (transformAction.AffectedStrokeGroupIds != null)
            {
                foreach (var gid in transformAction.AffectedStrokeGroupIds)
                {
                    if (_currentRedrawGroupStrokes.TryGetValue(gid, out var strokes))
                    {
                        var coll = new StrokeCollection();
                        foreach (var s in strokes) coll.Add(s);
                        coll.Transform(matrix, false);
                    }
                }
            }
        }

        // Được gọi khi undo/redo TRANSFORM action — gửi reverse TRANSFORM_SELECTION để sync các client khác.
        // Phải được gọi TRƯỚC OnUndoRedo/RedrawAllFromActions để stroke còn ở vị trí cũ → index còn đúng.
        private void HandleTransformUndoRedo(DrawAction transformAction, bool isUndo)
        {
            if (_viewModel == null || transformAction == null) return;

            // Tìm các strokes bị ảnh hưởng đang hiện trên canvas
            var affectedStrokes = new List<Stroke>();
            var affectedIds = new HashSet<string>(transformAction.AffectedActionIds ?? new List<string>());
            var affectedGroups = new HashSet<string>(transformAction.AffectedStrokeGroupIds ?? new List<string>());

            foreach (var stroke in MyCanvas.Strokes)
            {
                if (_strokeToAction.TryGetValue(stroke, out var action))
                {
                    if (affectedIds.Contains(action.Id) ||
                        (!string.IsNullOrEmpty(action.StrokeGroupId) && affectedGroups.Contains(action.StrokeGroupId)))
                        affectedStrokes.Add(stroke);
                }
                else if (affectedGroups.Count > 0)
                {
                    // Network stroke (không có trong _strokeToAction) — tra ngược _networkGroupStrokes
                    foreach (var kvp in _networkGroupStrokes)
                    {
                        if (kvp.Value == stroke && affectedGroups.Contains(kvp.Key))
                        {
                            affectedStrokes.Add(stroke);
                            break;
                        }
                    }
                }
            }

            if (affectedStrokes.Count == 0) return;

            // Dùng G:groupId thay vì index để sync undo/redo transform đúng trên các client
            var identifiers = new List<string>();
            foreach (var s in affectedStrokes)
            {
                string groupId = null;
                if (_strokeToAction.TryGetValue(s, out var act))
                    groupId = act.StrokeGroupId;
                else
                {
                    foreach (var kvp in _networkGroupStrokes)
                    {
                        if (kvp.Value == s) { groupId = kvp.Key; break; }
                    }
                }

                if (!string.IsNullOrEmpty(groupId))
                    identifiers.Add("G:" + groupId);
                else
                {
                    int fallbackIdx = MyCanvas.Strokes.IndexOf(s);
                    if (fallbackIdx >= 0) identifiers.Add(fallbackIdx.ToString());
                }
            }
            if (identifiers.Count == 0) return;

            // Reverse: nếu undo thì gửi newBounds→oldBounds, nếu redo thì gửi oldBounds→newBounds
            Rect oldB, newB;
            if (isUndo)
            {
                oldB = new Rect(transformAction.TransformNewX, transformAction.TransformNewY,
                                transformAction.TransformNewW, transformAction.TransformNewH);
                newB = new Rect(transformAction.TransformOldX, transformAction.TransformOldY,
                                transformAction.TransformOldW, transformAction.TransformOldH);
            }
            else
            {
                oldB = new Rect(transformAction.TransformOldX, transformAction.TransformOldY,
                                transformAction.TransformOldW, transformAction.TransformOldH);
                newB = new Rect(transformAction.TransformNewX, transformAction.TransformNewY,
                                transformAction.TransformNewW, transformAction.TransformNewH);
            }

            _viewModel.SendSelectionTransform(string.Join(",", identifiers), oldB, newB);
        }

        #region Replay/Play Handlers

        private void ReplayDraw(DrawMessage msg)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    string colorStr = msg.color ?? "#000000";
                    bool isHighlighter = msg.isHighlighter || colorStr.StartsWith("[HL]");
                    string colorToUse = colorStr.Replace("[HL]", "");

                    if (string.IsNullOrWhiteSpace(colorToUse))
                        colorToUse = "#000000";

                    StylusPointCollection points = new StylusPointCollection
                    {
                        new StylusPoint(msg.x1, msg.y1),
                        new StylusPoint(msg.x2, msg.y2)
                    };

                    Color parsedColor = (Color)ColorConverter.ConvertFromString(colorToUse);
                    bool isFountain = string.Equals(msg.penType?.Trim(), "fountain", StringComparison.OrdinalIgnoreCase);

                    DrawingAttributes da = new DrawingAttributes
                    {
                        Color = parsedColor,
                        IgnorePressure = true,
                        IsHighlighter = isHighlighter
                    };

                    if (isHighlighter)
                    {
                        da.Width = msg.thickness * 1.5;
                        da.Height = msg.thickness * 1.5;
                        da.StylusTip = StylusTip.Rectangle;
                        da.FitToCurve = false;
                    }
                    else if (isFountain)
                    {
                        da.StylusTip = StylusTip.Rectangle;
                        da.Width = msg.thickness * 0.8;
                        da.Height = msg.thickness * 1.8;
                        da.FitToCurve = true;
                        da.IsHighlighter = false;
                    }
                    else
                    {
                        da.Width = msg.thickness;
                        da.Height = msg.thickness;
                        da.StylusTip = StylusTip.Ellipse;
                        da.FitToCurve = true;
                    }

                    Stroke stroke = new Stroke(points) { DrawingAttributes = da };
                    MyCanvas.Strokes.Add(stroke);

                    // Track for undo/redo during replay
                    if (!string.IsNullOrEmpty(msg.actionId))
                    {
                        _replayStrokeMap[msg.actionId] = stroke;
                        _replayActionMap[msg.actionId] = msg;
                    }
                }
                catch { }
            });
        }

        private void ReplayErase(DrawMessage msg)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    double safeThickness = Math.Max(2.0, msg.thickness);
                    Point start = new Point(msg.x1, msg.y1);
                    Point end = new Point(msg.x2, msg.y2);

                    if (start.X == end.X && start.Y == end.Y)
                        end = new Point(start.X + 0.1, start.Y + 0.1);

                    MyCanvas.Strokes.Erase(
                        new Point[] { start, end },
                        new EllipseStylusShape(safeThickness, safeThickness));
                }
                catch { }
            });
        }

        private void ReplayShape(DrawMessage msg)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    StylusPointCollection points = null;
                    Point start = new Point(msg.x1, msg.y1);
                    Point end = new Point(msg.x2, msg.y2);

                    switch (msg.shapeType?.ToLower())
                    {
                        case "rectangle":
                        case "square":
                            points = CreateRectanglePoints(start, end);
                            break;
                        case "ellipse":
                        case "circle":
                            points = CreateEllipsePoints(start, end);
                            break;
                        case "triangle":
                            points = CreateTrianglePoints(start, end);
                            break;
                        case "line":
                            points = CreateLinePoints(start, end);
                            break;
                    }

                    if (points == null) return;

                    Stroke stroke = new Stroke(points)
                    {
                        DrawingAttributes = new DrawingAttributes
                        {
                            Color = (Color)ColorConverter.ConvertFromString(msg.color ?? "#000000"),
                            Width = msg.thickness,
                            Height = msg.thickness,
                            FitToCurve = false,
                            IgnorePressure = true
                        }
                    };

                    MyCanvas.Strokes.Add(stroke);

                    if (!string.IsNullOrEmpty(msg.actionId))
                    {
                        _replayStrokeMap[msg.actionId] = stroke;
                        _replayActionMap[msg.actionId] = msg;
                    }
                }
                catch { }
            });
        }

        private void ReplayText(DrawMessage msg)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    TextBlock tb = new TextBlock
                    {
                        Text = msg.text,
                        FontSize = msg.fontSize > 0 ? msg.fontSize : 14,
                        FontFamily = new FontFamily(!string.IsNullOrEmpty(msg.fontFamily) ? msg.fontFamily : "Roboto"),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(msg.color ?? "#000000")),
                        Background = Brushes.Transparent
                    };

                    InkCanvas.SetLeft(tb, msg.x1);
                    InkCanvas.SetTop(tb, msg.y1);
                    MyCanvas.Children.Add(tb);

                    if (msg.x2 > 0 && msg.y2 > 0)
                        MyCanvas.Strokes.Erase(new Rect(msg.x1, msg.y1, msg.x2, msg.y2));
                }
                catch { }
            });
        }

        private void ReplayUndo(string actionId)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_replayStrokeMap.TryGetValue(actionId, out var stroke))
                {
                    MyCanvas.Strokes.Remove(stroke);
                    _replayUndoneActions.Add(actionId);
                }
            });
        }

        private void ReplayRedo(string actionId)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_replayUndoneActions.Contains(actionId))
                {
                    // Re-add the stroke to canvas
                    if (_replayStrokeMap.TryGetValue(actionId, out var stroke))
                    {
                        if (!MyCanvas.Strokes.Contains(stroke))
                            MyCanvas.Strokes.Add(stroke);
                    }
                    _replayUndoneActions.Remove(actionId);
                }
            });
        }

        private void ReplayClear()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MyCanvas.Strokes.Clear();
                MyCanvas.Children.Clear();
                _replayStrokeMap.Clear();
                _replayActionMap.Clear();
                _replayUndoneActions.Clear();
            });
        }

        private void ReplayFinished()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MyCanvas.Strokes.Clear();
                MyCanvas.Children.Clear();
                _strokeToAction.Clear();
                _childToAction.Clear();
                _actionIdToStroke.Clear();
                _actionIdToChild.Clear();
                _replayStrokeMap.Clear();
                _replayActionMap.Clear();
                _replayUndoneActions.Clear();

                // Dùng RedrawAllFromActions để áp dụng đúng cả TRANSFORM actions
                RedrawAllFromActions();
            });
        }

        #endregion

        private Stroke DrawSingleAction(DrawAction action)
        {
            switch (action.ActionType)
            {
                case "DRAW":
                    string actionPenType = action.penType?.Trim();
                    bool isHighlighter = string.Equals(actionPenType, "highlighter", StringComparison.OrdinalIgnoreCase)
                                         || (action.Color?.StartsWith("[HL]") == true);
                    bool isFountain = string.Equals(actionPenType, "fountain", StringComparison.OrdinalIgnoreCase);

                    string colorToUse = action.Color.Replace("[HL]", "");
                    double thickness = action.Thickness;

                    if (string.IsNullOrWhiteSpace(colorToUse))
                        colorToUse = "#000000";

                    try
                    {
                        StylusPointCollection points = new StylusPointCollection
                        {
                            new StylusPoint(action.StartPoint.X, action.StartPoint.Y),
                            new StylusPoint(action.EndPoint.X, action.EndPoint.Y)
                        };

                        Color parsedColor = (Color)ColorConverter.ConvertFromString(colorToUse);

                        DrawingAttributes da = new DrawingAttributes
                        {
                            Color = parsedColor,
                            IgnorePressure = true,
                            IsHighlighter = isHighlighter
                        };
                        if (isHighlighter)
                        {
                            da.Width = thickness * 1.5;
                            da.Height = thickness * 1.5;
                            da.StylusTip = StylusTip.Rectangle;
                            da.FitToCurve = false;
                        }
                        else if (isFountain)
                        {
                            da.StylusTip = StylusTip.Rectangle;
                            da.Width = thickness * 0.8;
                            da.Height = thickness * 1.8;
                            da.FitToCurve = true;
                            da.IsHighlighter = false;
                        }
                        else
                        {
                            da.Width = thickness;
                            da.Height = thickness;
                            da.StylusTip = StylusTip.Ellipse;
                            da.FitToCurve = true;
                        }
                        Stroke stroke = new Stroke(points) { DrawingAttributes = da };
                        MyCanvas.Strokes.Add(stroke);
                        return stroke;
                    }
                    catch { }
                    return null;

                case "SHAPE":
                    var points2 = CreateShapePointsFromAction(action);
                    if (points2 != null)
                    {
                        try
                        {
                            var stroke = new Stroke(points2)
                            {
                                DrawingAttributes = new DrawingAttributes
                                {
                                    Color = (Color)ColorConverter.ConvertFromString(action.Color),
                                    Width = action.Thickness,
                                    Height = action.Thickness,
                                    FitToCurve = false,
                                    IgnorePressure = true
                                }
                            };
                            MyCanvas.Strokes.Add(stroke);
                            return stroke;
                        }
                        catch { }
                    }
                    return null;

                case "ERASE":
                    try
                    {
                        double safeThickness = Math.Max(2.0, action.Thickness);
                        Point start = action.StartPoint;
                        Point end = action.EndPoint;

                        if (start.X == end.X && start.Y == end.Y)
                            end = new Point(start.X + 0.1, start.Y + 0.1);

                        MyCanvas.Strokes.Erase(
                            new Point[] { start, end },
                            new EllipseStylusShape(safeThickness, safeThickness));
                    }
                    catch { }
                    return null;

                case "TEXT":
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(action.Text))
                        {
                            var tb = new TextBlock
                            {
                                Text = action.Text,
                                FontSize = action.FontSize > 0 ? action.FontSize : 14,
                                FontFamily = new FontFamily(!string.IsNullOrEmpty(action.FontFamily) ? action.FontFamily : "Roboto"),
                                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(action.Color ?? "#000000")),
                                Background = Brushes.Transparent,
                                TextWrapping = TextWrapping.Wrap,
                                Cursor = Cursors.IBeam
                            };
                            InkCanvas.SetLeft(tb, action.StartPoint.X);
                            InkCanvas.SetTop(tb, action.StartPoint.Y);
                            MyCanvas.Children.Add(tb);
                            _childToAction[tb] = action;
                            _actionIdToChild[action.Id] = tb;
                            if (action.EndPoint.X > 0 && action.EndPoint.Y > 0)
                                MyCanvas.Strokes.Erase(new Rect(
                                    action.StartPoint.X, action.StartPoint.Y,
                                    action.EndPoint.X, action.EndPoint.Y));
                        }
                    }
                    catch { }
                    return null;

                default:
                    return null;
            }
        }



        private StylusPointCollection CreateShapePointsFromAction(DrawAction action)
        {
            Point start = action.StartPoint;
            Point end = action.EndPoint;
            string shapeType = action.ShapeType?.ToLower();
            switch (shapeType)
            {
                case "rectangle":
                case "square":
                    return CreateRectanglePoints(start, end);
                case "circle":
                case "ellipse":
                    return CreateEllipsePoints(start, end);
                case "triangle":
                    return CreateTrianglePoints(start, end);
                case "line":
                    return CreateLinePoints(start, end);
                default:
                    return null;
            }
        }

        // Lắng nghe mỗi khi Size, Màu hoặc Loại bút thay đổi trong ViewModel
        private void Toolbar_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_viewModel != null && (e.PropertyName == "CurrentThickness" ||
                                       e.PropertyName == "PencilSize" ||
                                       e.PropertyName == "EraserSize" ||
                                       e.PropertyName == "CurrentColor" ||
                                       e.PropertyName == "CurrentPenType" ||
                                       e.PropertyName == "IsEraserSelected" ||
                                       e.PropertyName == "IsPencilSelected"))
            {
                if (_viewModel != null)
                {
                    // Chạy trên luồng UI để cập nhật giao diện nét vẽ
                    Dispatcher.Invoke(() => UpdateCurrentDrawingAttributes(_viewModel));
                }
                UpdateCurrentDrawingAttributes(_viewModel);
            }
        }

        private void Toolbar_ToolSelected(object sender, string e)
        {
            if (_viewModel != null)
            {
                UpdateCurrentDrawingAttributes(_viewModel);
            }
        }

        // Cập nhật trực tiếp lên Canvas thực tế

        private void UpdateCurrentDrawingAttributes(CanvasViewModel vm)
        {
            if (vm?.Toolbar == null) return;

            string penType = vm.Toolbar.CurrentPenType?.ToLowerInvariant();
            string selectedTool = vm.SelectedTool?.ToLowerInvariant();
            double size = vm.Toolbar.IsEraserSelected ? vm.Toolbar.EraserSize : vm.Toolbar.PencilSize;
            bool isEraser = vm.Toolbar.IsEraserSelected || selectedTool == "eraser";
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(vm.Toolbar.CurrentColor);
                var attributes = new DrawingAttributes
                {
                    Color = color,
                    Width = size,
                    Height = size,
                    FitToCurve = true,
                    IgnorePressure = true,
                    IsHighlighter = false,
                    StylusTip = StylusTip.Ellipse
                };

                // Chỉnh nét vẽ cho từng loại bút
                switch (vm.Toolbar.CurrentPenType?.ToLowerInvariant())
                {
                    case "fountain":
                        attributes.StylusTip = StylusTip.Rectangle;
                        attributes.Width = size * 0.8;
                        attributes.Height = size * 1.8;
                        attributes.FitToCurve = true;
                        break;

                    case "highlighter":
                        attributes.IsHighlighter = true;
                        attributes.Height = size * 1.5;
                        attributes.Width = size * 1.5;
                        attributes.StylusTip = StylusTip.Rectangle;
                        attributes.FitToCurve = false;
                        break;

                    case "laser":
                        break;
                }

                MyCanvas.DefaultDrawingAttributes = attributes;
                if (EraserCursor != null)
                {
                    EraserCursor.Width = size;
                    EraserCursor.Height = size;
                }

                var shapes = new List<string> { "square", "circle", "triangle", "line", "rectangle", "ellipse" };

                if (isEraser)
                {
                    MyCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
                }
                else if (selectedTool == "shape" || selectedTool == "text" || selectedTool == "ocr")
                {
                    MyCanvas.EditingMode = InkCanvasEditingMode.None;
                    return;
                }
                else if (selectedTool == "select")
                {
                    MyCanvas.EditingMode = InkCanvasEditingMode.Select;
                }
                else
                {
                    if (string.Equals(vm.Toolbar.CurrentPenType, "laser", StringComparison.OrdinalIgnoreCase))
                    {
                        MyCanvas.EditingMode = InkCanvasEditingMode.None;
                    }
                    else
                    {
                        MyCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi Update nét vẽ: " + ex.Message);
            }
        }
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_viewModel == null) return;

            // TOOL / MODE
            if (e.PropertyName == nameof(CanvasViewModel.CurrentEditingMode) ||
                e.PropertyName == nameof(CanvasViewModel.SelectedTool))
            {
                isDrawing = false;
                isShapeDrawing = false;

                // Giải phóng mouse capture khi đổi tool, tránh InkCanvas không nhận event ở Select mode.
                if (MyCanvas.IsMouseCaptured)
                    MyCanvas.ReleaseMouseCapture();

                // Xóa preview shape tạm thời ngay khi đổi tool (tránh ghost stroke)
                if (_currentTempStroke != null)
                {
                    MyCanvas.Strokes.Remove(_currentTempStroke);
                    _currentTempStroke = null;
                }

                // Clear laser trail immediately when switching away from pen/laser mode
                if (e.PropertyName == nameof(CanvasViewModel.SelectedTool) &&
                    _viewModel.SelectedTool?.ToLower() != "pen")
                {
                    _laserTimer.Stop();
                    if (_currentLaserPolyline != null)
                    {
                        laserCanvas.Children.Remove(_currentLaserPolyline);
                        _currentLaserPolyline = null;
                    }
                    laserDot.Visibility = Visibility.Collapsed;
                }

                bool isEraser = _viewModel.SelectedTool?.ToLower() == "eraser";

                if (isEraser)
                {
                    MyCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
                }
                else if (_viewModel.SelectedTool?.ToLower() == "shape")
                {
                    MyCanvas.EditingMode = InkCanvasEditingMode.None;
                    return;
                }
                else
                {
                    MyCanvas.EditingMode = _viewModel.CurrentEditingMode;

                    if (_viewModel.CurrentEditingMode == InkCanvasEditingMode.Select)
                    {
                        MyCanvas.UseCustomCursor = false;
                    }
                }
            }
        }

        private void UserControl_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null)
                return;
            lastPoint = e.GetPosition(MyCanvas);
            _startPoint = lastPoint;

            DependencyObject source = e.OriginalSource as DependencyObject;

            while (source != null)
            {
                if (source is FrameworkElement fe)
                {
                    if (fe.Name == "ProfilePopover")
                        return;

                    if (fe is Button button &&
                        button.Command == _viewModel.ToggleProfilePopoverCommand)
                    {
                        return;
                    }
                }

                source = VisualTreeHelper.GetParent(source);
            }

            if (_viewModel != null && _viewModel.Toolbar.IsEraserSelected)
            {
                EraseTextAtPoint(e.GetPosition(MyCanvas));
            }

            // Khởi nhóm nét vẽ sớm tại PreviewMouseDown để BeginStroke() luôn được gọi
            // kể cả khi InkCanvas chặn Canvas_MouseDown ở Ink mode (stylus promotion).
            // Chỉ áp dụng khi click trực tiếp trên InkCanvas (không phải toolbar).
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                string tool = _viewModel.SelectedTool?.ToLowerInvariant();
                bool isPen = tool == "pen" || tool == "pencil";
                bool isEraserTool = tool == "eraser" || (_viewModel.Toolbar?.IsEraserSelected == true);

                if (isPen || isEraserTool)
                {
                    bool clickedOnInkCanvas = false;
                    DependencyObject hitSrc = e.OriginalSource as DependencyObject;
                    while (hitSrc != null)
                    {
                        if (hitSrc is InkCanvas)
                        {
                            clickedOnInkCanvas = true;
                            break;
                        }
                        hitSrc = VisualTreeHelper.GetParent(hitSrc);
                    }

                    if (clickedOnInkCanvas)
                    {
                        isDrawing = true;
                        _viewModel.BeginStroke();
                    }
                }
            }

            _viewModel.IsProfilePopoverVisible = false;
        }

        private void ClearLocalCanvas()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MyCanvas.Strokes.Clear();
                MyCanvas.Children.Clear();
                _groupNativeStrokes.Clear();
                _groupNativeOriginals.Clear();
                _strokeToAction.Clear();
                _childToAction.Clear();
                _actionIdToStroke.Clear();
                _actionIdToChild.Clear();
                _networkGroupStrokes.Clear();
                _currentRedrawGroupStrokes = new Dictionary<string, List<Stroke>>();
            });
        }

        private void ProfilePopover_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // KHÔNG đánh dấu e.Handled = true ở đây —
            // nếu handled thì event không tunnel xuống Button bên trong → command không chạy.
            // Popup là visual tree riêng nên UserControl_PreviewMouseDown sẽ không bị trigger.
        }
        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel?.SelectedTool?.ToLowerInvariant() == "select")
            {
                isDrawing = false;
                isShapeDrawing = false;
                return; // Thoát ngay, nhường toàn quyền cho InkCanvas tự xử lý vùng chọn
            }

            if (_viewModel?.Toolbar == null) return;

            // ocr
            if (_viewModel.SelectedTool?.ToLowerInvariant() == "ocr")
            {
                MyCanvas.EditingMode = InkCanvasEditingMode.None;

                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    _ocrStartPoint = e.GetPosition(MyCanvas);

                    _ocrSelectionRect = new System.Windows.Shapes.Rectangle
                    {
                        Stroke = Brushes.DeepSkyBlue,
                        StrokeThickness = 1.5,
                        StrokeDashArray = new DoubleCollection { 4, 2 },
                        Fill = new SolidColorBrush(Color.FromArgb(25, 0, 120, 215))
                    };

                    System.Windows.Controls.Canvas.SetLeft(_ocrSelectionRect, _ocrStartPoint.X);
                    System.Windows.Controls.Canvas.SetTop(_ocrSelectionRect, _ocrStartPoint.Y);
                    OverlayCanvas.Children.Add(_ocrSelectionRect);

                    MyCanvas.CaptureMouse();
                    MyCanvas.Cursor = Cursors.Cross;
                }
                return;
            }

            // TEXT TOOL
            if (_viewModel.Toolbar.IsTextSelected)
            {
                MyCanvas.EditingMode = InkCanvasEditingMode.None;
                if (e.LeftButton != MouseButtonState.Pressed) return;

                var clickPos = e.GetPosition(MyCanvas);

                // If an active text box is open: check if the click is inside it or the floating toolbar
                if (_activeTextWrapper != null)
                {
                    // Block clicks that originated from the floating toolbar buttons
                    if (_textFloatingToolbar != null)
                    {
                        var src = e.OriginalSource as DependencyObject;
                        while (src != null)
                        {
                            if (src == _textFloatingToolbar) return;
                            src = VisualTreeHelper.GetParent(src);
                        }
                    }

                    double wLeft = InkCanvas.GetLeft(_activeTextWrapper);
                    double wTop = InkCanvas.GetTop(_activeTextWrapper);
                    _activeTextWrapper.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double wW = Math.Max(_activeTextWrapper.ActualWidth, _activeTextWrapper.DesiredSize.Width);
                    double wH = Math.Max(_activeTextWrapper.ActualHeight, _activeTextWrapper.DesiredSize.Height);
                    if (new Rect(wLeft, wTop, wW + 4, wH + 4).Contains(clickPos))
                        return; // click inside active box — let TextBox handle it

                    // Click outside: commit current, then check for re-edit target
                    CommitTextEdit();
                    TextBlock hit2 = FindTextBlockAt(clickPos);
                    if (hit2 != null)
                        BeginTextEdit(new Point(InkCanvas.GetLeft(hit2), InkCanvas.GetTop(hit2)), hit2);
                    return;
                }

                // No active edit: click on existing TextBlock → re-edit; else create new
                TextBlock hitBlock = FindTextBlockAt(clickPos);
                if (hitBlock != null)
                    BeginTextEdit(new Point(InkCanvas.GetLeft(hitBlock), InkCanvas.GetTop(hitBlock)), hitBlock);
                else
                    BeginTextEdit(clickPos);

                e.Handled = true;
                return;
            }

            // LASER POINTER: Visual-only, gửi data laser tới client khác
            if (_viewModel.Toolbar.CurrentPenType?.ToLower() == "laser")
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    Point p = e.GetPosition(laserCanvas);
                    StartLaserStroke(p);
                    SendLaserPoint(p);
                }
                return;
            }

            // KHỞI TẠO CÁC BIẾN KIỂM TRA TRẠNG THÁI
            string penType = _viewModel.Toolbar.CurrentPenType?.ToLowerInvariant();
            string selectedTool = _viewModel.SelectedTool?.ToLowerInvariant();
            bool isEraser = _viewModel.Toolbar.IsEraserSelected || selectedTool == "eraser";

            var shapes = new List<string> { "square", "circle", "triangle", "line", "rectangle", "ellipse" };
            bool isShape = penType != null && shapes.Contains(penType) && selectedTool == "shape";

            // Thoát nếu không thuộc chế độ được phép vẽ
            if (_viewModel.CurrentEditingMode != InkCanvasEditingMode.Ink
                && !isEraser
                && !isShape)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            // XỬ LÝ KHI DÙNG CỤC TẨY
            if (isEraser)
            {
                isDrawing = true;
                lastPoint = e.GetPosition(MyCanvas);
                MyCanvas.CaptureMouse();
                UpdateEraserCursor(lastPoint);
                if (EraserCursor != null) EraserCursor.Visibility = Visibility.Visible;
                _viewModel.BeginStroke(); // group erase segments of this drag
                return;
            }

            // XỬ LÝ KHI VẼ HÌNH KHỐI
            if (isShape)
            {
                isShapeDrawing = true; // Chỉ kích hoạt vẽ hình khi click hẳn vào Canvas
                _startPoint = e.GetPosition(MyCanvas);
                MyCanvas.CaptureMouse();
                MyCanvas.EditingMode = InkCanvasEditingMode.None;
                return;
            }

            // XỬ LÝ KHI VẼ BÚT BÌNH THƯỜNG
            // PreviewMouseDown đã set isDrawing=true và BeginStroke() rồi.
            // InkCanvas ở Ink mode tự quản lý mouse capture nội bộ — không gọi CaptureMouse()
            // ở đây để tránh xung đột với stylus plugin, gây mất nét đầu tiên.
            if (_viewModel.CurrentEditingMode == InkCanvasEditingMode.Ink)
            {
                isDrawing = true;
                lastPoint = e.GetPosition(MyCanvas);
                // Không gọi CaptureMouse() và BeginStroke() lại — đã được xử lý ở PreviewMouseDown
            }
        }
        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_viewModel?.SelectedTool?.ToLowerInvariant() == "select")
            {
                isDrawing = false;
                isShapeDrawing = false;
                return;
            }

            if (_viewModel?.Toolbar == null || e.LeftButton != MouseButtonState.Pressed) return;

            Point currentPoint = e.GetPosition(MyCanvas);

            // Tránh xử lý nếu chuột không thực sự di chuyển (tiết kiệm tài nguyên)
            if (currentPoint == lastPoint) return;

            // *** LASER POINTER CHECK FIRST (PRIORITY) ***
            // Laser không nên gửi dữ liệu lên server hay lưu history
            if (_viewModel?.Toolbar?.CurrentPenType?.ToLower() == "laser")
            {
                Point p = e.GetPosition(laserCanvas);
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    AddLaserPoint(p);
                    SendLaserPoint(p);
                }
                else
                {
                    ShowLaser(p);
                }
                return;  // Exit early - laser is visual-only
            }

            string tool = _viewModel.SelectedTool?.ToLowerInvariant();
            string penType = _viewModel.Toolbar.CurrentPenType?.ToLowerInvariant();
            bool isEraser = _viewModel.Toolbar.IsEraserSelected || tool == "eraser";

            // 1. CHẾ ĐỘ VẼ HÌNH (SHAPE MODE)
            if (isShapeDrawing)
            {
                if (_currentTempStroke != null)
                {
                    MyCanvas.Strokes.Remove(_currentTempStroke);
                }

                StylusPointCollection points = null;
                if (penType == "square" || penType == "rectangle")
                    points = CreateRectanglePoints(_startPoint, currentPoint);
                else if (penType == "circle" || penType == "ellipse")
                    points = CreateEllipsePoints(_startPoint, currentPoint);
                else if (penType == "triangle")
                    points = CreateTrianglePoints(_startPoint, currentPoint);
                else if (penType == "line")
                    points = CreateLinePoints(_startPoint, currentPoint);

                if (points != null)
                {
                    _currentTempStroke = new Stroke(points)
                    {
                        // Khởi tạo thuộc tính riêng cho Shape để không dính tới bút
                        DrawingAttributes = new DrawingAttributes
                        {
                            Color = (Color)ColorConverter.ConvertFromString(_viewModel.Toolbar.CurrentShapeColor),
                            Width = _viewModel.Toolbar.CurrentShapeThickness,
                            Height = _viewModel.Toolbar.CurrentShapeThickness,
                            FitToCurve = false,
                            IgnorePressure = true
                        }
                    };
                    MyCanvas.Strokes.Add(_currentTempStroke);
                }
                return;
            }

            // 2. CHẾ ĐỘ TẨY (ERASER)
            if (isEraser)
            {
                MyCanvas.Strokes.Erase(
                    new Point[] { lastPoint, currentPoint },
                    new EllipseStylusShape(_viewModel.Toolbar.EraserSize, _viewModel.Toolbar.EraserSize));

                // Track local erase for undo first to capture IDs for sync
                var eraseAction = new DrawAction(
                    "ERASE",
                    lastPoint,
                    currentPoint,
                    "#ERASE",
                    _viewModel.Toolbar.EraserSize,
                    ClientSocket.Instance.CurrentUserId,
                    ClientSocket.Instance.CurrentUsername,
                    _viewModel.RoomId)
                {
                    StrokeGroupId = _viewModel.CurrentStrokeGroupId
                };
                _viewModel.UndoRedoManager.AddAction(eraseAction);
                _viewModel.UpdateHistoryUI();

                // Gửi lệnh ERASE lên server kèm actionId/strokeGroupId để undo sync
                var eraseMsg = new DrawMessage
                {
                    type = "ERASE",
                    roomId = _viewModel.RoomId,
                    userId = ClientSocket.Instance.CurrentUserId,
                    username = ClientSocket.Instance.CurrentUsername,
                    x1 = lastPoint.X,
                    y1 = lastPoint.Y,
                    x2 = currentPoint.X,
                    y2 = currentPoint.Y,
                    thickness = _viewModel.Toolbar.EraserSize,
                    color = "#ERASE",
                    actionId = eraseAction.Id,
                    strokeGroupId = eraseAction.StrokeGroupId
                };
                ClientSocket.Instance.Send(eraseMsg);

                // Gọi hàm quét chữ khi rê chuột
                EraseTextAtPoint(currentPoint);
                lastPoint = currentPoint;
                UpdateEraserCursor(currentPoint);
                return;
            }

            // 3. CHẾ ĐỘ VẼ BÚT THƯỜNG (NORMAL DRAW / PENCIL)
            // Kiểm tra isDrawing để tránh gửi nét thừa khi chuột kéo từ ngoài vào canvas
            // mà không qua MouseDown (lastPoint sẽ có giá trị cũ từ nét trước).
            if (_viewModel.Toolbar.IsPencilSelected && isDrawing && e.LeftButton == MouseButtonState.Pressed)
            {
                _viewModel.SendDrawData(lastPoint, currentPoint);
                lastPoint = currentPoint;
            }

            // OCR
            if (_viewModel.SelectedTool?.ToLowerInvariant() == "ocr" && _ocrSelectionRect != null)
            {
                var x = Math.Min(currentPoint.X, _ocrStartPoint.X);
                var y = Math.Min(currentPoint.Y, _ocrStartPoint.Y);
                var w = Math.Max(currentPoint.X, _ocrStartPoint.X) - x;
                var h = Math.Max(currentPoint.Y, _ocrStartPoint.Y) - y;

                _ocrSelectionRect.Width = w;
                _ocrSelectionRect.Height = h;
                System.Windows.Controls.Canvas.SetLeft(_ocrSelectionRect, x);
                System.Windows.Controls.Canvas.SetTop(_ocrSelectionRect, y);
            }

        }
        private async void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel?.SelectedTool?.ToLowerInvariant() == "select")
            {
                isDrawing = false;
                isShapeDrawing = false;
                return;
            }
            // LASER: hoàn tất trail visual-only
            if (_viewModel?.Toolbar?.CurrentPenType?.ToLower() == "laser")
            {
                EndLaserStroke();
                return;
            }

            // NORMAL DRAW / ERASER
            if (isDrawing)
            {
                isDrawing = false;
                // Lưu group ID TRƯỚC khi EndStroke() xóa nó — dùng cho eraser click và StrokeCollected
                _lastStrokeGroupId = _viewModel?.CurrentStrokeGroupId;
                // Không gọi EndStroke() ở đây — SendDrawData phía dưới cần CurrentStrokeGroupId còn hợp lệ

                Point endPoint = e.GetPosition(MyCanvas);

                if (Math.Abs(endPoint.X - _startPoint.X) < 1 && Math.Abs(endPoint.Y - _startPoint.Y) < 1)
                {
                    Point tinyMove = new Point(endPoint.X + 1.0, endPoint.Y + 1.0);

                    bool isEraserClick = _viewModel.Toolbar.IsEraserSelected || _viewModel.SelectedTool?.ToLowerInvariant() == "eraser";

                    if (isEraserClick)
                    {
                        var eraseClickAction = new DrawAction(
                            "ERASE",
                            endPoint,
                            tinyMove,
                            "#ERASE",
                            _viewModel.Toolbar.EraserSize,
                            ClientSocket.Instance.CurrentUserId,
                            ClientSocket.Instance.CurrentUsername,
                            _viewModel.RoomId)
                        {
                            // Dùng group ID đã lưu để eraser click thuộc cùng nhóm với drag
                            StrokeGroupId = _lastStrokeGroupId
                        };
                        _viewModel.UndoRedoManager.AddAction(eraseClickAction);
                        _viewModel.UpdateHistoryUI();

                        ClientSocket.Instance.Send(new DrawMessage
                        {
                            type = "ERASE",
                            roomId = _viewModel.RoomId,
                            userId = ClientSocket.Instance.CurrentUserId,
                            username = ClientSocket.Instance.CurrentUsername,
                            x1 = endPoint.X,
                            y1 = endPoint.Y,
                            x2 = tinyMove.X,
                            y2 = tinyMove.Y,
                            thickness = _viewModel.Toolbar.EraserSize,
                            color = "#ERASE",
                            actionId = eraseClickAction.Id,
                            strokeGroupId = eraseClickAction.StrokeGroupId
                        });
                    }
                    else if (_viewModel.CurrentEditingMode == InkCanvasEditingMode.Ink)
                    {
                        // CurrentStrokeGroupId vẫn hợp lệ vì EndStroke() chưa được gọi
                        _viewModel.SendDrawData(endPoint, tinyMove);
                    }
                }

                // Gọi EndStroke() SAU khi đã gửi tất cả dữ liệu để StrokeGroupId được gán đúng
                _viewModel?.EndStroke();

                if (EraserCursor != null)
                {
                    EraserCursor.Visibility = Visibility.Collapsed;
                }

                _currentTempStroke = null;

                if (MyCanvas.IsMouseCaptured)
                {
                    MyCanvas.ReleaseMouseCapture();
                }
            }

            // SHAPE DRAW
            if (isShapeDrawing)
            {
                Point endPoint = e.GetPosition(MyCanvas);

                string penType =
                    _viewModel?.Toolbar?.CurrentPenType?.ToLowerInvariant();

                // Lưu ref stroke cuối trước khi null để map vào DrawAction
                Stroke finalShapeStroke = _currentTempStroke;
                _currentTempStroke = null;
                // Track local shape for undo first to capture ID for sync
                var shapeAction = new DrawAction(
                    "SHAPE",
                    _startPoint,
                    endPoint,
                    _viewModel.Toolbar.CurrentShapeColor,
                    _viewModel.Toolbar.CurrentShapeThickness,
                    ClientSocket.Instance.CurrentUserId,
                    ClientSocket.Instance.CurrentUsername,
                    _viewModel.RoomId)
                {
                    ShapeType = penType
                };
                _viewModel.UndoRedoManager.AddAction(shapeAction);
                _viewModel.UpdateHistoryUI();

                // Map stroke cuối (preview) sang shapeAction để SyncSelectionTransform cập nhật đúng coords
                if (finalShapeStroke != null)
                    _strokeToAction[finalShapeStroke] = shapeAction;

                // GỬI QUA SERVER kèm actionId để undo sync
                ClientSocket.Instance.Send(new DrawMessage
                {
                    type = "SHAPE",
                    roomId = _viewModel.RoomId,
                    userId = ClientSocket.Instance.CurrentUserId,
                    username = ClientSocket.Instance.CurrentUsername,
                    shapeType = penType,
                    x1 = _startPoint.X,
                    y1 = _startPoint.Y,
                    x2 = endPoint.X,
                    y2 = endPoint.Y,
                    color = _viewModel.Toolbar.CurrentShapeColor,
                    thickness = _viewModel.Toolbar.CurrentShapeThickness,
                    actionId = shapeAction.Id,
                    strokeGroupId = shapeAction.StrokeGroupId
                });

                isShapeDrawing = false;

                _currentTempStroke = null;

                if (MyCanvas.IsMouseCaptured)
                {
                    MyCanvas.ReleaseMouseCapture();
                }
            }

            // OCR
            if (_viewModel.SelectedTool?.ToLowerInvariant() == "ocr" && _ocrSelectionRect != null)
            {
                if (MyCanvas.IsMouseCaptured) MyCanvas.ReleaseMouseCapture();

                // Lấy tọa độ và kích thước khung chọn
                int x = (int)System.Windows.Controls.Canvas.GetLeft(_ocrSelectionRect);
                int y = (int)System.Windows.Controls.Canvas.GetTop(_ocrSelectionRect);
                int width = (int)_ocrSelectionRect.Width;
                int height = (int)_ocrSelectionRect.Height;

                OverlayCanvas.Children.Remove(_ocrSelectionRect);
                _ocrSelectionRect = null;

                if (width > 10 && height > 10) // Bỏ qua nếu khung quá nhỏ (click nhầm)
                {
                    try
                    {
                        // 1. Chụp màn hình InkCanvas KÈM NỀN TRẮNG
                        RenderTargetBitmap rtb = new RenderTargetBitmap((int)MyCanvas.ActualWidth, (int)MyCanvas.ActualHeight, 96d, 96d, PixelFormats.Default);

                        DrawingVisual dv = new DrawingVisual();
                        using (DrawingContext dc = dv.RenderOpen())
                        {
                            // Đổ một lớp nền màu trắng tinh
                            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, MyCanvas.ActualWidth, MyCanvas.ActualHeight));
                            // Đặt nét vẽ lên trên lớp nền trắng đó
                            dc.DrawRectangle(new VisualBrush(MyCanvas), null, new Rect(0, 0, MyCanvas.ActualWidth, MyCanvas.ActualHeight));
                        }
                        rtb.Render(dv);

                        // 2. Cắt đúng vùng người dùng chọn
                        CroppedBitmap crop = new CroppedBitmap(rtb, new Int32Rect(x, y, width, height));

                        // 3. Chuyển thành Base64
                        string base64String = "";
                        using (MemoryStream ms = new MemoryStream())
                        {
                            BitmapEncoder encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(crop));
                            encoder.Save(ms);
                            byte[] imageBytes = ms.ToArray();
                            base64String = Convert.ToBase64String(imageBytes);
                        }

                        // Đổi trỏ chuột thành Loading để user chờ
                        Mouse.OverrideCursor = Cursors.Wait;

                        // 4. Gọi API
                        string detectedText = await OcrService.RecognizeTextAsync(base64String);

                        Mouse.OverrideCursor = null;

                        // 5. In chữ ra màn hình và đồng bộ Socket
                        if (!string.IsNullOrEmpty(detectedText))
                        {
                            double calculatedFontSize = height * 0.75;

                            _viewModel.SendText(detectedText, new Point(x, y), width, height, calculatedFontSize);

                            // Xóa nét vẽ cục bộ tại máy người quét
                            MyCanvas.Strokes.Erase(new Rect(x, y, width, height));
                        }
                        else
                        {
                            MessageBox.Show("Không tìm thấy chữ nào trong vùng vừa chọn!", "OCR Magic", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        Mouse.OverrideCursor = null;
                        MessageBox.Show("Lỗi cắt ảnh: " + ex.Message);
                    }
                    finally
                    {
                        Mouse.OverrideCursor = null;
                    }
                }
                return;
            }
        }

        private void InkCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_viewModel?.Toolbar?.CurrentPenType?.ToLower() != "laser")
                return;

            // Only render laser when the pen tool is active (not select, eraser, shape, etc.)
            if (_viewModel?.SelectedTool?.ToLowerInvariant() != "pen")
                return;

            Point p = e.GetPosition(laserCanvas);

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                AddLaserPoint(p);
            }
            else
            {
                ShowLaser(p);
            }
        }
        private void ShowLaser(Point p)
        {
            laserDot.Visibility = Visibility.Visible;
            laserDot.Opacity = 1;

            System.Windows.Controls.Canvas.SetLeft(laserDot, p.X - laserDot.Width / 2);
            System.Windows.Controls.Canvas.SetTop(laserDot, p.Y - laserDot.Height / 2);

            // reset timer
            _laserTimer.Stop();
            _laserTimer.Start();
        }

        private void StartLaserStroke(Point p)
        {
            _laserTimer.Stop();
            if (_currentLaserPolyline != null)
            {
                laserCanvas.Children.Remove(_currentLaserPolyline);
                _currentLaserPolyline = null;
            }

            _currentLaserPolyline = new Polyline
            {
                Stroke = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(255, 255, 200, 0), 0),
                        new GradientStop(Color.FromArgb(180, 255, 100, 0), 0.4),
                        new GradientStop(Color.FromArgb(0, 255, 100, 0), 1)
                    }
                },
                StrokeThickness = _laserThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Opacity = 1,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromArgb(180, 255, 200, 0),
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.9
                },
                IsHitTestVisible = false
            };

            _currentLaserPolyline.Points.Add(p);
            laserCanvas.Children.Add(_currentLaserPolyline);
            ShowLaser(p);
        }

        private void AddLaserPoint(Point p)
        {
            if (_currentLaserPolyline == null)
            {
                StartLaserStroke(p);
                return;
            }

            _currentLaserPolyline.Points.Add(p);
            ShowLaser(p);
        }

        private void EndLaserStroke()
        {
            if (_currentLaserPolyline == null)
                return;

            DoubleAnimation fade = new DoubleAnimation
            {
                From = _currentLaserPolyline.Opacity,
                To = 0,
                Duration = _laserFadeDuration,
                FillBehavior = FillBehavior.Stop
            };
            fade.Completed += (s, e) =>
            {
                if (_currentLaserPolyline != null)
                {
                    laserCanvas.Children.Remove(_currentLaserPolyline);
                    _currentLaserPolyline = null;
                }
            };
            _currentLaserPolyline.BeginAnimation(UIElement.OpacityProperty, fade);

            // also fade the dot if still visible
            _laserTimer.Stop();
            FadeOutLaser();
        }

        private void FadeOutLaser()
        {
            DoubleAnimation fade = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(500)
            };

            fade.Completed += (s, e) =>
            {
                laserDot.Visibility = Visibility.Collapsed;
            };

            laserDot.BeginAnimation(UIElement.OpacityProperty, fade);

            if (_currentLaserPolyline != null)
            {
                DoubleAnimation lineFade = new DoubleAnimation
                {
                    From = _currentLaserPolyline.Opacity,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(500),
                    FillBehavior = FillBehavior.Stop
                };
                lineFade.Completed += (s, e) =>
                {
                    if (_currentLaserPolyline != null)
                    {
                        laserCanvas.Children.Remove(_currentLaserPolyline);
                        _currentLaserPolyline = null;
                    }
                };
                _currentLaserPolyline.BeginAnimation(UIElement.OpacityProperty, lineFade);
            }
        }

        private void UpdateEraserCursor(Point p)
        {
            if (EraserCursor != null)
            {
                double halfSize = _viewModel.Toolbar.CurrentThickness / 2;

                EraserCursor.Margin =
                    new Thickness(
                        p.X - halfSize,
                        p.Y - halfSize,
                        0,
                        0);
            }
        }

        private void DrawLineLocal(
            Point p1,
            Point p2,
            string hexColor,
            double thickness,
            string penType = null,
            bool isHighlighter = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hexColor))
                {
                    hexColor = "#000000";
                }

                // DEBUG: Log penType để kiểm tra
                Console.WriteLine($"[DrawLineLocal] penType={penType}, isHighlighter={isHighlighter}");

                // REMOTE ERASER
                if (hexColor == "#ERASE")
                {
                    var eraserShape =
                        new EllipseStylusShape(thickness, thickness);

                    MyCanvas.Strokes.Erase(
                        new Point[] { p1, p2 },
                        eraserShape);

                    return;
                }

                // KIỂM TRA XEM CÓ PHẢI LÀ NÉT HIGHLIGHT TỪ MÁY KHÁC KHÔNG
                bool isNetworkHighlighter = isHighlighter || hexColor.StartsWith("[HL]");
                if (hexColor.StartsWith("[HL]"))
                {
                    hexColor = hexColor.Replace("[HL]", ""); // Loại bỏ tiền tố để lấy mã màu Hex chuẩn
                }
                StylusPointCollection points =
                    new StylusPointCollection
                    {
                        new StylusPoint(p1.X, p1.Y),
                        new StylusPoint(p2.X, p2.Y)
                    };

                Color parsedColor =
                    (Color)ColorConverter.ConvertFromString(hexColor);

                // Thiết lập cấu hình DrawingAttributes chuẩn xác cho nét vẽ mạng
                DrawingAttributes da = new DrawingAttributes
                {
                    Color = parsedColor,
                    Width = isNetworkHighlighter ? thickness * 1.5 : thickness,
                    Height = isNetworkHighlighter ? thickness * 1.5 : thickness,
                    FitToCurve = !isNetworkHighlighter,
                    IgnorePressure = true,
                    IsHighlighter = isNetworkHighlighter,
                    StylusTip = isNetworkHighlighter ? StylusTip.Rectangle : StylusTip.Ellipse
                };

                if (!isNetworkHighlighter && string.Equals(penType?.Trim(), "fountain", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[DrawLineLocal] Applying FOUNTAIN style");

                    da.StylusTip = StylusTip.Rectangle;
                    da.Width = thickness * 0.8;
                    da.Height = thickness * 1.8;
                    da.FitToCurve = true;
                    da.IsHighlighter = false;
                }

                Stroke stroke = new Stroke(points)
                {
                    DrawingAttributes = da
                };

                MyCanvas.Strokes.Add(stroke);
            }
            catch (Exception ex)
            {
                Console.WriteLine("DrawLineLocal error: " + ex.Message);
            }
        }

        private void DrawNetworkLine(Point start, Point end, string colorStr, double thickness, string penType, bool isSync, string strokeGroupId = null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    // 1. KIỂM TRA LỆNH XÓA VÀ XỬ LÝ AN TOÀN
                    if (colorStr == "#ERASE" || penType?.Equals("eraser", StringComparison.OrdinalIgnoreCase) == true || penType?.Equals("ERASE", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        double safeThickness = Math.Max(2.0, thickness);
                        if (start.X == end.X && start.Y == end.Y)
                            end = new Point(start.X + 0.1, start.Y + 0.1);
                        MyCanvas.Strokes.Erase(new Point[] { start, end }, new EllipseStylusShape(safeThickness, safeThickness));
                        return;
                    }

                    // 2. Nếu có strokeGroupId, tích lũy điểm vào stroke đã tạo cho group này
                    //    thay vì tạo nhiều stroke rời — giúp index khớp với native InkCanvas stroke
                    if (!string.IsNullOrEmpty(strokeGroupId) &&
                        _networkGroupStrokes.TryGetValue(strokeGroupId, out var existingStroke) &&
                        MyCanvas.Strokes.Contains(existingStroke))
                    {
                        existingStroke.StylusPoints.Add(new StylusPoint(end.X, end.Y));
                        return;
                    }

                    string incomingPenType = penType?.Trim();
                    bool isHighlighter = string.Equals(incomingPenType, "highlighter", StringComparison.OrdinalIgnoreCase)
                                         || (colorStr?.StartsWith("[HL]") == true);
                    bool isFountain = string.Equals(incomingPenType, "fountain", StringComparison.OrdinalIgnoreCase);

                    string colorToUse = colorStr.Replace("[HL]", "");
                    Color parsedColor = (Color)ColorConverter.ConvertFromString(colorToUse);

                    StylusPointCollection points = new StylusPointCollection
                    {
                        new StylusPoint(start.X, start.Y),
                        new StylusPoint(end.X, end.Y)
                    };

                    DrawingAttributes da = new DrawingAttributes
                    {
                        Color = parsedColor,
                        IgnorePressure = true,
                        IsHighlighter = isHighlighter
                    };

                    if (isHighlighter)
                    {
                        da.Width = thickness * 1.5;
                        da.Height = thickness * 1.5;
                        da.StylusTip = StylusTip.Rectangle;
                        da.FitToCurve = false;
                    }
                    else if (isFountain)
                    {
                        da.StylusTip = StylusTip.Rectangle;
                        da.Width = thickness * 0.8;
                        da.Height = thickness * 1.8;
                        da.FitToCurve = true;
                        da.IsHighlighter = false;
                    }
                    else
                    {
                        da.Width = thickness;
                        da.Height = thickness;
                        da.StylusTip = StylusTip.Ellipse;
                        da.FitToCurve = true;
                    }

                    Stroke stroke = new Stroke(points) { DrawingAttributes = da };
                    MyCanvas.Strokes.Add(stroke);

                    // 3. Đăng ký stroke này để các segment tiếp theo của cùng group được tích lũy vào
                    if (!string.IsNullOrEmpty(strokeGroupId))
                        _networkGroupStrokes[strokeGroupId] = stroke;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Lỗi vẽ đường truyền từ mạng: " + ex.Message);
                }
            });
        }

        private void ShowRemoteLaser(Point position, string colorHex, double thickness, string penType, int userId)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string remoteUserId = userId.ToString();

                // Luôn dùng LinearGradientBrush vàng-cam-trong suốt và thickness cố định cho laser
                double fixedThickness = 8.0;
                LinearGradientBrush laserBrush = new LinearGradientBrush();
                laserBrush.StartPoint = new Point(0, 0.5);
                laserBrush.EndPoint = new Point(1, 0.5);
                laserBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 221, 51), 0.0)); // vàng sáng
                laserBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 179, 0), 0.3));   // cam vàng
                laserBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 102, 0), 0.7));   // cam đậm
                laserBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 102, 0), 1.0)); // trong suốt

                if (!_remoteLasers.ContainsKey(remoteUserId))
                {
                    // 1. Tạo mới Polyline với gradient laser đồng bộ giống bên máy vẽ
                    System.Windows.Shapes.Polyline newPolyline = new System.Windows.Shapes.Polyline
                    {
                        Stroke = laserBrush,
                        StrokeThickness = fixedThickness,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Opacity = 1.0,
                        Effect = new DropShadowEffect
                        {
                            Color = Colors.Orange,
                            BlurRadius = 15,
                            ShadowDepth = 0,
                            Opacity = 0.8
                        }
                    };
                    newPolyline.Points.Add(position);
                    laserCanvas.Children.Add(newPolyline);

                    DispatcherTimer fadeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(200)
                    };

                    fadeTimer.Tick += (s, e) =>
                    {
                        fadeTimer.Stop();
                        FadeOutRemoteLaser(remoteUserId);
                    };

                    _remoteLasers[remoteUserId] = (newPolyline, fadeTimer);
                    fadeTimer.Start();
                }
                else
                {
                    // 2. Nếu đang vẽ tiếp, cứ gán tiếp điểm mới (giữ nguyên gradient)
                    var laserData = _remoteLasers[remoteUserId];

                    laserData.Line.Stroke = laserBrush;
                    if (laserData.Line.Effect is DropShadowEffect shadow)
                    {
                        shadow.Color = Colors.Orange;
                    }

                    laserData.Line.Points.Add(position);

                    laserData.Timer.Stop();
                    laserData.Timer.Start();
                }
            });
        }        // Thêm hàm làm mờ nét vẽ này vào Canvas.xaml.cs
        private void FadeOutRemoteLaser(string remoteUserId)
        {
            if (_remoteLasers.TryGetValue(remoteUserId, out var laserData))
            {
                DoubleAnimation fade = new DoubleAnimation
                {
                    From = laserData.Line.Opacity,
                    To = 0.0,
                    Duration = _laserFadeDuration,
                    FillBehavior = FillBehavior.Stop
                };

                fade.Completed += (s, e) =>
                {
                    laserCanvas.Children.Remove(laserData.Line);
                    _remoteLasers.Remove(remoteUserId);
                };

                laserData.Line.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }
        private void DrawShape(DrawMessage msg)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StylusPointCollection points = null;

                Point start = new Point(msg.x1, msg.y1);
                Point end = new Point(msg.x2, msg.y2);

                switch (msg.shapeType?.ToLower())
                {
                    case "rectangle":
                    case "square":
                        points = CreateRectanglePoints(start, end);
                        break;

                    case "ellipse":
                    case "circle":
                        points = CreateEllipsePoints(start, end);
                        break;

                    case "triangle":
                        points = CreateTrianglePoints(start, end);
                        break;

                    case "line":
                        points = CreateLinePoints(start, end);
                        break;
                }

                if (points == null) return;

                Stroke stroke = new Stroke(points)
                {
                    DrawingAttributes = new DrawingAttributes
                    {
                        Color = (Color)ColorConverter.ConvertFromString(msg.color),
                        Width = msg.thickness,
                        Height = msg.thickness,
                        FitToCurve = false,
                        IgnorePressure = true
                    }
                };

                MyCanvas.Strokes.Add(stroke);

                // Map stroke → DrawAction để SyncSelectionTransform có thể ghi TRANSFORM action khi move
                if (!string.IsNullOrEmpty(msg.actionId) && _viewModel != null)
                {
                    var action = _viewModel.UndoRedoManager.GetActionById(msg.actionId);
                    if (action != null)
                    {
                        _strokeToAction[stroke] = action;
                        _actionIdToStroke[action.Id] = stroke;
                    }
                }
            });
        }
        private void DrawText(DrawMessage msg)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TextBlock tb = new TextBlock
                {
                    Text = msg.text,
                    FontSize = msg.fontSize > 0 ? msg.fontSize : 14,
                    FontFamily = new FontFamily(!string.IsNullOrEmpty(msg.fontFamily) ? msg.fontFamily : "Roboto"),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(msg.color ?? "#000000")),
                    Background = Brushes.Transparent
                };

                InkCanvas.SetLeft(tb, msg.x1);
                InkCanvas.SetTop(tb, msg.y1);

                MyCanvas.Children.Add(tb);

                if (msg.x2 > 0 && msg.y2 > 0)
                {
                    MyCanvas.Strokes.Erase(new Rect(msg.x1, msg.y1, msg.x2, msg.y2));
                }
            });
        }

        private void DeleteTextFromNetwork(DrawMessage msg)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var elementToRemove = MyCanvas.Children.OfType<TextBlock>()
                    .FirstOrDefault(tb =>
                    {
                        double tbX = InkCanvas.GetLeft(tb);
                        double tbY = InkCanvas.GetTop(tb);
                        if (double.IsNaN(tbX)) tbX = 0;
                        if (double.IsNaN(tbY)) tbY = 0;

                        bool isMatchPos = Math.Abs(tbX - msg.x1) < 5 && Math.Abs(tbY - msg.y1) < 5;

                        string localText = tb.Text != null ? tb.Text.Replace("\r", "").Replace("\n", "").Trim() : "";
                        string networkText = msg.text != null ? msg.text.Replace("\r", "").Replace("\n", "").Trim() : "";

                        bool isMatchText = string.Equals(localText, networkText, StringComparison.OrdinalIgnoreCase)
                                           || localText.Contains(networkText)
                                           || networkText.Contains(localText);

                        return isMatchPos && isMatchText;
                    });

                if (elementToRemove != null)
                {
                    MyCanvas.Children.Remove(elementToRemove);
                }
            });
        }
        private void EraseTextAtPoint(Point currentPoint)
        {
            var textBlocks = MyCanvas.Children.OfType<TextBlock>().ToList();
            foreach (var tb in textBlocks)
            {
                double tbX = InkCanvas.GetLeft(tb);
                double tbY = InkCanvas.GetTop(tb);

                if (double.IsNaN(tbX)) tbX = 0;
                if (double.IsNaN(tbY)) tbY = 0;

                double width = tb.ActualWidth > 0 ? tb.ActualWidth : tb.DesiredSize.Width;
                double height = tb.ActualHeight > 0 ? tb.ActualHeight : tb.DesiredSize.Height;

                Rect bounds = new Rect(tbX, tbY, width, height);
                double offset = _viewModel.Toolbar.EraserSize / 2;
                bounds.Inflate(offset, offset);

                if (bounds.Contains(currentPoint))
                {
                    MyCanvas.Children.Remove(tb);
                    _viewModel.SendDeleteText(tbX, tbY, tb.Text);
                }
            }
        }
        // ============================================================
        // TEXT TOOL — ComboBox selection handlers (called from XAML)
        // ============================================================
        private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is CanvasViewModel vm && sender is ComboBox cb)
            {
                var item = cb.SelectedItem as ComboBoxItem;
                if (item != null)
                    vm.Toolbar.CurrentTextFont = item.Tag?.ToString() ?? item.Content?.ToString() ?? "Roboto";
            }
        }

        private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is CanvasViewModel vm && sender is ComboBox cb)
            {
                var item = cb.SelectedItem as ComboBoxItem;
                if (item != null)
                {
                    string raw = item.Content?.ToString()?.Replace("pt", "").Trim() ?? "16";
                    if (double.TryParse(raw, out double size))
                        vm.Toolbar.CurrentTextSize = size;
                }
            }
        }

        // ============================================================
        // TEXT TOOL — Inline editing helpers
        // ============================================================

        private TextBlock FindTextBlockAt(Point canvasPos)
        {
            foreach (var child in MyCanvas.Children.OfType<TextBlock>().ToList())
            {
                double left = InkCanvas.GetLeft(child);
                double top = InkCanvas.GetTop(child);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var bounds = new Rect(left, top, Math.Max(child.DesiredSize.Width + 4, 20), Math.Max(child.DesiredSize.Height + 4, 20));
                if (bounds.Contains(canvasPos)) return child;
            }
            return null;
        }

        private void BeginTextEdit(Point canvasPos, TextBlock existing = null)
        {
            if (_activeTextBox != null) CommitTextEdit();

            double fontSize = _viewModel.Toolbar.CurrentTextSize;
            string fontFamily = _viewModel.Toolbar.CurrentTextFont;
            string color = _viewModel.Toolbar.CurrentTextColor;
            string initialText = "";

            if (existing != null)
            {
                fontSize = existing.FontSize;
                fontFamily = existing.FontFamily?.Source ?? fontFamily;
                if (existing.Foreground is SolidColorBrush scb)
                    color = $"#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}";
                initialText = existing.Text;
                _editingExistingBlock = existing;
                MyCanvas.Children.Remove(existing);
            }

            _activeTextBox = new TextBox
            {
                Text = initialText,
                FontSize = fontSize,
                FontFamily = new FontFamily(fontFamily),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                MinWidth = 160,
                Padding = new Thickness(6, 4, 6, 4),
                AcceptsReturn = false,
                TextWrapping = TextWrapping.Wrap,
                CaretBrush = new SolidColorBrush(Colors.Black),
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _activeTextBox.LostFocus += ActiveTextBox_LostFocus;
            _activeTextBox.KeyDown += ActiveTextBox_KeyDown;

            _activeTextWrapper = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(26, 115, 232)),
                BorderThickness = new Thickness(1.5),
                Background = new SolidColorBrush(Color.FromArgb(18, 26, 115, 232)),
                MinWidth = 160,
                MinHeight = 36,
                Child = _activeTextBox
            };

            InkCanvas.SetLeft(_activeTextWrapper, canvasPos.X);
            InkCanvas.SetTop(_activeTextWrapper, canvasPos.Y);
            MyCanvas.Children.Add(_activeTextWrapper);

            ShowTextToolbar(canvasPos, fontSize);

            Dispatcher.InvokeAsync(() =>
            {
                _activeTextBox.Focus();
                if (!string.IsNullOrEmpty(initialText)) _activeTextBox.SelectAll();
            }, DispatcherPriority.Input);
        }

        private void ShowTextToolbar(Point canvasPos, double fontSize)
        {
            RemoveTextToolbar();
            OverlayCanvas.IsHitTestVisible = true;

            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            panel.Children.Add(MakeTextToolbarButton("A−", () =>
            {
                if (_activeTextBox == null) return;
                _viewModel.Toolbar.CurrentTextSize = Math.Max(8, _viewModel.Toolbar.CurrentTextSize - 2);
                _activeTextBox.FontSize = _viewModel.Toolbar.CurrentTextSize;
                UpdateToolbarSizeLabel();
            }));

            var sizeLabel = new TextBlock
            {
                Text = $"{fontSize:0}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Black),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0),
                MinWidth = 22,
                TextAlignment = TextAlignment.Center,
                Tag = "sizeLabel"
            };
            panel.Children.Add(sizeLabel);

            panel.Children.Add(MakeTextToolbarButton("A+", () =>
            {
                if (_activeTextBox == null) return;
                _viewModel.Toolbar.CurrentTextSize = Math.Min(72, _viewModel.Toolbar.CurrentTextSize + 2);
                _activeTextBox.FontSize = _viewModel.Toolbar.CurrentTextSize;
                UpdateToolbarSizeLabel();
            }));

            panel.Children.Add(new Border
            {
                Width = 1, Height = 18,
                Background = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                Margin = new Thickness(6, 0, 6, 0),
                IsHitTestVisible = false
            });

            // Color picker button — shows current text color; click opens ColorDialog
            string initHex = "#000000";
            if (_activeTextBox?.Foreground is SolidColorBrush initScb)
                initHex = $"#{initScb.Color.R:X2}{initScb.Color.G:X2}{initScb.Color.B:X2}";
            _colorIndicatorBtn = new Border
            {
                Width = 22, Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(initHex)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                BorderThickness = new Thickness(1.5),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "Đổi màu chữ"
            };
            _colorIndicatorBtn.MouseLeftButtonUp += (s, ev) =>
            {
                ev.Handled = true;
                if (_activeTextBox == null) return;
                _colorPickerOpen = true;
                var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };
                if (_activeTextBox.Foreground is SolidColorBrush scb2)
                    dlg.Color = System.Drawing.Color.FromArgb(scb2.Color.R, scb2.Color.G, scb2.Color.B);
                bool ok = dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK;
                _colorPickerOpen = false;
                if (ok && _activeTextBox != null)
                {
                    var dc = dlg.Color;
                    var wpfColor = Color.FromRgb(dc.R, dc.G, dc.B);
                    _activeTextBox.Foreground = new SolidColorBrush(wpfColor);
                    if (_colorIndicatorBtn != null)
                        _colorIndicatorBtn.Background = new SolidColorBrush(wpfColor);
                    _activeTextBox.Focus();
                }
            };
            panel.Children.Add(_colorIndicatorBtn);

            panel.Children.Add(new Border
            {
                Width = 1, Height = 18,
                Background = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                Margin = new Thickness(2, 0, 6, 0),
                IsHitTestVisible = false
            });

            panel.Children.Add(MakeTextToolbarButton("Delete", () => DeleteTextEdit()));

            _textFloatingToolbar = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 8, 5),
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 220, 224)),
                BorderThickness = new Thickness(1),
                Child = panel
            };
            _textFloatingToolbar.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 6, ShadowDepth = 1, Opacity = 0.15, Color = Colors.Black
            };

            RepositionTextToolbar(canvasPos.X, canvasPos.Y);
            OverlayCanvas.Children.Add(_textFloatingToolbar);
        }

        private void RepositionTextToolbar(double wrapperLeft, double wrapperTop)
        {
            if (_textFloatingToolbar == null) return;
            _textFloatingToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double tbH = _textFloatingToolbar.DesiredSize.Height > 0 ? _textFloatingToolbar.DesiredSize.Height : 34;
            double y = wrapperTop - tbH - 6;
            if (y < 4) y = wrapperTop + (_activeTextWrapper?.ActualHeight ?? 40) + 6;
            System.Windows.Controls.Canvas.SetLeft(_textFloatingToolbar, wrapperLeft);
            System.Windows.Controls.Canvas.SetTop(_textFloatingToolbar, y);
        }

        private void UpdateToolbarSizeLabel()
        {
            if (_textFloatingToolbar?.Child is StackPanel p)
            {
                var lbl = p.Children.OfType<TextBlock>().FirstOrDefault(tb => tb.Tag?.ToString() == "sizeLabel");
                if (lbl != null) lbl.Text = $"{_viewModel.Toolbar.CurrentTextSize:0}";
            }
        }

        private Button MakeTextToolbarButton(string label, Action onClick)
        {
            var btn = new Button
            {
                Content = label,
                Focusable = false,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 64, 67))
            };
            btn.Click += (s, e) => { onClick(); e.Handled = true; };
            return btn;
        }

        private void RemoveTextToolbar()
        {
            if (_textFloatingToolbar != null)
            {
                OverlayCanvas.Children.Remove(_textFloatingToolbar);
                _textFloatingToolbar = null;
            }
            OverlayCanvas.IsHitTestVisible = false;
        }

        private void CommitTextEdit()
        {
            if (_activeTextBox == null) return;

            var tbRef = _activeTextBox;
            var wrapRef = _activeTextWrapper;
            _activeTextBox = null;
            _activeTextWrapper = null;

            string text = tbRef.Text?.Trim() ?? "";
            double left = InkCanvas.GetLeft(wrapRef);
            double top = InkCanvas.GetTop(wrapRef);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            MyCanvas.Children.Remove(wrapRef);
            RemoveTextToolbar();
            _editingExistingBlock = null;

            if (!string.IsNullOrWhiteSpace(text))
            {
                double fontSize = tbRef.FontSize;
                string fontFam = tbRef.FontFamily?.Source ?? _viewModel.Toolbar.CurrentTextFont;
                string color = tbRef.Foreground is SolidColorBrush scb
                    ? $"#{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}"
                    : _viewModel.Toolbar.CurrentTextColor;

                var rendered = new TextBlock
                {
                    Text = text,
                    FontSize = fontSize,
                    FontFamily = new FontFamily(fontFam),
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    Background = Brushes.Transparent,
                    TextWrapping = TextWrapping.Wrap,
                    Cursor = Cursors.IBeam
                };
                InkCanvas.SetLeft(rendered, left);
                InkCanvas.SetTop(rendered, top);
                MyCanvas.Children.Add(rendered);

                var textAction = _viewModel.SendText(text, new Point(left, top), 0, 0, fontSize, fontFam, color);
                if (textAction != null)
                    _childToAction[rendered] = textAction;
            }
        }

        private void CancelTextEdit()
        {
            if (_activeTextWrapper != null)
                MyCanvas.Children.Remove(_activeTextWrapper);
            RemoveTextToolbar();

            if (_editingExistingBlock != null)
                MyCanvas.Children.Add(_editingExistingBlock);

            _activeTextBox = null;
            _activeTextWrapper = null;
            _editingExistingBlock = null;
            _colorIndicatorBtn = null;
        }

        private void DeleteTextEdit()
        {
            var wrapRef = _activeTextWrapper;
            var existingRef = _editingExistingBlock;
            double posX = 0, posY = 0;
            string existingText = existingRef?.Text;

            if (wrapRef != null)
            {
                posX = InkCanvas.GetLeft(wrapRef);
                posY = InkCanvas.GetTop(wrapRef);
                if (double.IsNaN(posX)) posX = 0;
                if (double.IsNaN(posY)) posY = 0;
            }

            // Clear state first so LostFocus doesn't trigger CommitTextEdit
            _activeTextBox = null;
            _activeTextWrapper = null;
            _editingExistingBlock = null;
            _colorIndicatorBtn = null;

            if (wrapRef != null)
                MyCanvas.Children.Remove(wrapRef);
            RemoveTextToolbar();

            // If we were editing an existing block, delete it from the network too
            if (existingRef != null && !string.IsNullOrEmpty(existingText) && _viewModel != null)
                _viewModel.SendDeleteText(posX, posY, existingText);
        }

        private void ActiveTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_colorPickerOpen) return;
            var tbSender = (TextBox)sender;
            Dispatcher.InvokeAsync(() =>
            {
                if (_activeTextBox == tbSender && !tbSender.IsKeyboardFocusWithin)
                    CommitTextEdit();
            }, DispatcherPriority.Background);
        }

        private void ActiveTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelTextEdit();
                e.Handled = true;
            }
            else if (e.Key == Key.Return && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                CommitTextEdit();
                e.Handled = true;
            }
        }

        // Hàm mở bảng màu khi click dấu (+)
        private void OpenColorPicker_Click(object sender, RoutedEventArgs e)
        {
            var colorDialog = new System.Windows.Forms.ColorDialog();
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var c = colorDialog.Color;
                string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";

                if (DataContext is CanvasViewModel vm)
                {
                    // Gọi hàm thêm màu mới (vừa lưu vừa chọn)
                    vm.Toolbar.AddCustomColor(hex);
                    UpdateCurrentDrawingAttributes(vm);
                }
            }
        }
        // vẽ hình
        private StylusPointCollection CreateRectanglePoints(Point start, Point end)
        {
            var points = new StylusPointCollection();
            // Vẽ theo hình chữ nhật: 4 góc khép kín
            points.Add(new StylusPoint(start.X, start.Y));
            points.Add(new StylusPoint(end.X, start.Y));
            points.Add(new StylusPoint(end.X, end.Y));
            points.Add(new StylusPoint(start.X, end.Y));
            points.Add(new StylusPoint(start.X, start.Y));
            return points;
        }

        private StylusPointCollection CreateEllipsePoints(Point start, Point end)
        {
            var points = new StylusPointCollection();
            double radiusX = Math.Abs(end.X - start.X) / 2;
            double radiusY = Math.Abs(end.Y - start.Y) / 2;
            double centerX = Math.Min(start.X, end.X) + radiusX;
            double centerY = Math.Min(start.Y, end.Y) + radiusY;

            // Giảm khoảng cách góc xuống 5 để nét dày và khít hơn
            for (int i = 0; i <= 360; i += 5)
            {
                double angle = i * Math.PI / 180;
                double x = centerX + radiusX * Math.Cos(angle);
                double y = centerY + radiusY * Math.Sin(angle);
                points.Add(new StylusPoint(x, y));
            }

            // Đảm bảo điểm kết thúc luôn trùng khít 100% với điểm đầu tiên để đóng kín vòng
            points.Add(new StylusPoint(centerX + radiusX, centerY));

            return points;
        }
        private StylusPointCollection CreateLinePoints(Point start, Point end)
        {
            var points = new StylusPointCollection();
            // Đường thẳng chỉ cần 2 điểm: Điểm bắt đầu và Điểm kết thúc
            points.Add(new StylusPoint(start.X, start.Y));
            points.Add(new StylusPoint(end.X, end.Y));
            return points;
        }

        private StylusPointCollection CreateTrianglePoints(Point start, Point end)
        {
            var points = new StylusPointCollection();

            // Vẽ tam giác cân hướng lên: 
            // Đỉnh nằm ở giữa cạnh trên, 2 góc ở dưới
            double topX = start.X + (end.X - start.X) / 2;
            double topY = start.Y;

            points.Add(new StylusPoint(topX, topY));       // 1. Đỉnh trên cùng
            points.Add(new StylusPoint(end.X, end.Y));     // 2. Góc dưới cùng bên phải
            points.Add(new StylusPoint(start.X, end.Y));   // 3. Góc dưới cùng bên trái
            points.Add(new StylusPoint(topX, topY));       // 4. Vòng lại đỉnh trên để khép kín hình

            return points;
        }

        private void txtChatInput_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (DataContext is CanvasViewModel vm)
                {
                    vm.SendChatMessageCommand.Execute(null);
                }

                e.Handled = true;
            }
        }
        private void SendLaserPoint(Point p)
        {
            if (_viewModel == null || _viewModel.Toolbar == null) return;

            ClientSocket.Instance.Send(new DrawMessage
            {
                type = "LASER",
                roomId = _viewModel.RoomId,
                userId = ClientSocket.Instance.CurrentUserId,
                username = ClientSocket.Instance.CurrentUsername,
                x1 = p.X,
                y1 = p.Y,
                color = "#FFB300", // màu cam vàng laser chuẩn
                thickness = 8.0 // thickness cố định cho laser
            });
        }
        private void EraseNetworkStroke(Point p1, Point p2, double thickness)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    double safeThickness = Math.Max(2.0, thickness);

                    // Fix click at a single point
                    if (p1.X == p2.X && p1.Y == p2.Y)
                    {
                        p2 = new Point(p1.X + 0.1, p1.Y + 0.1);
                    }

                    MyCanvas.Strokes.Erase(
                        new Point[] { p1, p2 },
                        new EllipseStylusShape(safeThickness, safeThickness));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error in EraseNetworkStroke: " + ex.Message);
                }
            });
        }


        private void HandleRemoteSelectionTransform(string json)
        {
            // Always called from UI thread (via InvokeUI or Dispatcher.Invoke at the call site)
            try
            {
                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("text", out var textEl) || string.IsNullOrEmpty(textEl.GetString()))
                        return;

                    string transformData = textEl.GetString();
                    var parts = transformData.Split('|');
                    // Hỗ trợ cả format cũ (3 phần) và mới (4 phần có childIndices)
                    if (parts.Length < 3) return;

                    var oldB = parts[1].Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    var newB = parts[2].Split(',').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

                    Rect oldBounds = new Rect(oldB[0], oldB[1], oldB[2], oldB[3]);
                    Rect newBounds = new Rect(newB[0], newB[1], newB[2], newB[3]);

                    double scaleX = oldBounds.Width > 0 ? newBounds.Width / oldBounds.Width : 1;
                    double scaleY = oldBounds.Height > 0 ? newBounds.Height / oldBounds.Height : 1;
                    double offsetX = newBounds.X - (oldBounds.X * scaleX);
                    double offsetY = newBounds.Y - (oldBounds.Y * scaleY);

                    var matrix = new Matrix();
                    matrix.Scale(scaleX, scaleY);
                    matrix.Translate(offsetX, offsetY);

                    StrokeCollection strokesToTransform = new StrokeCollection();

                    if (!string.IsNullOrWhiteSpace(parts[0]))
                    {
                        foreach (string id in parts[0].Split(','))
                        {
                            string trimmedId = id.Trim();
                            if (string.IsNullOrEmpty(trimmedId)) continue;

                            if (trimmedId.StartsWith("G:"))
                            {
                                // Địa chỉ bằng strokeGroupId — tìm trong _networkGroupStrokes trước,
                                // sau đó trong _groupNativeStrokes (native stroke của chính client này).
                                string gid = trimmedId.Substring(2);
                                bool addedNet = false;
                                if (_networkGroupStrokes.TryGetValue(gid, out var netStroke))
                                {
                                    if (MyCanvas.Strokes.Contains(netStroke))
                                    {
                                        strokesToTransform.Add(netStroke);
                                        addedNet = true;
                                    }
                                }
                                if (!addedNet && _groupNativeStrokes.TryGetValue(gid, out var nativeList))
                                {
                                    foreach (var ns in nativeList)
                                    {
                                        if (MyCanvas.Strokes.Contains(ns))
                                            strokesToTransform.Add(ns);
                                    }
                                    addedNet = true;
                                }
                                // Fallback: Shape/Text nhận từ mạng — tra qua actionId
                                if (!addedNet && _actionIdToStroke.TryGetValue(gid, out var actionStroke))
                                {
                                    if (MyCanvas.Strokes.Contains(actionStroke))
                                        strokesToTransform.Add(actionStroke);
                                }
                            }
                            else if (int.TryParse(trimmedId, out int idx))
                            {
                                // Fallback: địa chỉ bằng index (data cũ từ DB)
                                if (idx >= 0 && idx < MyCanvas.Strokes.Count)
                                    strokesToTransform.Add(MyCanvas.Strokes[idx]);
                            }
                        }
                    }

                    if (strokesToTransform.Count > 0)
                    {
                        MyCanvas.SelectionMoved -= MyCanvas_SelectionMoved;
                        MyCanvas.SelectionResized -= MyCanvas_SelectionResized;

                        strokesToTransform.Transform(matrix, false);

                        MyCanvas.SelectionMoved += MyCanvas_SelectionMoved;
                        MyCanvas.SelectionResized += MyCanvas_SelectionResized;
                    }

                    // Apply transform cho TextBlock children (phần 4 — mới thêm)
                    if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]))
                    {
                        var allTbs = MyCanvas.Children.OfType<TextBlock>().ToList();
                        foreach (var idxStr in parts[3].Split(','))
                        {
                            if (int.TryParse(idxStr.Trim(), out int ci) && ci >= 0 && ci < allTbs.Count)
                            {
                                var tb = allTbs[ci];
                                double l = InkCanvas.GetLeft(tb); if (double.IsNaN(l)) l = 0;
                                double t = InkCanvas.GetTop(tb);  if (double.IsNaN(t)) t = 0;
                                InkCanvas.SetLeft(tb, l * scaleX + offsetX);
                                InkCanvas.SetTop(tb,  t * scaleY + offsetY);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi đồng bộ dịch chuyển vùng chọn: " + ex.Message);
            }
        }
    }


}
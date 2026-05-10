using DrawClient.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace DrawClient.Views.UserControls
{
    public partial class Canvas : UserControl
    {
        private Point lastPoint;
        private bool isDrawing = false;
        private CanvasViewModel _viewModel;
        private Point _startPoint;
        private Stroke _currentTempStroke; // Stroke tạm thời để hiển thị khi đang kéo chuột
        private bool isShapeDrawing = false;

        public Canvas()
        {
            InitializeComponent();

            this.DataContextChanged += Canvas_DataContextChanged;

            this.PreviewMouseDown += UserControl_PreviewMouseDown;

            // FIX MEMORY LEAK
            this.Unloaded += Canvas_Unloaded;

            // Khởi tạo thuộc tính vẽ mặc định cho Canvas
            MyCanvas.DefaultDrawingAttributes = new DrawingAttributes
            {
                FitToCurve = true,
                IgnorePressure = true,
                Width = 4,
                Height = 4,
                Color = Colors.Black
            };
        }

        private void Canvas_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null)
            {
                _viewModel.OnLineReceived -= DrawNetworkLine;
                _viewModel.OnCanvasCleared -= ClearLocalCanvas;
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

                // FIX SOCKET MEMORY LEAK
                _viewModel.Cleanup();
            }
        }

        private void Canvas_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is CanvasViewModel oldVm)
            {
                oldVm.OnLineReceived -= DrawNetworkLine;
                oldVm.OnCanvasCleared -= ClearLocalCanvas;
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;

                // Ngắt kết nối sự kiện cũ
                if (oldVm.Toolbar != null)
                {
                    oldVm.Toolbar.PropertyChanged -= Toolbar_PropertyChanged;
                    oldVm.Toolbar.ToolSelected -= Toolbar_ToolSelected;
                }
                oldVm.Cleanup();
            }

            if (e.NewValue is CanvasViewModel newVm)
            {
                _viewModel = newVm;
                _viewModel.OnLineReceived += DrawNetworkLine;
                _viewModel.OnCanvasCleared += ClearLocalCanvas;
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;

                // Kết nối sự kiện lắng nghe bút và màu thay đổi
                if (_viewModel.Toolbar != null)
                {
                    _viewModel.Toolbar.PropertyChanged += Toolbar_PropertyChanged;
                    _viewModel.Toolbar.ToolSelected += Toolbar_ToolSelected;
                }

                UpdateCurrentDrawingAttributes(_viewModel);
            }
        }

        // Lắng nghe mỗi khi Size, Màu hoặc Loại bút thay đổi trong ViewModel
        private void Toolbar_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_viewModel != null && (e.PropertyName == "CurrentThickness" || e.PropertyName == "PencilSize" || e.PropertyName == "CurrentColor" || e.PropertyName == "CurrentPenType"))
            {
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
            bool isEraser = selectedTool == "eraser";

            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(vm.Toolbar.CurrentColor);

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
                switch (vm.Toolbar.CurrentPenType)
                {
                    case "Fountain":
                        attributes.StylusTip = StylusTip.Rectangle;
                        attributes.Width = size * 0.4;
                        attributes.Height = size * 1.5;
                        break;
                    case "Highlighter":
                        attributes.IsHighlighter = true;
                        attributes.Height = size * 1.5;
                        attributes.Width = size * 1.5;
                        attributes.StylusTip = StylusTip.Rectangle;
                        attributes.Color = System.Windows.Media.Color.FromArgb(120, color.R, color.G, color.B);
                        break;
                    case "Laser":
                        attributes.Color = System.Windows.Media.Color.FromArgb(150, 255, 0, 0);
                        attributes.Width = size;
                        break;
                    case "Brush":
                        attributes.StylusTip = StylusTip.Ellipse;
                        attributes.Width = size;
                        attributes.Height = size;
                        attributes.IsHighlighter = false;
                        break;
                    default:
                        attributes.Width = size;
                        attributes.Height = size;
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
                else if (penType != null && shapes.Contains(penType)) // nhận diện tất cả các hình
                {
                    MyCanvas.EditingMode = InkCanvasEditingMode.None; // Tắt ink để vẽ hình
                }
                else
                {
                    MyCanvas.EditingMode = InkCanvasEditingMode.Ink;
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
                bool isEraser = _viewModel.SelectedTool?.ToLower() == "eraser";

                if (isEraser)
                {
                    MyCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
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

            _viewModel.IsProfilePopoverVisible = false;
        }

        private void ClearLocalCanvas()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                MyCanvas.Strokes.Clear();
            });
        }

        private void ProfilePopover_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel?.Toolbar == null) return;

            string penType = _viewModel.Toolbar.CurrentPenType?.ToLowerInvariant();
            string selectedTool = _viewModel.SelectedTool?.ToLowerInvariant();
            bool isEraser = selectedTool == "eraser";

            if (_viewModel.CurrentEditingMode != InkCanvasEditingMode.Ink && !isEraser)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            // 1. Nếu là Eraser
            if (isEraser)
            {
                isDrawing = true;
                lastPoint = e.GetPosition(MyCanvas);
                MyCanvas.CaptureMouse();
                UpdateEraserCursor(lastPoint);
                if (EraserCursor != null) EraserCursor.Visibility = Visibility.Visible;
                return;
            }

            // 2. Nếu là Shape
            var shapes = new List<string> { "square", "circle", "triangle", "line", "rectangle", "ellipse" };
            if (penType != null && shapes.Contains(penType))
            {
                isShapeDrawing = true;
                _startPoint = e.GetPosition(MyCanvas);
                MyCanvas.CaptureMouse();
                MyCanvas.EditingMode = InkCanvasEditingMode.None;
                return;
            }

            // 3. Nếu là vẽ bình thường (Ink)
            if (_viewModel.CurrentEditingMode == InkCanvasEditingMode.Ink)
            {
                isDrawing = true;
                lastPoint = e.GetPosition(MyCanvas);
                MyCanvas.CaptureMouse();
            }
        }
        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_viewModel?.Toolbar == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            Point currentPoint = e.GetPosition(MyCanvas);
            string tool = _viewModel.SelectedTool?.ToLowerInvariant();
            string penType = _viewModel.Toolbar.CurrentPenType?.ToLowerInvariant();

            // SHAPE MODE 
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
                        DrawingAttributes = MyCanvas.DefaultDrawingAttributes.Clone()
                    };
                    _currentTempStroke.DrawingAttributes.FitToCurve = false;
                    MyCanvas.Strokes.Add(_currentTempStroke);
                }
                return; // Đảm bảo thoát ra, không chạy xuống code vẽ nét
            }

            // ERASER
            if (_viewModel.CurrentEditingMode != InkCanvasEditingMode.Ink && tool == "eraser")
                return;

            if (tool == "eraser")
            {
                MyCanvas.Strokes.Erase(
                    new Point[] { lastPoint, currentPoint },
                    new EllipseStylusShape(_viewModel.Toolbar.CurrentThickness, _viewModel.Toolbar.CurrentThickness));

                _viewModel.SendDrawData(lastPoint, currentPoint);
                lastPoint = currentPoint;

                UpdateEraserCursor(currentPoint);
                return;
            }

            // NORMAL DRAW (INK)
            if (isDrawing)
            {
                _viewModel.SendDrawData(lastPoint, currentPoint);
                lastPoint = currentPoint;
            }
        }
        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isDrawing)
            {
                isDrawing = false;
                isShapeDrawing = false;
                _currentTempStroke = null; // Giải phóng stroke tạm
                MyCanvas.ReleaseMouseCapture();
            }

            if (EraserCursor != null)
            {
                EraserCursor.Visibility = Visibility.Collapsed;
            }

            if (MyCanvas.IsMouseCaptured)
            {
                MyCanvas.ReleaseMouseCapture();
            }
            if (isShapeDrawing)
            {
                isShapeDrawing = false;
                MyCanvas.ReleaseMouseCapture();
                // dọn dẹp biến tạm
                _currentTempStroke = null;
                // Trả lại chế độ Ink nếu cần hoặc giữ nguyên tùy 
                // MyCanvas.EditingMode = InkCanvasEditingMode.Ink;
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
            double thickness)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hexColor))
                {
                    hexColor = "#000000";
                }

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

                StylusPointCollection points =
                    new StylusPointCollection
                    {
                        new StylusPoint(p1.X, p1.Y),
                        new StylusPoint(p2.X, p2.Y)
                    };

                Color parsedColor =
                    (Color)ColorConverter.ConvertFromString(hexColor);

                Stroke stroke = new Stroke(points)
                {
                    DrawingAttributes = new DrawingAttributes
                    {
                        Color = parsedColor,
                        Width = thickness,
                        Height = thickness,
                        FitToCurve = true,
                        IgnorePressure = true
                    }
                };

                MyCanvas.Strokes.Add(stroke);
            }
            catch (Exception ex)
            {
                Console.WriteLine("DrawLineLocal error: " + ex.Message);
            }
        }

        private void DrawNetworkLine(
            Point p1,
            Point p2,
            string hexColor,
            double thickness)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DrawLineLocal(
                    p1,
                    p2,
                    hexColor,
                    thickness);
            });
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
    }
    }
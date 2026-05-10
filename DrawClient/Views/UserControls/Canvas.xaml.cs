using DrawClient.ViewModels;
using System;
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
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(vm.Toolbar.CurrentColor);
                var size = vm.Toolbar.CurrentThickness;

                var attributes = new DrawingAttributes
                {
                    Color = color,
                    Width = size,
                    Height = size,
                    FitToCurve = true,
                    IgnorePressure = true,
                    IsHighlighter = false, // Reset dạ quang
                    StylusTip = StylusTip.Ellipse // Reset đầu tròn
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
                        attributes.Color = System.Windows.Media.Color.FromArgb(120, color.R, color.G, color.B); // Màu trong suốt
                        break;
                    case "Laser":
                        attributes.Color = System.Windows.Media.Color.FromArgb(150, 255, 0, 0); // Đỏ phát sáng
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

                // Gán thuộc tính mới vào nét vẽ hiện tại
                MyCanvas.DefaultDrawingAttributes = attributes;

                // Cập nhật luôn size của cục tẩy cho đồng bộ
                try
                {
                    MyCanvas.EraserShape = new EllipseStylusShape(size, size);
                }
                catch { }

                // Trả về chế độ vẽ nét (tránh bị kẹt chế độ cục tẩy)
                if (vm.Toolbar.IsPencilSelected)
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
            if (_viewModel == null)
                return;

            bool isEraser =
                _viewModel.SelectedTool?.ToLower() == "eraser";

            if (_viewModel.CurrentEditingMode != InkCanvasEditingMode.Ink
                && !isEraser)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            isDrawing = true;

            lastPoint = e.GetPosition(MyCanvas);

            MyCanvas.CaptureMouse();

            if (isEraser)
            {
                UpdateEraserCursor(lastPoint);

                if (EraserCursor != null)
                {
                    EraserCursor.Visibility = Visibility.Visible;
                }
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_viewModel == null)
                return;

            if (!isDrawing ||
                e.LeftButton != MouseButtonState.Pressed)
                return;

            Point currentPoint = e.GetPosition(MyCanvas);

            if (_viewModel.CurrentEditingMode != InkCanvasEditingMode.Ink &&
            _viewModel.SelectedTool?.ToLower() != "eraser")
            {
                return;
            }

            // ERASER
            if (_viewModel.SelectedTool?.ToLower() == "eraser")
            {
                MyCanvas.Strokes.Erase(
                    new Point[] { lastPoint, currentPoint },
                    new EllipseStylusShape(
                        _viewModel.Toolbar.CurrentThickness, 
                        _viewModel.Toolbar.CurrentThickness)); 

                _viewModel.SendDrawData(lastPoint, currentPoint);

                lastPoint = currentPoint;

                UpdateEraserCursor(currentPoint);

                return;
            }

            // FIX DOUBLE DRAW:
            // KHÔNG DrawLineLocal local nữa
            // InkCanvas native sẽ tự vẽ

            _viewModel.SendDrawData(
                lastPoint,
                currentPoint);

            lastPoint = currentPoint;
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            isDrawing = false;

            if (EraserCursor != null)
            {
                EraserCursor.Visibility = Visibility.Collapsed;
            }

            if (MyCanvas.IsMouseCaptured)
            {
                MyCanvas.ReleaseMouseCapture();
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
    }

}
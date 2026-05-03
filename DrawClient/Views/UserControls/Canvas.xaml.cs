using DrawClient.ViewModels;
using System;
using System.Text.Json;
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

            this.Loaded += (s, e) =>
            {
                _viewModel = this.DataContext as CanvasViewModel;
                if (_viewModel != null)
                {
                    _viewModel.OnLineReceived += DrawNetworkLine;
                }
            };

            this.Unloaded += (s, e) =>
            {
                if (_viewModel != null)
                {
                    _viewModel.OnLineReceived -= DrawNetworkLine;
                }
            };
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Bắt chuột để nhận sự kiện kể cả khi rê ra ngoài viền
            MyCanvas.CaptureMouse();
            isDrawing = true;

            // Lấy tọa độ và ép ngay vào trong viền để nét vẽ không bị lỗi từ điểm xuất phát
            Point rawPoint = e.GetPosition(MyCanvas);
            double safeX = Math.Max(0, Math.Min(rawPoint.X, MyCanvas.ActualWidth));
            double safeY = Math.Max(0, Math.Min(rawPoint.Y, MyCanvas.ActualHeight));

            lastPoint = new Point(safeX, safeY);
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            // Đảm bảo chỉ vẽ khi đang nhấn giữ chuột trái và chuột đang được bắt
            if (!isDrawing || !MyCanvas.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
                return;

            Point rawPoint = e.GetPosition(MyCanvas);

            // Ép tọa độ X và Y không được vượt quá giới hạn của Canvas
            double safeX = Math.Max(0, Math.Min(rawPoint.X, MyCanvas.ActualWidth));
            double safeY = Math.Max(0, Math.Min(rawPoint.Y, MyCanvas.ActualHeight));

            Point currentPoint = new Point(safeX, safeY);

            if (currentPoint != lastPoint)
            {
                // vẽ local
                DrawLineLocal(lastPoint, currentPoint, _viewModel.CurrentColor, _viewModel.CurrentThickness);

                // gửi data
                _viewModel.SendDrawData(lastPoint, currentPoint);

                lastPoint = currentPoint;
            }
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            isDrawing = false;

            // Thả chuột ra để các thành phần khác của app hoạt động lại bình thường
            if (MyCanvas.IsMouseCaptured)
            {
                MyCanvas.ReleaseMouseCapture();
            }
        }

        private void DrawLineLocal(Point p1, Point p2, string hexColor, double thickness)
        {
            StylusPointCollection points = new StylusPointCollection
            {
                new StylusPoint(p1.X, p1.Y),
                new StylusPoint(p2.X, p2.Y)
            };

            Stroke stroke = new Stroke(points)
            {
                DrawingAttributes = new DrawingAttributes
                {
                    Color = (Color)ColorConverter.ConvertFromString(hexColor),
                    Width = thickness,
                    Height = thickness
                }
            };

            MyCanvas.Strokes.Add(stroke);
        }

        private void DrawNetworkLine(Point p1, Point p2, string hexColor, double thickness)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DrawLineLocal(p1, p2, hexColor, thickness);
            });
        }
    }
}
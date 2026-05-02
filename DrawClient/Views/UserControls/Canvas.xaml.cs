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

        public Canvas()
        {
            InitializeComponent();

            // FIX: tránh null crash
            if (MainWindow.clientSocket != null)
            {
                MainWindow.clientSocket.OnMessageReceived += OnServerMessage;
            }
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            isDrawing = true;
            lastPoint = e.GetPosition(MyCanvas);
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            isDrawing = false;
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDrawing || e.LeftButton != MouseButtonState.Pressed)
                return;

            Point currentPoint = e.GetPosition(MyCanvas);

            // vẽ local
            DrawLine(lastPoint, currentPoint);

            // gửi data
            var obj = new DrawMessage
            {
                type = "DRAW",
                roomId = "room1",
                x1 = lastPoint.X,
                y1 = lastPoint.Y,
                x2 = currentPoint.X,
                y2 = currentPoint.Y,
                color = "#000000",
                thickness = 2
            };

            string json = JsonSerializer.Serialize(obj);

            MainWindow.clientSocket?.Send(json);

            lastPoint = currentPoint;
        }

        private void DrawLine(Point p1, Point p2)
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
                    Color = Colors.Black,
                    Width = 2,
                    Height = 2
                }
            };

            MyCanvas.Strokes.Add(stroke);
        }

        private void OnServerMessage(string msg)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var data = JsonSerializer.Deserialize<DrawMessage>(msg);

                    if (data == null) return;

                    if (data.type == "DRAW")
                    {
                        Point p1 = new Point(data.x1, data.y1);
                        Point p2 = new Point(data.x2, data.y2);

                        DrawLine(p1, p2);
                    }
                });
            }
            catch
            {
                // tránh crash UI thread
            }
        }

        // tránh leak event khi reload UI
        ~Canvas()
        {
            if (MainWindow.clientSocket != null)
            {
                MainWindow.clientSocket.OnMessageReceived -= OnServerMessage;
            }
        }
    }
}
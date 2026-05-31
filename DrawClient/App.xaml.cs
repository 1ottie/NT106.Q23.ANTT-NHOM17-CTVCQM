using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DrawClient
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Dùng software rendering để tránh lỗi DirectX/UCEERR_RENDERTHREADFAILURE
            System.Windows.Media.RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            base.OnStartup(e);

            DispatcherUnhandledException += (s, args) =>
            {
                // 0x88980406 = UCEERR_RENDERTHREADFAILURE (D2DERR render target lost) — GPU/driver glitch, not a code bug
                if (args.Exception?.HResult == unchecked((int)0x88980406))
                {
                    args.Handled = true;
                    return;
                }
                MessageBox.Show($"Lỗi ứng dụng:\n{args.Exception.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                MessageBox.Show($"Lỗi nghiêm trọng:\n{args.ExceptionObject}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                MessageBox.Show($"Lỗi background task:\n{args.Exception.InnerException?.Message ?? args.Exception.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                args.SetObserved();
            };
        }
    }
}

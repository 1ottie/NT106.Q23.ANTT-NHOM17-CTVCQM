using System.ComponentModel;
using System.Windows;
using DrawClient.ViewModels;

namespace DrawClient
{
    public partial class MainWindow : Window
    {
        public static ClientSocket clientSocket = new ClientSocket();

        public MainWindow()
        {
            InitializeComponent();

            this.DataContext = new MainViewModel();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            try { ClientSocket.Instance?.LeaveRoom(); } catch { }
            base.OnClosing(e);
        }
    }
}
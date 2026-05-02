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

            bool ok = clientSocket.Connect("127.0.0.1", 5000);

            if (!ok)
            {
                MessageBox.Show("Connected fail");
                return;
            }

            MessageBox.Show("Connected");

            this.DataContext = new MainViewModel();
        }

    }
}
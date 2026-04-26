using System.Windows;

namespace DrawServer
{
    public partial class MainWindow : Window
    {
        ServerSocket server = new ServerSocket();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            int port = int.Parse(txtPort.Text);

            server.Start(port);

            MessageBox.Show("Server started!");
        }
    }
}
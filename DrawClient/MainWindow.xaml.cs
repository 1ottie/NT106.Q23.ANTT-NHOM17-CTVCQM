using System.Windows;

namespace DrawClient
{
    public partial class MainWindow : Window
    {
        ClientSocket client = new ClientSocket();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            string ip = txtIP.Text;
            int port = int.Parse(txtPort.Text);

            bool result = client.Connect(ip, port);

            if (result)
                MessageBox.Show("Connected!");
            else
                MessageBox.Show("Connect failed!");
        }
    }
}
using System;

namespace DrawServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = "Node Server - Drawing App";
            int port = 6001; // Port chạy Socket của Node vẽ này

            string connectionString = "server=192.168.2.2;database=online_Drawing_DB;user=root;password=182806";
            var cleanupService = new RoomCleanupService(connectionString);

            Console.Title = "Node Server - Drawing App";
            Console.WriteLine("=======================================");
            Console.WriteLine($"[NODE SERVER] Đang khởi tại Port: {port}");

            ServerSocket server = new ServerSocket();
            server.Start(port);

            Console.ReadKey();
        }
    }
}
using System;

namespace DrawServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = "Node Server - Drawing App";
            int port = AppConfig.NodePort;

            var cleanupService = new RoomCleanupService(AppConfig.DbConnectionString);
            cleanupService.StartCleanupTimer();

            Console.Title = "Node Server - Drawing App";
            Console.WriteLine("=======================================");
            Console.WriteLine($"[NODE SERVER] Đang khởi tại Port: {port}");

            ServerSocket server = new ServerSocket();
            server.Start(port);

            Console.ReadKey();
        }
    }
}
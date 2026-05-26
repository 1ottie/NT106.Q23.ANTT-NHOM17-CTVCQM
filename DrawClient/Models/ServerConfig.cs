namespace DrawClient.Models
{
    public static class ServerConfig
    {
        public static string MasterIp = "192.168.1.10";

        public static int MasterPort = 5274;

        public static string BaseApi =>
            $"http://{MasterIp}:{MasterPort}";
    }
}
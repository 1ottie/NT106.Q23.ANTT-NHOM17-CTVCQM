using System;
using System.IO;

namespace DrawClient
{
    public static class AppConfig
    {
        public static string MasterServerIp   { get; private set; }
        public static int    MasterServerPort  { get; private set; }

        static AppConfig()
        {
            // Giá trị mặc định — dùng khi config.ini không tồn tại
            MasterServerIp  = "127.0.0.1";
            MasterServerPort = 5274;

            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            if (!File.Exists(configPath)) return;

            foreach (string line in File.ReadAllLines(configPath))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#' || trimmed[0] == '[')
                    continue;

                int eq = trimmed.IndexOf('=');
                if (eq < 0) continue;

                string key = trimmed.Substring(0, eq).Trim();
                string val = trimmed.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "MasterServerIp":
                        if (!string.IsNullOrWhiteSpace(val)) MasterServerIp = val;
                        break;
                    case "MasterServerPort":
                        if (int.TryParse(val, out int port)) MasterServerPort = port;
                        break;
                }
            }
        }
    }
}

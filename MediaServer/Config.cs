using System.Text.Json;

partial class MediaServer
{
    public class Config
    {
        public Dictionary<string, string> Users { get; set; }
        public string BaseDirectory { get; set; }
        public string Interface { get; set; }
        public int Port { get; set; }
        public bool ShowNotification { get; set; }
    }
    public static Config loadConfig(string path)
    {
        if (!File.Exists(path))
        {
            Config _config = generateDefaultConfig();
            // Make sure the directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Make the JSON pretty/indented
            File.WriteAllText(path, JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true }));
            //File.WriteAllText(path, JsonSerializer.Serialize(_config));
        }
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Config>(json);
    }
    public static Config generateDefaultConfig()
    {
        Console.WriteLine("Config file not found, generating default config.");
        return new Config
        {
            Users = new Dictionary<string, string>
            {
                { "admin", "password" }
            },
            // Get the user's home directory
            BaseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Interface = "*", // Listen on all interfaces
            Port = 8080,
            ShowNotification = true
        };
    }
}
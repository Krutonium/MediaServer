using System.Text.Json;

internal partial class MediaServer
{
    public class Config
    {// Generate Default Options
        public Dictionary<string, string> Users { get; set; } = new Dictionary<string, string>();
        public string BaseDirectory { get; set; } = "";
        public string Interface { get; set; } = "*";
        public int Port { get; set; } = 8080;
        public bool ShowNotification { get; set; } = true;
    }

    private static Config LoadConfig(string path)
    {
        if (!File.Exists(path))
        {
            Config defaultConfig = GenerateDefaultConfig();
            // Make sure the directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException());
            // Make the JSON pretty/indented
            File.WriteAllText(path, JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true }));
            //File.WriteAllText(path, JsonSerializer.Serialize(_config));
        }
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Config>(json)!; // Suppressed because we KNOW it will not be null
    }
    private static Config GenerateDefaultConfig()
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
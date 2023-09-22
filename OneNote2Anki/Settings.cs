using Microsoft.Extensions.Configuration;

public class Settings {
    public string ClientId { get; set; }
    public string TenantId { get; set; }
    public string[] GraphUserScopes { get; set; }
    public string AnkiConnectUri { get; set; }

    public static Settings LoadSettings() {
        // Load settings
        IConfiguration config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<Program>()
            .Build();

        return config.GetRequiredSection("Settings").Get<Settings>() ??
            throw new Exception("Could not load application settings.");
    }
}

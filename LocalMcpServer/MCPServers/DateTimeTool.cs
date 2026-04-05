using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RisingTideAI.Trade.MCP.Host.MCPServers;

[McpServerToolType]
public class DateTimeTool
{
    [McpServerTool]
    [Description("Gets the current date and time in UTC, local timezone, or a specific timezone")]
    public DateTimeResponse GetDateTime(
        [Description("Optional timezone ID (e.g., 'America/New_York', 'Europe/London', 'Asia/Tokyo'). If not provided, returns UTC and local time")]
        string? timeZoneId = null)
    {
        var utcNow = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            var localNow = DateTime.Now;
            return new DateTimeResponse
            {
                LocalDateTime = localNow.ToString("yyyy-MM-dd HH:mm:ss"),
                UtcDateTime = utcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                TimeZone = TimeZoneInfo.Local.DisplayName,
                UnixTimestamp = new DateTimeOffset(utcNow).ToUnixTimeSeconds()
            };
        }

        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var zonedTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);

            return new DateTimeResponse
            {
                LocalDateTime = zonedTime.ToString("yyyy-MM-dd HH:mm:ss"),
                UtcDateTime = utcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                TimeZone = timeZone.DisplayName,
                UnixTimestamp = new DateTimeOffset(utcNow).ToUnixTimeSeconds()
            };
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ArgumentException($"Timezone '{timeZoneId}' not found. Use standard timezone IDs like 'America/New_York' or 'UTC'.");
        }
    }
}

public class DateTimeResponse
{
    public string LocalDateTime { get; set; } = string.Empty;
    public string UtcDateTime { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public long UnixTimestamp { get; set; }
}

using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RisingTideAI.Trade.MCP.Host.MCPServers;

/// <summary>
/// Provides current date/time information in multiple formats and timezones.
/// 
/// WHY USE THIS:
/// - AI models need current date context for time-sensitive queries (web searches, filtering, date ranges)
/// - Ensures accurate "today", "this month", "this year" references
/// - Critical for search queries requiring year (e.g., "latest .NET releases 2025")
/// 
/// WHEN TO USE:
/// - Before any web search that needs current context
/// - When user asks "today", "current", "latest" related questions
/// - To validate date-based user inputs
/// 
/// TOKEN COST: ~150 tokens (lightweight, safe to call frequently)
/// </summary>
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
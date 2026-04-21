namespace NuGetExplorer.Services;

/// <summary>
/// Stores and retrieves parsed XML doc maps for NuGet packages.
/// Key format: "xmldoc:{packageId}:{resolvedVersion}"
/// Value: Dictionary&lt;memberId, XmlDocEntry&gt; — e.g. "M:OpenAI.Chat.ChatClient.CompleteChat(...)"
/// Populated during LoadAndCache() before the .nupkg directory is deleted.
/// On-demand reads are pure Redis lookups — no re-download ever needed.
/// </summary>
public interface INuGetXmlDocCache
{
    void Set(string packageId, string version, Dictionary<string, XmlDocEntry> docMap);
    bool TryGet(string packageId, string version, out Dictionary<string, XmlDocEntry>? docMap);
    /// <summary>Finds and parses all XML doc files inside an extracted package directory.</summary>
    Dictionary<string, XmlDocEntry> ParseFromPackagePath(string packagePath);
}
public class XmlDocEntry
{
    public string MemberId  { get; set; } = string.Empty;  // full "M:Ns.Type.Method(...)" key
    public string? Summary  { get; set; }
    public string? Returns  { get; set; }
    public string? Remarks  { get; set; }
    public string? Example  { get; set; }
    /// <summary>param name → doc text</summary>
    public Dictionary<string, string> Params     { get; set; } = new();
    /// <summary>typeparam name → doc text</summary>
    public Dictionary<string, string> TypeParams  { get; set; } = new();
    /// <summary>exception cref → doc text</summary>
    public Dictionary<string, string> Exceptions  { get; set; } = new();
}

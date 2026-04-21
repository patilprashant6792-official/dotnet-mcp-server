using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace NuGetExplorer.Services;

/// <summary>
/// Parses NuGet XML documentation files and caches the result in Redis.
/// Called once per package load inside the DownloadPackage window (before cleanup).
/// All subsequent get_member_xml_doc calls are pure Redis + string-match lookups.
/// </summary>
public class NuGetXmlDocService : INuGetXmlDocCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<NuGetXmlDocService> _logger;
    private readonly TimeSpan _expiration;
    private readonly JsonSerializerOptions _jsonOpts;

    // Redis key prefix — separate key space from PackageMetadata cache
    private const string KeyPrefix = "xmldoc:";

    public NuGetXmlDocService(
        IConnectionMultiplexer redis,
        ILogger<NuGetXmlDocService> logger)
    {
        _redis      = redis ?? throw new ArgumentNullException(nameof(redis));
        _db         = _redis.GetDatabase();
        _logger     = logger ?? throw new ArgumentNullException(nameof(logger));
        _expiration = TimeSpan.FromDays(7);
        _jsonOpts   = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented        = false
        };
    }

    // ── INuGetXmlDocCache ────────────────────────────────────────────────────

    public void Set(string packageId, string version, Dictionary<string, XmlDocEntry> docMap)
    {
        if (docMap.Count == 0) return;
        try
        {
            var key  = BuildKey(packageId, version);
            var json = JsonSerializer.Serialize(docMap, _jsonOpts);
            _db.StringSet(key, json, _expiration);
            _logger.LogInformation("Cached {Count} XML doc entries for {PackageId}@{Version}",
                docMap.Count, packageId, version);
        }
        catch (Exception ex)
        {
            // Non-fatal — tool will return "no docs" if cache misses
            _logger.LogWarning(ex, "Failed to cache XML docs for {PackageId}@{Version}", packageId, version);
        }
    }

    public bool TryGet(string packageId, string version, out Dictionary<string, XmlDocEntry>? docMap)
    {
        docMap = null;
        try
        {
            var key   = BuildKey(packageId, version);
            var value = _db.StringGet(key);
            if (value.IsNullOrEmpty) return false;

            docMap = JsonSerializer.Deserialize<Dictionary<string, XmlDocEntry>>(
                (string)value!, _jsonOpts);
            return docMap != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read XML doc cache for {PackageId}@{Version}", packageId, version);
            return false;
        }
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds and parses all *.xml doc files inside an extracted package directory.
    /// Returns empty dict if no XML doc file exists (package didn't ship docs).
    /// </summary>
    public Dictionary<string, XmlDocEntry> ParseFromPackagePath(string packagePath)
    {
        var result = new Dictionary<string, XmlDocEntry>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(packagePath))
            return result;

        // XML docs live alongside DLLs: lib/net10.0/OpenAI.xml
        var xmlFiles = Directory.GetFiles(packagePath, "*.xml", SearchOption.AllDirectories)
            .Where(f =>
            {
                // Skip nuspec/package manifest XMLs — they live in the root or in [Content_Types].xml etc.
                var dir = Path.GetDirectoryName(f) ?? string.Empty;
                return dir.Contains(Path.DirectorySeparatorChar + "lib" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        foreach (var xmlFile in xmlFiles)
        {
            try
            {
                var entries = ParseXmlDocFile(xmlFile);
                foreach (var (k, v) in entries)
                    result.TryAdd(k, v);   // first file wins on duplicate keys
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse XML doc file: {File}", xmlFile);
            }
        }

        return result;
    }

    private static Dictionary<string, XmlDocEntry> ParseXmlDocFile(string xmlPath)
    {
        var result = new Dictionary<string, XmlDocEntry>(StringComparer.OrdinalIgnoreCase);

        var doc = XDocument.Load(xmlPath, LoadOptions.None);
        var members = doc.Root?.Element("members")?.Elements("member");
        if (members == null) return result;

        foreach (var member in members)
        {
            var name = member.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var entry = new XmlDocEntry { MemberId = name };

            entry.Summary  = ExtractText(member.Element("summary"));
            entry.Returns  = ExtractText(member.Element("returns"));
            entry.Remarks  = ExtractText(member.Element("remarks"));
            entry.Example  = ExtractText(member.Element("example"));

            foreach (var p in member.Elements("param"))
            {
                var pName = p.Attribute("name")?.Value;
                if (!string.IsNullOrWhiteSpace(pName))
                    entry.Params[pName] = ExtractText(p) ?? string.Empty;
            }

            foreach (var tp in member.Elements("typeparam"))
            {
                var tpName = tp.Attribute("name")?.Value;
                if (!string.IsNullOrWhiteSpace(tpName))
                    entry.TypeParams[tpName] = ExtractText(tp) ?? string.Empty;
            }

            foreach (var ex in member.Elements("exception"))
            {
                var cref = ex.Attribute("cref")?.Value ?? string.Empty;
                entry.Exceptions[cref] = ExtractText(ex) ?? string.Empty;
            }

            // Skip entries where everything is empty — not worth storing
            if (IsEmpty(entry)) continue;

            result[name] = entry;
        }

        return result;
    }

    /// <summary>
    /// Collapses inner XML to clean text: strips tags, collapses whitespace,
    /// resolves &lt;see cref="..."&gt; to just the short type name.
    /// </summary>
    private static string? ExtractText(XElement? el)
    {
        if (el == null) return null;

        var sb = new StringBuilder();
        foreach (var node in el.Nodes())
        {
            if (node is XText txt)
            {
                sb.Append(txt.Value);
            }
            else if (node is XElement child)
            {
                if (child.Name.LocalName is "see" or "seealso")
                {
                    var cref = child.Attribute("cref")?.Value ?? child.Attribute("href")?.Value;
                    if (!string.IsNullOrWhiteSpace(cref))
                    {
                        // "T:System.String" → "String"
                        var short_ = cref.Contains('.') ? cref[(cref.LastIndexOf('.') + 1)..] : cref;
                        short_ = short_.TrimStart('T', 'M', 'P', 'F', 'E', ':');
                        sb.Append(short_);
                    }
                }
                else if (child.Name.LocalName == "paramref" || child.Name.LocalName == "typeparamref")
                {
                    sb.Append(child.Attribute("name")?.Value ?? string.Empty);
                }
                else if (child.Name.LocalName == "c" || child.Name.LocalName == "code")
                {
                    sb.Append(child.Value);
                }
                else
                {
                    sb.Append(child.Value);
                }
            }
        }

        // Normalise whitespace
        var text = sb.ToString();
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool IsEmpty(XmlDocEntry e)
        => e.Summary == null && e.Returns == null && e.Remarks == null
        && e.Example == null && e.Params.Count == 0
        && e.TypeParams.Count == 0 && e.Exceptions.Count == 0;

    // ── Resolution ───────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a human-friendly identifier to matching XmlDocEntry objects.
    /// Supports:
    ///   "CompleteChat"               → all members named CompleteChat
    ///   "ChatClient"                 → the type T:...ChatClient
    ///   "ChatClient.CompleteChat"    → method on that type
    ///   "MaxOutputTokens"            → property/field named MaxOutputTokens
    /// Returns multiple results when ambiguous; caller formats them.
    /// </summary>
    public static List<XmlDocEntry> Resolve(
        Dictionary<string, XmlDocEntry> docMap,
        string identifier,
        string? typeHint = null)
    {
        identifier = identifier.Trim();

        // Dotted path — "ChatClient.CompleteChat"
        string? typeFilter   = null;
        string  memberFilter = identifier;

        var dotIdx = identifier.LastIndexOf('.');
        if (dotIdx > 0)
        {
            typeFilter   = identifier[..dotIdx];        // "ChatClient"
            memberFilter = identifier[(dotIdx + 1)..];  // "CompleteChat"
        }

        // typeHint overrides dotted type filter
        if (!string.IsNullOrWhiteSpace(typeHint))
            typeFilter = typeHint;

        var matches = new List<XmlDocEntry>();

        foreach (var (key, entry) in docMap)
        {
            // key examples:
            //   T:OpenAI.Chat.ChatClient
            //   M:OpenAI.Chat.ChatClient.CompleteChat(System.String)
            //   P:OpenAI.Chat.ChatCompletionOptions.MaxOutputTokens

            // Extract short member name from member ID
            // For methods: everything after last '.' before '('
            var withoutPrefix = key.Length > 2 && key[1] == ':' ? key[2..] : key;

            // Short name = last segment before '(' (if method)
            var parenIdx = withoutPrefix.IndexOf('(');
            var qualified = parenIdx > 0 ? withoutPrefix[..parenIdx] : withoutPrefix;
            var lastDot   = qualified.LastIndexOf('.');
            var shortName = lastDot >= 0 ? qualified[(lastDot + 1)..] : qualified;

            // Apply type filter
            if (typeFilter != null)
            {
                // qualified = "OpenAI.Chat.ChatClient.CompleteChat"
                // must contain the type segment
                var segBefore = lastDot >= 0 ? qualified[..lastDot] : string.Empty;
                var typeSeg   = segBefore.Length > 0
                    ? (segBefore.LastIndexOf('.') >= 0
                        ? segBefore[(segBefore.LastIndexOf('.') + 1)..]
                        : segBefore)
                    : string.Empty;

                if (!typeSeg.Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            // Member name match (case-insensitive)
            if (shortName.Equals(memberFilter, StringComparison.OrdinalIgnoreCase))
                matches.Add(entry);
        }

        return matches;
    }

    // ── Formatting ───────────────────────────────────────────────────────────

    public static string Format(List<XmlDocEntry> entries, string identifier)
    {
        if (entries.Count == 0)
            return $"No XML documentation found for '{identifier}'.";

        var sb = new StringBuilder();

        foreach (var e in entries)
        {
            sb.AppendLine($"### `{e.MemberId}`");

            if (!string.IsNullOrWhiteSpace(e.Summary))
                sb.AppendLine($"**Summary:** {e.Summary}");

            if (e.Params.Count > 0)
            {
                sb.AppendLine("**Parameters:**");
                foreach (var (name, doc) in e.Params)
                    sb.AppendLine($"  - `{name}`: {doc}");
            }

            if (e.TypeParams.Count > 0)
            {
                sb.AppendLine("**Type Parameters:**");
                foreach (var (name, doc) in e.TypeParams)
                    sb.AppendLine($"  - `{name}`: {doc}");
            }

            if (!string.IsNullOrWhiteSpace(e.Returns))
                sb.AppendLine($"**Returns:** {e.Returns}");

            if (!string.IsNullOrWhiteSpace(e.Remarks))
                sb.AppendLine($"**Remarks:** {e.Remarks}");

            if (!string.IsNullOrWhiteSpace(e.Example))
                sb.AppendLine($"**Example:** {e.Example}");

            if (e.Exceptions.Count > 0)
            {
                sb.AppendLine("**Exceptions:**");
                foreach (var (cref, doc) in e.Exceptions)
                    sb.AppendLine($"  - `{cref}`: {doc}");
            }

            sb.AppendLine();
        }

        if (entries.Count > 1)
            sb.Insert(0, $"{entries.Count} matches for '{identifier}' — provide typeName to narrow results.\n\n");

        return sb.ToString().TrimEnd();
    }

    private static string BuildKey(string packageId, string version)
        => $"{KeyPrefix}{packageId.ToLowerInvariant()}:{version}";
}

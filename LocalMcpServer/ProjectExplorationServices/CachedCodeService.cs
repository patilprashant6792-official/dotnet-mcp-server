using MCP.Core.Models;
using MCP.Core.Services;
using System.Diagnostics;

namespace MCP.Core.Services;

/// <summary>
/// Drop-in replacement for CodeSearchService.
/// Reads pre-analysed CSharpFileAnalysis from Redis instead of doing live
/// Roslyn parses per query. Falls back to live analysis on cache miss so
/// searches never fail during initial warm-up.
/// </summary>
public class CachedCodeSearchService : ICodeSearchService
{
    private readonly IAnalysisCacheService _cache;
    private readonly IProjectSkeletonService _skeleton;
    private readonly ICodeSearchService _fallback;
    private readonly ILogger<CachedCodeSearchService> _logger;

    public CachedCodeSearchService(
        IAnalysisCacheService cache,
        IProjectSkeletonService skeleton,
        CodeSearchService fallback,               // concrete type injected for fallback
        ILogger<CachedCodeSearchService> logger)
    {
        _cache = cache;
        _skeleton = skeleton;
        _fallback = fallback;
        _logger = logger;
    }

    public async Task<CodeSearchResponse> SearchGloballyAsync(
        CodeSearchRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Search query cannot be empty", nameof(request));

        var sw = Stopwatch.StartNew();
        var allResults = new List<CodeSearchResult>();
        var filesScanned = 0;

        var projects = _skeleton.GetAvailableProjectsWithInfo();
        var projectsToSearch = request.ProjectName == "*"
            ? projects.Keys.ToList()
            : [request.ProjectName];

        _logger.LogInformation("CachedSearch: {Count} project(s) for '{Query}'",
            projectsToSearch.Count, request.Query);

        foreach (var projectName in projectsToSearch)
        {
            if (ct.IsCancellationRequested) break;

            var (results, scanned) = await SearchProjectAsync(projectName, request, ct);

            // Per-project cap: topK budget is per project, not global.
            // Rank first, then cap — so each project contributes its best topK hits.
            var projectRanked = RankAndFilter(results, request)
                .Take(request.TopK)
                .ToList();

            allResults.AddRange(projectRanked);
            filesScanned += scanned;
        }

        // Re-rank the already-per-project-capped set for final cross-project ordering.
        var ranked = RankAndFilter(allResults, request);
        sw.Stop();

        return new CodeSearchResponse
        {
            Query = request.Query,
            ProjectName = request.ProjectName,
            TotalResults = allResults.Count,  // post-per-project-cap total (intentional — reflects what's returned)
            Results = ranked,                 // no second Take — already bounded per project above
            SearchDuration = sw.Elapsed,
            FilesScanned = filesScanned,
            ProjectsSearched = projectsToSearch.Count
        };
    }

    // ── Per-project search ────────────────────────────────────────────────────

    private async Task<(List<CodeSearchResult> Results, int FilesScanned)> SearchProjectAsync(
        string projectName,
        CodeSearchRequest request,
        CancellationToken ct)
    {
        var index = await _cache.GetIndexAsync(projectName);

        if (index.Count == 0)
        {
            _logger.LogWarning(
                "No cache index for '{Project}' — falling back to live search", projectName);
            var fallbackReq = new CodeSearchRequest
            {
                ProjectName = projectName,
                Query = request.Query,
                MemberType = request.MemberType,
                CaseSensitive = request.CaseSensitive,
                TopK = request.TopK
            };
            var fallbackResp = await _fallback.SearchGloballyAsync(fallbackReq, ct);
            return (fallbackResp.Results, fallbackResp.FilesScanned);
        }

        var results = new List<CodeSearchResult>();
        var scanned = 0;
        var liveFallbacks = 0;

        // Fetch all cached analyses in parallel (Redis GET is I/O, not CPU)
        var fetchTasks = index.Select(async relPath =>
        {
            var analysis = await _cache.GetAsync(projectName, relPath);

            if (analysis == null)
            {
                // Cache miss (TTL expired or race) — live fallback + async backfill
                Interlocked.Increment(ref liveFallbacks);
                analysis = await LiveAnalyseAndBackfillAsync(projectName, relPath, ct);
            }

            return analysis;
        });

        var analyses = await Task.WhenAll(fetchTasks);

        foreach (var analysis in analyses)
        {
            if (analysis == null || ct.IsCancellationRequested) continue;

            var fileResults = SearchInAnalysis(analysis, request);
            foreach (var r in fileResults)
                r.ProjectName = projectName;

            results.AddRange(fileResults);
            scanned++;
        }

        if (liveFallbacks > 0)
            _logger.LogInformation(
                "Project '{Project}': {Miss} cache misses required live analysis", projectName, liveFallbacks);

        _logger.LogInformation(
            "CachedSearch '{Project}': {Results} results from {Scanned} files",
            projectName, results.Count, scanned);

        return (results, scanned);
    }

    private async Task<CSharpFileAnalysis?> LiveAnalyseAndBackfillAsync(
        string projectName, string relativePath, CancellationToken ct)
    {
        try
        {
            var analysis = await _skeleton.AnalyzeCSharpFileAsync(
                projectName, relativePath, includePrivateMembers: true, ct);

            // Fire-and-forget backfill — don't block the search result
            _ = Task.Run(() => _cache.SetAsync(projectName, relativePath, analysis), CancellationToken.None);

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live fallback failed for {Project}:{Path}", projectName, relativePath);
            return null;
        }
    }

    // ── Search logic ──────────────────────────────────────────────────────────

    private static List<CodeSearchResult> SearchInAnalysis(CSharpFileAnalysis analysis, CodeSearchRequest request)
    {
        var results = new List<CodeSearchResult>();
        var cmp = request.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var cls in analysis.Classes)
        {
            if (ShouldInclude(request.MemberType, CodeMemberType.Class))
            {
                var s = Score(cls.Name, request.Query, cmp);
                if (s > 0) results.Add(new CodeSearchResult
                {
                    Name = cls.Name,
                    MemberType = CodeMemberType.Class,
                    FilePath = analysis.FilePath,
                    LineNumber = cls.LineNumber,
                    Modifiers = cls.Modifiers,
                    RelevanceScore = s,
                    TypeInfo = BuildClassTypeInfo(cls)
                });
            }

            if (ShouldInclude(request.MemberType, CodeMemberType.Interface))
            {
                foreach (var iface in cls.Interfaces)
                {
                    var s = Score(iface, request.Query, cmp);
                    if (s > 0) results.Add(new CodeSearchResult
                    {
                        Name = iface,
                        MemberType = CodeMemberType.Interface,
                        FilePath = analysis.FilePath,
                        LineNumber = cls.LineNumber,
                        ParentClass = cls.Name,
                        TypeInfo = $"Implemented by {cls.Name}",
                        Modifiers = new List<string>(),
                        RelevanceScore = s
                    });
                }
            }

            if (ShouldInclude(request.MemberType, CodeMemberType.Attribute))
                SearchAttributes(results, cls.Attributes, request.Query, cmp,
                    analysis.FilePath, cls.LineNumber, cls.Name, null);

            foreach (var m in cls.Methods)
            {
                if (ShouldInclude(request.MemberType, CodeMemberType.Method))
                {
                    var s = Score(m.Name, request.Query, cmp);
                    if (s > 0) results.Add(new CodeSearchResult
                    {
                        Name = m.Name,
                        MemberType = CodeMemberType.Method,
                        FilePath = analysis.FilePath,
                        LineNumber = m.LineNumber,
                        ParentClass = cls.Name,
                        Signature = BuildMethodSig(m),
                        Modifiers = m.Modifiers,
                        RelevanceScore = s
                    });
                }

                if (ShouldInclude(request.MemberType, CodeMemberType.Attribute))
                    SearchAttributes(results, m.Attributes, request.Query, cmp,
                        analysis.FilePath, m.LineNumber, cls.Name, m.Name);
            }

            foreach (var p in cls.Properties)
            {
                if (ShouldInclude(request.MemberType, CodeMemberType.Property))
                {
                    var s = Score(p.Name, request.Query, cmp);
                    if (s > 0) results.Add(new CodeSearchResult
                    {
                        Name = p.Name,
                        MemberType = CodeMemberType.Property,
                        FilePath = analysis.FilePath,
                        LineNumber = p.LineNumber,
                        ParentClass = cls.Name,
                        TypeInfo = p.Type,
                        Modifiers = p.Modifiers,
                        RelevanceScore = s
                    });
                }

                if (ShouldInclude(request.MemberType, CodeMemberType.Attribute))
                    SearchAttributes(results, p.Attributes, request.Query, cmp,
                        analysis.FilePath, p.LineNumber, cls.Name, p.Name);
            }

            foreach (var f in cls.Fields)
            {
                if (ShouldInclude(request.MemberType, CodeMemberType.Field))
                {
                    var s = Score(f.Name, request.Query, cmp);
                    if (s > 0) results.Add(new CodeSearchResult
                    {
                        Name = f.Name,
                        MemberType = CodeMemberType.Field,
                        FilePath = analysis.FilePath,
                        LineNumber = f.LineNumber,
                        ParentClass = cls.Name,
                        TypeInfo = f.Type,
                        Modifiers = f.Modifiers,
                        RelevanceScore = s
                    });
                }

                if (ShouldInclude(request.MemberType, CodeMemberType.Attribute))
                    SearchAttributes(results, f.Attributes, request.Query, cmp,
                        analysis.FilePath, f.LineNumber, cls.Name, f.Name);
            }
        }

        return results;
    }

    // ── Scoring ───────────────────────────────────────────────────────────────
    //
    // Mirrors Visual Studio Ctrl+, "Navigate To" — four tiers, no fuzzy:
    //
    //   1000  Exact match        "Redis"  → Redis
    //    500  Prefix match       "Red"    → RedisCache
    //    300  CamelCase acronym  "RTC"    → RisingTideCache   (all-uppercase query only, always case-sensitive)
    //    100  Substring match    "edi"    → Redis
    //      0  No match
    //
    // The old character-subsequence fuzzy is intentionally removed — it matched
    // "US" inside "UpdateSettings" just because U and S appear in order, producing
    // noise across large cross-project searches.
    // ─────────────────────────────────────────────────────────────────────────

    private static double Score(string name, string query, StringComparison cmp)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(query)) return 0;

        if (name.Equals(query, cmp)) return 1000;
        if (name.StartsWith(query, cmp)) return 500;
        if (IsCamelCaseAcronym(name, query)) return 300;
        if (name.Contains(query, cmp)) return 100;

        return 0;
    }

    /// <summary>
    /// Only activates when query is all-uppercase (user is in acronym mode).
    /// Extracts uppercase initials from PascalCase/camelCase name and checks prefix.
    /// "RTC" → "RisingTideCache" ✓    "rtc" → skipped (not acronym mode)
    /// </summary>
    private static bool IsCamelCaseAcronym(string name, string query)
    {
        if (query != query.ToUpperInvariant() || query.Length < 2) return false;
        var initials = string.Concat(name.Where(char.IsUpper));
        return initials.StartsWith(query, StringComparison.Ordinal);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SearchAttributes(
        List<CodeSearchResult> results,
        List<AttributeInfo>? attributes,
        string query, StringComparison cmp,
        string filePath, int lineNumber,
        string parentClass, string? parentMember)
    {
        if (attributes == null || attributes.Count == 0) return;
        foreach (var attr in attributes)
        {
            var attrName = ExtractAttributeName(attr.Name);
            var s = Score(attrName, query, cmp);
            if (s > 0) results.Add(new CodeSearchResult
            {
                Name = attrName,
                MemberType = CodeMemberType.Attribute,
                FilePath = filePath,
                LineNumber = lineNumber,
                ParentClass = parentClass,
                ParentMember = parentMember,
                Signature = parentMember != null ? $"On {parentMember}" : "Class-level",
                Modifiers = new List<string>(),
                RelevanceScore = s
            });
        }
    }

    private static string ExtractAttributeName(string attributeText)
    {
        var name = attributeText.TrimStart('[').Split('(')[0].Trim().TrimEnd(']');
        return name.EndsWith("Attribute") ? name[..^"Attribute".Length] : name;
    }

    private static bool ShouldInclude(CodeMemberType filter, CodeMemberType type) =>
        filter == CodeMemberType.All || filter == type;

    private static string BuildClassTypeInfo(ClassInfo c)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(c.BaseClass)) parts.Add($"extends {c.BaseClass}");
        if (c.Interfaces.Count > 0) parts.Add($"implements {string.Join(", ", c.Interfaces)}");
        return string.Join(", ", parts);
    }

    private static string BuildMethodSig(MethodInfo m)
    {
        var ps = string.Join(", ", m.Parameters.Select(p => $"{p.Type} {p.Name}"));
        return $"{m.ReturnType} {m.Name}({ps})";
    }

    private static List<CodeSearchResult> RankAndFilter(List<CodeSearchResult> results, CodeSearchRequest req) =>
        results.OrderByDescending(r => r.RelevanceScore).ThenBy(r => r.Name).ToList();
}
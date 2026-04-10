# dotnet-mcp-server

A self-hosted [Model Context Protocol (MCP)](https://modelcontextprotocol.io) server that gives Claude surgical, token-efficient access to your local C# codebase — without ever sending raw source to the cloud.

Built on **.NET 10**, powered by **Roslyn**, backed by **Redis**.

---

## The problem it solves

Every AI coding tool eventually hits the same wall: context windows fill up, library APIs get hallucinated, and your code leaves your machine with every paste.

`dotnet-mcp-server` is a different model. Instead of dumping files into Claude's context, it runs locally and exposes your codebase as a set of structured MCP tools. Claude calls only what it needs — one class at a time, one method at a time — from the exact version of every dependency you have installed, across all your projects at once, with zero data leaving your machine.

---

## How it works

```
Claude.ai ──HTTPS──► ngrok tunnel ──SSE──► dotnet-mcp-server ──Roslyn──► Your C# source
                                                   │
                                                   ├── Redis ──► AST cache + project index
                                                   │
                                                   └── NuGet ──► Installed package reflection
```

1. **Claude calls a tool** — e.g. `analyze_c_sharp_file`.
2. **Redis is checked first.** Cache hit → response in milliseconds, zero disk I/O.
3. **On a miss**, Roslyn parses the `.cs` file and extracts structured metadata: namespaces, classes, methods with line ranges, DI constructor graphs, attributes, XML docs. The result is serialized and stored in Redis with a configurable TTL.
4. **A `FileSystemWatcher`** monitors every registered project path. On any `Create`, `Change`, `Delete`, or `Rename` event, a 300 ms debounce fires and the affected file is re-analysed and written back to Redis — so Claude always sees the code as it exists on disk right now.
5. **A background indexer** runs on startup and on a configurable schedule (default: every 60 minutes), walking every `.cs` file across all registered projects and pre-warming the cache. The first tool call is never cold.
6. **Claude gets a compact representation** — never raw source — and uses it to reason, suggest edits, check impact, or look up exact library signatures.

---

## Token efficiency — why it matters

A 500-line service class costs roughly 2 000 tokens to load raw. Ten files and you've burned your entire context budget before writing a single line of code.

This server uses a tiered reading strategy instead:

| Step | Tool | What you get | Typical token cost |
|------|------|--------------|--------------------|
| 1 | `get_project_skeleton` | Full folder tree, file sizes, NuGet packages | ~200 |
| 2 | `analyze_c_sharp_file` | Class API surface: methods, properties, DI graph | ~300–500 |
| 3 | `fetch_method_implementation` | Exact method body with line numbers | ~80–150 |
| 4 | `read_file_content` | Raw content — non-C# files or tiny scripts only | full file |

For a typical multi-service session this approach saves **10–20× tokens** compared to loading files directly. All four steps can batch: analyze 7 files or fetch 3 methods in a single tool call.

---

## Features

### Roslyn-powered C# analysis

The core of the server is a Roslyn syntax tree walker that runs on every `.cs` file. It extracts:

- Namespace and all `using` directives
- Every class with its modifiers, base type, and implemented interfaces
- Constructor parameters mapped as a dependency injection graph (what the class depends on, typed)
- Every method: full signature, return type, parameters with types, attributes, XML doc comments, and exact start/end line numbers
- Properties, fields, and constants with modifiers and types
- A `private` vs `public` mode toggle — default is public API surface only; flip `includePrivateMembers=true` to see internals during debugging

Batch mode accepts comma-separated file paths with no spaces: `Services/UserService.cs,Controllers/OrderController.cs`.

### Precise method fetching with line numbers

`fetch_method_implementation` returns the complete body of a method with every line numbered. Line numbers are exact — Claude can reference them directly in `edit_lines` patch operations. Batch mode (`Method1,Method2`) fetches multiple methods from one file in a single round trip.

### Method call graph analysis

Before touching a method signature, run `analyze_method_call_graph`. It walks every `.cs` file in the project using a Roslyn syntax walker and returns:

- Every caller: exact file path, class name, and line number
- Every outgoing call from the target method
- Paginated results (`page` / `pageSize` up to 200) for high-traffic methods

This is the difference between a safe refactor and a breaking change that only surfaces in CI.

### Live file operations with safety guarantees

Claude can create, edit, move, rename, and delete files — all guarded by:

- **Per-file semaphore locking**: concurrent writes to the same file are serialized, never dropped or interleaved
- **Atomic batch validation for moves**: all destinations are validated before any file moves; one failure aborts the entire batch
- **Path sandboxing**: every operation is resolved against the registered project root — path traversal is structurally impossible
- **Blocked path patterns**: `bin/`, `obj/`, `.git/`, `.vs/`, `node_modules/`, and any file matching password/token/secret filename patterns are permanently blocked at the service layer, not as a config flag
- **Automatic cache eviction**: every successful write invalidates the corresponding Redis keys so the next analysis call sees the updated file, not a stale snapshot
- **`edit_lines` bottom-up application**: patches are validated for overlaps, then applied in descending line-number order — original line numbers stay correct for every patch in the batch

Supported write modes for `write_file`: `create` (fails if file exists), `overwrite` (fails if file is missing), `upsert` (always succeeds).

### NuGet package exploration — zero hallucinations

The server reflects the actual DLL installed in your project using `System.Reflection.MetadataLoadContext`. It does not guess from training data.

Four-step pipeline:

```
search_nu_get_packages          ← find the package, confirm the ID
get_nu_get_package_namespaces   ← list all namespaces in the installed version
get_namespace_summary           ← all types, methods, and properties, copy-paste ready
get_method_overloads            ← expand collapsed overload groups on demand
```

Every signature targets your exact installed version and target framework. If the answer is "that method doesn't exist in the version you have" — that's the answer Claude gets.

### Global code search with pagination

`search_code_globally` finds classes, interfaces, methods, properties, and fields by name or keyword. Scope it to one project or pass `projectName="*"` to search across every registered project simultaneously. Results are fully paginated (`page` / `pageSize` up to 200 per page).

Practical uses:
- Security audit: `search_code_globally("*", "Authorize")` — find every authorization point across all services
- Dependency check: `search_code_globally("*", "IOrderService")` — find every consumer before renaming an interface
- Pre-refactor impact: locate all references to a class before splitting it

### Multi-project support from day one

Register as many projects as you have. Every tool accepts `projectName`. Claude can reason across your entire microservices solution in a single conversation — skeleton one service, read a method from another, edit a third — with no context switching and no copy-pasting between windows.

### Background indexing and real-time cache coherence

Two background services run independently:

- **`CSharpAnalysisBackgroundService`**: on startup, walks all registered projects and pre-warms Redis with full Roslyn analysis and method bodies. Re-runs on a configurable schedule (default: 60 minutes). Concurrency is bounded by `IndexingConcurrency` (default: 4 parallel Roslyn parses).
- **`CSharpFileWatcherService`**: registers a `FileSystemWatcher` per project. On any `.cs` file event, debounces 300 ms (to handle the burst of events Visual Studio fires on a single save), then re-analyses only the changed file and updates Redis. Delete events evict the key. Rename events evict the old key and index the new path.

Both services coexist without blocking each other. The watcher loop and the scheduled full-pass loop run on separate tasks.

---

## Tech stack

| Layer | Technology |
|-------|------------|
| Runtime | .NET 10, ASP.NET Core |
| C# analysis | Microsoft.CodeAnalysis.CSharp (Roslyn) 5.0 |
| MCP host | ModelContextProtocol + ModelContextProtocol.AspNetCore |
| Cache & index | Redis via StackExchange.Redis + NRedisStack |
| NuGet reflection | NuGet.Protocol, NuGet.Packaging, System.Reflection.MetadataLoadContext |
| Config serialization | Tomlyn (TOML) |
| Transport | Server-Sent Events (SSE) over HTTPS via ngrok |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Redis](https://redis.io/docs/getting-started/) (local or Docker)
- [ngrok](https://ngrok.com/download) (to expose the server to Claude.ai)
- A [Claude.ai](https://claude.ai) account with MCP connector support
- Windows, macOS, or Linux

---

## Setup

### 1. Clone and build

```bash
git clone https://github.com/patilprashant6792-official/dotnet-mcp-server
cd dotnet-mcp-server/LocalMcpServer
dotnet build
```

### 2. Start Redis

```bash
# Docker — quickest path
docker run -d -p 6379:6379 redis:latest

# Or use an existing local Redis on the default port 6379
```

### 3. Configure

Edit `appsettings.Development.json` for local overrides:

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379",
    "ConnectTimeout": 3000,
    "SyncTimeout": 3000,
    "ConnectRetry": 2
  },
  "AnalysisCache": {
    "TtlHours": 24,
    "RefreshIntervalMinutes": 60,
    "IndexingConcurrency": 4,
    "FileWatcherDebounceMs": 300
  }
}
```

| Setting | Default | What it controls |
|---------|---------|------------------|
| `TtlHours` | 24 | How long each file analysis lives in Redis |
| `RefreshIntervalMinutes` | 60 | How often the background indexer re-scans all projects |
| `IndexingConcurrency` | 4 | Max parallel Roslyn parses during bulk indexing |
| `FileWatcherDebounceMs` | 300 | Wait after a file-change event before re-analysing |

### 4. Run the server

```bash
cd LocalMcpServer
dotnet run
# Starts on http://localhost:5000
```

### 5. Register your projects

Open the web UI at `http://localhost:5000/config.html` and add your project paths. The UI is a plain HTML page served by the server itself — no separate frontend to run.

Alternatively via the REST API:

```bash
curl -X POST http://localhost:5000/api/project-config \
  -H "Content-Type: application/json" \
  -d '{"name": "MyApi", "path": "C:/source/MyApi", "description": "Main API service"}'
```

Project config is persisted in Redis — register once, it survives server restarts.

### 6. Expose with ngrok

Claude.ai requires a publicly reachable HTTPS URL. ngrok creates one in seconds:

```bash
# Install
brew install ngrok          # macOS
winget install ngrok        # Windows

# Tunnel
ngrok http 5000
```

ngrok prints:
```
Forwarding  https://a1b2-203-0-113-42.ngrok-free.app -> http://localhost:5000
```

Copy that `https://` URL.

> **Free plan note:** The ngrok URL changes on every restart. Update the Claude.ai connector URL when this happens, or use a paid ngrok plan for a stable domain.

### 7. Connect Claude.ai

In Claude.ai → **Settings** → **Connectors** → **Add connector**:

| Field | Value |
|-------|-------|
| Name | `dotnet-mcp-server` |
| URL | `https://<your-ngrok-id>.ngrok-free.app/sse` |

Claude discovers all tools automatically via the MCP capability negotiation protocol.

### 7b. Connect Claude Code Desktop (alternative)

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "https://<your-ngrok-id>.ngrok-free.app/sse", "--transport", "sse-only"]
    }
  }
}
```

`mcp-remote` is an npm bridge that proxies SSE-based MCP servers into the stdio transport Claude Code Desktop expects. `npx -y` installs it on demand — no global install needed.

---

## All 20 MCP tools

### Code analysis

| Tool | Description |
|------|-------------|
| `analyze_c_sharp_file` | Roslyn-extracted metadata: namespaces, classes, methods, properties, DI graph, line ranges. Batch mode: comma-separated paths, no spaces. |
| `fetch_method_implementation` | Complete method body with per-line line numbers. Batch mode: comma-separated method names. |
| `read_file_content` | Raw file content. Blocked for `appsettings.json`, secrets, `.env`, and `bin/`/`obj/` paths. |
| `analyze_method_call_graph` | All callers (file, class, line) and outgoing calls for a method. Paginated — `page` / `pageSize` up to 200. |

### Project exploration

| Tool | Description |
|------|-------------|
| `get_project_skeleton` | ASCII folder tree with file sizes and NuGet package list. Pass `"*"` to list all registered projects. Supports `sinceTimestamp` for incremental diffs. |
| `search_folder_files` | Paginated file listing within a folder. Optional filename filter. Use when a folder has 50+ files. |
| `search_code_globally` | Name/keyword search across one project or all (`"*"`). Filters by member type. Paginated — `page` / `pageSize` up to 200. |

### File operations

| Tool | Description |
|------|-------------|
| `write_file` | Create or overwrite files. Modes: `create` / `overwrite` / `upsert`. Parent dirs created automatically. Batch supported. |
| `edit_lines` | Apply multiple `patch` / `insert` / `delete` / `append` operations to a file atomically. Bottom-up application. Overlap-validated. |
| `move_file` | Move or rename files. All-or-nothing batch: validation runs on every destination before any file moves. |
| `delete_file` | Delete files. Each file is independent — partial success is possible. Blocked paths enforced. |
| `create_folder` | Create one or more folders including nested paths. Idempotent. |
| `move_folder` | Move or rename a folder. Single operation only (high-impact). Evicts all Redis keys under the old path before moving. |
| `delete_folder` | Recursively delete folders. Deepest-first ordering. Non-existent folders silently skipped. |
| `get_file_info` | Metadata only — existence, line count, byte size, last modified (UTC ISO 8601). Never reads content. |

### NuGet exploration

| Tool | Description |
|------|-------------|
| `search_nu_get_packages` | Search NuGet.org by package ID. Use the exact ID (`Microsoft.EntityFrameworkCore`, not `EntityFrameworkCore`). |
| `get_nu_get_package_namespaces` | List all namespaces in a package at a specific version and target framework. |
| `get_namespace_summary` | All types, methods, and properties in a namespace — production-ready, copy-paste C# signatures. |
| `get_method_overloads` | Expand collapsed overload groups when `get_namespace_summary` shows `+ N overloads`. |

### Utility

| Tool | Description |
|------|-------------|
| `get_date_time` | Current date/time in UTC, server local time, or any IANA timezone (e.g. `Asia/Kolkata`). |

---

## Recommended workflows

### Exploring and editing code

```
1. get_project_skeleton("*")                     orient — see all registered projects
2. get_project_skeleton("MyApi")                 understand the target project structure
3. analyze_c_sharp_file("MyApi", "Svc/A.cs,Svc/B.cs")   batch — inspect two classes at once
4. fetch_method_implementation("MyApi", "Svc/A.cs", "ProcessOrder")   read the method
5. analyze_method_call_graph("MyApi", "Svc/A.cs", "ProcessOrder")      who calls it?
6. edit_lines(...)                               make the change
7. analyze_c_sharp_file(...)                     verify the result
```

### Looking up a library API

```
1. search_nu_get_packages("Microsoft.EntityFrameworkCore")   confirm the package ID
2. get_nu_get_package_namespaces(packageId, version)         discover namespaces
3. get_namespace_summary(namespace, packageId, version)      get all signatures
4. get_method_overloads(...)                                 expand if needed
```

---

**Design rules enforced throughout:**
- MCP tool classes are thin wrappers — no business logic, only parameter parsing and formatting
- Every tool class depends only on interfaces — concrete implementations are injected
- All services are registered as singletons; thread safety is handled inside each service
- Redis keys follow `{type}:{project}:{normalizedPath}` — lowercase, forward-slash normalized
- Blocked path patterns are enforced at the `IFileModificationService` layer, not in individual tools

---

## Contributing

Pull requests are welcome.

- Keep files under 500 lines; split by responsibility
- New MCP tools go in `MCPServers/` as thin wrappers over a new service interface
- New services use constructor injection and must be registered in `Program.cs`
- Redis keys must follow the `type:project:path` convention in `RedisAnalysisCacheService`
- Never access `appsettings.json` or `.env` directly from a tool handler — use `IOptions<T>`

---

## License

MIT

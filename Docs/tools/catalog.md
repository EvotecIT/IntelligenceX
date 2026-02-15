---
title: Tool Catalog
description: Complete listing of all IntelligenceX tool packs and the tools they expose.
---

# Tool Catalog

IntelligenceX ships 8 tool packs. Each pack is a standalone NuGet package.

## Email

**Package**: `IntelligenceX.Tools.Email`
**Platform**: Cross-platform

Compose, search, and manage emails via Exchange Web Services and Microsoft Graph API.

| Tool | Description |
|---|---|
| `SendEmail` | Compose and send an email message |
| `SearchEmail` | Search mailbox by subject, sender, date range |
| `GetMailboxInfo` | Get mailbox statistics and folder counts |

## Active Directory

**Package**: `IntelligenceX.Tools.ActiveDirectory`
**Platform**: Windows

Query and manage AD users, groups, computers, and organizational units.

| Tool | Description |
|---|---|
| `GetADUser` | Look up user properties by name or SAM account |
| `GetADGroup` | Get group membership and properties |
| `GetADComputer` | Query computer objects and their attributes |
| `SearchAD` | LDAP search with custom filters |

## File System

**Package**: `IntelligenceX.Tools.FileSystem`
**Platform**: Cross-platform

Read, write, search, and manage files and directories with safety controls.

| Tool | Description |
|---|---|
| `ReadFile` | Read file contents with encoding detection |
| `WriteFile` | Write or append to files with backup |
| `SearchFiles` | Glob-based file search with content matching |
| `ListDirectory` | List directory contents with filtering |

## Web

**Package**: `IntelligenceX.Tools.Web`
**Platform**: Cross-platform

HTTP requests, web scraping, and API interaction with configurable auth.

| Tool | Description |
|---|---|
| `HttpRequest` | Send HTTP requests with headers and body |
| `FetchPage` | Fetch and extract text content from a URL |
| `ParseHtml` | Parse HTML and extract elements via CSS selectors |

## Database

**Package**: `IntelligenceX.Tools.Database`
**Platform**: Cross-platform

Execute queries and manage connections for SQL Server, PostgreSQL, and SQLite.

| Tool | Description |
|---|---|
| `ExecuteQuery` | Run a SQL query and return results |
| `GetSchema` | Get table schemas and column definitions |
| `ListTables` | List all tables in a database |

## Azure

**Package**: `IntelligenceX.Tools.Azure`
**Platform**: Cross-platform

Manage Azure resources, subscriptions, and services through AI-driven commands.

| Tool | Description |
|---|---|
| `GetAzureResource` | Get details of an Azure resource |
| `ListSubscriptions` | List available Azure subscriptions |
| `GetAzureStatus` | Check Azure service health status |

## Office

**Package**: `IntelligenceX.Tools.Office`
**Platform**: Cross-platform

Create and manipulate Word, Excel, and PowerPoint documents programmatically.

| Tool | Description |
|---|---|
| `CreateDocument` | Create a Word document from template |
| `ReadSpreadsheet` | Read Excel spreadsheet data |
| `GenerateReport` | Generate formatted reports |

## System

**Package**: `IntelligenceX.Tools.System`
**Platform**: Windows

System information, process management, and Windows service monitoring.

| Tool | Description |
|---|---|
| `GetSystemInfo` | Get OS, CPU, memory, and disk information |
| `GetProcess` | List and query running processes |
| `GetService` | Query Windows service status |
| `GetEventLog` | Search Windows Event Log entries |

## Installing Tool Packs

```bash
# Install individual packs
dotnet add package IntelligenceX.Tools.Email
dotnet add package IntelligenceX.Tools.FileSystem

# Or reference in your .csproj
```

```xml
<PackageReference Include="IntelligenceX.Tools.Email" Version="*" />
<PackageReference Include="IntelligenceX.Tools.FileSystem" Version="*" />
```

## Related

- [IX Tools Overview](/docs/tools/overview/) -- Design goals and architecture
- [Tool Calling](/docs/library/tools/) -- How to use tools in code
- [IX Chat](/docs/chat/overview/) -- Desktop app with tool support

using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Describes a native sidebar entry used by the project/chat/artifact rail.
/// </summary>
internal sealed class NativeSidebarItem {
    private NativeSidebarItem(
        string id,
        string category,
        string title,
        string subtitle,
        string badge,
        string workspaceTitle,
        string workspaceSubtitle) {
        Id = id;
        Category = category;
        Title = title;
        Subtitle = subtitle;
        Badge = badge;
        WorkspaceTitle = workspaceTitle;
        WorkspaceSubtitle = workspaceSubtitle;
    }

    public string Id { get; }

    public string Category { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string Badge { get; }

    public string WorkspaceTitle { get; }

    public string WorkspaceSubtitle { get; }

    public static NativeSidebarItem Default { get; } = Create(
        "chat-risky-inactive-admins",
        "Chats",
        "Risky inactive admins",
        "Table + topology ready",
        "now",
        "Risky inactive admins",
        "AD Security Sweep / chat with native tables and diagrams");

    public static IReadOnlyList<NativeSidebarItem> All { get; } = new[] {
        Create("project-ad-security", "Projects", "AD Security Sweep", "Risky admins, group evidence", "3", "AD Security Sweep", "Project workspace / risky admins, group evidence"),
        Create("project-m365-tenant", "Projects", "M365 Tenant Review", "Exchange, Teams, licenses", "8", "M365 Tenant Review", "Project workspace / Exchange, Teams, licenses"),
        Create("project-dns-mail", "Projects", "DNS / Mail Auth", "SPF, DKIM, DMARC", "2", "DNS / Mail Auth", "Project workspace / SPF, DKIM, DMARC evidence"),
        Create("project-incident-notes", "Projects", "Incident Notes", "Suspicious admin change", "1", "Incident Notes", "Project workspace / suspicious admin change"),
        Default,
        Create("chat-group-nesting", "Chats", "Group nesting cleanup", "Completed", "done", "Group nesting cleanup", "AD Security Sweep / completed cleanup chat"),
        Create("chat-mfa-exceptions", "Chats", "MFA exceptions", "Draft", "draft", "MFA exceptions", "M365 Tenant Review / draft exception review"),
        Create("artifact-directory-objects", "Pinned", "Directory Objects", "74 rows", "table", "Directory Objects", "Pinned native table artifact / 74 rows"),
        Create("artifact-ad-topology", "Pinned", "AD Topology", "2 domains, 8 DCs", "map", "AD Topology", "Pinned ChartForgeX topology artifact / 2 domains, 8 DCs")
    };

    public bool Matches(string? query) {
        var text = (query ?? string.Empty).Trim();
        if (text.Length == 0) {
            return true;
        }

        return SearchParts().Any(part => part.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<string> SearchParts() {
        yield return Category;
        yield return Title;
        yield return Subtitle;
        yield return Badge;
        yield return WorkspaceTitle;
        yield return WorkspaceSubtitle;
    }

    private static NativeSidebarItem Create(
        string id,
        string category,
        string title,
        string subtitle,
        string badge,
        string workspaceTitle,
        string workspaceSubtitle) =>
        new(id, category, title, subtitle, badge, workspaceTitle, workspaceSubtitle);
}

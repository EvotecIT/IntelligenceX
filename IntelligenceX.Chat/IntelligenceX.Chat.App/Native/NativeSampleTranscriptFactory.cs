using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Creates deterministic sample-mode transcript content for native shell verification.
/// </summary>
internal static class NativeSampleTranscriptFactory {
    public static IReadOnlyList<NativeChatTranscriptItem> Create(NativeSidebarItem item, DateTimeOffset now) {
        if (item == null) throw new ArgumentNullException(nameof(item));

        return item.Id switch {
            "project-ad-security" => BuildAdSecuritySweep(now),
            "project-m365-tenant" => BuildM365TenantReview(now),
            "project-dns-mail" => BuildDnsMailAuth(now),
            "project-incident-notes" => BuildIncidentNotes(now),
            "artifact-ad-topology" => BuildAdTopology(now),
            "artifact-directory-objects" => BuildDirectoryObjects(now),
            "chat-group-nesting" => BuildGroupNesting(now),
            "chat-mfa-exceptions" => BuildMfaExceptions(now),
            _ => BuildRiskyInactiveAdmins(now)
        };
    }

    private static IReadOnlyList<NativeChatTranscriptItem> BuildAdSecuritySweep(DateTimeOffset now) =>
        CreatePair(
            "Summarize the AD security sweep and list the open evidence groups.",
            """
            AD Security Sweep has 3 active workstreams: privileged accounts, nested groups, and stale GPO links.

            | Workstream | Open Items | Highest Risk | Next Action |
            | --- | --- | --- | --- |
            | Privileged accounts | 4 | High | Review stale admins |
            | Nested groups | 2 | Medium | Confirm cleanup batch |
            | Stale GPO links | 3 | Medium | Export linked-disabled GPOs |

            Recommended next step: continue the risky inactive admins chat.
            """,
            now);

    private static IReadOnlyList<NativeChatTranscriptItem> BuildRiskyInactiveAdmins(DateTimeOffset now) =>
        CreatePair(
            "Review risky inactive admins and show the evidence as tables and diagrams.",
            """
            Found 4 privileged accounts that need review. Two are stale, one has weak sign-in evidence, and one has an unclear owner.

            | Account | Type | OU | Enabled | MFA | Risk | Last Sign-in |
            | --- | --- | --- | --- | --- | --- | --- |
            | svc-sync-legacy | Service | OU=Service Accounts | true | n/a | High | 189 days |
            | adam.kowalski | User | OU=Admins | true | false | High | 46 days |
            | breakglass-02 | User | OU=Emergency | true | true | Medium | 91 days |
            | old-da-review | User | OU=Disabled | false | true | Low | 301 days |

            ```mermaid
            flowchart LR
              A[Collect AD objects] --> B[Check privileged groups]
              B --> C[Join Entra sign-in evidence]
              C --> D[Review stale or risky admins]
              D --> E[Export cleanup plan]
            ```

            Recommended next step: simulate disabling stale non-breakglass accounts and export the evidence bundle.
            """,
            now);

    private static IReadOnlyList<NativeChatTranscriptItem> BuildM365TenantReview(DateTimeOffset now) =>
        CreatePair(
            "Summarize the tenant review and show the riskiest workload gaps.",
            """
            The tenant review has 8 active findings across Exchange, Teams, and licensing.

            | Workload | Finding | Severity | Owner |
            | --- | --- | --- | --- |
            | Exchange | External forwarding exceptions | High | Messaging |
            | Teams | Guest access policy drift | Medium | Collaboration |
            | Entra | 14 unused privileged licenses | Medium | Identity |
            | SharePoint | Anonymous links older than 90 days | High | Content |

            Recommended next step: open the Exchange forwarding evidence and prepare a change plan.
            """,
            now);

    private static IReadOnlyList<NativeChatTranscriptItem> BuildDnsMailAuth(DateTimeOffset now) =>
        CreatePair(
            "Check mail authentication posture and call out missing records.",
            """
            SPF and DKIM are present, but DMARC is still in monitor mode.

            | Domain | SPF | DKIM | DMARC | Risk |
            | --- | --- | --- | --- | --- |
            | contoso.com | pass | pass | p=none | Medium |
            | mail.contoso.com | pass | missing selector2 | p=none | High |
            | fabrikam.test | softfail | pass | missing | High |

            Recommended next step: fix DKIM selector2 before moving DMARC toward quarantine.
            """,
            now);

    private static IReadOnlyList<NativeChatTranscriptItem> BuildIncidentNotes(DateTimeOffset now) =>
        CreatePair(
            "Open the incident notes and extract the suspicious admin change timeline.",
            """
            Incident notes show one suspicious privileged change that still needs owner confirmation.

            | Time | Actor | Action | Evidence |
            | --- | --- | --- | --- |
            | 08:13 | helpdesk-jr | Added user to Server Operators | DC security log |
            | 08:19 | helpdesk-jr | Removed user from Server Operators | DC security log |
            | 08:27 | sync-agent | Modified adminDescription | Directory audit |

            ```mermaid
            timeline
              title Suspicious admin change
              08:13 : Privileged membership added
              08:19 : Membership removed
              08:27 : Directory metadata changed
            ```

            Recommended next step: verify whether the helpdesk action came from an approved ticket.
            """,
            now);

    private static IReadOnlyList<NativeChatTranscriptItem> BuildAdTopology(DateTimeOffset now) =>
        CreatePair(
            "Show the current AD topology as a native visual artifact.",
            """
            The topology contains two domains and eight domain controllers. The branch site has only one writable DC.

            ```mermaid
            flowchart LR
              Forest[corp.example forest] --> Root[corp.example]
              Forest --> Child[emea.corp.example]
              Root --> DC1[HQ-DC01]
              Root --> DC2[HQ-DC02]
              Child --> DC3[EMEA-DC01]
              Child --> Branch[Branch-RODC01]
            ```

            Recommended next step: validate replication health before planned maintenance.
            """,
            now);

    private static IReadOnlyList<NativeChatTranscriptItem> BuildDirectoryObjects(DateTimeOffset now) =>
        CreatePair(
            "Open the pinned directory object evidence.",
            """
            The pinned directory object sample is filtered to high-value evidence rows.

            | Object | Kind | Owner | Finding |
            | --- | --- | --- | --- |
            | Domain Admins | Group | Identity | Nested legacy group |
            | svc-sync-legacy | User | Platform | Stale service account |
            | breakglass-02 | User | Security | Review sign-in evidence |
            | GPO-Old-LoginScript | GPO | Endpoint | Disabled but linked |
            """,
            now);

    private static IReadOnlyList<NativeChatTranscriptItem> BuildGroupNesting(DateTimeOffset now) =>
        CreatePair(
            "Show the completed group nesting cleanup summary.",
            """
            Group nesting cleanup is complete for the first batch.

            | Group | Removed Members | Remaining Risk |
            | --- | --- | --- |
            | Helpdesk-Admins | 4 | Low |
            | Legacy-Operators | 7 | Medium |
            | Domain Admins | 1 | Low |

            No new privileged inheritance paths were introduced.
            """,
            now);

    private static IReadOnlyList<NativeChatTranscriptItem> BuildMfaExceptions(DateTimeOffset now) =>
        CreatePair(
            "Draft an MFA exception review with evidence.",
            """
            Draft review found three active exceptions. One has no expiry date.

            | Account | Exception | Expires | Risk |
            | --- | --- | --- | --- |
            | scanner-app | Legacy auth scanner | 2026-07-01 | Medium |
            | breakglass-01 | Emergency access | reviewed quarterly | Low |
            | vendor-sync | Conditional access bypass | none | High |

            Recommended next step: assign an owner and expiry date for vendor-sync before approval.
            """,
            now);

    private static IReadOnlyList<NativeChatTranscriptItem> CreatePair(string prompt, string response, DateTimeOffset now) =>
        new[] {
            new NativeChatTranscriptItem("user", prompt, now.AddMinutes(-4)),
            new NativeChatTranscriptItem("assistant", response, now.AddMinutes(-3), "Complete")
        };
}

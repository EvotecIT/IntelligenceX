# IntelligenceX.Ledger.Contracts

Typed Ledger contracts and authenticated HTTP clients for .NET 8 and .NET 10.
Use the shared client in dashboards, tray applications and command-line tools
that read evidence-backed work state from a Ledger service.

```sh
dotnet add package IntelligenceX.Ledger.Contracts
```

```csharp
using IntelligenceX.Ledger.Contracts;

using var client = new LedgerReadClient(
    LedgerClientSettings.Load("ledger-reader.json"));

var work = await client.WorkAsync();
foreach (var row in work.Items) {
    Console.WriteLine($"{row.Label} · {row.Item.Verdict} · since {row.Item.SinceUtc:O}");
}

if (work.Items.FirstOrDefault() is { } first) {
    var detail = await client.ItemAsync(first.Item.Id);
    Console.WriteLine(detail.ReasonCode);
}
```

Keep the reader configuration and its token in local application data outside
the source checkout. Configure the service endpoint and optional certificate
SHA-256 pin through `LedgerClientSettings`. The client bounds response sizes,
disables redirects and proxy credential forwarding, and supports cancellation.

The service supplies verdicts, reason codes, evidence and observation times.
Nullable facts and coverage notes preserve gaps in the available evidence.

The package also contains event, declaration and coordination contracts.
Coordination commands use separately enrolled credentials through
`LedgerReadClient.ForCoordination`. The service checks each principal's role,
project, machine and task scope. Observation credentials retain their read
access boundary.

# IntelligenceX.Storage.SQLite

`IntelligenceX.Storage.SQLite` adds persistent SQLite storage to the `IntelligenceX` SDK. It provides stores for:

- AI usage events, account bindings, source roots, and raw artifacts
- GitHub repository, fork, and monitoring snapshots
- local Codex state diagnostics and recovery

The package depends on `IntelligenceX` and `DBAClientX.SQLite`. Applications that only need the in-memory SDK should install `IntelligenceX` alone.

The package currently supports .NET 8 and .NET 10. The dependency-light `IntelligenceX` SDK also supports .NET Standard 2.0 and .NET Framework 4.7.2; SQLite packaging for .NET Framework remains unavailable until its database dependency provides a compatible standalone runtime graph.

```powershell
dotnet add package IntelligenceX.Storage.SQLite
```

```csharp
using IntelligenceX.Telemetry.Usage;

using var events = new SqliteUsageEventStore("intelligencex.db");
```

Documentation and source are available in the [IntelligenceX repository](https://github.com/EvotecIT/IntelligenceX).

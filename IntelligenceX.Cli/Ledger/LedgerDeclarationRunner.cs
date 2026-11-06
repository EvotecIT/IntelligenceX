using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;

namespace IntelligenceX.Cli.Ledger;

/// <summary>Explicit worker boundary declarations use the reporter's durable outbox and its configured identity.</summary>
internal static class LedgerDeclarationRunner {
    internal static int Run(string[] args) {
        var validateOnly = args.Length == 7 && args[6] == "--validate";
        if ((args.Length != 6 && !validateOnly) || args[4] != "--config" || args[1] is not ("codex" or "claude") ||
            !Guid.TryParse(args[2], out var sessionId)) {
            Console.Error.WriteLine("ix ledger declare <codex|claude> <source-session-id> <declaration-json> --config <reporter-json> [--validate]");
            return 2;
        }
        try {
            if (new FileInfo(args[3]).Length > 16 * 1024) throw new InvalidDataException();
            var declaration = JsonSerializer.Deserialize<WorkerDeclaration>(File.ReadAllText(args[3]), LedgerJson.Options);
            if (declaration is null || declaration.Scope is null || declaration.Scope.TurnId is null || !DeclarationValidation.Valid(declaration, DateTimeOffset.UtcNow))
                throw new InvalidDataException();
            if (validateOnly) {
                Console.WriteLine("Declaration is valid.");
                return 0;
            }
            using var document = JsonDocument.Parse(File.ReadAllText(args[5]));
            var root = document.RootElement;
            var machine = root.GetProperty("machineId").GetString();
            var reporter = root.GetProperty("reporterId").GetString();
            var directory = root.GetProperty("spoolDirectory").GetString();
            if (!DeclarationValidation.Identifier(machine) || !DeclarationValidation.Identifier(reporter) ||
                directory is null || !Path.IsPathFullyQualified(directory)) throw new InvalidDataException();
            var capacity = root.TryGetProperty("maximumSpoolBytes", out var bound) ? bound.GetInt64() : 64 * 1024 * 1024;
            if (capacity < LedgerJson.MaximumBatchBytes) throw new InvalidDataException();
            var itemId = LedgerScope.SessionItemId(machine!, args[1], sessionId.ToString("D"));
            var parentItemId = declaration.Kind == "question" && !declaration.Blocking ? itemId : null;
            if (parentItemId is not null) itemId = LedgerScope.InputItemId(parentItemId, declaration.ConditionRef);
            var eventId = Guid.ParseExact(LedgerScope.Id(itemId, declaration.Id)[..32], "N");
            var observation = new LedgerEvent {
                EventId = eventId, Epoch = eventId, Sequence = 1, ReporterId = reporter!, MachineId = machine!,
                ItemId = itemId, Provider = args[1], Source = "evidence", SourceVersion = "declaration-v2",
                ParentItemId = parentItemId, ItemKind = parentItemId is null ? "session" : "input",
                ObservedAtUtc = declaration.RecordedAtUtc, Declaration = declaration
            };
            using var spool = new ReporterSpool(directory, capacity, ownsDelivery: false);
            if (!spool.TryAppend(observation)) {
                Console.Error.WriteLine("Declaration retained in its input file. Reporter outbox is full.");
                return 1;
            }
            Console.WriteLine("Declaration saved to the reporter outbox.");
            return 0;
        } catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException or
            ArgumentException or KeyNotFoundException or FormatException or UnauthorizedAccessException) {
            Console.Error.WriteLine("Declaration or local reporter configuration is invalid or unavailable.");
            return 2;
        }
    }
}

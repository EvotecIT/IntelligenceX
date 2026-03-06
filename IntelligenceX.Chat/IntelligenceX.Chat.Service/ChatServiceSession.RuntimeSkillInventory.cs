using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private async Task RefreshConnectedRuntimeSkillInventoryAsync(IntelligenceXClient client, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(client);

        if (_options.OpenAITransport != OpenAITransportKind.AppServer) {
            ResetConnectedRuntimeSkillInventory();
            return;
        }

        if (_connectedRuntimeSkillInventoryHydrated) {
            return;
        }

        try {
            var skillList = await client.RawClient.ListSkillsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            _connectedRuntimeSkillInventory = NormalizeSkillInventoryValues(
                (skillList.Groups ?? Array.Empty<IntelligenceX.OpenAI.AppServer.Models.SkillGroup>())
                .SelectMany(static group => group.Skills ?? Array.Empty<IntelligenceX.OpenAI.AppServer.Models.SkillInfo>())
                .Where(static skill => skill.Enabled)
                .Select(static skill => skill.Name),
                maxItems: 0);
            _connectedRuntimeSkillInventoryHydrated = true;
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            Trace.TraceWarning("App-server skill inventory discovery failed: {0}", ex.Message);
            _connectedRuntimeSkillInventory = Array.Empty<string>();
            _connectedRuntimeSkillInventoryHydrated = true;
        }
    }

    private void ResetConnectedRuntimeSkillInventory() {
        _connectedRuntimeSkillInventory = Array.Empty<string>();
        _connectedRuntimeSkillInventoryHydrated = false;
    }
}

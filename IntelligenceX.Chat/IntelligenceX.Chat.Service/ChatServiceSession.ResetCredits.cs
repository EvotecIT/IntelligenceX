using System.Linq;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI.Usage;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static NativeResetCreditsDto? MapNativeResetCredits(ChatGptResetCredits? value) => value is null ? null : new() {
        AvailableCount = value.AvailableCount,
        ApplicableAvailableCount = value.ApplicableAvailableCount,
        DetailsAvailable = value.DetailsAvailable,
        Credits = value.Credits.Select(static credit => new NativeResetCreditDto {
            Id = credit.Id, ResetType = credit.ResetType, Title = credit.Title, Status = credit.Status,
            IsSupportedByPlan = credit.IsSupportedByPlan, GrantedAt = credit.GrantedAt, ExpiresAt = credit.ExpiresAt,
            RedeemStartedAt = credit.RedeemStartedAt, RedeemedAt = credit.RedeemedAt
        }).ToArray(),
        HistoryAvailable = value.HistoryAvailable,
        History = value.History.Select(static item => new NativeResetCreditEventDto {
            Id = item.Id, Kind = item.Kind, OccurredAt = item.OccurredAt
        }).ToArray(),
        HistoryWindowStart = value.HistoryWindowStart, HistoryAsOf = value.HistoryAsOf, HistoryNextCursor = value.HistoryNextCursor
    };
}

using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a response to a user input prompt.
/// </summary>
/// <example>
/// <code>
/// var response = await client.SubmitUserInputAsync("prompt-id", new[] { "yes" });
/// Console.WriteLine(response.Answers.Count);
/// </code>
/// </example>
public sealed class UserInputResponse {
    public UserInputResponse(IReadOnlyList<string> answers, IReadOnlyDictionary<string, IReadOnlyList<string>>? answersById,
        JsonObject raw, JsonObject? additional) {
        Answers = answers;
        AnswersById = answersById;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Flat list of answers.</summary>
    public IReadOnlyList<string> Answers { get; }
    /// <summary>Answers grouped by id (if provided).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? AnswersById { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a user input response from JSON.</summary>
    public static UserInputResponse FromJson(JsonObject obj) {
        var answers = new List<string>();
        IReadOnlyDictionary<string, IReadOnlyList<string>>? answersById = null;

        if (obj.TryGetValue("answers", out var answerValue)) {
            var answerArray = answerValue?.AsArray();
            if (answerArray is not null) {
                foreach (var item in answerArray) {
                    var value = item.AsString();
                    if (!string.IsNullOrWhiteSpace(value)) {
                        answers.Add(value!);
                    }
                }
            } else {
                var answerObj = answerValue?.AsObject();
                if (answerObj is not null) {
                    var map = new Dictionary<string, IReadOnlyList<string>>();
                    foreach (var entry in answerObj) {
                        var itemObj = entry.Value?.AsObject();
                        if (itemObj is null) {
                            continue;
                        }
                        var values = new List<string>();
                        var itemArray = itemObj.GetArray("answers");
                        if (itemArray is not null) {
                            foreach (var answer in itemArray) {
                                var text = answer.AsString();
                                if (!string.IsNullOrWhiteSpace(text)) {
                                    values.Add(text!);
                                }
                            }
                        }
                        map[entry.Key] = values;
                    }
                    answersById = map;
                }
            }
        }

        var additional = obj.ExtractAdditional("answers");
        return new UserInputResponse(answers, answersById, obj, additional);
    }
}

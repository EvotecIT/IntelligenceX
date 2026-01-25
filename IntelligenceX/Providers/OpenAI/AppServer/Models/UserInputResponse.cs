using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class UserInputResponse {
    public UserInputResponse(IReadOnlyList<string> answers, IReadOnlyDictionary<string, IReadOnlyList<string>>? answersById,
        JsonObject raw, JsonObject? additional) {
        Answers = answers;
        AnswersById = answersById;
        Raw = raw;
        Additional = additional;
    }

    public IReadOnlyList<string> Answers { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? AnswersById { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static UserInputResponse FromJson(JsonObject obj) {
        var answers = new List<string>();
        IReadOnlyDictionary<string, IReadOnlyList<string>>? answersById = null;

        if (obj.TryGetValue("answers", out var answerValue)) {
            var answerArray = answerValue?.AsArray();
            if (answerArray is not null) {
                foreach (var item in answerArray) {
                    var value = item.AsString();
                    if (!string.IsNullOrWhiteSpace(value)) {
                        answers.Add(value);
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
                                    values.Add(text);
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

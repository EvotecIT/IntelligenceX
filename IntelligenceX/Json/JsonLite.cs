using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IntelligenceX.Json;

public static class JsonLite {
    public static JsonValue Parse(string json) {
        if (json is null) {
            throw new ArgumentNullException(nameof(json));
        }
        var parser = new Parser(json);
        var value = parser.ParseValue();
        parser.SkipWhitespace();
        if (!parser.End) {
            throw new FormatException("Unexpected characters after JSON value.");
        }
        return value;
    }

    public static string Serialize(JsonValue value) {
        var builder = new StringBuilder();
        AppendValue(builder, value);
        return builder.ToString();
    }

    public static string Serialize(object? value) {
        var builder = new StringBuilder();
        AppendObject(builder, value);
        return builder.ToString();
    }

    private static void AppendValue(StringBuilder builder, JsonValue value) {
        switch (value.Kind) {
            case JsonValueKind.Null:
                builder.Append("null");
                break;
            case JsonValueKind.Boolean:
                builder.Append(value.AsBoolean() ? "true" : "false");
                break;
            case JsonValueKind.Number:
                AppendNumber(builder, value.Value);
                break;
            case JsonValueKind.String:
                AppendString(builder, value.AsString() ?? string.Empty);
                break;
            case JsonValueKind.Object:
                AppendObject(builder, value.AsObject());
                break;
            case JsonValueKind.Array:
                AppendArray(builder, value.AsArray());
                break;
            default:
                builder.Append("null");
                break;
        }
    }

    private static void AppendObject(StringBuilder builder, object? value) {
        if (value is null) {
            builder.Append("null");
            return;
        }

        switch (value) {
            case JsonValue jsonValue:
                AppendValue(builder, jsonValue);
                return;
            case JsonObject jsonObject:
                AppendObject(builder, jsonObject);
                return;
            case JsonArray jsonArray:
                AppendArray(builder, jsonArray);
                return;
            case string str:
                AppendString(builder, str);
                return;
            case bool b:
                builder.Append(b ? "true" : "false");
                return;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                AppendNumber(builder, value);
                return;
            case IDictionary<string, object?> map:
                AppendDictionary(builder, map);
                return;
            case IEnumerable enumerable:
                AppendEnumerable(builder, enumerable);
                return;
        }

        AppendString(builder, value.ToString() ?? string.Empty);
    }

    private static void AppendObject(StringBuilder builder, JsonObject? value) {
        if (value is null) {
            builder.Append("null");
            return;
        }
        builder.Append('{');
        var first = true;
        foreach (var entry in value) {
            if (!first) {
                builder.Append(',');
            }
            first = false;
            AppendString(builder, entry.Key);
            builder.Append(':');
            AppendValue(builder, entry.Value);
        }
        builder.Append('}');
    }

    private static void AppendDictionary(StringBuilder builder, IDictionary<string, object?> map) {
        builder.Append('{');
        var first = true;
        foreach (var entry in map) {
            if (!first) {
                builder.Append(',');
            }
            first = false;
            AppendString(builder, entry.Key);
            builder.Append(':');
            AppendObject(builder, entry.Value);
        }
        builder.Append('}');
    }

    private static void AppendArray(StringBuilder builder, JsonArray? value) {
        if (value is null) {
            builder.Append("null");
            return;
        }
        builder.Append('[');
        var first = true;
        foreach (var entry in value) {
            if (!first) {
                builder.Append(',');
            }
            first = false;
            AppendValue(builder, entry);
        }
        builder.Append(']');
    }

    private static void AppendEnumerable(StringBuilder builder, IEnumerable enumerable) {
        builder.Append('[');
        var first = true;
        foreach (var item in enumerable) {
            if (!first) {
                builder.Append(',');
            }
            first = false;
            AppendObject(builder, item);
        }
        builder.Append(']');
    }

    private static void AppendNumber(StringBuilder builder, object? value) {
        if (value is null) {
            builder.Append("0");
            return;
        }

        switch (value) {
            case byte b:
                builder.Append(b.ToString(CultureInfo.InvariantCulture));
                break;
            case sbyte sb:
                builder.Append(sb.ToString(CultureInfo.InvariantCulture));
                break;
            case short s:
                builder.Append(s.ToString(CultureInfo.InvariantCulture));
                break;
            case ushort us:
                builder.Append(us.ToString(CultureInfo.InvariantCulture));
                break;
            case int i:
                builder.Append(i.ToString(CultureInfo.InvariantCulture));
                break;
            case uint ui:
                builder.Append(ui.ToString(CultureInfo.InvariantCulture));
                break;
            case long l:
                builder.Append(l.ToString(CultureInfo.InvariantCulture));
                break;
            case ulong ul:
                builder.Append(ul.ToString(CultureInfo.InvariantCulture));
                break;
            case float f:
                builder.Append(f.ToString("R", CultureInfo.InvariantCulture));
                break;
            case double d:
                builder.Append(d.ToString("R", CultureInfo.InvariantCulture));
                break;
            case decimal dec:
                builder.Append(dec.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static void AppendString(StringBuilder builder, string value) {
        builder.Append('"');
        foreach (var ch in value) {
            switch (ch) {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(ch)) {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    } else {
                        builder.Append(ch);
                    }
                    break;
            }
        }
        builder.Append('"');
    }

#if NETSTANDARD2_0 || NET472
    private sealed class Parser {
        private readonly string _json;
        private int _index;

        public Parser(string json) {
            _json = json ?? string.Empty;
            _index = 0;
        }

        public bool End => _index >= _json.Length;

        public void SkipWhitespace() {
            while (!End && char.IsWhiteSpace(_json[_index])) {
                _index++;
            }
        }

        public JsonValue ParseValue() {
            SkipWhitespace();
            if (End) {
                throw new FormatException("Unexpected end of JSON.");
            }

            var ch = _json[_index];
            switch (ch) {
                case '{':
                    return JsonValue.From(ParseObject());
                case '[':
                    return JsonValue.From(ParseArray());
                case '"':
                    return JsonValue.From(ParseString());
                case 't':
                    ConsumeLiteral("true");
                    return JsonValue.From(true);
                case 'f':
                    ConsumeLiteral("false");
                    return JsonValue.From(false);
                case 'n':
                    ConsumeLiteral("null");
                    return JsonValue.Null;
                default:
                    if (ch == '-' || char.IsDigit(ch)) {
                        return ParseNumber();
                    }
                    throw new FormatException($"Unexpected character '{ch}' at position {_index}.");
            }
        }

        private JsonObject ParseObject() {
            Expect('{');
            var obj = new JsonObject();
            SkipWhitespace();
            if (TryConsume('}')) {
                return obj;
            }
            while (true) {
                SkipWhitespace();
                var key = ParseString();
                SkipWhitespace();
                Expect(':');
                var value = ParseValue();
                obj.Add(key, value);
                SkipWhitespace();
                if (TryConsume('}')) {
                    return obj;
                }
                Expect(',');
            }
        }

        private JsonArray ParseArray() {
            Expect('[');
            var array = new JsonArray();
            SkipWhitespace();
            if (TryConsume(']')) {
                return array;
            }
            while (true) {
                var value = ParseValue();
                array.Add(value);
                SkipWhitespace();
                if (TryConsume(']')) {
                    return array;
                }
                Expect(',');
            }
        }

        private string ParseString() {
            Expect('"');
            var builder = new StringBuilder();
            while (true) {
                if (End) {
                    throw new FormatException("Unterminated string literal.");
                }
                var ch = _json[_index++];
                if (ch == '"') {
                    return builder.ToString();
                }
                if (ch == '\\') {
                    if (End) {
                        throw new FormatException("Unterminated escape sequence.");
                    }
                    var esc = _json[_index++];
                    switch (esc) {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            builder.Append(ParseUnicode());
                            break;
                        default:
                            throw new FormatException($"Invalid escape sequence '\\{esc}'.");
                    }
                } else {
                    builder.Append(ch);
                }
            }
        }

        private char ParseUnicode() {
            if (_index + 4 > _json.Length) {
                throw new FormatException("Incomplete unicode escape sequence.");
            }
            var hex = _json.Substring(_index, 4);
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) {
                throw new FormatException("Invalid unicode escape sequence.");
            }
            _index += 4;
            return (char)code;
        }

        private JsonValue ParseNumber() {
            var start = _index;
            if (_json[_index] == '-') {
                _index++;
            }
            while (!End && char.IsDigit(_json[_index])) {
                _index++;
            }
            if (!End && _json[_index] == '.') {
                _index++;
                while (!End && char.IsDigit(_json[_index])) {
                    _index++;
                }
            }
            if (!End && (_json[_index] == 'e' || _json[_index] == 'E')) {
                _index++;
                if (!End && (_json[_index] == '+' || _json[_index] == '-')) {
                    _index++;
                }
                while (!End && char.IsDigit(_json[_index])) {
                    _index++;
                }
            }

            var text = _json.Substring(start, _index - start);
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue)) {
                return JsonValue.From(longValue);
            }
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)) {
                return JsonValue.From(doubleValue);
            }
            throw new FormatException($"Invalid numeric literal '{text}'.");
        }

        private void ConsumeLiteral(string literal) {
            for (var i = 0; i < literal.Length; i++) {
                if (End || _json[_index] != literal[i]) {
                    throw new FormatException($"Expected literal '{literal}'.");
                }
                _index++;
            }
        }

        private void Expect(char expected) {
            if (End || _json[_index] != expected) {
                throw new FormatException($"Expected '{expected}'.");
            }
            _index++;
        }

        private bool TryConsume(char ch) {
            if (!End && _json[_index] == ch) {
                _index++;
                return true;
            }
            return false;
        }
    }
#else
    private ref struct Parser {
        private readonly ReadOnlySpan<char> _span;
        private int _index;

        public Parser(string json) {
            _span = json.AsSpan();
            _index = 0;
        }

        public bool End => _index >= _span.Length;

        public void SkipWhitespace() {
            while (!End && char.IsWhiteSpace(_span[_index])) {
                _index++;
            }
        }

        public JsonValue ParseValue() {
            SkipWhitespace();
            if (End) {
                throw new FormatException("Unexpected end of JSON.");
            }

            var ch = _span[_index];
            switch (ch) {
                case '{':
                    return JsonValue.From(ParseObject());
                case '[':
                    return JsonValue.From(ParseArray());
                case '"':
                    return JsonValue.From(ParseString());
                case 't':
                    ConsumeLiteral("true");
                    return JsonValue.From(true);
                case 'f':
                    ConsumeLiteral("false");
                    return JsonValue.From(false);
                case 'n':
                    ConsumeLiteral("null");
                    return JsonValue.Null;
                default:
                    if (ch == '-' || char.IsDigit(ch)) {
                        return ParseNumber();
                    }
                    throw new FormatException($"Unexpected character '{ch}' at position {_index}.");
            }
        }

        private JsonObject ParseObject() {
            Expect('{');
            var obj = new JsonObject();
            SkipWhitespace();
            if (TryConsume('}')) {
                return obj;
            }
            while (true) {
                SkipWhitespace();
                var key = ParseString();
                SkipWhitespace();
                Expect(':');
                var value = ParseValue();
                obj.Add(key, value);
                SkipWhitespace();
                if (TryConsume('}')) {
                    return obj;
                }
                Expect(',');
            }
        }

        private JsonArray ParseArray() {
            Expect('[');
            var array = new JsonArray();
            SkipWhitespace();
            if (TryConsume(']')) {
                return array;
            }
            while (true) {
                var value = ParseValue();
                array.Add(value);
                SkipWhitespace();
                if (TryConsume(']')) {
                    return array;
                }
                Expect(',');
            }
        }

        private string ParseString() {
            Expect('"');
            var builder = new StringBuilder();
            while (true) {
                if (End) {
                    throw new FormatException("Unterminated string literal.");
                }
                var ch = _span[_index++];
                if (ch == '"') {
                    return builder.ToString();
                }
                if (ch == '\\') {
                    if (End) {
                        throw new FormatException("Unterminated escape sequence.");
                    }
                    var esc = _span[_index++];
                    switch (esc) {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            builder.Append(ParseUnicode());
                            break;
                        default:
                            throw new FormatException($"Invalid escape sequence '\\{esc}'.");
                    }
                } else {
                    builder.Append(ch);
                }
            }
        }

        private char ParseUnicode() {
            if (_index + 4 > _span.Length) {
                throw new FormatException("Incomplete unicode escape sequence.");
            }
            var hex = _span.Slice(_index, 4);
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) {
                throw new FormatException("Invalid unicode escape sequence.");
            }
            _index += 4;
            return (char)code;
        }

        private JsonValue ParseNumber() {
            var start = _index;
            if (_span[_index] == '-') {
                _index++;
            }
            while (!End && char.IsDigit(_span[_index])) {
                _index++;
            }
            if (!End && _span[_index] == '.') {
                _index++;
                while (!End && char.IsDigit(_span[_index])) {
                    _index++;
                }
            }
            if (!End && (_span[_index] == 'e' || _span[_index] == 'E')) {
                _index++;
                if (!End && (_span[_index] == '+' || _span[_index] == '-')) {
                    _index++;
                }
                while (!End && char.IsDigit(_span[_index])) {
                    _index++;
                }
            }

            var slice = _span.Slice(start, _index - start);
            var text = slice.ToString();
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue)) {
                return JsonValue.From(longValue);
            }
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)) {
                return JsonValue.From(doubleValue);
            }
            throw new FormatException($"Invalid numeric literal '{text}'.");
        }

        private void ConsumeLiteral(string literal) {
            for (var i = 0; i < literal.Length; i++) {
                if (End || _span[_index] != literal[i]) {
                    throw new FormatException($"Expected literal '{literal}'.");
                }
                _index++;
            }
        }

        private void Expect(char expected) {
            if (End || _span[_index] != expected) {
                throw new FormatException($"Expected '{expected}'.");
            }
            _index++;
        }

        private bool TryConsume(char ch) {
            if (!End && _span[_index] == ch) {
                _index++;
                return true;
            }
            return false;
        }
    }
#endif
}

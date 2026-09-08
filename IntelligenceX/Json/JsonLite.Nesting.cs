namespace IntelligenceX.Json;

public static partial class JsonLite {
    // Run before either framework-specific recursive parser. Delimiters inside strings do not
    // contribute to depth; the parser remains responsible for all other JSON syntax checks.
    private static void ValidateNesting(string json) {
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        foreach (char value in json) {
            if (inString) {
                if (escaped) escaped = false;
                else if (value == '\\') escaped = true;
                else if (value == '"') inString = false;
                continue;
            }
            if (value == '"') inString = true;
            else if (value is '{' or '[') {
                if (++depth > 128) throw new System.FormatException("JSON exceeds the maximum nesting depth of 128.");
            } else if (value is '}' or ']') {
                if (--depth < 0) throw new System.FormatException("Unexpected closing JSON container.");
            }
        }
    }
}

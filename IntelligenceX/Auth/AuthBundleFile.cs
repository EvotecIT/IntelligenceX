using System.Collections.Generic;

namespace IntelligenceX.Auth;

internal sealed class AuthBundleFile {
    public AuthBundleFile(int version, Dictionary<string, AuthBundle> bundles) {
        Version = version;
        Bundles = bundles;
    }

    public int Version { get; }
    public Dictionary<string, AuthBundle> Bundles { get; }
}

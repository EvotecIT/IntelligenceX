using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntelligenceX.Ledger.Agents;

/// <summary>On the machine that hosts the Ledger service, gives another machine what its agent service needs: a host
/// principal for its agent service and a client principal for its sessions' ask tools, both scoped to that machine, and
/// the two client files (endpoint, token and certificate pin) to copy to it privately. Existing principals are reused.
/// Preview is the default. The service reads principals when it starts, so a new one needs a service restart, and the
/// machine must be in the live coordination policy for its heartbeats to be accepted.</summary>
internal static class AgentEnrollment {
    private const string Usage = "Usage: IntelligenceX.Ledger agent-enroll --service <service.json> --machine <id> --out <folder> [--local] [--apply]";
    private static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int Run(string[] args) {
        string? service = null, machine = null, output = null;
        bool local = false, apply = false;
        for (var i = 1; i < args.Length; i++) {
            switch (args[i]) {
                case "--service" when i + 1 < args.Length: service = args[++i]; break;
                case "--machine" when i + 1 < args.Length: machine = args[++i]; break;
                case "--out" when i + 1 < args.Length: output = args[++i]; break;
                case "--local": local = true; break;
                case "--apply": apply = true; break;
                default: Console.Error.WriteLine(Usage); return 2;
            }
        }
        if (service is null || machine is null || output is null) { Console.Error.WriteLine(Usage); return 2; }
        try {
            var result = Enroll(Path.GetFullPath(service), machine, Path.GetFullPath(output), local, apply);
            Console.WriteLine(JsonSerializer.Serialize(result, Indented));
            return 0;
        } catch (InvalidOperationException error) {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    internal sealed record Result(bool Applied, string Machine, string HostPrincipal, string SessionsPrincipal, bool ConfigurationChanged,
        string Endpoint, string[] Files, string? Next);

    internal static Result Enroll(string servicePath, string machine, string output, bool local, bool apply) {
        var service = JsonNode.Parse(File.ReadAllText(servicePath))!.AsObject();
        var configurationPath = service["coordinationConfigPath"]?.GetValue<string>()
            ?? throw new InvalidOperationException("The service has no coordinationConfigPath.");
        var configuration = JsonNode.Parse(File.ReadAllText(configurationPath))!.AsObject();
        var principals = configuration["principals"]!.AsArray();
        if (!Coordination.CoordinationValidation.Id(machine)) throw new InvalidOperationException("Invalid machine id " + machine + ".");
        var projects = principals.Select(p => p!.AsObject()).Where(p => p["role"]?.GetValue<string>() == "host")
            .SelectMany(p => p["projects"]!.AsArray().Select(x => x!.GetValue<string>())).Distinct(StringComparer.Ordinal).ToArray();
        var changed = false;
        JsonObject Principal(string role, string id) {
            var found = principals.Select(p => p!.AsObject()).FirstOrDefault(p => p["role"]?.GetValue<string>() == role &&
                p["machineId"]?.GetValue<string>() == machine && (role == "host" || p["id"]?.GetValue<string>() == id));
            if (found is not null) return found;
            changed = true;
            var created = new JsonObject {
                ["id"] = id, ["role"] = role, ["token"] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
                ["projects"] = new JsonArray(projects.Select(p => (JsonNode)p).ToArray()), ["machineId"] = machine
            };
            principals.Add(created);
            return created;
        }
        var host = Principal("host", machine + "-host");
        var sessions = Principal("client", machine + "-sessions");

        var port = service["port"]?.GetValue<int>() ?? 7348;
        string endpoint;
        string? pin = null;
        if (local) endpoint = "http://127.0.0.1:" + port + "/";
        else {
            var address = service["lanAddress"]?.GetValue<string>() ?? throw new InvalidOperationException("The service has no lanAddress for other machines.");
            endpoint = new UriBuilder(Uri.UriSchemeHttps, address, port, "/").Uri.ToString();
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(service["certificatePath"]!.GetValue<string>(),
                service["certificatePassword"]?.GetValue<string>(), X509KeyStorageFlags.EphemeralKeySet);
            pin = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        }
        JsonObject Client(JsonObject principal) {
            var client = new JsonObject { ["endpoint"] = endpoint, ["token"] = principal["token"]!.GetValue<string>() };
            if (pin is not null) client["certificateSha256"] = pin;
            return client;
        }
        var files = new[] { Path.Combine(output, "host-client.json"), Path.Combine(output, "sessions-client.json") };
        if (apply) {
            if (changed) {
                File.Copy(configurationPath, configurationPath + ".before-enroll-" + machine, overwrite: true);
                LocalFiles.WriteAtomic(configurationPath, configuration.ToJsonString(Indented));
            }
            Directory.CreateDirectory(output);
            LocalFiles.WriteAtomic(files[0], Client(host).ToJsonString(Indented));
            LocalFiles.WriteAtomic(files[1], Client(sessions).ToJsonString(Indented));
        }
        return new(apply, machine, host["id"]!.GetValue<string>(), sessions["id"]!.GetValue<string>(), changed, endpoint, files,
            changed ? "Restart the Ledger service so it loads the new principals, then copy both files to the machine's coordination folder." :
                "Copy both files to the machine's coordination folder.");
    }
}

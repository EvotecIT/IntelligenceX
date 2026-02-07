namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestDoctorHelp() {
        var (exitCode, output) = RunDoctorAndCaptureOutput(new[] { "--help" });
        AssertEqual(0, exitCode, "doctor help exit");
        AssertContainsText(output, "Usage: intelligencex doctor", "doctor help usage");
        AssertContainsText(output, "--workspace", "doctor help workspace");
    }

    private static void TestDoctorMissingAuthStoreFails() {
        var previousAuthPath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH");
        try {
            var temp = Path.Combine(Path.GetTempPath(), "ix-tests-doctor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "reviewer.json"), """
{
  "review": {
    "provider": "openai",
    "openaiTransport": "native",
    "model": "gpt-5.3-codex"
  }
}
""");

            var authPath = Path.Combine(temp, "auth.json");
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", authPath);

            var (exitCode, output) = RunDoctorAndCaptureOutput(new[] {
                "--workspace", temp,
                "--skip-github"
            });

            AssertEqual(1, exitCode, "doctor missing auth store exit");
            AssertContainsText(output, "Missing OpenAI auth store", "doctor missing auth store message");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", previousAuthPath);
        }
    }

    private static void TestDoctorMultipleBundlesWarns() {
        var previousAuthPath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH");
        try {
            var temp = Path.Combine(Path.GetTempPath(), "ix-tests-doctor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            Directory.CreateDirectory(Path.Combine(temp, ".intelligencex"));

            File.WriteAllText(Path.Combine(temp, ".intelligencex", "reviewer.json"), """
{
  "review": {
    "provider": "openai",
    "openaiTransport": "native",
    "model": "gpt-5.3-codex"
  }
}
""");

            var authPath = Path.Combine(temp, "auth.json");
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", authPath);
            File.WriteAllText(authPath, """
{
  "version": 1,
  "bundles": {
    "openai-codex:acc-a": {
      "provider": "openai-codex",
      "access_token": "t",
      "refresh_token": "r",
      "account_id": "acc-a",
      "expires_at": 4102444800000
    },
    "openai-codex:acc-b": {
      "provider": "openai-codex",
      "access_token": "t",
      "refresh_token": "r",
      "account_id": "acc-b",
      "expires_at": 4102444800000
    }
  }
}
""");

            var (exitCode, output) = RunDoctorAndCaptureOutput(new[] {
                "--workspace", temp,
                "--skip-github"
            });

            AssertEqual(0, exitCode, "doctor multiple bundles exit");
            AssertContainsText(output, "Multiple ChatGPT accounts found", "doctor multiple bundles warning");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", previousAuthPath);
        }
    }

    private static (int ExitCode, string Output) RunDoctorAndCaptureOutput(string[] args) {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try {
            var exitCode = IntelligenceX.Cli.Doctor.DoctorRunner.RunAsync(args).GetAwaiter().GetResult();
            outWriter.Flush();
            errWriter.Flush();
            return (exitCode, outWriter.ToString() + errWriter.ToString());
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
#endif
}


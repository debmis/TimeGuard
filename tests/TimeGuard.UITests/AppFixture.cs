using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FlaUI.Core;
using FlaUI.UIA3;
using TimeGuard.Services;

namespace TimeGuard.UITests;

/// <summary>
/// Base xUnit class fixture — launches TimeGuard.exe with a throw-away temp DB
/// so tests run in full isolation without touching the real %AppData%\TimeGuard DB.
///
/// Subclass <see cref="SeededAppFixture"/> for tests that need a pre-configured
/// app (password already set, first-run skipped).
/// </summary>
public class AppFixture : IDisposable
{
    /// <summary>
    /// The password used to seed the test DB and typed into password prompts.
    /// Set the <c>TIMEGUARD_TEST_PASSWORD</c> environment variable to use your
    /// own password. Defaults to "test123" for fresh / CI environments.
    /// </summary>
    public static string TestPassword =>
        Environment.GetEnvironmentVariable("TIMEGUARD_TEST_PASSWORD") ?? "test123";

    private readonly string _dbPath;
    protected string DbPath => _dbPath;
    private Application? _app;
    private UIA3Automation? _automation;

    public Application App => _app ?? throw new InvalidOperationException("App not launched.");
    public UIA3Automation Automation => _automation ?? throw new InvalidOperationException("App not launched.");

    public AppFixture()
    {
        // Unique temp DB so parallel test classes never collide
        _dbPath = Path.Combine(Path.GetTempPath(), $"timeguard_test_{Guid.NewGuid():N}.db");
        SeedDatabase();
        Launch();
    }

    // ── Override in subclasses to pre-populate the DB ────────────────────────

    protected virtual void SeedDatabase() { /* blank DB → first-run flow */ }

    // ── Helpers exposed to subclasses ────────────────────────────────────────

    protected void SaveConfigToDb(string hash, string salt)
    {
        var db = new DatabaseService($"Data Source={_dbPath};");
        var config = db.LoadConfig();
        config.PasswordHash = hash;
        config.PasswordSalt = salt;
        db.SaveConfig(config);
    }

    protected DatabaseService OpenDb() => new($"Data Source={_dbPath};");

    protected static (string hash, string salt) HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(32);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            saltBytes, 100_000,
            HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    // ── Launch / teardown ────────────────────────────────────────────────────

    private void Launch()
    {
        // Kill any stale TimeGuard process so the single-instance mutex is free
        foreach (var p in Process.GetProcessesByName("TimeGuard"))
        {
            try { p.Kill(); p.WaitForExit(2000); } catch { }
        }

        var exePath = ResolveExePath();
        var runtimeConfig = Path.Combine(Path.GetDirectoryName(exePath)!, "TimeGuard.runtimeconfig.json");
        if (!File.Exists(exePath) || !File.Exists(runtimeConfig))
            BuildApp(exePath);

        // Set env var on THIS process — child processes inherit it even with UseShellExecute=true
        Environment.SetEnvironmentVariable("TIMEGUARD_TEST_DB", _dbPath);

        var info = new ProcessStartInfo(exePath) { UseShellExecute = true };

        _automation = new UIA3Automation();
        _app = Application.Launch(info);

        // Wait for the app to fully init and register the global hotkey
        Thread.Sleep(2500);
    }

    private static string ResolveExePath()
    {
        // Walk up from test DLL location to find the repo root, then locate the app exe
        var testDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        // testDir is something like: tests/TimeGuard.UITests/bin/Debug/net8.0-windows/
        var repoRoot = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "src", "TimeGuard.App", "bin", "Debug",
            "net8.0-windows", "TimeGuard.exe");
    }

    private static void BuildApp(string exePath)
    {
        var repoRoot = Path.GetFullPath(
            Path.Combine(Path.GetDirectoryName(exePath)!, "..", "..", "..", ".."));
        var proc = Process.Start(new ProcessStartInfo("dotnet", $"build \"{repoRoot}\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        })!;
        proc.WaitForExit(60_000);
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"TimeGuard.exe not found after build: {exePath}");
    }

    public void Dispose()
    {
        try { _app?.Kill(); } catch { }
        _automation?.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}

/// <summary>
/// Fixture variant that pre-seeds the DB with a known password (<see cref="TestPassword"/>),
/// so the first-run window is skipped and settings are accessible.
/// </summary>
public class SeededAppFixture : AppFixture
{
    protected override void SeedDatabase()
    {
        var (hash, salt) = HashPassword(TestPassword);
        SaveConfigToDb(hash, salt);
    }
}

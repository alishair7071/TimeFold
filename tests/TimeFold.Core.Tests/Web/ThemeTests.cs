using System.Net;
using System.Text;
using TimeFold.Core.Logging;
using TimeFold.Core.Privacy;
using TimeFold.Core.Recording;
using TimeFold.Core.Settings;
using TimeFold.Core.Storage;
using TimeFold.Core.Storage.Migrations;
using TimeFold.Core.Watchers;
using TimeFold.Core.Watchers.Native;
using TimeFold.Web;
using Xunit;

namespace TimeFold.Core.Tests.Web;

/// <summary>
/// Tests for the light and dark switch.
///
/// The choices behind it: light is the default, dark is chosen with a button
/// and not taken from the Windows setting, the choice is remembered by TimeFold
/// itself (a browser would forget it, because the server picks a new port every
/// time it starts), and the server writes it into the page so there is no flash
/// of the wrong colours.
/// </summary>
public class ThemeTests : IDisposable
{
    private readonly string _databaseFile;
    private readonly Database _database;
    private readonly SettingsService _settings;
    private readonly WebServer _server;
    private readonly HttpClient _http = new HttpClient();

    private readonly string _root;
    private readonly string _key;

    public ThemeTests()
    {
        _databaseFile = Path.Combine(
            Path.GetTempPath(),
            "timefold-test-" + Guid.NewGuid().ToString("N") + ".db");

        IAppLogger logger = new NullLogger();

        _database = new Database(_databaseFile);
        _database.Prepare();
        new MigrationRunner(_database, logger).RunPendingMigrations();

        SqliteActivityStore store = new SqliteActivityStore(_database);
        _settings = new SettingsService(_database);
        SqliteBlockRuleStore blockRules = new SqliteBlockRuleStore(_database);

        BlockRuleProvider provider = new BlockRuleProvider(blockRules);
        provider.Reload();

        RecordingService recording = new RecordingService(
            new WindowWatcher(new ForegroundWindowReader(), new PresenceReader(), logger, "test"),
            new PrivacyGate(provider),
            store,
            _settings,
            logger);

        ApiHandler api = new ApiHandler(
            store, blockRules, provider, recording, _settings, _database, logger);

        _server = new WebServer(api, logger);

        Uri uri = new Uri(_server.Start());

        _root = "http://127.0.0.1:" + uri.Port;
        _key = uri.Query.Split("key=")[1];
    }

    public void Dispose()
    {
        _server.Stop();
        _http.Dispose();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            File.Delete(_databaseFile);
        }
        catch
        {
            // A leftover temp file is not worth failing a test over.
        }
    }

    private string GetText(string path)
    {
        return _http.GetStringAsync(_root + path).Result;
    }

    private HttpResponseMessage ChooseTheme(string theme, bool withKey)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _root + "/api/theme");

        request.Content = new StringContent(
            "{\"theme\":\"" + theme + "\"}", Encoding.UTF8, "application/json");

        if (withKey)
        {
            request.Headers.Add("X-TimeFold-Key", _key);
        }

        return _http.Send(request);
    }

    // ---- the setting -------------------------------------------------------

    [Fact]
    public void LightIsTheDefault()
    {
        Assert.Equal("light", _settings.GetTheme());
    }

    [Fact]
    public void DarkIsRememberedAcrossRestarts()
    {
        _settings.SetTheme("dark");

        // A brand new SettingsService on the same file is what a restart looks like.
        SettingsService afterRestart = new SettingsService(_database);

        Assert.Equal("dark", afterRestart.GetTheme());
    }

    [Theory]
    [InlineData("")]
    [InlineData("blue")]
    [InlineData("DARK")]
    [InlineData("<script>alert(1)</script>")]
    public void AnythingButDarkMeansLight(string stored)
    {
        // The value is written into the page the server sends, so it must only
        // ever be one of two known words.
        _settings.SetValue("ui.theme", stored);

        Assert.Equal("light", _settings.GetTheme());
    }

    // ---- the page ----------------------------------------------------------

    [Fact]
    public void ThePageArrivesLightByDefault()
    {
        string page = GetText("/");

        Assert.Contains("data-theme=\"light\"", page);
        Assert.DoesNotContain("__THEME__", page);
    }

    [Fact]
    public void ThePageArrivesDarkAfterYouChooseDark()
    {
        // Written in by the server, so the very first paint is right and nobody
        // who chose dark sees a flash of white.
        ChooseTheme("dark", true);

        string page = GetText("/");

        Assert.Contains("data-theme=\"dark\"", page);
        Assert.DoesNotContain("__THEME__", page);
    }

    [Fact]
    public void SwitchingBackToLightWorks()
    {
        ChooseTheme("dark", true);
        ChooseTheme("light", true);

        Assert.Contains("data-theme=\"light\"", GetText("/"));
    }

    [Fact]
    public void ANonsenseThemeCannotGetIntoThePage()
    {
        ChooseTheme("x\\\" onload=\\\"alert(1)", true);

        string page = GetText("/");

        Assert.Contains("data-theme=\"light\"", page);
        Assert.DoesNotContain("onload", page);
    }

    // ---- the key -----------------------------------------------------------

    [Fact]
    public void ChoosingATheme_NeedsTheKey()
    {
        // Changing a setting is a write. It must not work for a page that has
        // only guessed the port.
        HttpResponseMessage response = ChooseTheme("dark", false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("light", _settings.GetTheme());
    }

    // ---- the stylesheet ----------------------------------------------------

    [Fact]
    public void TheStylesheetDoesNotFollowTheWindowsSetting()
    {
        // The choice is the user's, made with the button. A rule that reacts to
        // the operating system would quietly override it.
        string css = GetText("/style.css");

        Assert.DoesNotContain("prefers-color-scheme", css);
        Assert.Contains("[data-theme=\"dark\"]", css);
    }

    [Fact]
    public void ThereIsAButtonToSwitch()
    {
        string page = GetText("/");

        Assert.Contains("id=\"themeButton\"", page);
    }

    private class NullLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Error(string message, Exception error) { }
    }
}

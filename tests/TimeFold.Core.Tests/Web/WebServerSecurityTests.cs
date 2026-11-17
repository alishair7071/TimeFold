using System.Net;
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
/// Tests for the web interface, and mostly for the door it opens.
///
/// Serving your history over a port is the most dangerous thing TimeFold does.
/// Before this existed, only the app could read your history. These tests check
/// the four things that keep that door shut, because a mistake in any of them
/// hands your entire history to anything that can make an HTTP request.
/// </summary>
public class WebServerSecurityTests : IDisposable
{
    private readonly string _databaseFile;
    private readonly Database _database;
    private readonly WebServer _server;
    private readonly HttpClient _http = new HttpClient();

    private readonly string _root;
    private readonly string _key;

    public WebServerSecurityTests()
    {
        _databaseFile = Path.Combine(
            Path.GetTempPath(),
            "timefold-test-" + Guid.NewGuid().ToString("N") + ".db");

        IAppLogger logger = new NullLogger();

        _database = new Database(_databaseFile);
        _database.Prepare();
        new MigrationRunner(_database, logger).RunPendingMigrations();

        SqliteActivityStore store = new SqliteActivityStore(_database);
        SettingsService settings = new SettingsService(_database);
        SqliteBlockRuleStore blockRules = new SqliteBlockRuleStore(_database);

        BlockRuleProvider provider = new BlockRuleProvider(blockRules);
        provider.Reload();

        RecordingService recording = new RecordingService(
            new WindowWatcher(new ForegroundWindowReader(), new PresenceReader(), logger, "test"),
            new PrivacyGate(provider),
            store,
            settings,
            logger);

        ApiHandler api = new ApiHandler(
            store, blockRules, provider, recording, settings, _database, logger);

        _server = new WebServer(api, logger);

        string address = _server.Start();

        Uri uri = new Uri(address);

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

    private HttpResponseMessage Get(string path, bool withKey)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, _root + path);

        if (withKey)
        {
            request.Headers.Add("X-TimeFold-Key", _key);
        }

        return _http.Send(request);
    }

    // ---- the key -----------------------------------------------------------

    [Theory]
    [InlineData("/api/state")]
    [InlineData("/api/day?date=2026-11-17")]
    [InlineData("/api/search?q=anything")]
    [InlineData("/api/rules")]
    [InlineData("/api/apps")]
    [InlineData("/api/export")]
    public void NoHistoryComesOutWithoutTheKey(string path)
    {
        // The test that matters most in this file. Any of these answering
        // without a key would mean a web page you happened to visit could read
        // your history by guessing the port.
        HttpResponseMessage response = Get(path, false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public void AWrongKeyIsRefused(string guess)
    {
        HttpResponseMessage response = Get("/api/state?key=" + guess, false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void TheRightKeyWorks()
    {
        HttpResponseMessage response = Get("/api/state", true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void TheKeyIsLongEnoughThatGuessingIsPointless()
    {
        // 32 random bytes written as hex: 64 characters, 256 bits.
        Assert.Equal(64, _key.Length);
    }

    [Fact]
    public void EveryStartGetsADifferentKeyAndPort()
    {
        // A key that survived a restart would eventually leak - into a browser
        // history, a screenshot, a shared link. A fresh one every time means an
        // old one is worth nothing.
        string firstAddress = _server.Address;

        _server.Stop();
        string secondAddress = _server.Start();

        Assert.NotEqual(firstAddress, secondAddress);
    }

    // ---- the page itself ---------------------------------------------------

    [Fact]
    public void ThePageLoadsWithoutAKey()
    {
        // The browser has to load something before it can send a key. The page
        // holds no history of its own - everything comes from /api/.
        HttpResponseMessage response = Get("/", false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- escaping the page folder -----------------------------------------

    [Theory]
    [InlineData("/../../../Windows/win.ini")]
    [InlineData("/../TimeFold.exe")]
    [InlineData("/wwwroot/../../secret.txt")]
    public void CannotReachFilesOutsideThePageFolder(string path)
    {
        // The oldest bug in web servers. A request must never be able to walk
        // up out of the folder it is meant to be reading from.
        HttpResponseMessage response = Get(path, false);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- what the replies say ---------------------------------------------

    [Fact]
    public void RepliesCarryTheHeadersThatKeepOtherPagesOut()
    {
        HttpResponseMessage response = Get("/api/state", true);

        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));

        // No cross-origin permission at all, so another site cannot read a
        // reply even if it manages to send a request.
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void TheHistoryIsNotCached()
    {
        // Your history should not sit in a browser cache on disk.
        HttpResponseMessage response = Get("/api/state", true);

        Assert.Contains("no-store", response.Headers.GetValues("Cache-Control"));
    }

    // ---- stopping ----------------------------------------------------------

    [Fact]
    public void StoppingClosesThePort()
    {
        Assert.True(_server.IsRunning);

        _server.Stop();

        Assert.False(_server.IsRunning);
    }

    private class NullLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Error(string message, Exception error) { }
    }
}

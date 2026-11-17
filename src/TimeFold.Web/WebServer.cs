using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using TimeFold.Core.Logging;

namespace TimeFold.Web;

/// <summary>
/// A very small web server, so the TimeFold interface can be a web page.
///
/// Built on HttpListener, which is part of .NET itself - no ASP.NET, no Kestrel,
/// no packages. This whole project adds nothing to the one NuGet package
/// TimeFold has always had.
///
/// ---------------------------------------------------------------------------
/// THE SECURITY RULES. Read these before changing anything in this file.
/// ---------------------------------------------------------------------------
///
/// Opening a port is the most dangerous thing TimeFold does. Before this, your
/// history could only be read by the app itself. A careless server would let
/// any program on this computer - or any web page you happen to visit - read
/// everything you have ever done.
///
/// Four rules keep that door shut:
///
///   1. 127.0.0.1 only. Never 0.0.0.0, never a machine name. Nothing on your
///      Wi-Fi, your office network or the internet can reach this. Only this
///      computer can.
///
///   2. A secret key, generated fresh every time the server starts. Every
///      request must carry it. A web page that guesses the port still gets
///      nothing, because it cannot guess 256 random bits.
///
///   3. A random port. Not a fixed one that something could look for.
///
///   4. It only starts when you ask to open the interface. If you never open
///      it, no port is ever opened.
///
/// On top of that, every response says Access-Control-Allow-Origin is absent
/// and sends X-Content-Type-Options and a strict Content-Security-Policy, so a
/// page from somewhere else cannot read what comes back even if it asks.
/// </summary>
public class WebServer
{
    private readonly IAppLogger _logger;
    private readonly ApiHandler _api;
    private readonly StaticFiles _files;

    private HttpListener _listener;
    private Thread _thread;
    private volatile bool _stopping;

    /// <summary>The address to open in a browser, including the secret key.</summary>
    public string Address { get; private set; }

    public bool IsRunning
    {
        get { return _listener != null && _listener.IsListening; }
    }

    private string _secretKey;
    private int _port;

    public WebServer(ApiHandler api, IAppLogger logger)
    {
        _api = api;
        _logger = logger;
        _files = new StaticFiles(logger, api.GetTheme);
    }

    /// <summary>
    /// Starts listening, if it is not already. Returns the address to open.
    /// </summary>
    public string Start()
    {
        if (IsRunning)
        {
            return Address;
        }

        _secretKey = CreateSecretKey();
        _port = FindAFreePort();

        _listener = new HttpListener();
        _listener.Prefixes.Add("http://127.0.0.1:" + _port + "/");
        _listener.Start();

        Address = "http://127.0.0.1:" + _port + "/?key=" + _secretKey;

        _stopping = false;
        _thread = new Thread(ListenForever);
        _thread.IsBackground = true;
        _thread.Start();

        // The key is deliberately NOT logged. A log file is a plain text file
        // that gets copied around and pasted into bug reports.
        _logger.Info("Web interface started on port " + _port + ".");

        return Address;
    }

    public void Stop()
    {
        if (_listener == null)
        {
            return;
        }

        _stopping = true;

        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
            // Shutting down must never throw.
        }

        _listener = null;
        _secretKey = null;
        Address = null;

        _logger.Info("Web interface stopped.");
    }

    // ---- the listening loop ------------------------------------------------

    private void ListenForever()
    {
        while (!_stopping)
        {
            HttpListenerContext context;

            try
            {
                // Waits here until a request arrives.
                context = _listener.GetContext();
            }
            catch
            {
                // Stop() closed the listener out from under us. That is the
                // normal way this loop ends.
                return;
            }

            // Each request is handled on a pool thread so a slow one cannot
            // hold up the next.
            ThreadPool.QueueUserWorkItem(delegate { HandleOneRequest(context); });
        }
    }

    private void HandleOneRequest(HttpListenerContext context)
    {
        try
        {
            AddSafetyHeaders(context.Response);

            if (!IsFromThisComputer(context.Request))
            {
                // Should be impossible, since we only bind to 127.0.0.1. Checked
                // anyway, because "impossible" is where the bad ones live.
                Reject(context, 403, "Only this computer may use this interface.");
                return;
            }

            string path = context.Request.Url.AbsolutePath;

            // The page itself is allowed without a key, because the browser has
            // to load something before it can send one. It contains no data -
            // every piece of history comes from /api/, which does need the key.
            bool needsKey = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);

            if (needsKey && !HasValidKey(context.Request))
            {
                Reject(context, 401, "Wrong or missing key.");
                return;
            }

            if (needsKey)
            {
                _api.Handle(context, path);
            }
            else
            {
                _files.Serve(context, path);
            }
        }
        catch (Exception error)
        {
            _logger.Error("A web request failed.", error);

            try
            {
                Reject(context, 500, "Something went wrong. See the TimeFold log.");
            }
            catch
            {
                // The connection is gone. Nothing more to do.
            }
        }
    }

    /// <summary>
    /// Checks the secret key.
    ///
    /// The comparison is done one character at a time over the whole string,
    /// never stopping early on a mismatch. Comparing normally would return
    /// faster for a wrong first character than a wrong last one, and that
    /// difference can be measured and used to work the key out one character at
    /// a time. CryptographicOperations.FixedTimeEquals always takes the same
    /// time whatever the answer is.
    /// </summary>
    private bool HasValidKey(HttpListenerRequest request)
    {
        string given = request.QueryString["key"];

        if (string.IsNullOrEmpty(given))
        {
            given = request.Headers["X-TimeFold-Key"];
        }

        if (string.IsNullOrEmpty(given) || string.IsNullOrEmpty(_secretKey))
        {
            return false;
        }

        byte[] givenBytes = System.Text.Encoding.UTF8.GetBytes(given);
        byte[] realBytes = System.Text.Encoding.UTF8.GetBytes(_secretKey);

        return CryptographicOperations.FixedTimeEquals(givenBytes, realBytes);
    }

    private static bool IsFromThisComputer(HttpListenerRequest request)
    {
        IPAddress address = request.RemoteEndPoint.Address;

        return IPAddress.IsLoopback(address);
    }

    private static void AddSafetyHeaders(HttpListenerResponse response)
    {
        // No cross-origin header at all, so a page from another site cannot
        // read a reply even if it manages to send a request.
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Cache-Control"] = "no-store";

        // Everything the page needs is served from here. No outside scripts,
        // no outside images, and it may not be put inside a frame.
        response.Headers["Content-Security-Policy"] =
            "default-src 'self'; img-src 'self' data:; frame-ancestors 'none'";
    }

    private static void Reject(HttpListenerContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain; charset=utf-8";

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(message);

        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }

    // ---- starting up -------------------------------------------------------

    /// <summary>
    /// 32 random bytes, as text. Guessing it is not a realistic thing to try.
    ///
    /// RandomNumberGenerator, not Random. Random is predictable: knowing a few
    /// of its numbers tells you the rest, which would make the key worthless.
    /// </summary>
    private static string CreateSecretKey()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Finds a port nobody is using.
    ///
    /// Asking the operating system for port 0 means "give me any free one".
    /// We take the number it picked, let it go, and use it. A fixed port would
    /// be something another program could look for, and would clash if two
    /// copies ever ran.
    /// </summary>
    private static int FindAFreePort()
    {
        TcpListener probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        int port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }
}

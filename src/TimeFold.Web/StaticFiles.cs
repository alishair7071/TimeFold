using System.Net;
using TimeFold.Core.Logging;

namespace TimeFold.Web;

/// <summary>
/// Sends the web page files - index.html, style.css, app.js.
///
/// They live in a wwwroot folder next to the program. Serving files from disk
/// rather than building them into the exe means you can open style.css in
/// Notepad, change a colour, reload the page and see it. That is worth more
/// than the small neatness of a single file.
/// </summary>
public class StaticFiles
{
    private readonly IAppLogger _logger;
    private readonly string _rootFolder;

    public StaticFiles(IAppLogger logger)
    {
        _logger = logger;

        // AppContext.BaseDirectory is the folder the program is running from,
        // which is where the build puts wwwroot.
        _rootFolder = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    }

    public void Serve(HttpListenerContext context, string path)
    {
        if (path == "/" || string.IsNullOrEmpty(path))
        {
            path = "/index.html";
        }

        string fullPath = ResolveInsideRoot(path);

        if (fullPath == null || !File.Exists(fullPath))
        {
            SendText(context, 404, "Not found.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(fullPath);

        context.Response.StatusCode = 200;
        context.Response.ContentType = GuessContentType(fullPath);
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }

    /// <summary>
    /// Turns a web path into a file path, and refuses anything that would
    /// escape the wwwroot folder.
    ///
    /// This is the oldest bug in web servers. A request for
    /// "/../../../../Windows/System32/config/SAM" must not return that file.
    /// Checking for ".." in the text is not enough - there are many ways to
    /// write it. So instead we work out the real, complete path and then check
    /// that it still starts inside wwwroot. There is no way to spell your way
    /// out of that.
    /// </summary>
    private string ResolveInsideRoot(string path)
    {
        try
        {
            string relative = Uri.UnescapeDataString(path.TrimStart('/'));

            string combined = Path.Combine(_rootFolder, relative);
            string fullPath = Path.GetFullPath(combined);
            string fullRoot = Path.GetFullPath(_rootFolder);

            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info("Refused a web request that pointed outside the page folder.");
                return null;
            }

            return fullPath;
        }
        catch
        {
            return null;
        }
    }

    private static string GuessContentType(string fullPath)
    {
        string extension = Path.GetExtension(fullPath).ToLowerInvariant();

        switch (extension)
        {
            case ".html":
                return "text/html; charset=utf-8";

            case ".css":
                return "text/css; charset=utf-8";

            case ".js":
                return "text/javascript; charset=utf-8";

            case ".json":
                return "application/json; charset=utf-8";

            case ".svg":
                return "image/svg+xml";

            case ".png":
                return "image/png";

            case ".ico":
                return "image/x-icon";

            case ".woff2":
                return "font/woff2";

            default:
                // Deliberately not guessing. An unknown type served as
                // something specific is how a file gets treated as code.
                return "application/octet-stream";
        }
    }

    private static void SendText(HttpListenerContext context, int statusCode, string message)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(message);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }
}

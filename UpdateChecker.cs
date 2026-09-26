using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace UnboundKeys;

// The one place UnboundKeys ever goes online, and only when Fizzil clicks
// Check for updates and confirms: asks GitHub which release of the repo is
// newest, and on request downloads its win-x64 zip, unpacks it into a
// folder beside the running one, and starts it. Nothing here runs on its
// own — the README's "nothing is sent anywhere" stays true until that
// click. Everything is plain .NET 8 (HttpClient, System.Text.Json,
// ZipFile): no new dependencies.
public static class UpdateChecker
{
    public const string ReleasesPage = "https://github.com/Fizzil/UnboundKeys/releases";
    private const string LatestReleaseApi = "https://api.github.com/repos/Fizzil/UnboundKeys/releases/latest";

    public sealed record UpdateInfo(Version Current, Version Latest, string PageUrl, string ZipUrl, long ZipBytes)
    {
        public bool IsNewer => Latest > Current;
        public bool HasZip => ZipUrl.Length > 0;
    }

    // Major.Minor.Build, the same shape as the release tags (v4.0.1).
    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return Trim(v);
        }
    }

    private static Version Trim(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static HttpClient MakeClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        // GitHub refuses requests with no User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("UnboundKeys", Current.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<UpdateInfo> CheckAsync(CancellationToken ct)
    {
        using var client = MakeClient();
        using var stream = await client.GetStreamAsync(LatestReleaseApi, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
            throw new InvalidOperationException($"GitHub's newest release is tagged \"{tag}\", which isn't a version number.");

        string page = root.TryGetProperty("html_url", out var url) ? url.GetString() ?? ReleasesPage : ReleasesPage;
        string zipUrl = "";
        long zipBytes = 0;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (name.StartsWith("UnboundKeys-v", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase))
                {
                    zipUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                    zipBytes = asset.GetProperty("size").GetInt64();
                }
            }
        }
        return new UpdateInfo(Current, Trim(latest), page, zipUrl, zipBytes);
    }

    // Where a version gets unpacked: a sibling of the running folder, named
    // the way the release zips are (UnboundKeys-v4.1.0), so File Explorer
    // reads it at a glance and the old folder is left untouched.
    public static string InstallDirFor(Version version)
    {
        string here = RunningDir();
        string parent = Path.GetDirectoryName(here) ?? here;
        return Path.Combine(parent, $"UnboundKeys-v{version}");
    }

    private static string RunningDir() =>
        Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);

    // Streams the zip into the temp folder, reporting 0..1. A cancel or a
    // failure leaves no half-file behind.
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct)
    {
        string zipPath = Path.Combine(Path.GetTempPath(), $"UnboundKeys-v{info.Latest}-win-x64.zip");
        try
        {
            using var client = MakeClient();
            using var response = await client.GetAsync(info.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? info.ZipBytes;

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var file = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 16];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0)
                    progress.Report((double)done / total);
            }
            return zipPath;
        }
        catch
        {
            TryDelete(zipPath);
            throw;
        }
    }

    // Unpacks the zip into InstallDirFor(version) and returns the new exe's
    // path. Entry names are checked to stay inside that folder.
    public static async Task<string> InstallAsync(string zipPath, Version version, IProgress<double> progress, CancellationToken ct)
    {
        string dir = Path.GetFullPath(InstallDirFor(version));
        if (string.Equals(dir, RunningDir(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That version is the one running from this folder.");

        await Task.Run(() =>
        {
            Directory.CreateDirectory(dir);
            using var archive = ZipFile.OpenRead(zipPath);
            int count = Math.Max(archive.Entries.Count, 1);
            int i = 0;
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                // Compress-Archive writes backslash-separated names; Windows
                // paths take those as separators.
                string target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                if (!target.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The zip tried to write outside its own folder.");
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(target);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
                progress.Report((double)++i / count);
            }
        }, ct);

        TryDelete(zipPath);
        return Directory.GetFiles(dir, "UnboundKeys-v*.exe").FirstOrDefault()
            ?? throw new InvalidOperationException("The unpacked folder has no UnboundKeys exe in it.");
    }

    // This process is already elevated, so the new one starts elevated too
    // without another prompt. The caller then quits this one.
    public static void Launch(string exePath) =>
        Process.Start(new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath),
        });

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* a leftover temp file is not worth failing over */ }
    }
}

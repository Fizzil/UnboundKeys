using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace UnboundKeys.Tests;

// The updater's fingerprint check (see UpdateChecker.DownloadAsync), against
// a tiny web server on this machine: nothing here goes online. A download
// whose SHA-256 matches the listed one is kept; one that does not is thrown
// away before anything is unpacked.
internal static class UpdaterTests
{
    private static bool _ok;

    public static bool Run()
    {
        Console.WriteLine("--- updater: the download is checked against its fingerprint ---");
        _ok = true;
        try
        {
            Fingerprints();
            Downloads().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Check(false, $"threw {ex.GetType().Name}: {ex.Message}");
        }
        return _ok;
    }

    private static void Fingerprints()
    {
        // SHA-256 of the three bytes "abc", the standard's own worked example.
        const string abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        string path = Path.Combine(Path.GetTempPath(), "UnboundKeys.Tests-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "abc");
        Check(UpdateChecker.Sha256OfFile(path) == abc, "a file's fingerprint comes out as the standard says it should");
        File.Delete(path);

        Check(ListedDigest($$"""{"digest":"sha256:{{abc.ToUpperInvariant()}}"}""") == abc, "GitHub's \"sha256:…\" is read, in lowercase");
        Check(ListedDigest("""{"name":"x.zip"}""") == "", "a release that lists no fingerprint reads as none");
        Check(ListedDigest("""{"digest":null}""") == "", "so does an empty one");
        Check(ListedDigest("""{"digest":"md5:abcdef"}""") == "", "and one of a kind this app does not check");
    }

    private static async Task Downloads()
    {
        byte[] body = Encoding.UTF8.GetBytes("not really a zip, but the check does not care what the bytes are");
        string good = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant();
        string wrong = new string('0', 64);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        // Answers every request with the same bytes.
        var serving = Task.Run(async () =>
        {
            for (int i = 0; i < 3; i++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                // Read the request up to its blank line before answering.
                var request = new StringBuilder();
                var buffer = new byte[1024];
                while (!request.ToString().Contains("\r\n\r\n"))
                {
                    int read = await stream.ReadAsync(buffer);
                    if (read == 0)
                        break;
                    request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
                byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/zip\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
            }
        });

        var version = new Version(0, 0, 1);
        string url = $"http://127.0.0.1:{port}/UnboundKeys-v0.0.1-win-x64.zip";
        var quiet = new Progress<double>(_ => { });
        UpdateChecker.UpdateInfo Info(string sha) => new(UpdateChecker.Current, version, "", url, body.Length, sha);

        string zip = await UpdateChecker.DownloadAsync(Info(good), quiet, CancellationToken.None);
        Check(File.Exists(zip) && File.ReadAllBytes(zip).SequenceEqual(body), "a download that matches its fingerprint is kept");
        File.Delete(zip);

        bool refused = false;
        try
        {
            await UpdateChecker.DownloadAsync(Info(wrong), quiet, CancellationToken.None);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }
        Check(refused, "a download that does not match is refused");
        Check(!File.Exists(zip), "and the file is thrown away, so nothing can unpack it");

        zip = await UpdateChecker.DownloadAsync(Info(""), quiet, CancellationToken.None);
        Check(File.Exists(zip), "a release that lists no fingerprint still downloads (older releases)");
        File.Delete(zip);

        await serving;
        listener.Stop();
    }

    private static string ListedDigest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return UpdateChecker.Sha256Of(doc.RootElement);
    }

    private static void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        _ok &= condition;
    }
}

using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AMWindowsAssistant;

internal sealed record AppUpdate(
    Version Version,
    string TagName,
    Uri DownloadUri,
    string? Sha256,
    string ReleaseNotes);

internal static class UpdateService
{
    private const string ExecutableName = "AMWindowsAssistant.exe";
    private static readonly HttpClient Client = CreateClient();

    public static bool IsValidRepository(string repository) =>
        Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");

    public static async Task<AppUpdate?> CheckAsync(string repository, Version currentVersion)
    {
        if (!IsValidRepository(repository))
        {
            throw new ArgumentException("Enter a GitHub repository as owner/repository.", nameof(repository));
        }

        var parts = repository.Split('/');
        var apiUrl = $"https://api.github.com/repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/releases/latest";
        using var response = await Client.GetAsync(apiUrl, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("The GitHub repository or its latest release could not be found.");
        }
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden &&
            response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
            remaining.Contains("0"))
        {
            throw new InvalidOperationException("GitHub’s unauthenticated API limit was reached. Try again later.");
        }

        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(responseStream);
        var release = document.RootElement;
        var tag = release.GetProperty("tag_name").GetString()
            ?? throw new InvalidOperationException("The GitHub release has no version tag.");
        var versionMatch = Regex.Match(tag, @"^[vV]?(?<version>\d+\.\d+(?:\.\d+){0,2})");
        if (!versionMatch.Success || !Version.TryParse(versionMatch.Groups["version"].Value, out var version))
        {
            throw new InvalidOperationException($"The latest release tag '{tag}' is not a supported version.");
        }

        var asset = release.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.GetProperty("name").GetString(), ExecutableName,
                    StringComparison.OrdinalIgnoreCase));
        if (asset.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException(
                $"The latest GitHub release does not include {ExecutableName}.");
        }

        var downloadUrl = asset.GetProperty("browser_download_url").GetString();
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("The GitHub release contains an invalid download URL.");
        }

        string? sha256 = null;
        if (asset.TryGetProperty("digest", out var digestElement))
        {
            var digest = digestElement.GetString();
            if (!string.IsNullOrWhiteSpace(digest))
            {
                if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
                    !Regex.IsMatch(digest.Substring("sha256:".Length), @"\A[0-9a-fA-F]{64}\z"))
                {
                    throw new InvalidOperationException("The release asset has an invalid SHA-256 digest.");
                }

                sha256 = digest.Substring("sha256:".Length);
            }
        }
        if (sha256 is null)
        {
            throw new InvalidOperationException(
                "The GitHub release does not provide a SHA-256 digest for its executable, so the update cannot be verified.");
        }

        var notes = release.TryGetProperty("body", out var bodyElement)
            ? bodyElement.GetString() ?? string.Empty
            : string.Empty;

        return version > currentVersion
            ? new AppUpdate(version, tag, downloadUri, sha256, notes)
            : null;
    }

    public static async Task DownloadAsync(AppUpdate update, string destinationPath)
    {
        using var response = await Client.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var destination = new FileStream(
            destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        var isExecutable = false;
        long bytesWritten = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer);
            if (bytesRead == 0)
            {
                break;
            }

            if (bytesWritten == 0)
            {
                isExecutable = bytesRead >= 2 && buffer[0] == (byte)'M' && buffer[1] == (byte)'Z';
            }

            hash.AppendData(buffer, 0, bytesRead);
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead));
            bytesWritten += bytesRead;
        }

        await destination.FlushAsync();
        if (!isExecutable)
        {
            throw new InvalidOperationException("The downloaded release file is not a Windows executable.");
        }

        if (update.Sha256 is not null)
        {
            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actualHash, update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The downloaded file did not match GitHub’s SHA-256 digest.");
            }
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("AMWindowsAssistant", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}

using System.Globalization;
using System.Text.Json;

namespace ClipboardWatcher.Core;

public sealed class GitHubReleaseInfo
{
    public Version Version { get; }
    public string HtmlUrl { get; }
    public string TagName { get; }

    private GitHubReleaseInfo(Version version, string htmlUrl, string tagName)
    {
        Version = version;
        HtmlUrl = htmlUrl;
        TagName = tagName;
    }

    public static bool TryParseLatestReleasePayload(string payload, out GitHubReleaseInfo? releaseInfo)
    {
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;

        if (!root.TryGetProperty("tag_name", out var tagElement))
        {
            releaseInfo = null;
            return false;
        }

        if (!root.TryGetProperty("html_url", out var htmlUrlElement))
        {
            releaseInfo = null;
            return false;
        }

        var tagName = tagElement.GetString();
        var htmlUrl = htmlUrlElement.GetString();

        if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(htmlUrl))
        {
            releaseInfo = null;
            return false;
        }

        if (!TryParseVersionFromTag(tagName, out var version))
        {
            releaseInfo = null;
            return false;
        }

        releaseInfo = new GitHubReleaseInfo(version, htmlUrl, tagName);
        return true;
    }

    public static bool TryParseVersionFromTag(string tagName, out Version version)
    {
        var normalized = tagName.Trim();
        if (normalized.StartsWith("v", true, CultureInfo.InvariantCulture))
        {
            normalized = normalized[1..];
        }

        return Version.TryParse(normalized, out version!);
    }
}

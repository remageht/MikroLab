using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MikroLab.Presentation
{
    /// <summary>Information about an available update.</summary>
    public sealed class UpdateInfo
    {
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;

        public bool UpdateAvailable => !string.IsNullOrEmpty(LatestVersion)
            && string.CompareOrdinal(LatestVersion, CurrentVersion) > 0;

        /// <summary>Version of the running build.</summary>
        public static string CurrentBuildVersion()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrEmpty(informational))
                {
                    int plus = informational.IndexOf('+');
                    return plus > 0 ? informational.Substring(0, plus) : informational;
                }

                Version version = assembly.GetName().Version;
                return version != null
                    ? version.ToString(version.Revision >= 0 ? 3 : 2)
                    : "0.0.0";
            }
            catch
            {
                return "0.0.0";
            }
        }
    }

    /// <summary>
    /// Checks GitHub for a newer release of MikroLab. Failures are silent: the
    /// studio must start even without a network connection.
    /// </summary>
    public static class UpdateChecker
    {
        public const string Repository = "remageht/MikroLab";

        private const string LatestReleaseApi =
            "https://api.github.com/repos/remageht/MikroLab/releases/latest";

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MikroLab");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        public static async Task<UpdateInfo> CheckAsync(CancellationToken token = default)
        {
            UpdateInfo info = new UpdateInfo { CurrentVersion = UpdateInfo.CurrentBuildVersion() };

            try
            {
                string json = await Client.GetStringAsync(LatestReleaseApi, token).ConfigureAwait(false);
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    if (root.TryGetProperty("tag_name", out JsonElement tag))
                    {
                        string value = tag.GetString() ?? string.Empty;
                        info.LatestVersion = value.TrimStart('v', 'V');
                    }

                    if (root.TryGetProperty("html_url", out JsonElement url))
                        info.Url = url.GetString() ?? string.Empty;

                    if (root.TryGetProperty("body", out JsonElement body))
                        info.Notes = body.GetString() ?? string.Empty;
                }
            }
            catch
            {
                // нет сети или лимит запросов — молча продолжаем работу
            }

            return info;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Federation.Services
{
    /// <summary>
    /// Upgrade from the svoeth fork's 0.0.196, which had its own usage history
    /// and "Popular on (friend)" collections. Its monthly byte totals become
    /// the data-limit totals here, and its collections are removed (popular
    /// titles are a Home row now). Both are no-ops on any other install.
    /// </summary>
    internal static class ForkUpgradeCleanup
    {
        /// <summary>Provider id the fork stamped on its popular collections.</summary>
        internal const string PopularCollectionProviderId = "FederationPopular";

        /// <summary>
        /// Reads the fork's <c>federation-friend-usage.json</c> (sessions plus
        /// <c>MonthlyBytes</c> keyed "friend id|yyyy-MM") as per-friend months
        /// keyed by federation id, the shape <see cref="FriendDataLimits"/>
        /// stores. Null when the text is not the fork's format.
        /// </summary>
        internal static Dictionary<string, Dictionary<string, long>>? ReadForkUsage(string json, Func<string, string?> federationIdOfFriend)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("MonthlyBytes", out var monthly)
                || monthly.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var result = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);
            foreach (var entry in monthly.EnumerateObject())
            {
                var split = entry.Name.LastIndexOf('|');
                if (split <= 0 || !entry.Value.TryGetInt64(out var bytes) || bytes <= 0)
                {
                    continue;
                }

                var federationId = federationIdOfFriend(entry.Name[..split]);
                if (string.IsNullOrEmpty(federationId))
                {
                    continue;
                }

                var months = result.TryGetValue(federationId, out var existing) ? existing : result[federationId] = new Dictionary<string, long>(StringComparer.Ordinal);
                var month = entry.Name[(split + 1)..];
                months[month] = months.GetValueOrDefault(month) + bytes;
            }

            return result;
        }

        /// <summary>Removes the fork's popular collections and their list.</summary>
        internal static void RemovePopularCollections(ILibraryManager libraryManager, string? dataFolder, ILogger logger)
        {
            try
            {
                var leftovers = libraryManager.GetItemList(new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { Jellyfin.Data.Enums.BaseItemKind.BoxSet },
                        Recursive = true
                    })
                    .OfType<BoxSet>()
                    .Where(b => b.ProviderIds != null && b.ProviderIds.ContainsKey(PopularCollectionProviderId))
                    .ToList();
                foreach (var boxSet in leftovers)
                {
                    libraryManager.DeleteItem(boxSet, new DeleteOptions { DeleteFileLocation = true }, true);
                    logger.LogInformation("[Federation] Removed the old collection \"{Name}\" (popular titles are a Home row now)", boxSet.Name);
                }

                var state = string.IsNullOrEmpty(dataFolder) ? null : Path.Combine(dataFolder, "federation-popular-collections.json");
                if (state != null && File.Exists(state))
                {
                    File.Delete(state);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Federation] Could not remove old popular collections");
            }
        }
    }
}

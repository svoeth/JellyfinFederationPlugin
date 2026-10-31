using System;
using System.IO;
using Jellyfin.Plugin.Federation.Configuration;
using Jellyfin.Plugin.Federation.Services;
using Xunit;

namespace Jellyfin.Plugin.Federation.Tests;

[Collection("PluginInstance")]
public sealed class ForkUpgradeCleanupTests : IDisposable
{
    private const string ForkFile = "{\"Sessions\":[{\"FriendId\":\"srv-a\",\"Bytes\":5}],\"LocalPlays\":[],"
        + "\"MonthlyBytes\":{\"srv-a|2026-10\":3000000000,\"srv-a|2026-09\":7,\"srv-gone|2026-10\":9,\"broken\":1}}";

    private readonly RealPluginInstance _plugin = new();

    public void Dispose() => _plugin.Dispose();

    [Fact]
    public void ForkUsage_BecomesMonthsPerFederationId()
    {
        var read = ForkUpgradeCleanup.ReadForkUsage(ForkFile, id => id == "srv-a" ? "fed-a" : null)!;

        Assert.Single(read);
        Assert.Equal(3000000000L, read["fed-a"]["2026-10"]);
        Assert.Equal(7L, read["fed-a"]["2026-09"]);
        Assert.Null(ForkUpgradeCleanup.ReadForkUsage("{\"fed-a\":{\"2026-10\":5}}", _ => "x"));
    }

    [Fact]
    public void DataLimits_ReadTheForkFile_SoThisMonthsUsageStillCounts()
    {
        _plugin.Configuration.RemoteServers.Add(new RemoteServer { Id = "srv-a", FederationId = "fed-a" });
        var path = Path.Combine(Path.GetTempPath(), "fed-usage-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, ForkFile.Replace("2026-10", DateTime.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)));
        try
        {
            using var limits = new FriendDataLimits(null, () => path);

            Assert.Equal(3000000000L, limits.UsedThisMonth("fed-a"));
            Assert.True(limits.IsOverCap("fed-a", FriendDataLimits.CapBytes(2)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

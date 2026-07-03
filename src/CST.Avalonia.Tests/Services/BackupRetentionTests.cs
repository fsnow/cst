using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// STATE-7: a backup is written before every state save, so the old "keep newest 10" policy churned
// the whole set out within ~10 minutes of use — no recovery point from earlier today or a previous
// session. These tests pin the tiered policy (recent N + one-per-day) on the pure decision helper.
public class BackupRetentionTests
{
    private static (string path, DateTime when) B(int id, DateTime when) => ($"application-state-{id}.json", when);

    [Fact]
    public void PreviousSessionBackupSurvivesTodaysChurn()
    {
        var noon = new DateTime(2026, 7, 3, 12, 0, 0);
        var backups = new List<(string path, DateTime when)>();
        // 40 backups today, one minute apart, newest first (simulates a busy session).
        for (int i = 0; i < 40; i++)
            backups.Add(B(i, noon.AddMinutes(-i)));
        var yesterday = B(100, noon.AddDays(-1));
        var lastWeek = B(101, noon.AddDays(-6));
        backups.Add(yesterday);
        backups.Add(lastWeek);

        var toDelete = ApplicationStateService.SelectBackupsToDelete(backups);

        // The key STATE-7 property: older-session recovery points are NOT churned out.
        Assert.DoesNotContain(yesterday.Item1, toDelete);
        Assert.DoesNotContain(lastWeek.Item1, toDelete);
        // Only surplus within-today backups are deleted (kept: 8 newest today; days are all within 14).
        Assert.All(toDelete, p => Assert.Contains(p, backups.Where(b => b.when.Date == noon.Date).Select(b => b.path)));
    }

    [Fact]
    public void KeepsTheEightNewest()
    {
        var noon = new DateTime(2026, 7, 3, 12, 0, 0);
        var backups = Enumerable.Range(0, 20).Select(i => B(i, noon.AddMinutes(-i))).ToList();

        var toDelete = ApplicationStateService.SelectBackupsToDelete(backups);

        // 8 newest kept; the newest is also the day-representative (same set), so 12 of 20 deleted.
        for (int i = 0; i < 8; i++)
            Assert.DoesNotContain(backups[i].path, toDelete);
        Assert.Equal(12, toDelete.Count);
    }

    [Fact]
    public void KeepsOnePerDayAcrossManyDays()
    {
        var noon = new DateTime(2026, 7, 3, 12, 0, 0);
        // One backup per day for 20 consecutive days, newest first.
        var backups = Enumerable.Range(0, 20).Select(i => B(i, noon.AddDays(-i))).ToList();

        var toDelete = ApplicationStateService.SelectBackupsToDelete(backups);

        // 14 daily reps kept; days 15..20 (index 14..19) dropped, except the recent-8 overlap keeps 0..7.
        // So kept = union(newest 8, newest-of-day for 14 days) = first 14; delete = indices 14..19 = 6.
        Assert.Equal(6, toDelete.Count);
        for (int i = 0; i < 14; i++)
            Assert.DoesNotContain(backups[i].path, toDelete);
    }

    [Fact]
    public void NothingToDeleteWhenFew()
    {
        var noon = new DateTime(2026, 7, 3, 12, 0, 0);
        var backups = Enumerable.Range(0, 5).Select(i => B(i, noon.AddMinutes(-i))).ToList();
        Assert.Empty(ApplicationStateService.SelectBackupsToDelete(backups));
    }
}

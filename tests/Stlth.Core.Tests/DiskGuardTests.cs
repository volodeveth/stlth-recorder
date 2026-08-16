using Stlth.Core.Storage;

namespace Stlth.Core.Tests;

public class DiskGuardTests
{
    [Fact]
    public void Thresholds_map_to_levels()
    {
        Assert.Equal(DiskLevel.Critical, DiskGuard.Level(100L * 1024 * 1024));
        Assert.Equal(DiskLevel.Low, DiskGuard.Level(500L * 1024 * 1024));
        Assert.Equal(DiskLevel.Ok, DiskGuard.Level(50L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void Free_space_is_reported_for_a_path_that_does_not_exist_yet()
    {
        // Дефект №3 an earlier build: на свіжому встановленні теки сесій ще немає, API
        // відповідають лише для наявних шляхів — і застосунок оголосив диск повним
        // на машині з 419 ГБ вільних, заблокувавши найперший запис кожного нового
        // користувача. Том той самий незалежно від того, чи існує листок.
        var missing = Path.Combine(Path.GetTempPath(), "stlth-" + Guid.NewGuid(), "a", "b", "c");

        Assert.True(DiskGuard.FreeBytes(missing) > 0);
    }

    [Fact]
    public void A_zero_is_treated_as_no_space_not_as_an_answer()
    {
        // Дефект №10: та сама вада вдруге, з іншої причини — нуль від системного
        // API прийняли як вимірювання. Рівень для нуля справді Critical, але
        // FreeBytes для реального тому нулем бути не може.
        Assert.Equal(DiskLevel.Critical, DiskGuard.Level(0));
        Assert.True(DiskGuard.FreeBytes(Path.GetTempPath()) > 0);
    }

    [Fact]
    public void Unreachable_volume_returns_zero_rather_than_throwing()
        => Assert.Equal(0, DiskGuard.FreeBytes(@"Z:\немає\такого\тому"));

    [Fact]
    public void Empty_path_does_not_throw()
        => Assert.Equal(0, DiskGuard.FreeBytes(string.Empty));

    [Fact]
    public void Bytes_per_second_covers_both_tracks()
        => Assert.Equal(48000 * 2 * 3, DiskGuard.BytesPerSecond); // моно + стерео, 16 біт

    [Fact]
    public void Remaining_minutes_are_estimated_from_free_space()
    {
        // 1 ГіБ / 288 000 Б/с / 60 = 62 хвилини. Коментар у an earlier build казав «58»,
        // бо рахував десятковий гігабайт при двійковому порозі — число, переписане
        // без перевірки. Тут воно перевірене.
        Assert.Equal(62, DiskGuard.EstimatedMinutesRemaining(1024L * 1024 * 1024));
    }

    [Fact]
    public void No_space_means_no_minutes()
        => Assert.Equal(0, DiskGuard.EstimatedMinutesRemaining(0));

    [Fact]
    public void Level_at_a_real_path_is_measurable()
        => Assert.NotEqual(DiskLevel.Critical, DiskGuard.LevelAt(Path.GetTempPath()));
}

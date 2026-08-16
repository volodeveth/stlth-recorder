using Stlth.Core.Timeline;

namespace Stlth.Core.Tests;

public class TimelineAccountantTests
{
    private static TimelineAccountant Started(double at = 0)
    {
        var accountant = new TimelineAccountant(48000);
        accountant.Start(at);
        return accountant;
    }

    [Fact]
    public void Contiguous_buffers_need_no_padding()
    {
        var a = Started();
        Assert.Equal(0, a.FramesToInsertBefore(0.0, 480));
        Assert.Equal(0, a.FramesToInsertBefore(0.01, 480));
        Assert.Equal(960, a.TotalFrames);
    }

    [Fact]
    public void Gap_is_filled_with_silence()
    {
        var a = Started();
        a.FramesToInsertBefore(0.0, 480);                    // 0.00–0.01
        var pad = a.FramesToInsertBefore(0.51, 480);         // прогалина 0.5 с
        Assert.Equal(24000, pad);
        Assert.Equal(480 + 24000 + 480, a.TotalFrames);
    }

    [Fact]
    public void Jitter_under_five_milliseconds_is_not_a_gap()
    {
        var a = Started();
        a.FramesToInsertBefore(0.0, 480);
        Assert.Equal(0, a.FramesToInsertBefore(0.014, 480)); // 4 мс джитеру
    }

    [Fact]
    public void Silence_before_the_first_buffer_is_accounted_for()
    {
        // Loopback не віддає пакетів, поки жоден процес нічого не грає. Якщо звук
        // почався на третій секунді, перші три секунди мають стати тишею, а не зникнути.
        var a = Started();
        Assert.Equal(144000, a.FramesToInsertBefore(3.0, 480));
    }

    [Fact]
    public void Track_is_padded_to_the_end_of_the_session()
    {
        var a = Started();
        a.FramesToInsertBefore(0.0, 480);
        Assert.Equal(479520, a.FramesToReach(10.0));         // 10 с × 48000 − 480
        Assert.Equal(480000, a.TotalFrames);
    }

    [Fact]
    public void Total_silence_still_produces_a_full_length_track()
    {
        // Жодного пакета за всю сесію — файл однаково має бути 8 секунд.
        var a = Started();
        Assert.Equal(384000, a.FramesToReach(8.0));
        Assert.Equal(384000, a.TotalFrames);
    }

    [Fact]
    public void Reaching_a_point_already_passed_adds_nothing()
    {
        var a = Started();
        a.FramesToInsertBefore(0.0, 48000);                  // 1 с
        Assert.Equal(0, a.FramesToReach(0.5));
        Assert.Equal(48000, a.TotalFrames);
    }

    [Fact]
    public void Invariant_holds_across_a_gap_ridden_session()
    {
        var a = Started();
        a.FramesToInsertBefore(0.0, 4800);                   // 0.0–0.1
        a.FramesToInsertBefore(0.6, 4800);                   // прогалина 0.5
        a.FramesToInsertBefore(0.7, 4800);                   // впритул
        a.FramesToReach(5.0);
        Assert.Equal(5 * 48000, a.TotalFrames);              // тривалість × частота, точно
    }

    [Fact]
    public void A_packet_timestamped_in_the_past_does_not_rewind_the_track()
    {
        // Драйвер, який віддав зіпсований qpcPosition, не має зсувати всю решту
        // доріжки вперед: такий пакет дописується впритул, а не переписує минуле.
        var a = Started();
        a.FramesToInsertBefore(0.0, 48000);   // 0.0–1.0
        a.FramesToInsertBefore(0.2, 480);     // таймстемп із минулого

        Assert.Equal(0, a.FramesToInsertBefore(1.01, 480)); // прогалини немає
        Assert.Equal(48000 + 480 + 480, a.TotalFrames);
    }

    [Fact]
    public void Two_tracks_padded_to_the_same_instant_are_exactly_equal()
    {
        // Причина, з якої облік абсолютний, а не інкрементний: на 30-секундному
        // записі інкрементне округлення вже дало доріжки, що відрізнялися на 2 кадри.
        // Помилка мікроскопічна, але вона накопичується — на годині з неї виріс би
        // справжній розсинхрон.
        var a = Started();
        var b = Started();

        a.FramesToInsertBefore(0.021, 480);
        a.FramesToInsertBefore(0.031, 480);
        b.FramesToInsertBefore(0.2063, 480);
        b.FramesToInsertBefore(0.2163, 480);

        a.FramesToReach(30.545);
        b.FramesToReach(30.545);

        Assert.Equal(a.TotalFrames, b.TotalFrames);
        Assert.Equal((long)Math.Round(30.545 * 48000), a.TotalFrames);
    }

    [Fact]
    public void Rounding_does_not_accumulate_over_many_packets()
    {
        // Тисяча пакетів по 10 мс — і жодного зайвого кадру наприкінці.
        var a = Started();
        for (var i = 0; i < 1000; i++)
        {
            a.FramesToInsertBefore(i * 0.01, 480);
        }

        a.FramesToReach(60.0);

        Assert.Equal(60 * 48000, a.TotalFrames);
    }

    [Fact]
    public void A_session_that_starts_at_a_nonzero_origin_is_still_exact()
    {
        // QPC не починається з нуля: origin — це момент старту сесії, і все
        // рахується відносно нього.
        var a = Started(at: 1234.5);
        a.FramesToInsertBefore(1234.5, 4800);
        a.FramesToReach(1234.5 + 30);
        Assert.Equal(30 * 48000, a.TotalFrames);
    }
}

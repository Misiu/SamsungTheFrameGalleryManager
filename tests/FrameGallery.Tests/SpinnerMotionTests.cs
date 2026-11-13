using FrameGallery.Desktop.Controls;

namespace FrameGallery.Tests;

public class SpinnerMotionTests
{
    const double Step = 0.005;   // 5 ms
    const double Seconds = 12;   // several cycles

    [Fact]
    public void Neither_end_of_the_arc_ever_moves_backwards()
    {
        var (tail, head) = Spinner.Positions(0);
        for (var t = Step; t <= Seconds; t += Step)
        {
            var (nextTail, nextHead) = Spinner.Positions(t);
            Assert.True(nextTail >= tail - 1e-9, $"the tail moved backwards at {t:F3} s");
            Assert.True(nextHead >= head - 1e-9, $"the head moved backwards at {t:F3} s");
            (tail, head) = (nextTail, nextHead);
        }
    }

    [Fact]
    public void The_arc_grows_by_its_head_and_shrinks_by_its_tail()
    {
        // within one cycle: the first half moves only the head, the second half only the tail
        var start = Spinner.Positions(0.01);
        var middle = Spinner.Positions(0.675 - 0.005);   // end of the growing half (the cycle is 1.35 s)
        var end = Spinner.Positions(1.35 - 0.01);

        Assert.True(middle.Head - start.Head > 200);                 // the head went a long way
        Assert.True(middle.Tail - start.Tail < 1);                   // while the tail waited
        Assert.True(end.Tail - middle.Tail > 200);                   // then the tail caught up
        Assert.True(end.Head - middle.Head < 1);                     // while the head waited
    }

    [Fact]
    public void The_arc_is_never_longer_than_one_advance_and_never_negative()
    {
        for (var t = 0.0; t <= Seconds; t += Step)
        {
            var (tail, head) = Spinner.Positions(t);
            Assert.InRange(head - tail, 0, 250 + 1e-9);
        }
    }

    [Fact]
    public void Motion_is_continuous_across_cycles()
    {
        // no jump bigger than what the fastest part of the easing allows in 5 ms
        var (tail, head) = Spinner.Positions(0);
        for (var t = Step; t <= Seconds; t += Step)
        {
            var (nextTail, nextHead) = Spinner.Positions(t);
            Assert.True(nextTail - tail < 8 && nextHead - head < 8, $"a jump at {t:F3} s");
            (tail, head) = (nextTail, nextHead);
        }
    }

    [Fact]
    public void Easing_goes_from_0_to_1_without_overshoot()
    {
        Assert.Equal(0, Spinner.FastOutSlowIn(0), 6);
        Assert.Equal(1, Spinner.FastOutSlowIn(1), 6);
        double previous = 0;
        for (var x = 0.0; x <= 1; x += 0.01)
        {
            var y = Spinner.FastOutSlowIn(x);
            Assert.InRange(y, previous - 1e-9, 1 + 1e-9);
            previous = y;
        }
    }
}

using Xunit;
using PC98Emu.Scheduler;

namespace PC98Emu.Tests.Scheduler;

public class SchedulerTests
{
    [Fact]
    public void ScheduleAndFireEvent()
    {
        var scheduler = new EventScheduler();
        bool fired = false;
        scheduler.Schedule(100, "test", () => fired = true);
        scheduler.Advance(99);
        Assert.False(fired);
        scheduler.Advance(1);
        Assert.True(fired);
    }

    [Fact]
    public void RecurringEvent()
    {
        var scheduler = new EventScheduler();
        int count = 0;
        scheduler.ScheduleRecurring(50, "tick", () => count++);
        scheduler.Advance(50);
        Assert.Equal(1, count);
        scheduler.Advance(50);
        Assert.Equal(2, count);
    }

    [Fact]
    public void CancelEvent()
    {
        var scheduler = new EventScheduler();
        bool fired = false;
        scheduler.Schedule(100, "test", () => fired = true);
        scheduler.Cancel("test");
        scheduler.Advance(200);
        Assert.False(fired);
    }

    [Fact]
    public void NextEventCycles()
    {
        var scheduler = new EventScheduler();
        scheduler.Schedule(50, "a", () => { });
        scheduler.Schedule(100, "b", () => { });
        Assert.Equal(50, scheduler.CyclesUntilNextEvent());
    }

    [Fact]
    public void MultipleEventsFireInOrder()
    {
        var scheduler = new EventScheduler();
        var order = new List<string>();
        scheduler.Schedule(50, "first", () => order.Add("first"));
        scheduler.Schedule(100, "second", () => order.Add("second"));
        scheduler.Advance(100);
        Assert.Equal(new[] { "first", "second" }, order);
    }
}

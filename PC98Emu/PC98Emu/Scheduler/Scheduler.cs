namespace PC98Emu.Scheduler;

public class EventScheduler
{
    private readonly SortedList<long, ScheduledEvent> _events = new();
    private long _currentCycle;

    public void Schedule(long cyclesFromNow, string name, Action callback)
    {
        AddEvent(new ScheduledEvent { TargetCycle = _currentCycle + cyclesFromNow, Name = name, Callback = callback });
    }

    public void ScheduleRecurring(long interval, string name, Action callback)
    {
        AddEvent(new ScheduledEvent { TargetCycle = _currentCycle + interval, Name = name, Callback = callback, Recurring = true, Interval = interval });
    }

    public void Cancel(string name)
    {
        foreach (var key in _events.Where(e => e.Value.Name == name).Select(e => e.Key).ToList())
            _events.Remove(key);
    }

    public void Advance(long cycles)
    {
        _currentCycle += cycles;
        while (_events.Count > 0 && _events.First().Value.TargetCycle <= _currentCycle)
        {
            var evt = _events.First().Value;
            _events.RemoveAt(0);
            evt.Callback();
            if (evt.Recurring)
            {
                evt.TargetCycle += evt.Interval;
                AddEvent(evt);
            }
        }
    }

    public long CyclesUntilNextEvent()
    {
        if (_events.Count == 0) return long.MaxValue;
        return Math.Max(0, _events.First().Value.TargetCycle - _currentCycle);
    }

    private void AddEvent(ScheduledEvent evt)
    {
        long key = evt.TargetCycle;
        while (_events.ContainsKey(key)) key++;
        _events.Add(key, evt);
    }

    private class ScheduledEvent
    {
        public long TargetCycle;
        public string Name = "";
        public Action Callback = () => { };
        public bool Recurring;
        public long Interval;
    }
}

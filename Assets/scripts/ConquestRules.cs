using System;

/// <summary>Engine-independent capture and round rules; only the master advances them.</summary>
public sealed class ConquestRules
{
    public const double Duration = 600, Intermission = 15;
    public const float CaptureSeconds = 10;
    public int Round;
    public double StartedAt, SimulatedAt;
    public double BlueScore, OrangeScore;
    public readonly float[] Control = new float[3];
    public readonly int[] Owner = new int[3];
    public readonly bool[] Contested = new bool[3];
    public double EndsAt => StartedAt + Duration;
    public int BluePoints => (int)Math.Floor(BlueScore + .001f);
    public int OrangePoints => (int)Math.Floor(OrangeScore + .001f);
    public int Winner => BluePoints == OrangePoints ? 0 : BluePoints > OrangePoints ? 1 : 2;
    public bool Playing(double now) => now >= StartedAt && now < EndsAt;
    public ConquestRules(int round, double now) { Round = round; StartedAt = SimulatedAt = now; }

    public void Step(double now, int[] blue, int[] orange)
    {
        if (SimulatedAt >= EndsAt) return;
        // A suspended host must not award captures for time it did not observe.
        float dt = (float)Math.Max(0, Math.Min(.25, Math.Min(now, EndsAt) - SimulatedAt));
        SimulatedAt = Math.Max(SimulatedAt, Math.Min(now, EndsAt));
        for (int i = 0; i < 3; i++)
        {
            Contested[i] = blue[i] > 0 && orange[i] > 0;
            if (Contested[i] || dt <= 0) continue;
            // Score the owner at the start of this simulation interval.
            if (Owner[i] == 1) BlueScore += dt;
            if (Owner[i] == 2) OrangeScore += dt;
            int team = blue[i] > 0 ? 1 : orange[i] > 0 ? 2 : 0;
            if (team == 0) continue;
            float delta = dt / CaptureSeconds * (team == 1 ? 1 : -1);
            Control[i] = Math.Max(-1, Math.Min(1, Control[i] + delta));
            if (Math.Abs(Control[i]) < .00001f) Control[i] = 0;
            if ((Owner[i] == 1 && Control[i] <= 0) || (Owner[i] == 2 && Control[i] >= 0)) Owner[i] = 0;
            if (Control[i] >= .99999f) { Control[i] = 1; Owner[i] = 1; }
            if (Control[i] <= -.99999f) { Control[i] = -1; Owner[i] = 2; }
        }
    }
    public object[] Pack() => new object[] { Round, StartedAt, SimulatedAt, BlueScore, OrangeScore,
        (float[])Control.Clone(), (int[])Owner.Clone(), (bool[])Contested.Clone() };
    public static ConquestRules Read(object value)
    {
        if (!(value is object[] a) || a.Length != 8 || !(a[0] is int round) || round < 1 ||
            !(a[1] is double start) || !(a[2] is double tick) || !(a[3] is double blue) || !(a[4] is double orange) ||
            !(a[5] is float[] control) || control.Length != 3 || !(a[6] is int[] owners) || owners.Length != 3 ||
            !(a[7] is bool[] contested) || contested.Length != 3 || double.IsNaN(start) || double.IsInfinity(start) ||
            double.IsNaN(tick) || double.IsInfinity(tick) || double.IsNaN(blue) || double.IsInfinity(blue) ||
            double.IsNaN(orange) || double.IsInfinity(orange) || blue < 0 || orange < 0) return null;
        var state = new ConquestRules(round, start) { SimulatedAt = tick, BlueScore = blue, OrangeScore = orange };
        for (int i = 0; i < 3; i++)
        {
            if (float.IsNaN(control[i]) || Math.Abs(control[i]) > 1 || owners[i] < 0 || owners[i] > 2) return null;
            state.Control[i] = control[i]; state.Owner[i] = owners[i]; state.Contested[i] = contested[i];
        }
        return state;
    }
}

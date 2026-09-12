namespace SmartX.Api.Models;

public readonly struct PowerMetric : IEquatable<PowerMetric>, IComparable<PowerMetric>
{
    public double Watts { get; init; }
    public PowerMetric(double watts) => Watts = watts;

    // Direct aggregation: Meter3 = Meter1 + Meter2
    public static PowerMetric operator +(PowerMetric a, PowerMetric b) => new(a.Watts + b.Watts);

    // Delta difference: Delta = Meter1 - Meter2
    public static PowerMetric operator -(PowerMetric a, PowerMetric b) => new(Math.Abs(a.Watts - b.Watts));

    // Threshold comparisons
    public static bool operator >(PowerMetric a, PowerMetric b) => a.Watts > b.Watts;
    public static bool operator <(PowerMetric a, PowerMetric b) => a.Watts < b.Watts;
    public static bool operator >=(PowerMetric a, PowerMetric b) => a.Watts >= b.Watts;
    public static bool operator <=(PowerMetric a, PowerMetric b) => a.Watts <= b.Watts;

    public bool Equals(PowerMetric other) => Watts.Equals(other.Watts);
    public int CompareTo(PowerMetric other) => Watts.CompareTo(other.Watts);
    public override bool Equals(object? obj) => obj is PowerMetric other && Equals(other);
    public override int GetHashCode() => Watts.GetHashCode();
}

namespace SmartX.Api.Models;

/// <summary>
/// Reference: Operator overloading (C# reference, Microsoft Learn) - https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/operator-overloading
/// </summary>
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
    public static bool operator ==(PowerMetric a, PowerMetric b) => Math.Abs(a.Watts - b.Watts) < 0.0001;
    public static bool operator !=(PowerMetric a, PowerMetric b) => !(a == b);

    public bool Equals(PowerMetric other) => Math.Abs(Watts - other.Watts) < 0.0001;
    public int CompareTo(PowerMetric other) => Watts.CompareTo(other.Watts);
    public override bool Equals(object? obj) => obj is PowerMetric other && Equals(other);
    public override int GetHashCode() => Watts.GetHashCode();
}

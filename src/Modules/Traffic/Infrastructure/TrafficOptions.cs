namespace Traffic.Infrastructure;

// Settings of the traffic log. Section: Modules:Traffic.
public sealed class TrafficOptions {
    public const string CaptureBodiesKey = "captureBodies";
    public const string KeepKey = "trafficKeep";

    static readonly int[] Sizes = [200, 500, 1000];

    // Keeps response bodies in memory, max 16 KB each, secrets removed.
    public bool CaptureBodies { get; set; }

    // The capacity of the log: 200, 500 or 1000. Another value gives 500.
    public int TrafficKeep { get; set; } = 500;

    public int Capacity => Sizes.Contains(TrafficKeep) ? TrafficKeep : 500;
}

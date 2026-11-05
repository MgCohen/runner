using System.Globalization;

namespace App.Views.Components;

public static class Format {
    public static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Time(DateTimeOffset value) => value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Seconds(DateTimeOffset value) => value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public static string Kb(double value) => value < 10 ? value.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(value).ToString(CultureInfo.InvariantCulture);
}

namespace ThermoForge;

internal static class DeviceMetrics
{
    internal static bool IsCore(string name)
    {
        int index = name.IndexOf("Core ", StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;
        var number = name.AsSpan(index + 5).TrimStart('#');
        return number.Length > 0 && char.IsAsciiDigit(number[0]);
    }

    internal static Reading? Clock(DeviceReading? device) => device?.Sensors
        .Where(s => s.Kind == "Clock" && Valid(s.Value) && (device.IsCpu ?
            IsCore(s.Name) && !s.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase) : s.Name == "GPU Core"))
        .OrderByDescending(s => s.Value).FirstOrDefault();

    internal static Reading? Power(DeviceReading? device)
    {
        string[] names = device?.IsCpu == true ? ["CPU Package", "Package"] :
            ["GPU Board Power", "GPU Package", "GPU Power", "GPU Core"];
        foreach (string name in names)
            if (device?.Sensors.FirstOrDefault(s => s.Kind == "Power" && s.Name == name && Valid(s.Value)) is { } sensor)
                return sensor;
        return null;
    }

    private static bool Valid(float? value) => value is float v && float.IsFinite(v) && v >= 0;

    internal static string Text(DeviceReading? device)
    {
        var clock = Clock(device);
        var power = Power(device);
        string scope = power?.Name switch
        {
            "CPU Package" or "Package" or "GPU Package" => "Snaga paketa",
            "GPU Board Power" => "Snaga kartice",
            "GPU Core" => "Snaga jezgre",
            _ => device?.IsCpu == true ? "Snaga paketa" : "Snaga GPU-a"
        };
        return (device?.IsCpu == true ? "Takt (maks.)" : "Takt jezgre") + "  " +
            (clock == null ? "—" : $"{clock.Value:F0} MHz") + "\n" + scope + "  " +
            (power == null ? "—" : $"{power.Value:F1} W");
    }
}

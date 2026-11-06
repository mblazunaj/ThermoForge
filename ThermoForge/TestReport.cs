using System.Text;

namespace ThermoForge;

internal sealed record ResultField(string Label, string Value);

internal static class TestReport
{
    internal static ResultField[] Fields(TestResult result) =>
    [
        new("Završen", result.FinishedAt.ToLocalTime().ToString("g")),
        new("Trajanje", $"{(int)result.Duration.TotalHours:00}:{result.Duration.Minutes:00}:{result.Duration.Seconds:00}"),
        new("Opterećeni uređaji", result.CpuTested ? result.GpuName == null ? "CPU" : "CPU + " + result.GpuName : result.GpuName ?? "—"),
        new("Razlog završetka", result.Reason),
        new("Najviša CPU temperatura", Peak(result.CpuPeak)),
        new("Najtoplija jezgra", Peak(result.CorePeak)),
        new("Najviša GPU temperatura", result.GpuName == null ? "Nije testiran" : Peak(result.GpuPeak)),
        new("CPU paket · nadzor iGPU-a", result.PackageMonitored ? Peak(result.PackagePeak) : "Nije korišten")
    ];

    private static string Peak(ThermalPeak? value) => value == null ? "—" : $"{value.Celsius:F1} °C · {value.Sensor}";

    internal const string ComparisonNote = "Usporedi trajanje, opterećene uređaje i način nadzora prije tumačenja temperatura. Sažeci ne bilježe uvjete hlađenja ni promjene konfiguracije.";

    internal static string Build(TestResult[] results, SpecSection[] sections, DateTimeOffset specsReadAt, bool includeComputerName)
    {
        var text = new StringBuilder();
        text.AppendLine("ThermoForge " + UpdateService.Version + " · izvještaj").AppendLine();
        if (results.Length > 1) text.AppendLine(ComparisonNote).AppendLine();
        for (int i = 0; i < results.Length; i++)
        {
            text.AppendLine(results.Length == 1 ? "REZULTAT TESTA" : i == 0 ? "ZADNJI TEST" : "PRETHODNI TEST");
            foreach (var field in Fields(results[i])) text.AppendLine(field.Label + ": " + field.Value);
            text.AppendLine();
        }
        text.AppendLine("SPECIFIKACIJE RAČUNALA");
        text.AppendLine("Očitano: " + (sections.Length > 0 ? specsReadAt.ToLocalTime().ToString("g") : "nije dostupno"));
        text.AppendLine("Trenutačna konfiguracija računala; nije snimka konfiguracije u trenutku testa.");
        text.AppendLine("Naziv računala: " + (includeComputerName ? "uključen" : "izostavljen"));
        if (sections.Length == 0) text.AppendLine("Specifikacije nisu dostupne.");
        foreach (var section in sections)
        {
            text.AppendLine().AppendLine(section.Name + (section.Incomplete ? " · dio podataka nije dostupan" : ""));
            foreach (var group in section.Groups)
            {
                text.AppendLine(group.Title);
                foreach (var field in group.Fields)
                    if (includeComputerName || field.Label != "Naziv računala")
                        text.AppendLine("  " + field.Label + ": " + (field.Value ?? "—"));
            }
        }
        return text.ToString();
    }
}

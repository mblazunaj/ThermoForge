using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ThermoForge;

internal sealed record SpecGroup(string Title, params SpecField[] Fields);
internal sealed record SpecField(string Label, string? Value);
internal sealed record SpecSection(string Name, List<SpecGroup> Groups)
{
    public bool Incomplete { get; set; }
}

internal static class ComputerSpecs
{
    internal static SpecSection[] Read(CancellationToken token)
    {
        var sections = new List<SpecSection>();
        var system = Section("Sustav");
        var computer = ReadWmi(system, "Win32_ComputerSystem", "Manufacturer,Model").FirstOrDefault();
        var os = ReadWmi(system, "Win32_OperatingSystem", "Caption,Version,BuildNumber").FirstOrDefault();
        var firmware = TryRead(system, () =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            string[] names = ["SystemManufacturer", "SystemProductName", "BaseBoardManufacturer", "BaseBoardProduct",
                "BaseBoardVersion", "BIOSVendor", "BIOSVersion", "BIOSReleaseDate"];
            return key == null ? null : names.ToDictionary(name => name, name => key.GetValue(name));
        });
        var windows = Text(os, "Caption") ?? "Windows";
        var osVersion = Environment.OSVersion.Version;
        system.Groups.Add(new(windows,
            new("Verzija", Text(os, "Version") ?? osVersion.ToString(3)),
            new("Međuverzija", Text(os, "BuildNumber") ?? osVersion.Build.ToString()),
            new("Arhitektura", RuntimeInformation.OSArchitecture.ToString())));
        var machine = Join(Text(computer, "Manufacturer") ?? Text(firmware, "SystemManufacturer"),
            Text(computer, "Model") ?? Text(firmware, "SystemProductName"));
        if (machine != null) system.Groups.Add(new("Računalo", new SpecField("Proizvođač i model", machine)));

        var cpu = Section("Procesor");
        var processors = ReadWmi(cpu, "Win32_Processor", "Name,NumberOfCores,NumberOfLogicalProcessors,SocketDesignation,MaxClockSpeed,L2CacheSize,L3CacheSize");
        foreach (var chip in processors)
            cpu.Groups.Add(new(Text(chip, "Name") ?? "Procesor",
                new("Fizičke jezgre", Positive(chip, "NumberOfCores")),
                new("Logički procesori", Positive(chip, "NumberOfLogicalProcessors")),
                new("Podnožje", Text(chip, "SocketDesignation")),
                new("Najveći takt prema SMBIOS-u", Positive(chip, "MaxClockSpeed", " MHz")),
                new("Predmemorija L2", KiB(chip, "L2CacheSize")),
                new("Predmemorija L3", KiB(chip, "L3CacheSize"))));
        if (cpu.Groups.Count == 0)
        {
            string? name = TryRead(cpu, () =>
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return (key?.GetValue("ProcessorNameString") as string)?.Trim();
            });
            cpu.Groups.Add(new(name ?? "Procesor", new SpecField("Logički procesori dostupni programu", Environment.ProcessorCount.ToString())));
        }

        var ram = Section("Memorija");
        var modules = ReadWmi(ram, "Win32_PhysicalMemory", "DeviceLocator,Capacity,SMBIOSMemoryType,ConfiguredClockSpeed,Manufacturer,PartNumber");
        var installed = GetPhysicallyInstalledSystemMemory(out ulong kib) ? Size(kib * 1024) : null;
        if (installed == null && modules.Count > 0 && modules.All(m => Number(m, "Capacity") > 0))
            installed = Size(modules.Aggregate(0UL, (sum, module) => sum + Number(module, "Capacity")));
        var memoryStatus = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        var usable = GlobalMemoryStatusEx(ref memoryStatus) ? Size(memoryStatus.TotalPhysical) : null;
        ram.Groups.Add(new("Radna memorija", new("Ugrađeni kapacitet", installed),
            new("Kapacitet dostupan sustavu", usable),
            new("Memorijski uređaji prema SMBIOS-u", modules.Count > 0 ? modules.Count.ToString() : null)));
        foreach (var module in modules)
            ram.Groups.Add(new(Text(module, "DeviceLocator") ?? "Memorijski uređaj",
                new("Kapacitet", SizeIfReported(module, "Capacity")),
                new("Vrsta", MemoryType(Number(module, "SMBIOSMemoryType"))),
                new("Konfigurirana brzina", Positive(module, "ConfiguredClockSpeed", " MT/s")),
                new("Proizvođač", Text(module, "Manufacturer")),
                new("Oznaka modela", Text(module, "PartNumber"))));

        var board = Section("Matična ploča i BIOS");
        var boards = ReadWmi(board, "Win32_BaseBoard", "Manufacturer,Product,Version");
        var boardModel = JoinNames(boards.Select(part => Text(part, "Product"))) ?? Text(firmware, "BaseBoardProduct");
        foreach (var part in boards)
            board.Groups.Add(new("Matična ploča", new("Proizvođač", Text(part, "Manufacturer")),
                new("Model", Text(part, "Product")), new("Revizija", Text(part, "Version"))));
        if (boards.Count == 0 && boardModel != null)
            board.Groups.Add(new("Matična ploča", new("Proizvođač", Text(firmware, "BaseBoardManufacturer")),
                new("Model", boardModel), new("Revizija", Text(firmware, "BaseBoardVersion"))));
        var biosEntries = ReadWmi(board, "Win32_BIOS", "Manufacturer,SMBIOSBIOSVersion,ReleaseDate");
        foreach (var bios in biosEntries)
            board.Groups.Add(new("BIOS / UEFI", new("Proizvođač", Text(bios, "Manufacturer")),
                new("Verzija", Text(bios, "SMBIOSBIOSVersion")), new("Datum izdanja", Date(Text(bios, "ReleaseDate")))));
        if (biosEntries.Count == 0 && Text(firmware, "BIOSVersion") is { } biosVersion)
            board.Groups.Add(new("BIOS / UEFI", new("Proizvođač", Text(firmware, "BIOSVendor")),
                new("Verzija", biosVersion), new("Datum izdanja", Text(firmware, "BIOSReleaseDate"))));

        var graphics = Section("Grafika i zasloni");
        var gpus = TryRead(graphics, GpuWorkload.Adapters) ?? [];
        foreach (var gpu in gpus)
            graphics.Groups.Add(new(gpu.Name, new("Namjenska video memorija", Size(gpu.Memory)),
                new("Dijeljena sistemska memorija (maks.)", Size(gpu.SharedMemory))));
        var screens = TryRead(graphics, () => Screen.AllScreens) ?? [];
        foreach (var screen in screens)
            graphics.Groups.Add(new(screen.DeviceName.Replace(@"\\.\", "") + (screen.Primary ? " · glavni zaslon" : ""),
                new SpecField("Razlučivost radne površine", $"{screen.Bounds.Width} × {screen.Bounds.Height} px")));

        var storage = Section("Pohrana");
        var disks = ReadWmi(storage, "Win32_DiskDrive", "Model,Size,FirmwareRevision");
        foreach (var disk in disks)
            storage.Groups.Add(new(Text(disk, "Model") ?? "Fizički disk", new("Kapacitet", SizeIfReported(disk, "Size")),
                new("Firmware", Text(disk, "FirmwareRevision"))));
        var volumes = TryRead(storage, DriveInfo.GetDrives) ?? [];
        var fixedVolumes = volumes.Where(drive => drive.DriveType == DriveType.Fixed).ToArray();
        foreach (var volume in fixedVolumes)
        {
            var group = TryRead(storage, () => volume.IsReady ? new SpecGroup("Pogon " + volume.Name,
                new("Datotečni sustav", volume.DriveFormat), new("Kapacitet", Size((ulong)volume.TotalSize)),
                new("Slobodno", Size((ulong)volume.TotalFreeSpace))) : null);
            if (group != null) storage.Groups.Add(group);
        }

        var network = Section("Mreža");
        var interfaces = TryRead(network, NetworkInterface.GetAllNetworkInterfaces) ?? [];
        var physical = ReadWmi(network, "Win32_NetworkAdapter", "Name,GUID,NetConnectionID", "PhysicalAdapter = TRUE");
        var configured = interfaces.Where(adapter => adapter.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) &&
            (adapter.Supports(NetworkInterfaceComponent.IPv4) || adapter.Supports(NetworkInterfaceComponent.IPv6))).ToArray();
        foreach (var adapter in physical)
        {
            string? id = Text(adapter, "GUID")?.Trim('{', '}');
            var link = id == null ? null : interfaces.FirstOrDefault(item => string.Equals(item.Id.Trim('{', '}'), id, StringComparison.OrdinalIgnoreCase));
            network.Groups.Add(NetworkGroup(Text(adapter, "Name") ?? "Mrežni adapter", Text(adapter, "NetConnectionID"), link));
        }
        if (physical.Count == 0)
            foreach (var adapter in configured)
                network.Groups.Add(NetworkGroup(adapter.Description, adapter.Name, adapter));

        var audio = Section("Zvuk");
        foreach (var device in ReadWmi(audio, "Win32_SoundDevice", "Name,Manufacturer"))
            audio.Groups.Add(new(Text(device, "Name") ?? "Zvučni uređaj", new SpecField("Proizvođač", Text(device, "Manufacturer"))));

        var overview = new SpecSection("Pregled", [new("Glavne komponente",
            new("Naziv računala", Environment.MachineName),
            new("Windows", Join(windows, Text(os, "BuildNumber") ?? osVersion.Build.ToString())),
            new("Model računala", machine),
            new("Procesor", JoinNames(cpu.Groups)),
            new(installed == null && usable != null ? "Memorija dostupna sustavu" : "Memorija", installed ?? usable),
            new("Matična ploča", boardModel),
            new("Grafika", JoinNames(gpus.Select(gpu => gpu.Name))),
            new(disks.Count == 0 && fixedVolumes.Length > 0 ? "Pogoni" : "Diskovi",
                JoinNames(disks.Select(disk => Text(disk, "Model"))) ?? JoinNames(fixedVolumes.Select(drive => drive.Name))),
            new("Mreža", JoinNames(configured.Where(a => a.OperationalStatus == OperationalStatus.Up).Select(a => a.Description)) ??
                JoinNames(physical.Select(adapter => Text(adapter, "Name")))),
            new("Zvuk", JoinNames(audio.Groups)))]) { Incomplete = sections.Any(section => section.Incomplete) };
        return [overview, .. sections];

        SpecSection Section(string name)
        {
            token.ThrowIfCancellationRequested();
            var section = new SpecSection(name, []);
            sections.Add(section);
            return section;
        }

        T? TryRead<T>(SpecSection section, Func<T?> read) where T : class
        {
            token.ThrowIfCancellationRequested();
            try { return read(); }
            catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException or IOException
                or NetworkInformationException or SharpGen.Runtime.SharpGenException or System.Security.SecurityException)
            {
                section.Incomplete = true;
                return null;
            }
        }

        List<Dictionary<string, object?>> ReadWmi(SpecSection section, string type, string fields, string? condition = null) =>
            TryRead(section, () => Query(type, fields, condition, token)) ?? [];
    }

    private static List<Dictionary<string, object?>> Query(string type, string fields, string? condition, CancellationToken token)
    {
        string statement = $"SELECT {fields} FROM {type}";
        if (condition != null) statement += $" WHERE {condition}";
        using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\cimv2"), new ObjectQuery(statement),
            new System.Management.EnumerationOptions { ReturnImmediately = true, Rewindable = false, Timeout = TimeSpan.FromSeconds(3) });
        using var results = searcher.Get();
        var properties = fields.Split(',');
        var rows = new List<Dictionary<string, object?>>();
        foreach (ManagementObject row in results)
        {
            using (row)
            {
                token.ThrowIfCancellationRequested();
                rows.Add(properties.ToDictionary(property => property, property => (object?)row[property]));
            }
        }
        return rows;
    }

    private static string? Text(Dictionary<string, object?>? row, string key)
    {
        string? value = row?.GetValueOrDefault(key)?.ToString()?.Trim();
        return string.IsNullOrEmpty(value) || value is "To Be Filled By O.E.M." or "Default string" ? null : value;
    }
    private static ulong Number(Dictionary<string, object?>? row, string key) =>
        ulong.TryParse(Text(row, key), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static string? Positive(Dictionary<string, object?>? row, string key, string unit = "") =>
        Number(row, key) is > 0 and var value ? value + unit : null;
    private static string? SizeIfReported(Dictionary<string, object?> row, string key) =>
        Number(row, key) is > 0 and var value ? Size(value) : null;
    private static string? KiB(Dictionary<string, object?> row, string key) =>
        Number(row, key) is > 0 and var value ? Size(value * 1024) : null;
    private static string Size(ulong bytes) => bytes switch
    {
        >= 1UL << 40 => $"{bytes / (double)(1UL << 40):0.##} TiB",
        >= 1UL << 30 => $"{bytes / (double)(1UL << 30):0.##} GiB",
        >= 1UL << 20 => $"{bytes / (double)(1UL << 20):0.##} MiB",
        >= 1UL << 10 => $"{bytes / 1024d:0.##} KiB",
        _ => $"{bytes} B"
    };
    private static string? Join(params string?[] values) =>
        string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value))) is { Length: > 0 } text ? text : null;
    private static string? JoinNames(IEnumerable<string?> names) => Join(names.ToArray());
    private static string? JoinNames(IEnumerable<SpecGroup> groups) => JoinNames(groups.Select(group => group.Title));
    private static string? Date(string? value) => value is { Length: >= 8 } &&
        DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date.ToString("d", CultureInfo.GetCultureInfo("hr-HR")) : null;
    private static string? MemoryType(ulong code) => code switch
    {
        18 => "DDR", 19 => "DDR2", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR", 28 => "LPDDR2",
        29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => null
    };
    private static SpecGroup NetworkGroup(string title, string? name, NetworkInterface? adapter)
    {
        var status = adapter?.OperationalStatus;
        var rate = status == OperationalStatus.Up ? adapter!.Speed : 0;
        return new(title, new("Sučelje", name ?? adapter?.Name), new("Status", status is { } state ? LinkStatus(state) : null),
            new("Brzina veze", rate > 0 ? $"{rate / 1_000_000d:0.##} Mb/s" : null));
    }
    private static string LinkStatus(OperationalStatus status) => status switch
    {
        OperationalStatus.Up => "Aktivno", OperationalStatus.Down => "Nije povezano",
        OperationalStatus.Dormant => "U mirovanju", OperationalStatus.NotPresent => "Nedostupno", _ => "Nepoznato"
    };

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}

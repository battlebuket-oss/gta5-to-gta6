using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GtaTransformationHub.Services;

public sealed record HardwareSnapshot(
    int LogicalProcessorCount,
    ulong TotalPhysicalMemoryMb,
    ulong AvailablePhysicalMemoryMb,
    long? DedicatedGpuMemoryMb,
    string CpuModel = "Unknown CPU",
    IReadOnlyList<string>? GpuAdapters = null,
    string? WindowsVersion = null);

public sealed record PerformanceProfile(
    string Id,
    string Name,
    string Target,
    int TextureQualityLevel,
    int PopulationDensityPercent,
    int DistanceScalingPercent,
    bool RayTracing,
    string Guidance);

/// <summary>
/// Provides conservative guidance profiles. It does not write game settings or
/// promise an FPS result; a profile becomes actionable only when a tested package
/// adapter exists for the selected GTA V build.
/// </summary>
public static class PerformanceProfileService
{
    public static HardwareSnapshot CaptureHardware(long? manuallyConfirmedGpuMemoryMb = null)
    {
        var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        var succeeded = OperatingSystem.IsWindows() && GlobalMemoryStatusEx(ref memory);
        return new HardwareSnapshot(
            Environment.ProcessorCount,
            succeeded ? memory.TotalPhysical / (1024 * 1024) : 0,
            succeeded ? memory.AvailablePhysical / (1024 * 1024) : 0,
            manuallyConfirmedGpuMemoryMb,
            ReadCpuModel(),
            ReadGpuAdapters(),
            Environment.OSVersion.VersionString);
    }


    public static string FormatHardwareEvidence(HardwareSnapshot hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        var totalRam = hardware.TotalPhysicalMemoryMb > 0
            ? (hardware.TotalPhysicalMemoryMb / 1024.0).ToString("0.0") + " GB"
            : "Not detected";
        var gpuNames = hardware.GpuAdapters is { Count: > 0 }
            ? string.Join("; ", hardware.GpuAdapters)
            : "Not detected — enter exact GPU model";
        var vram = hardware.DedicatedGpuMemoryMb is > 0
            ? (hardware.DedicatedGpuMemoryMb.Value / 1024.0).ToString("0.0") + " GB (manually confirmed)"
            : "Not reliably detected — enter VRAM manually";

        return string.Join(Environment.NewLine,
            "Windows: " + (hardware.WindowsVersion ?? Environment.OSVersion.VersionString),
            "Architecture: " + RuntimeInformation.OSArchitecture,
            "Logical processors: " + hardware.LogicalProcessorCount,
            "CPU: " + hardware.CpuModel,
            "GPU adapter(s): " + gpuNames,
            "Installed physical RAM: " + totalRam,
            "GPU VRAM: " + vram,
            "GPU driver version: enter / confirm in Device Manager",
            "Launcher: enter Steam or Rockstar Games Launcher",
            "Display / settings: enter resolution, refresh rate and graphics settings",
            "Loaders / scripts: enter Script Hook V, SHVDN and other runtime versions");
    }

    private static string ReadCpuModel()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                writable: false);
            var value = key?.GetValue("ProcessorNameString") as string;
            return string.IsNullOrWhiteSpace(value) ? "Unknown CPU" : value.Trim();
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return "CPU model could not be read from Windows registry";
        }
    }

    private static IReadOnlyList<string> ReadGpuAdapters()
    {
        const string displayClassPath =
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        var adapters = new List<string>();

        try
        {
            using var displayClass = Registry.LocalMachine.OpenSubKey(displayClassPath, writable: false);
            if (displayClass is null)
                return adapters;

            foreach (var subKeyName in displayClass.GetSubKeyNames())
            {
                if (subKeyName.Length != 4 || !subKeyName.All(char.IsAsciiDigit))
                    continue;

                try
                {
                    using var adapter = displayClass.OpenSubKey(subKeyName, writable: false);
                    var description = adapter?.GetValue("DriverDesc") as string;
                    if (string.IsNullOrWhiteSpace(description))
                        description = adapter?.GetValue("Device Description") as string;
                    if (string.IsNullOrWhiteSpace(description))
                        continue;

                    var driverVersion = adapter?.GetValue("DriverVersion") as string;
                    var label = description.Trim();
                    if (!string.IsNullOrWhiteSpace(driverVersion))
                        label += " (driver " + driverVersion.Trim() + ")";
                    if (!adapters.Contains(label, StringComparer.OrdinalIgnoreCase))
                        adapters.Add(label);
                }
                catch (Exception exception) when (
                    exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    // Skip unreadable adapter entries; never fail the evidence form.
                }
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // GPU detection is best-effort. Manual entry remains available.
        }

        return adapters;
    }

    public static IReadOnlyList<PerformanceProfile> GetProfiles() =>
    [
        new PerformanceProfile(
            "performance", "Performance", "Stable frame pacing at 1080p",
            1, 50, 55, false,
            "Use conservative texture and distance settings. Preserve the original animal, plant and vegetation species; lower visual density only through tested native settings."),
        new PerformanceProfile(
            "balanced", "Balanced", "Balanced visual quality and frame pacing",
            2, 70, 70, false,
            "Recommended default when GPU memory is unknown. Change only settings that are confirmed to exist in the installed game build."),
        new PerformanceProfile(
            "visual", "Visual", "Higher-quality textures and longer draw distance",
            3, 85, 85, false,
            "Use only when measured VRAM headroom and frame-time testing support the selected asset packs."),
        new PerformanceProfile(
            "cinematic", "Cinematic", "Image quality over maximum frame rate",
            3, 100, 100, false,
            "For benchmarked high-headroom systems. This is a guidance profile, not a promise of a particular frame rate.")
    ];

    public static PerformanceProfile Recommend(HardwareSnapshot hardware)
    {
        if (hardware.TotalPhysicalMemoryMb > 0 &&
            hardware.TotalPhysicalMemoryMb < 16 * 1024)
            return GetProfiles()[0];

        if (hardware.DedicatedGpuMemoryMb is long vram && vram <= 4096)
            return GetProfiles()[0];

        if (hardware.TotalPhysicalMemoryMb >= 32 * 1024 &&
            hardware.DedicatedGpuMemoryMb is >= 8192)
            return GetProfiles()[2];

        return GetProfiles()[1];
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

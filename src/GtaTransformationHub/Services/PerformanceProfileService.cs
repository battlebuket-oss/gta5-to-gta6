using System.Runtime.InteropServices;

namespace GtaTransformationHub.Services;

public sealed record HardwareSnapshot(
    int LogicalProcessorCount,
    ulong TotalPhysicalMemoryMb,
    ulong AvailablePhysicalMemoryMb,
    long? DedicatedGpuMemoryMb);

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
            manuallyConfirmedGpuMemoryMb);
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

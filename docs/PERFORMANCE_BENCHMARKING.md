# GTA V Legacy performance benchmarking

## Goal

Compare the unmodified GTA V Legacy baseline against one mod group at a time on the target laptop (RTX 4050 Laptop GPU with 6 GB VRAM, 1080p). Do not publish FPS expectations until the actual game build, driver, settings, capture and repeatability conditions are recorded.

The Hub imports a PresentMon CSV and writes a JSON benchmark report under:

\`%LOCALAPPDATA%\GtaTransformationHub\Benchmarks\`

It analyses only rows whose \`Application\` field identifies \`GTA5.exe\`. It accepts common PresentMon v1/v2 frame-time columns, including \`MsBetweenPresents\` and \`FrameTime\`; CSV header names have changed across PresentMon versions, so unsupported/missing columns are reported rather than guessed.

## Capture

1. Obtain PresentMon from its upstream project/release: https://github.com/GameTechDev/PresentMon
2. Start GTA V Legacy Story Mode and load a repeatable save/location. Avoid GTA Online.
3. Start an elevated PowerShell prompt if PresentMon reports that it cannot start/read the tracing session.
4. Capture a fixed 90-second run. Example (adjust the executable and output paths):

\`\`\`powershell
.\PresentMon.exe --process_name GTA5.exe --output_file .\gta5-vanilla-run1.csv --timed 90 --terminate_after_timed --v2_metrics
\`\`\`

5. Repeat the exact same route and conditions three times. Then repeat for each profile or mod group, changing only the package set being evaluated.
6. In the Hub, select **IMPORT PRESENTMON CSV**, enter the actual GTA V file version, resolution, GPU, driver, profile and scenario, and analyse the capture.

PresentMon CLI documentation: https://github.com/GameTechDev/PresentMon/blob/main/PresentMon/CommandLine.cpp  
CSV field documentation: https://github.com/GameTechDev/PresentMon/blob/main/Tools/generate/readme/CSVIntro.md  
V2 metric definitions: https://github.com/GameTechDev/PresentMon/blob/main/README-CaptureApplication.md

## Controlled test conditions

Record and hold constant:

- GTA V Legacy executable file version and Story Mode save.
- Resolution, display mode, refresh rate, VSync, frame cap, graphics settings and population sliders.
- GPU driver and laptop power mode; keep the laptop plugged in and use the same thermal/fan mode.
- Time of day, weather, location/route, traffic density and camera path as closely as possible.
- Capture duration and warm-up time. Avoid comparing one cold shader/asset-streaming run with warmed-up runs.

Use three or more captures for every tested configuration. Report median results across captures where possible, not the best run. Keep the source CSVs with the JSON reports.

## Metrics

The Hub reports:

- Frame count and matching GTA5.exe rows.
- Mean and median frame time, P95 frame time and P99 frame time.
- Average FPS calculated as 1000 / mean frame time.
- Estimated 1% low calculated as 1000 / P99 frame time.
- Dropped frame count where the CSV provides the \`Dropped\` field.
- Average CPU Busy and GPU Busy when those columns are present in the imported CSV.
- SHA-256 of the source CSV and benchmark metadata.

The estimated 1% low is a percentile-based approximation, not a substitute for every vendor's 1%-low calculation. Frame-time percentiles are the main smoothness signal; an average FPS number alone hides stutters.

## Metrics not inferred

A PresentMon frame capture alone is not a complete laptop hardware telemetry record. The Hub does not infer or report:

- GPU VRAM allocation/peak usage.
- GPU temperature, hotspot temperature, power, voltage, clock or utilisation percentage.
- CPU temperature, package power, per-core utilisation or thermal throttling.
- RAM usage, disk I/O or asset-streaming residency.

Record those separately using a trusted hardware monitor such as HWiNFO or MSI Afterburner, and keep its log synchronized to the PresentMon capture. The Hub currently does not parse those hardware-monitor logs.

## RTX 4050 profile constraints

Until real measurements are available:

- Start with native 1080p, one major graphics/water/vegetation overhaul at a time, and conservative texture resolutions.
- Do not enable 8K water textures or stack multiple population/density systems by default on a 6 GB VRAM laptop GPU.
- Keep ray tracing and experimental shader packages disabled unless individually tested.
- Evaluate minimum and 1% low performance, frame-time spikes, VRAM headroom, temperatures and asset-streaming stutter—not only average FPS.
- Maintain a clean rollback point and compare with the exact same vanilla baseline.

## Acceptance gate

A profile is not marked tested solely because the Hub can parse its CSV. Approval requires three repeatable captures, the full hardware/game metadata, no unexplained crash or save corruption, acceptable frame-time/VRAM/thermal headroom, and a successful uninstall/rollback followed by a clean Story Mode launch. Actual testing must run on the target laptop; GitHub CI cannot emulate the user's game installation or RTX 4050.

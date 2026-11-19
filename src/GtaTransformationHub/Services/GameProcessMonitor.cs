using System.Diagnostics;
using System.ComponentModel;

namespace GtaTransformationHub.Services;

public sealed class GameProcessMonitor
{
    private static readonly string[] ProtectedProcessNames =
    {
        "GTA5",
        "GTA5_Enhanced"
    };

    public bool IsStoryModeProcessRunning()
    {
        try
        {
            foreach (var processName in ProtectedProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    using (process)
                    {
                        try
                        {
                            if (!process.HasExited)
                                return true;
                        }
                        catch (InvalidOperationException)
                        {
                            // Process exited between enumeration and inspection.
                        }
                        catch (Win32Exception)
                        {
                            // If process inspection is denied, callers must fail closed.
                            return true;
                        }
                    }
                }
            }
        }
        catch (Win32Exception)
        {
            return true;
        }
        catch
        {
            // Any uncertainty in process inspection must block writes to the game folder.
            return true;
        }

        return false;
    }

    public void EnsureConfigurationUnlocked()
    {
        if (IsStoryModeProcessRunning())
            throw new InvalidOperationException(
                "GTA V is running or its process state cannot be verified. Mod configuration and deployment are locked until the game exits.");
    }
}

using System.Diagnostics;

namespace GtaTransformationHub.Services;

public sealed class GameProcessMonitor
{
    public bool IsStoryModeProcessRunning()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("GTA5"))
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
                }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // If process inspection is denied, callers must fail closed.
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }

        return false;
    }

    public void EnsureConfigurationUnlocked()
    {
        if (IsStoryModeProcessRunning())
            throw new InvalidOperationException("GTA V is running. Mod configuration and deployment are locked until the game exits.");
    }
}

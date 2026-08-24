using System.ComponentModel;
using System.Diagnostics;

namespace PowerPlanTray.Core.Services;

public sealed class ElevationService
{
    public bool LastOperationWasCancelled { get; private set; }

    public Task<bool> DuplicateSchemeAsync(Guid sourceSchemeGuid) =>
        RunPowerCfgAsync($"-duplicatescheme {sourceSchemeGuid:D}");

    public Task<bool> DeleteSchemeAsync(Guid schemeGuid) =>
        RunPowerCfgAsync($"-delete {schemeGuid:D}");

    public Task<bool> SetSettingHiddenAsync(Guid subgroupGuid, Guid settingGuid, bool hidden) =>
        RunPowerCfgAsync($"-attributes {subgroupGuid:D} {settingGuid:D} {(hidden ? "+ATTRIB_HIDE" : "-ATTRIB_HIDE")}");

    private async Task<bool> RunPowerCfgAsync(string arguments)
    {
        LastOperationWasCancelled = false;

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
            });

            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            LastOperationWasCancelled = true;
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}

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

    public Task<bool> SetSettingsHiddenAsync(IEnumerable<(Guid SubgroupGuid, Guid SettingGuid)> settings, bool hidden)
        => SetSettingsHiddenAsync(settings.Select(setting => (setting.SubgroupGuid, setting.SettingGuid, hidden)));

    public Task<bool> SetSettingsHiddenAsync(IEnumerable<(Guid SubgroupGuid, Guid SettingGuid, bool Hidden)> settings)
    {
        string commands = string.Join(" & ", settings.Distinct().Select(setting =>
            $"powercfg.exe -attributes {setting.SubgroupGuid:D} {setting.SettingGuid:D} {(setting.Hidden ? "+ATTRIB_HIDE" : "-ATTRIB_HIDE")}"));
        return string.IsNullOrEmpty(commands) ? Task.FromResult(true) : RunElevatedAsync("cmd.exe", $"/d /s /c \"{commands}\"");
    }

    private async Task<bool> RunPowerCfgAsync(string arguments)
        => await RunElevatedAsync("powercfg.exe", arguments);

    private async Task<bool> RunElevatedAsync(string fileName, string arguments)
    {
        LastOperationWasCancelled = false;

        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
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

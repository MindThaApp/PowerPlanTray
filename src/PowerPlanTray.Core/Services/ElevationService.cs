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

    public async Task<bool> SetSettingsHiddenAsync(IEnumerable<(Guid SubgroupGuid, Guid SettingGuid, bool Hidden)> settings)
    {
        string[] commands = settings.Distinct().Select(setting =>
            $"powercfg.exe -attributes {setting.SubgroupGuid:D} {setting.SettingGuid:D} {(setting.Hidden ? "+ATTRIB_HIDE" : "-ATTRIB_HIDE")}").ToArray();
        if (commands.Length == 0) return true;

        string batchFile = Path.Combine(Path.GetTempPath(), $"PowerPlanTray-{Guid.NewGuid():N}.cmd");
        try
        {
            await using var batchStream = new FileStream(
                batchFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
            await using (var writer = new StreamWriter(
                batchStream, System.Text.Encoding.UTF8, bufferSize: 1024, leaveOpen: true))
            {
                await writer.WriteLineAsync("@echo off");
                foreach (string command in commands) await writer.WriteLineAsync(command);
            }
            return await RunElevatedAsync("cmd.exe", $"/d /s /c \"\"{batchFile}\"\"");
        }
        finally
        {
            try { File.Delete(batchFile); } catch { }
        }
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

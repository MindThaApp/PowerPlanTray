using Windows.Services.Store;

namespace PowerPlanTray.Core.Services;

public sealed class LicensingService
{
    public const string ProUnlockStoreId = "9PJQSLQCK9S3";
    public static readonly TimeSpan TrialDuration = TimeSpan.FromDays(7);
    private static readonly TimeSpan LicenseCacheDuration = TimeSpan.FromMinutes(5);

    private readonly AppSettingsService _appSettingsService;
    private readonly StoreContext? _storeContext;
    private readonly SemaphoreSlim _licenseCheckLock = new(1, 1);
    private bool _cachedOwnership;
    private DateTimeOffset _ownershipCheckedUtc = DateTimeOffset.MinValue;

    public LicensingService(AppSettingsService appSettingsService)
    {
        _appSettingsService = appSettingsService;
        try
        {
            _storeContext = StoreContext.GetDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PowerPlanTray: Store context unavailable: {ex}");
        }
    }

    public bool IsTrialActive => IsTrialActiveAt(EffectiveFirstLaunchUtc, DateTimeOffset.UtcNow);

    public TimeSpan TrialRemaining
    {
        get
        {
            TimeSpan remaining = TrialDuration - (DateTimeOffset.UtcNow - EffectiveFirstLaunchUtc);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public static bool IsTrialActiveAt(DateTimeOffset firstLaunchUtc, DateTimeOffset nowUtc) =>
        nowUtc >= firstLaunchUtc && nowUtc - firstLaunchUtc < TrialDuration;

    private DateTimeOffset EffectiveFirstLaunchUtc
    {
        get
        {
#if DEBUG
            try
            {
                string expiredMarker = Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "pro-trial-expired.debug");
                if (File.Exists(expiredMarker)) return DateTimeOffset.UtcNow - TrialDuration - TimeSpan.FromMinutes(1);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PowerPlanTray: trial test marker unavailable: {ex}");
            }
#endif
            return _appSettingsService.FirstLaunchUtc;
        }
    }

    public async Task<bool> IsProUnlockedAsync(bool forceLicenseRefresh = false)
    {
        if (IsTrialActive) return true;
        if (!forceLicenseRefresh && DateTimeOffset.UtcNow - _ownershipCheckedUtc < LicenseCacheDuration)
            return _cachedOwnership;

        await _licenseCheckLock.WaitAsync();
        try
        {
            if (!forceLicenseRefresh && DateTimeOffset.UtcNow - _ownershipCheckedUtc < LicenseCacheDuration)
                return _cachedOwnership;

            try
            {
                if (_storeContext is null)
                {
                    _cachedOwnership = false;
                }
                else
                {
                    StoreAppLicense appLicense = await _storeContext.GetAppLicenseAsync();
                    _cachedOwnership = appLicense.AddOnLicenses.TryGetValue(ProUnlockStoreId, out StoreLicense? license) &&
                        license.IsActive;
                }
            }
            catch (Exception ex)
            {
                _cachedOwnership = false;
                System.Diagnostics.Debug.WriteLine($"PowerPlanTray: Store license check unavailable: {ex}");
            }

            _ownershipCheckedUtc = DateTimeOffset.UtcNow;
            return _cachedOwnership;
        }
        finally
        {
            _licenseCheckLock.Release();
        }
    }

    public async Task<StorePurchaseStatus> PurchaseProUnlockAsync()
    {
        try
        {
            if (_storeContext is null) return StorePurchaseStatus.NetworkError;
            StorePurchaseResult result = await _storeContext.RequestPurchaseAsync(ProUnlockStoreId);
            if (result.Status is StorePurchaseStatus.Succeeded or StorePurchaseStatus.AlreadyPurchased)
                await IsProUnlockedAsync(forceLicenseRefresh: true);
            return result.Status;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PowerPlanTray: Store purchase unavailable: {ex}");
            return StorePurchaseStatus.NetworkError;
        }
    }
}

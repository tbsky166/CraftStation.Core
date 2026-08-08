using CraftStation.Core.Models;
using CmlLib.Core.Auth;
using Microsoft.Identity.Client;

namespace CraftStation.Core.Services;

public enum MicrosoftLoginMode
{
    EmbeddedWebView,
    SystemBrowser,
    DeviceCode
}

public sealed class MicrosoftLoginOptions
{
    public string ClientId { get; set; } = "";
    public MicrosoftLoginMode Mode { get; set; } = MicrosoftLoginMode.EmbeddedWebView;
    public Action<DeviceCodeResult>? DeviceCodeCallback { get; set; }
}

public interface IAccountService
{
    IReadOnlyList<AccountEntry> Accounts { get; }
    AccountEntry? CurrentAccount { get; }
    Task InitializeAsync();
    AccountEntry AddOfflineAccount(string name);
    Task<AccountEntry> LoginMicrosoftAsync(MicrosoftLoginOptions options, CancellationToken ct = default);
    Task<AccountEntry> RefreshMicrosoftAsync(AccountEntry entry, CancellationToken ct = default);
    Task RemoveAccountAsync(string id);
    Task SetCurrentAccountAsync(string id);
    Task<MSession> GetLaunchSessionAsync(AccountEntry entry, CancellationToken ct = default);
}

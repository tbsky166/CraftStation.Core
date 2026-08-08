using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using CraftStation.Core.Models;
using CraftStation.Core.Utils;
using Microsoft.Identity.Client;
using XboxAuthNet.Game;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.Accounts.JsonStorage;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.SessionStorages;

namespace CraftStation.Core.Services;

public sealed class AccountService : IAccountService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ISettingsService _settings;
    private readonly IMsalAppFactory? _msalAppFactory;
    private readonly List<AccountEntry> _offlineAccounts = new();
    private JELoginHandler? _handler;
    private IPublicClientApplication? _msalApp;
    private string _msalClientId = "";
    private string _msalRedirectUri = "";

    public AccountService(ISettingsService settings, IMsalAppFactory? msalAppFactory = null)
    {
        _settings = settings;
        _msalAppFactory = msalAppFactory;
    }

    public IReadOnlyList<AccountEntry> Accounts { get; private set; } = Array.Empty<AccountEntry>();
    public AccountEntry? CurrentAccount =>
        Accounts.FirstOrDefault(a => a.Id == _settings.Settings.CurrentAccountId) ??
        Accounts.FirstOrDefault();

    private JELoginHandler Handler => _handler ??= new JELoginHandlerBuilder()
        .WithAccountManager(new CraftStationAccountManager(new DpapiJsonStorage(_settings.AccountsFile)))
        .Build();

    public async Task InitializeAsync()
    {
        _offlineAccounts.Clear();
        if (File.Exists(_settings.OfflineAccountsFile))
        {
            try
            {
                await using var stream = File.OpenRead(_settings.OfflineAccountsFile);
                var list = await JsonSerializer.DeserializeAsync<List<AccountEntry>>(stream, JsonOptions);
                if (list != null)
                    _offlineAccounts.AddRange(list);
            }
            catch (JsonException)
            {
                _offlineAccounts.Clear();
            }
        }
        RefreshAccountsCache();
    }

    public AccountEntry AddOfflineAccount(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("用户名不能为空");
        var entry = new AccountEntry
        {
            Kind = AccountKind.Offline,
            UserName = name,
            Uuid = OfflineUuid.Generate(name)
        };
        _offlineAccounts.Add(entry);
        _settings.Settings.CurrentAccountId = entry.Id;
        SaveOfflineAccountsSync();
        RefreshAccountsCache();
        return entry;
    }

    private void SaveOfflineAccountsSync()
    {
        Directory.CreateDirectory(_settings.DataDirectory);
        File.WriteAllText(
            _settings.OfflineAccountsFile,
            JsonSerializer.Serialize(_offlineAccounts, JsonOptions));
    }

    public async Task<AccountEntry> LoginMicrosoftAsync(MicrosoftLoginOptions options, CancellationToken ct = default)
    {
        var clientId = string.IsNullOrWhiteSpace(options.ClientId)
                ? (string.IsNullOrWhiteSpace(_settings.Settings.MicrosoftClientId)
                ? Config.MicrosoftClientId
                : _settings.Settings.MicrosoftClientId)
            : options.ClientId;
        var app = await GetMsalAppAsync(clientId);
        var authenticator = Handler.CreateAuthenticatorWithNewAccount(ct);
        authenticator.AddMsalOAuth(app, msal =>
        {
            switch (options.Mode)
            {
                case MicrosoftLoginMode.EmbeddedWebView:
                    return msal.EmbeddedWebView();
                case MicrosoftLoginMode.SystemBrowser:
                    return msal.SystemBrowser();
                default:
                    return msal.DeviceCode(result =>
                    {
                        options.DeviceCodeCallback?.Invoke(result);
                        return Task.CompletedTask;
                    });
            }
        });
        authenticator.AddXboxAuthForJE(xbox => xbox.Basic());
        authenticator.AddForceJEAuthenticator();
        await authenticator.ExecuteForLauncherAsync();
        RefreshAccountsCache();

        var account = Handler.AccountManager.GetDefaultAccount() as JEGameAccount;
        if (account == null)
            throw new InvalidOperationException("登录失败：未获取到有效的微软账户。");
        var entry = ToEntry(account);
        _settings.Settings.CurrentAccountId = entry.Id;
        await _settings.SaveAsync();
        return entry;
    }

    public async Task<AccountEntry> RefreshMicrosoftAsync(AccountEntry entry, CancellationToken ct = default)
    {
        var account = FindMicrosoftAccount(entry);
        var clientId = string.IsNullOrWhiteSpace(_settings.Settings.MicrosoftClientId)
            ? Config.MicrosoftClientId
            : _settings.Settings.MicrosoftClientId;
        var app = await GetMsalAppAsync(clientId);
        var authenticator = Handler.CreateAuthenticator(account, ct);
        authenticator.AddMsalOAuth(app, msal => msal.Silent());
        authenticator.AddXboxAuthForJE(xbox => xbox.Basic());
        authenticator.AddJEAuthenticator();
        await authenticator.ExecuteForLauncherAsync();
        RefreshAccountsCache();
        return ToEntry(FindMicrosoftAccount(entry));
    }

    public async Task RemoveAccountAsync(string id)
    {
        var entry = Accounts.FirstOrDefault(a => a.Id == id);
        if (entry == null)
            return;
        if (entry.Kind == AccountKind.Microsoft)
        {
            var manager = (CraftStationAccountManager)Handler.AccountManager;
            manager.Remove(id);
        }
        else
        {
            _offlineAccounts.RemoveAll(a => a.Id == id);
            await SaveOfflineAccountsAsync();
        }
        if (_settings.Settings.CurrentAccountId == id)
            _settings.Settings.CurrentAccountId = null;
        await _settings.SaveAsync();
        RefreshAccountsCache();
    }

    public Task SetCurrentAccountAsync(string id)
    {
        _settings.Settings.CurrentAccountId = id;
        return _settings.SaveAsync();
    }

    public Task<MSession> GetLaunchSessionAsync(AccountEntry entry, CancellationToken ct = default)
    {
        if (entry.Kind == AccountKind.Offline)
            return Task.FromResult(MSession.CreateOfflineSession(entry.UserName));

        var account = FindMicrosoftAccount(entry);
        var session = account.ToLauncherSession();
        if (string.IsNullOrEmpty(session.AccessToken) || account.Token == null || !account.Token.Validate())
        {
            return RefreshMicrosoftAsync(entry, ct).ContinueWith(
                t => FindMicrosoftAccount(t.Result).ToLauncherSession(),
                ct,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        return Task.FromResult(session);
    }

    private JEGameAccount FindMicrosoftAccount(AccountEntry entry)
    {
        var account = Handler.AccountManager.GetAccounts().GetAccount(entry.Id);
        return account as JEGameAccount
            ?? throw new InvalidOperationException($"未找到账户 {entry.UserName} 的本地会话。");
    }

    private async Task<IPublicClientApplication> GetMsalAppAsync(string clientId)
    {
        clientId = string.IsNullOrWhiteSpace(clientId) ? Config.MicrosoftClientId : clientId;
        var redirectUri = string.IsNullOrWhiteSpace(_settings.Settings.MicrosoftRedirectUri)
            ? Config.MicrosoftRedirectUri
            : _settings.Settings.MicrosoftRedirectUri.Trim();

        if (_msalApp == null ||
            !string.Equals(_msalApp.AppConfig.ClientId, clientId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_msalRedirectUri, redirectUri, StringComparison.OrdinalIgnoreCase))
        {
            _msalApp = _msalAppFactory != null
                ? await _msalAppFactory.CreateAsync(clientId, redirectUri)
                : await BuildDefaultAppAsync(clientId, redirectUri);
            _msalClientId = clientId;
            _msalRedirectUri = redirectUri;
        }
        return _msalApp;
    }

    private static async Task<IPublicClientApplication> BuildDefaultAppAsync(string clientId, string redirectUri)
    {
        var app = PublicClientApplicationBuilder.Create(clientId)
            .WithTenantId(Config.MicrosoftTenant)
            .WithRedirectUri(redirectUri)
            .Build();
        await MsalClientHelper.RegisterCache(app, new MsalCacheSettings());
        return app;
    }

    private void RefreshAccountsCache()
    {
        var microsoft = Handler.AccountManager.GetAccounts()
            .OfType<JEGameAccount>()
            .Select(ToEntry)
            .ToList();
        Accounts = microsoft.Concat(_offlineAccounts)
            .OrderByDescending(a => a.LastUsedUtc)
            .ToList();
    }

    private static AccountEntry ToEntry(JEGameAccount account)
    {
        return new AccountEntry
        {
            Id = account.Identifier ?? account.Profile?.UUID ?? Guid.NewGuid().ToString("N"),
            Kind = AccountKind.Microsoft,
            UserName = account.Profile?.Username ?? account.Gamertag ?? "",
            Uuid = account.Profile?.UUID,
            AccessToken = account.Token?.AccessToken,
            SkinUrl = account.Profile?.Skins.FirstOrDefault(s => s.State == "ACTIVE")?.Url,
            CapeUrl = account.Profile?.Capes.FirstOrDefault(c => c.State == "ACTIVE")?.Url,
            ExpiresAtUtc = account.Token?.ExpiresOn,
            LastUsedUtc = account.LastAccess
        };
    }

    private async Task SaveOfflineAccountsAsync()
    {
        Directory.CreateDirectory(_settings.DataDirectory);
        await using var stream = File.Create(_settings.OfflineAccountsFile);
        await JsonSerializer.SerializeAsync(stream, _offlineAccounts, JsonOptions);
    }

    private sealed class CraftStationAccountManager : IXboxGameAccountManager
    {
        private readonly IJsonStorage _storage;
        private readonly List<IXboxGameAccount> _accounts = new();
        private readonly JsonSerializerOptions? _jsonOptions = JsonXboxGameAccountManager.DefaultSerializerOption;
        private bool _loaded;

        public CraftStationAccountManager(IJsonStorage storage)
        {
            _storage = storage;
        }

        public XboxGameAccountCollection GetAccounts()
        {
            if (!_loaded)
            {
                Load();
                _loaded = true;
            }
            return XboxGameAccountCollection.FromAccounts(_accounts);
        }

        public IXboxGameAccount GetDefaultAccount() =>
            GetAccounts().FirstOrDefault() ?? NewAccount();

        public IXboxGameAccount NewAccount()
        {
            var account = JEGameAccount.FromSessionStorage(JsonSessionStorage.CreateEmpty(_jsonOptions));
            _accounts.Add(account);
            return account;
        }

        public void ClearAccounts()
        {
            _accounts.Clear();
            SaveAccounts();
        }

        public void Remove(string identifier)
        {
            _accounts.RemoveAll(a => a.Identifier == identifier);
            SaveAccounts();
        }

        public void SaveAccounts()
        {
            var root = new JsonObject();
            foreach (var account in _accounts)
            {
                if (string.IsNullOrEmpty(account.Identifier))
                    continue;
                if (account.SessionStorage is not JsonSessionStorage sessionStorage)
                    continue;
                root.Add(account.Identifier, sessionStorage.ToJsonObjectForStoring());
            }
            _storage.Write(root, _jsonOptions);
            _loaded = true;
        }

        private void Load()
        {
            _accounts.Clear();
            var node = _storage.ReadAsJsonNode();
            if (node is not JsonObject root)
                return;
            foreach (var kv in root)
            {
                if (kv.Value is not JsonObject inner)
                    continue;
                var session = new JsonSessionStorage(inner, _jsonOptions);
                var account = JEGameAccount.FromSessionStorage(session);
                if (!string.IsNullOrEmpty(account.Identifier))
                    _accounts.Add(account);
            }
        }
    }

    private sealed class DpapiJsonStorage : IJsonStorage
    {
        private readonly string _path;

        public DpapiJsonStorage(string path)
        {
            _path = path;
        }

        public JsonNode? ReadAsJsonNode()
        {
            if (!File.Exists(_path))
                return null;
            try
            {
                var bytes = File.ReadAllBytes(_path);
                if (bytes.Length == 0)
                    return null;
                var plain = OperatingSystem.IsWindows()
                    ? ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser)
                    : bytes;
                return JsonNode.Parse(plain);
            }
            catch
            {
                return null;
            }
        }

        public void Write(JsonNode node, JsonSerializerOptions? serializerOptions)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
                node.WriteTo(writer, serializerOptions);
            var plain = ms.ToArray();
            var bytes = OperatingSystem.IsWindows()
                ? ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser)
                : plain;
            File.WriteAllBytes(_path, bytes);
        }
    }
}

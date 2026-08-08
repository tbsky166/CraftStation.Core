using Microsoft.Identity.Client;

namespace CraftStation.Core.Services;

public interface IMsalAppFactory
{
    Task<IPublicClientApplication> CreateAsync(string clientId, string redirectUri);
}

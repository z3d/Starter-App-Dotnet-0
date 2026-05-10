using System.Security.Cryptography;
using System.Text;

namespace StarterApp.Api.Infrastructure.Caching;

internal static class OwnerScopedCacheKey
{
    public static string Create(string cacheKey, ICurrentUser currentUser)
    {
        var material = $"{currentUser.TenantId}\n{currentUser.Subject}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        return $"{cacheKey}:Owner:{hash}";
    }
}

using Microsoft.AspNetCore.Http;

namespace OnlineContract.Services;

/// <summary>
/// Stores and retrieves pending actions (like pending Add to Cart) in server-side session.
/// </summary>
public class SessionBridgeService
{
    private const string PendingAddKey = "oc.pendingAddProductId";

    public void SetPendingAdd(HttpContext http, int productId)
    {
        http.Session.SetInt32(PendingAddKey, productId);
    }

    public int? GetPendingAdd(HttpContext http)
    {
        return http.Session.GetInt32(PendingAddKey);
    }

    public void ClearPendingAdd(HttpContext http)
    {
        http.Session.Remove(PendingAddKey);
    }
}

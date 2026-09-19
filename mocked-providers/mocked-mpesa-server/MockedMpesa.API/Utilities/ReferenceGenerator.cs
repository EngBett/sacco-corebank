using NUlid;

namespace MockedMpesa.API.Utilities;

public static class ReferenceGenerator
{
    public static string GenerateShortReference()
    {
        var ulid = Ulid.NewUlid();
        var shortRef = ulid.ToString().Substring(0, 9);
        return shortRef;
    }

    public static string GenerateMpesaReceiptNumber()
    {
        var ulid = Ulid.NewUlid();
        var shortRef = ulid.ToString().Substring(0, 9);
        return $"QGR{shortRef}";
    }

    public static string GenerateMerchantRequestId()
    {
        var ulid = Ulid.NewUlid();
        var shortRef = ulid.ToString().Substring(0, 9);
        return $"MR-{DateTime.UtcNow:yyyyMMddHHmmss}-{shortRef}";
    }

    public static string GenerateCheckoutRequestId()
    {
        var ulid = Ulid.NewUlid();
        var shortRef = ulid.ToString().Substring(0, 8);
        return $"ws_CO_{DateTime.UtcNow:yyyyMMddHHmmss}{shortRef}";
    }

    public static string GenerateConversationId()
    {
        var ulid = Ulid.NewUlid();
        var shortRef = ulid.ToString().Substring(0, 16);
        return $"AG_{DateTime.UtcNow:yyyyMMddHHmmss}_{shortRef}";
    }

    public static string GenerateTransactionId()
    {
        return GenerateMpesaReceiptNumber();
    }
}

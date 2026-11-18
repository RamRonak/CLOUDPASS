using System.Security.Cryptography;
using System.Text;
using CloudPass.Application.Interfaces;
using CloudPass.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudPass.Infrastructure.Services;

public class TicketSecurityService : ITicketSecurityService
{
    private readonly byte[] _ticketSigningKey;
    private readonly byte[] _paymentKeySecret;
    private readonly byte[] _webhookSecret;

    public TicketSecurityService(
        IOptions<SecurityOptions> securityOptions,
        IOptions<PaymentGatewayOptions> paymentOptions,
        ILogger<TicketSecurityService> logger)
    {
        var signingSecret = securityOptions.Value.TicketSigningSecret;
        if (string.IsNullOrWhiteSpace(signingSecret))
        {
            signingSecret = Environment.GetEnvironmentVariable("CLOUDPASS_TICKET_SIGNING_SECRET")
                ?? "dev-ephemeral-signing-key-configure-via-env-or-keyvault";
            logger.LogWarning("Security:TicketSigningSecret not explicitly configured; using development fallback. Set CLOUDPASS_TICKET_SIGNING_SECRET in production.");
        }

        var paySecret = paymentOptions.Value.KeySecret;
        if (string.IsNullOrWhiteSpace(paySecret))
        {
            paySecret = Environment.GetEnvironmentVariable("CLOUDPASS_PAYMENT_KEY_SECRET")
                ?? "dev-razorpay-test-secret-configure-via-env";
        }

        var whSecret = paymentOptions.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(whSecret))
        {
            whSecret = Environment.GetEnvironmentVariable("CLOUDPASS_PAYMENT_WEBHOOK_SECRET")
                ?? "dev-razorpay-webhook-secret-configure-via-env";
        }

        _ticketSigningKey = Encoding.UTF8.GetBytes(signingSecret);
        _paymentKeySecret = Encoding.UTF8.GetBytes(paySecret);
        _webhookSecret = Encoding.UTF8.GetBytes(whSecret);
    }

    public (string FullToken, string Nonce, string Signature, string TokenHash) GenerateSignedTicketToken(string bookingReference)
        => GenerateToken("TKT", bookingReference);

    public (string FullToken, string Nonce, string Signature, string TokenHash) GenerateSignedPassToken(string passNumber)
        => GenerateToken("PAS", passNumber);

    private (string FullToken, string Nonce, string Signature, string TokenHash) GenerateToken(string type, string reference)
    {
        var nonceBytes = RandomNumberGenerator.GetBytes(16);
        var nonce = Base64UrlEncode(nonceBytes);
        var payload = $"CP1.{type}.{reference}.{nonce}";
        var signature = ComputeHmacBase64Url(_ticketSigningKey, payload);
        var fullToken = $"{payload}.{signature}";
        var hash = ComputeSha256Hex(fullToken);
        return (fullToken, nonce, signature, hash);
    }

    public bool ValidateTokenStructureAndSignature(
        string token,
        out string tokenType,
        out string reference,
        out string nonce,
        out string signature)
    {
        tokenType = string.Empty;
        reference = string.Empty;
        nonce = string.Empty;
        signature = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        var trimmed = token.Trim();
        // Support full verification URLs scanned from QR code (e.g., https://.../Ticket/Verify?token=CP1...)
        if (trimmed.Contains("token=", StringComparison.OrdinalIgnoreCase))
        {
            var idx = trimmed.IndexOf("token=", StringComparison.OrdinalIgnoreCase);
            trimmed = Uri.UnescapeDataString(trimmed[(idx + 6)..].Split('&')[0]);
        }

        var parts = trimmed.Split('.');
        if (parts.Length != 5 || parts[0] != "CP1")
            return false;

        tokenType = parts[1];
        reference = parts[2];
        nonce = parts[3];
        signature = parts[4];

        if (tokenType is not ("TKT" or "PAS") || string.IsNullOrWhiteSpace(reference) || nonce.Length < 12)
            return false;

        var payload = $"CP1.{tokenType}.{reference}.{nonce}";
        var expectedSignature = ComputeHmacBase64Url(_ticketSigningKey, payload);

        var providedBytes = Encoding.UTF8.GetBytes(signature);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);

        return providedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    public string ComputeSha256Hex(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string GeneratePaymentSignature(string gatewayOrderId, string gatewayPaymentId)
    {
        var payload = $"{gatewayOrderId}|{gatewayPaymentId}";
        using var hmac = new HMACSHA256(_paymentKeySecret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool VerifyPaymentSignature(string gatewayOrderId, string gatewayPaymentId, string providedSignature)
    {
        if (string.IsNullOrWhiteSpace(gatewayOrderId) ||
            string.IsNullOrWhiteSpace(gatewayPaymentId) ||
            string.IsNullOrWhiteSpace(providedSignature))
            return false;

        var expected = GeneratePaymentSignature(gatewayOrderId, gatewayPaymentId);
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(providedSignature.Trim().ToLowerInvariant());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public string GenerateWebhookSignature(string rawPayload)
    {
        using var hmac = new HMACSHA256(_webhookSecret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawPayload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool VerifyWebhookSignature(string rawPayload, string providedSignature)
    {
        if (string.IsNullOrWhiteSpace(rawPayload) || string.IsNullOrWhiteSpace(providedSignature))
            return false;

        var expected = GenerateWebhookSignature(rawPayload);
        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(providedSignature.Trim().ToLowerInvariant());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string ComputeHmacBase64Url(byte[] key, string payload)
    {
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

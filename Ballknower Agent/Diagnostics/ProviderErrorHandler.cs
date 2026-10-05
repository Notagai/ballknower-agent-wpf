using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ballknower.Diagnostics;

public static class ProviderErrorHandler
{
    public static AiProviderException CreateException(
        string provider,
        HttpResponseMessage response,
        string responseBody)
    {
        var statusCode =
            (int)response.StatusCode;

        var retryAfter =
            GetRetryAfter(
                response,
                responseBody);

        string message =
            response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests =>
                    FormatRateLimit(retryAfter),

                HttpStatusCode.Unauthorized =>
                    "Authentication failed. Check your API key in Settings.",

                HttpStatusCode.Forbidden =>
                    "The AI provider denied this request. " +
                    "Check your account permissions or model access.",

                HttpStatusCode.NotFound =>
                    "The requested model or endpoint was not found. " +
                    "Check the model ID in Settings.",

                HttpStatusCode.RequestTimeout =>
                    "The AI provider timed out. Please try again.",

                HttpStatusCode.BadGateway or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout =>
                    "The AI provider is temporarily unavailable. " +
                    "Please try again later.",

                _ when statusCode >= 500 =>
                    "The AI provider encountered a server error. " +
                    "Please try again later.",

                _ =>
                    $"The AI provider returned HTTP {statusCode}." +
                    GetProviderErrorDetails(responseBody)
            };

        return new AiProviderException(
            provider,
            statusCode,
            message,
            retryAfter);
    }

    private static string FormatRateLimit(
        TimeSpan? retryAfter)
    {
        if (retryAfter is null)
        {
            return "Rate limit reached. Please try again shortly.";
        }

        var seconds = Math.Max(
            1,
            (int)Math.Ceiling(
                retryAfter.Value.TotalSeconds));

        if (seconds < 60)
        {
            return
                $"Rate limit reached. Try again in approximately " +
                $"{seconds} seconds.";
        }

        var minutes =
            (int)Math.Ceiling(seconds / 60.0);

        return
            $"Rate limit reached. Try again in approximately " +
            $"{minutes} minute(s).";
    }

    private static string GetProviderErrorDetails(
        string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return string.Empty;

        try
        {
            using var document =
                JsonDocument.Parse(responseBody);

            if (document.RootElement.TryGetProperty(
                    "error",
                    out var error))
            {
                if (error.TryGetProperty(
                        "message",
                        out var messageElement) &&
                    messageElement.ValueKind == JsonValueKind.String)
                {
                    var message =
                        messageElement.GetString();

                    if (!string.IsNullOrWhiteSpace(message))
                        return $" Details: {message}";
                }

                if (error.ValueKind == JsonValueKind.String)
                {
                    var message =
                        error.GetString();

                    if (!string.IsNullOrWhiteSpace(message))
                        return $" Details: {message}";
                }
            }
        }
        catch (JsonException)
        {
            // The provider may return plain text instead of JSON.
        }

        return responseBody.Length <= 500
            ? $" Details: {responseBody.Trim()}"
            : $" Details: {responseBody[..500].Trim()}";
    }

    private static TimeSpan? GetRetryAfter(
        HttpResponseMessage response,
        string responseBody)
    {
        var retryHeader =
            response.Headers.RetryAfter;

        if (retryHeader?.Delta is TimeSpan delta)
        {
            return delta > TimeSpan.Zero
                ? delta
                : TimeSpan.Zero;
        }

        if (retryHeader?.Date is DateTimeOffset date)
        {
            var remaining =
                date - DateTimeOffset.UtcNow;

            return remaining > TimeSpan.Zero
                ? remaining
                : TimeSpan.Zero;
        }

        try
        {
            using var document =
                JsonDocument.Parse(responseBody);

            if (document.RootElement.TryGetProperty(
                    "error",
                    out var error) &&
                error.TryGetProperty(
                    "message",
                    out var messageElement))
            {
                var message =
                    messageElement.GetString() ?? "";

                var match = Regex.Match(
                    message,
                    @"try again in\s+(\d+(?:\.\d+)?)s",
                    RegexOptions.IgnoreCase);

                if (match.Success &&
                    double.TryParse(
                        match.Groups[1].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var seconds))
                {
                    return TimeSpan.FromSeconds(
                        seconds);
                }
            }
        }
        catch (JsonException)
        {
            // The provider may return plain text instead of JSON.
        }

        return null;
    }
}

using System;

namespace Ballknower.Diagnostics;

public sealed class AiProviderException : Exception
{
    public string Provider { get; }

    public int StatusCode { get; }

    public TimeSpan? RetryAfter { get; }

    public AiProviderException(
        string provider,
        int statusCode,
        string message,
        TimeSpan? retryAfter = null)
        : base(message)
    {
        Provider = provider;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }
}
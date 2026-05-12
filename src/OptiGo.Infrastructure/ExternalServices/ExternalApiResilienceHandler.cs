using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;

namespace OptiGo.Infrastructure.ExternalServices;

public class ExternalApiResilienceHandler : DelegatingHandler
{
    private static readonly ConcurrentDictionary<string, CircuitState> Circuits = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(30);
    private const int FailureThreshold = 5;
    private const int MaxAttempts = 3;

    private readonly ILogger<ExternalApiResilienceHandler> _logger;

    public ExternalApiResilienceHandler(ILogger<ExternalApiResilienceHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? "unknown";
        var circuit = Circuits.GetOrAdd(host, _ => new CircuitState());
        if (circuit.IsOpen)
        {
            throw new HttpRequestException($"Circuit breaker is open for {host}.");
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var attemptRequest = await CloneRequestAsync(request, cancellationToken);
                var response = await base.SendAsync(attemptRequest, cancellationToken);
                if (!IsTransient(response.StatusCode))
                {
                    circuit.RecordSuccess();
                    return response;
                }

                if (attempt == MaxAttempts)
                {
                    circuit.RecordFailure();
                    return response;
                }

                response.Dispose();
            }
            catch (Exception) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                circuit.RecordFailure();
            }

            var delay = TimeSpan.FromMilliseconds(120 * Math.Pow(2, attempt - 1));
            _logger.LogWarning("Retrying external API call to {Host}. Attempt {Attempt}/{MaxAttempts}", host, attempt + 1, MaxAttempts);
            await Task.Delay(delay, cancellationToken);
        }

        throw new HttpRequestException($"External API call to {host} failed after retries.");
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == (HttpStatusCode)429 ||
        (int)statusCode >= 500;

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        foreach (var option in request.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);

        if (request.Content != null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync(ct);
            clone.Content = new ByteArrayContent(contentBytes);
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private sealed class CircuitState
    {
        private int _consecutiveFailures;
        private DateTimeOffset _openedUntil;

        public bool IsOpen => DateTimeOffset.UtcNow < _openedUntil;

        public void RecordSuccess()
        {
            _consecutiveFailures = 0;
            _openedUntil = default;
        }

        public void RecordFailure()
        {
            if (Interlocked.Increment(ref _consecutiveFailures) >= FailureThreshold)
            {
                _openedUntil = DateTimeOffset.UtcNow.Add(BreakDuration);
            }
        }
    }
}

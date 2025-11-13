using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapPost("/payment", PaymentHandler)
   .AddEndpointFilter<IdempotencyFilter>();

app.Run();

async Task<IResult> PaymentHandler(PaymentRequest body)
{
    await Task.Delay(2000);

    var response = new PaymentResponse
    {
        Message = "Payment processed successfully",
        Order = Guid.NewGuid().ToString("N")[..8],
        Amount = body.Amount,
        Date = DateTime.UtcNow
    };

    return Results.Ok(response);
}

record PaymentRequest(decimal Amount);
record PaymentResponse
{
    public string? Message { get; init; }
    public string? Order { get; init; }
    public decimal Amount { get; init; }
    public DateTime Date { get; init; }
}

internal sealed class IdempotencyFilter(int cacheTimeInMinutes = 15) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var keyHeader)
            || string.IsNullOrWhiteSpace(keyHeader))
        {
            return Results.BadRequest(new { error = "Idempotency-Key header is required" });
        }

        var idempotencyKey = keyHeader.ToString();

        var body = context.Arguments.FirstOrDefault();
        var bodyHash = ComputeHash(body);

        var cache = httpContext.RequestServices.GetRequiredService<IMemoryCache>();

        string cacheKey = $"idempotent_{idempotencyKey}";

        if (cache.TryGetValue(cacheKey, out CachedResponse? cached))
        {
            if (cached?.BodyHash != bodyHash)
                return Results.BadRequest(new { error = "Request body does not match the original for this Idempotency-Key" });

            return Results.Json(cached.Body, statusCode: cached.StatusCode);
        }

        var lockKey = $"lock_{idempotencyKey}";
        if (cache.TryGetValue(lockKey, out _))
            return Results.Json(new { error = "Request is already in progress" }, statusCode: 409);

        try
        {
            var result = await next(context);

            if (result is IStatusCodeHttpResult statusCodeResult && result is IValueHttpResult valueResult)
            {
                int statusCode = statusCodeResult.StatusCode ?? StatusCodes.Status200OK;

                var cachedResponse = new CachedResponse
                {
                    StatusCode = statusCode,
                    Body = valueResult.Value,
                    BodyHash = bodyHash
                };

                cache.Set(cacheKey, cachedResponse, TimeSpan.FromMinutes(cacheTimeInMinutes));
            }

            return result;
        }
        finally
        {
            cache.Remove(lockKey);
        }
    }

    private static string ComputeHash(object? obj)
    {
        if (obj is null) return string.Empty;

        var json = JsonSerializer.Serialize(obj);
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private record CachedResponse
    {
        public int StatusCode { get; init; }
        public object? Body { get; init; }
        public string? BodyHash { get; init; }
    }
}

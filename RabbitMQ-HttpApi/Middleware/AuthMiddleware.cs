using Microsoft.Extensions.Options;
using System.Net;
using RabbitMqApi.Configuration;

namespace RabbitMqApi.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _expectedToken;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(RequestDelegate next, IOptions<ApiConfig> apiConfig, ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _expectedToken = $"Bearer {apiConfig.Value.AuthToken}";
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeaderValue))
        {
            _logger.LogWarning("Authorization header missing.");
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsync("Authorization header is missing.");
            return;
        }

        if (authHeaderValue.ToString() != _expectedToken)
        {
            _logger.LogWarning("Invalid token provided. Expected: '{ExpectedToken}', Received: '{ReceivedToken}'", _expectedToken, authHeaderValue.ToString());
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await context.Response.WriteAsync("Invalid token.");
            return;
        }

        await _next(context);
    }
}
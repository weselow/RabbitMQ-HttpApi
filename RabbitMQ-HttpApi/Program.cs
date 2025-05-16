using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using RabbitMqApi.Configuration;
using RabbitMqApi.Middleware;
using RabbitMqApi.Services;
using System.Net.Mime;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Services
// Load configurations
builder.Services.Configure<RabbitMqConfig>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.Configure<ApiConfig>(builder.Configuration.GetSection("Api"));

// Register RabbitService as Singleton
builder.Services.AddSingleton<RabbitService>();

// Add logging
builder.Services.AddLogging(configure => configure.AddConsole());

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "RabbitMQ Minimal API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter a valid token",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "Token", // Можно просто "JWT" или "Token"
        Scheme = "bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            []
        }
    });
});

var app = builder.Build();

// 2. Configure HTTP request pipeline
var logger = app.Services.GetRequiredService<ILogger<Program>>(); // Получаем логгер

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "RabbitMQ API V1");
        c.RoutePrefix = "swagger"; // Доступ к Swagger UI по /swagger
    });
    logger.LogInformation("Swagger UI enabled at /swagger");
}

//app.UseHttpsRedirection();

// Use custom authentication middleware for all endpoints
app.UseMiddleware<AuthMiddleware>();


// 3. Define Endpoints

// GET /get/{queue}
app.MapGet("/get/{queue}", async (
    string queue,
    HttpContext httpContext,
    RabbitService rabbitService,
    ILogger<Program> endpointLogger) =>
{
    endpointLogger.LogInformation("GET /get/{Queue} called.", queue);
    try
    {
        var (body, deliveryTag, messageFound) = rabbitService.GetMessage(queue);

        if (!messageFound)
        {
            endpointLogger.LogInformation("No message in queue '{Queue}'. Returning 204 No Content.", queue);
            return Results.NoContent();
        }

        string responseBody = Encoding.UTF8.GetString(body!);
        string contentType = MediaTypeNames.Text.Plain; // Default to text/plain

        var acceptHeader = httpContext.Request.Headers["Accept"].ToString();
        if (acceptHeader.Contains(MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase))
        {
            contentType = MediaTypeNames.Application.Json;
            // Если мы хотим гарантировать JSON, можно обернуть:
            // responseBody = JsonSerializer.Serialize(new { message = responseBody });
            // Но по задаче: "Поддерживает Accept: application/json и text/plain"
            // Это означает, что мы можем отдать контент в указанном формате.
            // Если в очереди лежит текст, а клиент просит JSON, он получит текст с Content-Type: application/json.
            // Клиент должен быть готов к этому или данные в очереди должны соответствовать.
        }

        // Важно: сначала отправить ответ клиенту, потом подтвердить сообщение
        // Для этого мы не можем использовать Results.Text или Results.Content напрямую, если они закрывают соединение
        // или не позволяют выполнить код после.
        // Вместо этого запишем в Response вручную.
        httpContext.Response.ContentType = contentType;
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        await httpContext.Response.WriteAsync(responseBody, Encoding.UTF8);

        // После успешной отправки клиенту
        rabbitService.AckMessage(deliveryTag);
        endpointLogger.LogInformation("Message from queue '{Queue}' sent to client and ACKed.", queue);

        // Results.Ok() или другие Results.* уже отправили бы ответ и завершили бы его.
        // Поскольку мы уже записали ответ, возвращаем Empty чтобы не было конфликта.
        return Results.Empty;
    }
    catch (Exception ex)
    {
        endpointLogger.LogError(ex, "Error processing GET /get/{Queue}", queue);
        return Results.Problem("An error occurred while processing your request.", statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName($"GetMessageFromQueue")
.WithOpenApi(operation => new(operation)
{
    Summary = "Получает одно сообщение из указанной очереди RabbitMQ.",
    Description = "Использует BasicGet (autoAck: false). После успешной отправки клиенту вызывает BasicAck. Если очередь пуста, возвращает 204 No Content.",
    Parameters = { new OpenApiParameter { Name = "queue", In = ParameterLocation.Path, Required = true, Description = "Имя очереди RabbitMQ." } }
})
.Produces(StatusCodes.Status200OK, typeof(string), MediaTypeNames.Text.Plain, MediaTypeNames.Application.Json)
.Produces(StatusCodes.Status204NoContent)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status500InternalServerError);


// POST /add/{queue}
app.MapPost("/add/{queue}", async (
    string queue,
    HttpContext httpContext,
    RabbitService rabbitService,
    ILogger<Program> endpointLogger) =>
{
    endpointLogger.LogInformation("POST /add/{Queue} called.", queue);
    string requestBody;
    using (var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8))
    {
        requestBody = await reader.ReadToEndAsync();
    }

    if (string.IsNullOrEmpty(requestBody))
    {
        endpointLogger.LogWarning("Request body is empty for POST /add/{Queue}", queue);
        return Results.BadRequest("Request body cannot be empty.");
    }

    string? messageContentType = httpContext.Request.ContentType;

    if (string.Equals(messageContentType, MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            // Проверяем, является ли строка валидным JSON
            using var jsonDoc = JsonDocument.Parse(requestBody);
            // Если мы дошли сюда, JSON валиден. Отправляем его как строку.
        }
        catch (JsonException jsonEx)
        {
            endpointLogger.LogWarning(jsonEx, "Invalid JSON format in request body for POST /add/{Queue}", queue);
            return Results.BadRequest("Invalid JSON format in request body.");
        }
    }
    // Для text/plain или других типов контента дополнительной валидации не требуется, передаем как есть.

    bool success = rabbitService.PublishMessage(queue, requestBody, messageContentType);

    if (success)
    {
        endpointLogger.LogInformation("Message successfully published to queue '{Queue}'", queue);
        return Results.Ok("Message published successfully.");
    }
    else
    {
        endpointLogger.LogError("Failed to publish message to queue '{Queue}'", queue);
        // Ошибка уже залогирована в RabbitService
        return Results.Problem("Failed to publish message.", statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("AddMessageToQueue")
.WithOpenApi(operation => new(operation)
{
    Summary = "Отправляет сообщение в указанную очередь RabbitMQ.",
    Description = "Принимает тело запроса (application/json или text/plain). Публикация с delivery_mode = 2 (persistent).",
    Parameters = { new OpenApiParameter { Name = "queue", In = ParameterLocation.Path, Required = true, Description = "Имя очереди RabbitMQ." } },
    RequestBody = new OpenApiRequestBody
    {
        Description = "Тело сообщения для отправки в очередь.",
        Required = true,
        Content = new Dictionary<string, OpenApiMediaType>
        {
            [MediaTypeNames.Application.Json] = new OpenApiMediaType { Schema = new OpenApiSchema { Type = "object" } }, // или string
            [MediaTypeNames.Text.Plain] = new OpenApiMediaType { Schema = new OpenApiSchema { Type = "string" } }
        }
    }
})
.Accepts<object>(MediaTypeNames.Application.Json, MediaTypeNames.Text.Plain) // Указываем, что эндпоинт принимает эти типы
.Produces(StatusCodes.Status200OK, typeof(string))
.Produces(StatusCodes.Status400BadRequest, typeof(string))
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status500InternalServerError);


// 4. Run application
var apiConfig = app.Services.GetRequiredService<IOptions<ApiConfig>>().Value;
var listenUrl = $"http://*:{apiConfig.Port}"; // Слушаем на всех интерфейсах
logger.LogInformation("Application starting. Listening on: {ListenUrl}", listenUrl);

app.Run(listenUrl); // Используем порт из конфигурации
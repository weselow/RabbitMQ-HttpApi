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
        BearerFormat = "Token", // ����� ������ "JWT" ��� "Token"
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
var logger = app.Services.GetRequiredService<ILogger<Program>>(); // �������� ������

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "RabbitMQ API V1");
        c.RoutePrefix = "swagger"; // ������ � Swagger UI �� /swagger
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
            // ���� �� ����� ������������� JSON, ����� ��������:
            // responseBody = JsonSerializer.Serialize(new { message = responseBody });
            // �� �� ������: "������������ Accept: application/json � text/plain"
            // ��� ��������, ��� �� ����� ������ ������� � ��������� �������.
            // ���� � ������� ����� �����, � ������ ������ JSON, �� ������� ����� � Content-Type: application/json.
            // ������ ������ ���� ����� � ����� ��� ������ � ������� ������ ���������������.
        }

        // �����: ������� ��������� ����� �������, ����� ����������� ���������
        // ��� ����� �� �� ����� ������������ Results.Text ��� Results.Content ��������, ���� ��� ��������� ����������
        // ��� �� ��������� ��������� ��� �����.
        // ������ ����� ������� � Response �������.
        httpContext.Response.ContentType = contentType;
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        await httpContext.Response.WriteAsync(responseBody, Encoding.UTF8);

        // ����� �������� �������� �������
        rabbitService.AckMessage(deliveryTag);
        endpointLogger.LogInformation("Message from queue '{Queue}' sent to client and ACKed.", queue);

        // Results.Ok() ��� ������ Results.* ��� ��������� �� ����� � ��������� �� ���.
        // ��������� �� ��� �������� �����, ���������� Empty ����� �� ���� ���������.
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
    Summary = "�������� ���� ��������� �� ��������� ������� RabbitMQ.",
    Description = "���������� BasicGet (autoAck: false). ����� �������� �������� ������� �������� BasicAck. ���� ������� �����, ���������� 204 No Content.",
    Parameters = { new OpenApiParameter { Name = "queue", In = ParameterLocation.Path, Required = true, Description = "��� ������� RabbitMQ." } }
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
            // ���������, �������� �� ������ �������� JSON
            using var jsonDoc = JsonDocument.Parse(requestBody);
            // ���� �� ����� ����, JSON �������. ���������� ��� ��� ������.
        }
        catch (JsonException jsonEx)
        {
            endpointLogger.LogWarning(jsonEx, "Invalid JSON format in request body for POST /add/{Queue}", queue);
            return Results.BadRequest("Invalid JSON format in request body.");
        }
    }
    // ��� text/plain ��� ������ ����� �������� �������������� ��������� �� ���������, �������� ��� ����.

    bool success = rabbitService.PublishMessage(queue, requestBody, messageContentType);

    if (success)
    {
        endpointLogger.LogInformation("Message successfully published to queue '{Queue}'", queue);
        return Results.Ok("Message published successfully.");
    }
    else
    {
        endpointLogger.LogError("Failed to publish message to queue '{Queue}'", queue);
        // ������ ��� ������������ � RabbitService
        return Results.Problem("Failed to publish message.", statusCode: StatusCodes.Status500InternalServerError);
    }
})
.WithName("AddMessageToQueue")
.WithOpenApi(operation => new(operation)
{
    Summary = "���������� ��������� � ��������� ������� RabbitMQ.",
    Description = "��������� ���� ������� (application/json ��� text/plain). ���������� � delivery_mode = 2 (persistent).",
    Parameters = { new OpenApiParameter { Name = "queue", In = ParameterLocation.Path, Required = true, Description = "��� ������� RabbitMQ." } },
    RequestBody = new OpenApiRequestBody
    {
        Description = "���� ��������� ��� �������� � �������.",
        Required = true,
        Content = new Dictionary<string, OpenApiMediaType>
        {
            [MediaTypeNames.Application.Json] = new OpenApiMediaType { Schema = new OpenApiSchema { Type = "object" } }, // ��� string
            [MediaTypeNames.Text.Plain] = new OpenApiMediaType { Schema = new OpenApiSchema { Type = "string" } }
        }
    }
})
.Accepts<object>(MediaTypeNames.Application.Json, MediaTypeNames.Text.Plain) // ���������, ��� �������� ��������� ��� ����
.Produces(StatusCodes.Status200OK, typeof(string))
.Produces(StatusCodes.Status400BadRequest, typeof(string))
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status500InternalServerError);


// 4. Run application
var apiConfig = app.Services.GetRequiredService<IOptions<ApiConfig>>().Value;
var listenUrl = $"http://*:{apiConfig.Port}"; // ������� �� ���� �����������
logger.LogInformation("Application starting. Listening on: {ListenUrl}", listenUrl);

app.Run(listenUrl); // ���������� ���� �� ������������
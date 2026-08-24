using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Checkin.Models;
using Checkin.Services;
using Checkin.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// OpenTelemetry Configuration
// -----------------------------------------------------------------------------
// Tanto GitHub Actions (--set-env-vars) como Cloud Run pueden dejar una variable
// de entorno "definida pero vacía" en lugar de ausente (ej. si el secret aún no
// existe). Environment.GetEnvironmentVariable("") no es null, así que el operador
// ?? no la descarta — hay que tratar explícitamente la cadena vacía/whitespace
// como "no configurado" para no romper el arranque del contenedor.
static string ResolveOtlpEndpoint(string? envValue, string? configValue) =>
    !string.IsNullOrWhiteSpace(envValue) ? envValue :
    !string.IsNullOrWhiteSpace(configValue) ? configValue :
    "http://localhost:4317";

// -----------------------------------------------------------------------------
// Feature Flag: OBSERVABILITY_ENABLED (para benchmark de overhead - Fase 4)
// -----------------------------------------------------------------------------
// Permite desactivar POR COMPLETO el pipeline de OpenTelemetry (tracing,
// métricas y logging exporter) en runtime, sin rebuild de la imagen, para
// comparar el servicio "sin instrumentación" vs "con instrumentación".
// Default: true (instrumentación activa). Establecer OBSERVABILITY_ENABLED=false
// para el escenario baseline del benchmark.
//
// Nota: aun con la bandera en true, el SDK de OpenTelemetry también respeta la
// variable estándar OTEL_SDK_DISABLED=true (spec oficial), que pone el SDK en
// modo no-op de exportación mientras deja los instrumentadores registrados.
// Úsala si sólo quieres aislar el overhead de exportación/red, en lugar del
// overhead total de instrumentación.
static bool ResolveObservabilityEnabled(string? envValue) =>
    !string.Equals(envValue, "false", StringComparison.OrdinalIgnoreCase);

var observabilityEnabled = ResolveObservabilityEnabled(
    Environment.GetEnvironmentVariable("OBSERVABILITY_ENABLED"));

if (observabilityEnabled)
{
    var otlpEndpoint = ResolveOtlpEndpoint(
        Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"),
        builder.Configuration["OpenTelemetry:OtlpEndpoint"]);

    var resourceBuilder = ResourceBuilder.CreateDefault()
        .AddService(serviceName: Diagnostics.ServiceName, serviceVersion: Diagnostics.ServiceVersion);

    // 1. Configure OpenTelemetry Tracing
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing
                .SetResourceBuilder(resourceBuilder)
                .AddSource(Diagnostics.ServiceName)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.RecordException = true;
                })
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OtlpExportProtocol.Grpc;
                });
        })
        // 2. Configure OpenTelemetry Metrics
        .WithMetrics(metrics =>
        {
            metrics
                .SetResourceBuilder(resourceBuilder)
                .AddMeter(Diagnostics.ServiceName)
                .AddMeter("Microsoft.AspNetCore.Hosting")
                .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                .AddMeter("System.Net.Http")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter((options, readerOptions) =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OtlpExportProtocol.Grpc;
                    readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 2000;
                });
        });

    // 3. Configure OpenTelemetry Logging
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.FormatterName = OtelJsonConsoleFormatter.FormatterName)
        .AddConsoleFormatter<OtelJsonConsoleFormatter, ConsoleFormatterOptions>();
    builder.Logging.AddOpenTelemetry(logging =>
    {
        logging.SetResourceBuilder(resourceBuilder);
        logging.IncludeScopes = true;
        logging.IncludeFormattedMessage = true;
        logging.AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otlpEndpoint);
            options.Protocol = OtlpExportProtocol.Grpc;
        });
    });
}
else
{
    // Baseline sin instrumentación: sólo logging de consola estándar,
    // sin ActivitySource/Meter listeners ni exportador OTLP registrados.
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options =>
    {
        options.IncludeScopes = false;
        options.SingleLine = true;
    });
    Console.WriteLine("[Startup] OBSERVABILITY_ENABLED=false -> OpenTelemetry pipeline disabled (baseline mode for overhead benchmarking).");
}

// -----------------------------------------------------------------------------
// HttpClient & Couchbase Service Registration
// -----------------------------------------------------------------------------
var passengersUrl = builder.Configuration["Services:PassengersUrl"] ?? "http://localhost:5000";

builder.Services.AddHttpClient("PassengersClient", client =>
{
    client.BaseAddress = new Uri(passengersUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton<ICouchbaseService, CouchbaseService>();

var app = builder.Build();

// -----------------------------------------------------------------------------
// Middleware: Structured Logging & Correlation ID Propagation
// -----------------------------------------------------------------------------
app.Use(async (context, next) =>
{
    const string CorrelationHeader = "X-Correlation-ID";
    var correlationId = context.Request.Headers[CorrelationHeader].FirstOrDefault() 
                        ?? Activity.Current?.TraceId.ToString() 
                        ?? Guid.NewGuid().ToString();

    context.Response.Headers[CorrelationHeader] = correlationId;

    // Attach correlation ID to active activity span
    Activity.Current?.SetTag("correlation_id", correlationId);

    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    using (logger.BeginScope(new Dictionary<string, object>
    {
        ["CorrelationId"] = correlationId,
        ["TraceId"] = Activity.Current?.TraceId.ToString() ?? string.Empty
    }))
    {
        await next();
    }
});

// -----------------------------------------------------------------------------
// API Endpoints
// -----------------------------------------------------------------------------

/// <summary>
/// Health check endpoint.
/// </summary>
app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = Diagnostics.ServiceName,
    timestamp = DateTime.UtcNow
}));

/// <summary>
/// POST /checkin - Process passenger check-in.
/// Validates passenger with Passengers service via HTTP and stores record in Couchbase.
/// Supports simulation parameters: ?delay=ms and ?error=true.
/// </summary>
app.MapPost("/checkin", async (
    [FromBody] CheckinRequest request,
    [FromQuery] int? delay,
    [FromQuery] bool? error,
    IHttpClientFactory httpClientFactory,
    ICouchbaseService couchbaseService,
    HttpContext httpContext,
    ILogger<Program> logger) =>
{
    using var activity = Diagnostics.ActivitySource.StartActivity("ProcessCheckin");
    activity?.SetTag("passenger.id", request.PassengerId);
    activity?.SetTag("flight.number", request.FlightNumber);
    activity?.SetTag("seat.number", request.SeatNumber);

    Diagnostics.CheckinRequestCounter.Add(1);

    // Simulate artificial delay if requested
    if (delay.HasValue && delay.Value > 0)
    {
        activity?.SetTag("simulation.delay_ms", delay.Value);
        Diagnostics.CheckinDelayHistogram.Record(delay.Value);
        logger.LogInformation("Simulating artificial delay of {Delay} ms for POST /checkin", delay.Value);
        await Task.Delay(delay.Value);
    }

    // Simulate forced error if requested
    if (error == true)
    {
        activity?.SetTag("simulation.forced_error", true);
        activity?.SetStatus(ActivityStatusCode.Error, "Forced error requested via query parameter");
        Diagnostics.CheckinErrorCounter.Add(1, new KeyValuePair<string, object?>("reason", "forced_error"));
        logger.LogError("Forced error triggered for POST /checkin");
        return Results.Problem(
            detail: "Forced error simulated as requested by parameter error=true.",
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Simulated Internal Server Error");
    }

    // 1. Call Passengers microservice via HTTP to validate passenger
    var client = httpClientFactory.CreateClient("PassengersClient");
    
    // Forward Correlation ID header if available
    var correlationId = httpContext.Response.Headers["X-Correlation-ID"].FirstOrDefault();
    if (!string.IsNullOrEmpty(correlationId))
    {
        client.DefaultRequestHeaders.Remove("X-Correlation-ID");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
    }

    logger.LogInformation("Validating passenger {PassengerId} via Passengers service...", request.PassengerId);
    
    using var validationActivity = Diagnostics.ActivitySource.StartActivity("ValidatePassengerHttp");
    HttpResponseMessage response;
    try
    {
        response = await client.GetAsync($"/passengers/{Uri.EscapeDataString(request.PassengerId)}");
    }
    catch (Exception ex)
    {
        validationActivity?.AddException(ex);
        validationActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        Diagnostics.CheckinErrorCounter.Add(1, new KeyValuePair<string, object?>("reason", "passengers_service_unreachable"));
        logger.LogError(ex, "Failed to reach Passengers microservice at {PassengersUrl}", passengersUrl);
        return Results.Problem(
            detail: $"Failed to connect to Passengers service: {ex.Message}",
            statusCode: StatusCodes.Status502BadGateway,
            title: "Bad Gateway");
    }

    if (response.StatusCode == HttpStatusCode.NotFound)
    {
        validationActivity?.SetStatus(ActivityStatusCode.Error, "Passenger not found");
        Diagnostics.CheckinErrorCounter.Add(1, new KeyValuePair<string, object?>("reason", "passenger_not_found"));
        logger.LogWarning("Validation failed: Passenger {PassengerId} does not exist in Passengers service", request.PassengerId);
        return Results.NotFound(new { message = $"Passenger '{request.PassengerId}' not found. Check-in rejected." });
    }

    if (!response.IsSuccessStatusCode)
    {
        validationActivity?.SetStatus(ActivityStatusCode.Error, $"Passengers service returned {response.StatusCode}");
        Diagnostics.CheckinErrorCounter.Add(1, new KeyValuePair<string, object?>("reason", "passengers_service_error"));
        logger.LogError("Validation error: Passengers service returned HTTP {StatusCode}", response.StatusCode);
        return Results.Problem(
            detail: $"Passengers service returned HTTP {response.StatusCode}",
            statusCode: StatusCodes.Status502BadGateway,
            title: "Upstream Validation Error");
    }

    var passengerInfo = await response.Content.ReadFromJsonAsync<PassengerDto>();
    logger.LogInformation("Passenger {PassengerId} ({FirstName} {LastName}) validated successfully", 
        request.PassengerId, passengerInfo?.FirstName, passengerInfo?.LastName);

    // 2. Persist Check-in record in Couchbase
    var checkinId = $"CHK-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
    activity?.SetTag("checkin.id", checkinId);

    var checkinRecord = new CheckinRecord
    {
        Id = checkinId,
        PassengerId = request.PassengerId,
        FlightNumber = request.FlightNumber,
        SeatNumber = request.SeatNumber,
        BaggageCount = request.BaggageCount,
        Status = "COMPLETED",
        CreatedAt = DateTime.UtcNow
    };

    using var couchbaseActivity = Diagnostics.ActivitySource.StartActivity("PersistCheckinCouchbase");
    try
    {
        await couchbaseService.SaveCheckinAsync(checkinRecord);
    }
    catch (Exception ex)
    {
        couchbaseActivity?.AddException(ex);
        couchbaseActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        Diagnostics.CheckinErrorCounter.Add(1, new KeyValuePair<string, object?>("reason", "couchbase_persistence_error"));
        logger.LogError(ex, "Error storing check-in record {CheckinId} in Couchbase", checkinId);
        return Results.Problem(
            detail: "Failed to persist check-in record to database.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    Diagnostics.CheckinSuccessCounter.Add(1);
    logger.LogInformation("Check-in process completed successfully. Checkin ID: {CheckinId}", checkinId);

    return Results.Created($"/checkin/{checkinId}", new
    {
        message = "Check-in completed successfully",
        checkinRecord,
        passenger = passengerInfo
    });
});

app.Run();

using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Console;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Passengers.Data;
using Passengers.Models;
using Passengers.Telemetry;

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
                .AddNpgsql()
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
// Database Configuration (PostgreSQL EF Core)
// -----------------------------------------------------------------------------
var postgresHost = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
var postgresUser = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";
var postgresPass = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";
var postgresDb = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "passengers_db";
var defaultConnStr = $"Host={postgresHost};Port=5432;Database={postgresDb};Username={postgresUser};Password={postgresPass}";

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? defaultConnStr;
builder.Services.AddDbContext<PassengerDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

var app = builder.Build();

// Auto-initialize Postgres database schema with retry logic
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<PassengerDbContext>();

    const int maxRetries = 10;
    var delay = TimeSpan.FromSeconds(3);

    for (int retry = 1; retry <= maxRetries; retry++)
    {
        try
        {
            logger.LogInformation("Attempting PostgreSQL schema initialization (Attempt {Retry}/{MaxRetries})...", retry, maxRetries);
            await dbContext.Database.EnsureCreatedAsync();
            logger.LogInformation("PostgreSQL database connection established and schema 'Passengers' initialized successfully.");
            break;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PostgreSQL database initialization attempt {Retry}/{MaxRetries} failed. Retrying in {Delay}s...", retry, maxRetries, delay.TotalSeconds);
            if (retry == maxRetries)
            {
                logger.LogError(ex, "Failed to initialize PostgreSQL database schema after {MaxRetries} retries.", maxRetries);
            }
            else
            {
                await Task.Delay(delay);
            }
        }
    }
}

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
/// GET /passengers/{id} - Retrieve passenger information from PostgreSQL database.
/// Supports simulation parameters: ?delay=ms and ?error=true.
/// </summary>
app.MapGet("/passengers/{id}", async (
    string id,
    [FromQuery] int? delay,
    [FromQuery] bool? error,
    PassengerDbContext dbContext,
    ILogger<Program> logger) =>
{
    using var activity = Diagnostics.ActivitySource.StartActivity("GetPassengerById");
    activity?.SetTag("passenger.id", id);

    // Simulate artificial delay if requested
    if (delay.HasValue && delay.Value > 0)
    {
        activity?.SetTag("simulation.delay_ms", delay.Value);
        Diagnostics.PassengerDelayHistogram.Record(delay.Value);
        logger.LogInformation("Simulating artificial delay of {Delay} ms for GET /passengers/{Id}", delay.Value, id);
        await Task.Delay(delay.Value);
    }

    // Simulate forced error if requested
    if (error == true)
    {
        activity?.SetTag("simulation.forced_error", true);
        activity?.SetStatus(ActivityStatusCode.Error, "Forced error requested via query parameter");
        Diagnostics.PassengerErrorCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "GET /passengers/{id}"));
        logger.LogError("Forced error triggered for GET /passengers/{Id}", id);
        return Results.Problem(
            detail: "Forced error simulated as requested by parameter error=true.",
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Simulated Internal Server Error");
    }

    Diagnostics.PassengerRetrievedCounter.Add(1);
    logger.LogInformation("Fetching passenger with ID {Id} from PostgreSQL", id);

    try
    {
        var passenger = await dbContext.Passengers.FindAsync(id);
        if (passenger is null)
        {
            activity?.SetTag("passenger.found", false);
            logger.LogWarning("Passenger with ID {Id} was not found", id);
            return Results.NotFound(new { message = $"Passenger '{id}' not found." });
        }

        activity?.SetTag("passenger.found", true);
        return Results.Ok(passenger);
    }
    catch (Exception ex)
    {
        activity?.AddException(ex);
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        Diagnostics.PassengerErrorCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "GET /passengers/{id}"));
        logger.LogError(ex, "Error occurred while fetching passenger {Id} from database", id);
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});

/// <summary>
/// POST /passengers - Create a new passenger record in PostgreSQL database.
/// Supports simulation parameters: ?delay=ms and ?error=true.
/// </summary>
app.MapPost("/passengers", async (
    [FromBody] CreatePassengerDto dto,
    [FromQuery] int? delay,
    [FromQuery] bool? error,
    PassengerDbContext dbContext,
    ILogger<Program> logger) =>
{
    using var activity = Diagnostics.ActivitySource.StartActivity("CreatePassenger");

    // Simulate artificial delay if requested
    if (delay.HasValue && delay.Value > 0)
    {
        activity?.SetTag("simulation.delay_ms", delay.Value);
        Diagnostics.PassengerDelayHistogram.Record(delay.Value);
        logger.LogInformation("Simulating artificial delay of {Delay} ms for POST /passengers", delay.Value);
        await Task.Delay(delay.Value);
    }

    // Simulate forced error if requested
    if (error == true)
    {
        activity?.SetTag("simulation.forced_error", true);
        activity?.SetStatus(ActivityStatusCode.Error, "Forced error requested via query parameter");
        Diagnostics.PassengerErrorCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "POST /passengers"));
        logger.LogError("Forced error triggered for POST /passengers");
        return Results.Problem(
            detail: "Forced error simulated as requested by parameter error=true.",
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Simulated Internal Server Error");
    }

    var passengerId = string.IsNullOrWhiteSpace(dto.Id) ? $"PAS-{Guid.NewGuid().ToString("N")[..8].ToUpper()}" : dto.Id;
    activity?.SetTag("passenger.id", passengerId);

    var passenger = new Passenger
    {
        Id = passengerId,
        FirstName = dto.FirstName,
        LastName = dto.LastName,
        Email = dto.Email,
        PassportNumber = dto.PassportNumber,
        CreatedAt = DateTime.UtcNow
    };

    try
    {
        logger.LogInformation("Saving new passenger with ID {Id} to PostgreSQL", passengerId);
        dbContext.Passengers.Add(passenger);
        await dbContext.SaveChangesAsync();

        Diagnostics.PassengerCreatedCounter.Add(1);
        logger.LogInformation("Passenger {Id} created successfully", passengerId);

        return Results.Created($"/passengers/{passengerId}", passenger);
    }
    catch (Exception ex)
    {
        activity?.AddException(ex);
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        Diagnostics.PassengerErrorCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "POST /passengers"));
        logger.LogError(ex, "Error occurred while saving passenger {Id} to database", passengerId);
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.Run();

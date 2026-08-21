using Couchbase;
using Couchbase.Management.Buckets;
using Checkin.Models;

namespace Checkin.Services;

/// <summary>
/// Service interface for Couchbase database operations.
/// </summary>
public interface ICouchbaseService
{
    /// <summary>
    /// Persists a check-in record document to Couchbase.
    /// </summary>
    Task SaveCheckinAsync(CheckinRecord record);
}

/// <summary>
/// Couchbase persistence implementation using Couchbase .NET SDK 3.x.
/// </summary>
public class CouchbaseService : ICouchbaseService, IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CouchbaseService> _logger;
    private ICluster? _cluster;
    private bool _bucketInitialized = false;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public CouchbaseService(IConfiguration configuration, ILogger<CouchbaseService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private async Task<ICluster?> GetClusterAsync()
    {
        if (_cluster != null) return _cluster;

        await _semaphore.WaitAsync();
        try
        {
            if (_cluster != null) return _cluster;

            var cbHost = Environment.GetEnvironmentVariable("COUCHBASE_HOST") ?? "localhost";
            var connStr = _configuration["Couchbase:ConnectionString"] ?? $"couchbase://{cbHost}";
            var username = _configuration["Couchbase:Username"] ?? Environment.GetEnvironmentVariable("COUCHBASE_USER") ?? "Administrator";
            var password = _configuration["Couchbase:Password"] ?? Environment.GetEnvironmentVariable("COUCHBASE_PASSWORD") ?? "password";

            _logger.LogInformation("Connecting to Couchbase cluster 'airline' at {ConnectionString}", connStr);
            _cluster = await Cluster.ConnectAsync(connStr, username, password);
            return _cluster;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to establish connection to Couchbase cluster. Ensure Couchbase server is reachable.");
            return null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task EnsureBucketExistsAsync(ICluster cluster, string bucketName)
    {
        if (_bucketInitialized) return;

        try
        {
            try
            {
                await cluster.Buckets.GetBucketAsync(bucketName);
                _logger.LogInformation("Couchbase bucket '{Bucket}' verified.", bucketName);
            }
            catch (BucketNotFoundException)
            {
                _logger.LogInformation("Couchbase bucket '{Bucket}' does not exist. Creating bucket automatically...", bucketName);
                await cluster.Buckets.CreateBucketAsync(new BucketSettings
                {
                    Name = bucketName,
                    BucketType = BucketType.Couchbase,
                    RamQuotaMB = 256
                });
                await Task.Delay(2000);
                _logger.LogInformation("Couchbase bucket '{Bucket}' created successfully.", bucketName);
            }
            _bucketInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bucket initialization check for '{Bucket}' produced warning. Proceeding to bucket access.", bucketName);
        }
    }

    public async Task SaveCheckinAsync(CheckinRecord record)
    {
        var bucketName = _configuration["Couchbase:BucketName"] ?? "checkin_bucket";
        try
        {
            var cluster = await GetClusterAsync();
            if (cluster is not null)
            {
                await EnsureBucketExistsAsync(cluster, bucketName);

                var bucket = await cluster.BucketAsync(bucketName);
                // Use default scope (_default) and default collection (_default)
                var scope = bucket.Scope("_default");
                var collection = scope.Collection("_default");

                await collection.InsertAsync(record.Id, record);
                _logger.LogInformation("Check-in document '{Id}' inserted into Couchbase bucket '{Bucket}' (_default scope, _default collection)", record.Id, bucketName);
            }
            else
            {
                _logger.LogWarning("Couchbase cluster unavailable. Check-in record '{Id}' skipped Couchbase write.", record.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert check-in document '{Id}' into Couchbase bucket '{Bucket}'", record.Id, bucketName);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.DisposeAsync();
        }
        _semaphore.Dispose();
    }
}

using DocumentReprocessor.Configuration;
using DocumentReprocessor.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Build configuration from appsettings.json next to the executable.
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

var settings = configuration.Get<AppSettings>()
    ?? throw new InvalidOperationException("Unable to load AppSettings from appsettings.json.");

ValidateSettings(settings);

var reportFolder = string.IsNullOrWhiteSpace(settings.Paths.ReportFolder)
    ? settings.Paths.LogFolder
    : settings.Paths.ReportFolder;

var httpTimeoutSeconds = Math.Max(30, settings.Processing.HttpTimeoutSeconds);

var services = new ServiceCollection();
services.AddSingleton(settings.Api);
services.AddSingleton(settings.Paths);
services.AddSingleton(settings.Processing);
services.AddSingleton(new JsonFileLogger(settings.Paths.LogFolder));
services.AddSingleton(new ExcelRunReportWriter(reportFolder));
services.AddHttpClient<DianApiClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(httpTimeoutSeconds);
});
services.AddTransient<DocumentProcessor>();

await using var provider = services.BuildServiceProvider();

Console.WriteLine("Document Reprocessor");
Console.WriteLine($"Endpoint       : {settings.Api.EndpointUrl}");
Console.WriteLine($"Content-Type   : {settings.Api.ContentType}");
Console.WriteLine($"Wrap JSON      : {settings.Api.WrapBodyAsJsonString}");
Console.WriteLine($"Parallelism    : {Math.Max(1, settings.Processing.MaxDegreeOfParallelism)}");
Console.WriteLine($"Retries        : {Math.Max(0, settings.Processing.MaxRetryAttempts)}");
Console.WriteLine($"HTTP timeout   : {httpTimeoutSeconds}s");
Console.WriteLine($"Input          : {settings.Paths.InputFolder}");
Console.WriteLine($"Processed      : {settings.Paths.ProcessedFolder}");
Console.WriteLine($"Logs           : {settings.Paths.LogFolder}");
Console.WriteLine($"Reports        : {reportFolder}");
Console.WriteLine();

var processor = provider.GetRequiredService<DocumentProcessor>();
var result = await processor.ProcessAllAsync();

Console.WriteLine();
Console.WriteLine($"Done. Processed: {result.TotalFiles}. Successful: {result.SuccessCount}.");
if (!string.IsNullOrWhiteSpace(result.ExcelReportPath))
{
    Console.WriteLine($"Excel report saved to: {result.ExcelReportPath}");
}

static void ValidateSettings(AppSettings settings)
{
    if (string.IsNullOrWhiteSpace(settings.Api.EndpointUrl))
    {
        throw new InvalidOperationException("Api:EndpointUrl is required in appsettings.json.");
    }

    if (string.IsNullOrWhiteSpace(settings.Api.BearerToken)
        || settings.Api.BearerToken is "PASTE_YOUR_TOKEN_HERE")
    {
        throw new InvalidOperationException(
            "Api:BearerToken is missing or still has the placeholder value. Update appsettings.json with a valid token.");
    }

    if (string.IsNullOrWhiteSpace(settings.Paths.InputFolder)
        || string.IsNullOrWhiteSpace(settings.Paths.ProcessedFolder)
        || string.IsNullOrWhiteSpace(settings.Paths.LogFolder))
    {
        throw new InvalidOperationException("Paths:InputFolder, Paths:ProcessedFolder and Paths:LogFolder are required.");
    }

    if (settings.Processing.MaxDegreeOfParallelism < 1)
    {
        throw new InvalidOperationException("Processing:MaxDegreeOfParallelism must be >= 1.");
    }
}

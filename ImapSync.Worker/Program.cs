using ImapSync.Application.Services;
using ImapSync.Core.Interfaces;
using ImapSync.Core.Models;
using ImapSync.Infrastructure.Services;
using ImapSync.Worker;
using Microsoft.Extensions.Options;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("ImapSync starting up...");

    var builder = Host.CreateApplicationBuilder(args);

    builder.Configuration.AddJsonFile("mailboxes.json", optional: false, reloadOnChange: true);

    builder.Services.AddSerilog((services, loggerConfig) =>
        loggerConfig.ReadFrom.Configuration(builder.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext());

    builder.Services.Configure<SyncSettings>(
        builder.Configuration.GetSection(SyncSettings.SectionName));

    builder.Services.AddSingleton<IImapService, ImapService>();
    builder.Services.AddSingleton<ISyncCacheService, SyncCacheService>();

    builder.Services.AddSingleton<ISyncStateService>(sp =>
    {
        var settings = sp.GetRequiredService<IOptions<SyncSettings>>().Value;
        var logger = sp.GetRequiredService<ILogger<SyncStateService>>();
        return new SyncStateService(settings.StateFilePath, logger);
    });

    builder.Services.AddSingleton<ISyncService, SyncService>();

    builder.Services.AddSingleton<IEmailNotificationService>(sp =>
    {
        var settings = sp.GetRequiredService<IOptions<SyncSettings>>().Value;
        var logger = sp.GetRequiredService<ILogger<EmailNotificationService>>();
        var smtpSettings = settings.ErrorNotification ?? new SmtpSettings();
        return new EmailNotificationService(smtpSettings, logger);
    });

    builder.Services.AddHostedService<Worker>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ImapSync terminated unexpectedly.");
}
finally
{
    await Log.CloseAndFlushAsync();
}

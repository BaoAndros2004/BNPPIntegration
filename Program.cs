using BNPPIntegration.BNPP;
using BNPPIntegration.BNPP.Configuration;
using BNPPIntegration.BNPP.FSR;
using BNPPIntegration.BNPP.MT940;
using BNPPIntegration.BNPP.MT942;
using BNPPIntegration.BNPP.Pain001;
using BNPPIntegration.BNPP.PSR;
using BNPPIntegration.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<WmsIntegrationOptions>(builder.Configuration.GetSection(WmsIntegrationOptions.SectionName));
builder.Services.Configure<ProcessingStorageOptions>(builder.Configuration.GetSection(ProcessingStorageOptions.SectionName));
builder.Services.AddSingleton<FSRParser>();
builder.Services.AddSingleton<FSRValidator>();
builder.Services.AddSingleton<FSRMapper>();
builder.Services.AddSingleton<PSRParser>();
builder.Services.AddSingleton<PSRValidator>();
builder.Services.AddSingleton<PSRMapper>();
builder.Services.AddSingleton<MT940Parser>();
builder.Services.AddSingleton<MT940Validator>();
builder.Services.AddSingleton<MT940Mapper>();
builder.Services.AddSingleton<MT942Parser>();
builder.Services.AddSingleton<MT942Validator>();
builder.Services.AddSingleton<MT942Mapper>();
builder.Services.AddSingleton<Pain001XmlGenerator>();
builder.Services.AddHttpClient<BNPPService>();

builder.Services.AddHostedService<InboundProcessingWorker>();
builder.Services.AddHostedService<OutboundProcessingWorker>();

var host = builder.Build();
host.Run();

using BNPPIntegration.BNPP.BankReports.FSR;
using BNPPIntegration.BNPP.BankReports.MT940;
using BNPPIntegration.BNPP.BankReports.MT942;
using BNPPIntegration.BNPP.BankReports.PSR;
using BNPPIntegration.BNPP.Payments.Domestic;
using BNPPIntegration.BNPP.Payments.IntraCompany;
using BNPPIntegration.BNPP.Payments.International;
using BNPPIntegration.BNPP.Security;
using BNPPIntegration.Infrastructure;
using BNPPIntegration.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "BNPP Integration Service";
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddSingleton<FSRParser>();
builder.Services.AddSingleton<FSRMapper>();
builder.Services.AddSingleton<PSRParser>();
builder.Services.AddSingleton<PSRMapper>();
builder.Services.AddSingleton<MT940Parser>();
builder.Services.AddSingleton<MT940Mapper>();
builder.Services.AddSingleton<MT942Parser>();
builder.Services.AddSingleton<MT942Mapper>();

builder.Services.AddSingleton<DomesticGenerator>();
builder.Services.AddSingleton<IntraCompanyGenerator>();
builder.Services.AddSingleton<InternationalGenerator>();
builder.Services.AddSingleton<PgpEncryptionService>();
builder.Services.AddSingleton<SftpService>();

builder.Services.AddHttpClient<WmsApiClient>();

builder.Services.AddHostedService<BankReportWorker>();
builder.Services.AddHostedService<PaymentWorker>();

var host = builder.Build();
host.Run();

using BNPPIntegration.BNPP.FSR;
using BNPPIntegration.BNPP.PSR;

namespace BNPPIntegration
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddSingleton<FSRParser>();
            builder.Services.AddSingleton<FSRValidator>();
            builder.Services.AddSingleton<FSRMapper>();
            builder.Services.AddSingleton<PSRParser>();
            builder.Services.AddSingleton<PSRValidator>();
            builder.Services.AddSingleton<PSRMapper>();

            var host = builder.Build();
            host.Run();
        }
    }
}

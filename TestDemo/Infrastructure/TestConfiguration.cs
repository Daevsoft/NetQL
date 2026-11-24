using Microsoft.Extensions.Configuration;

namespace TestDemo.Infrastructure
{
    internal class TestConfiguration
    {
        public static IConfiguration Load()
        {
            return new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Testing.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
        }
    }
}

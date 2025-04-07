using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


namespace HttpRequestService
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Create a HostBuilder configured to run as a Windows Service
            IHostBuilder builder = Host.CreateDefaultBuilder(args)
                .UseWindowsService()  // Allows the app to run as a Windows service
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Worker>(); // Register the Worker service
                });

            await builder.RunConsoleAsync();  // Run the application
  
        }


        public class Worker : BackgroundService
        {
            private readonly IConfiguration _configuration;
            private readonly ILogger<Worker> _logger;
            private readonly HttpClient _client;
            private int _interval;
            private int _retryDelaySeconds;

            public Worker(IConfiguration configuration, ILogger<Worker> logger)
            {
                _configuration = configuration;
                _logger = logger;
                _client = new HttpClient();

                // Load configuration settings
             
                _interval = int.Parse(_configuration["Settings:IntervalSeconds"]!); // Changed to seconds
            }

            // This method is called when the service starts and should return immediately
            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await DoWorkAsync(stoppingToken);
                    }
                    catch(Exception ex)
                    {
                        // Only one log per hour if all retries fail
                        _logger.LogError($"Job failed after all retries. Exception: {ex.Message}");
                    }

                    _logger.LogInformation($"Waiting for {_interval} seconds ( {_interval / 60} minutes) before next attempt...");
                    await Task.Delay(TimeSpan.FromSeconds(_interval), stoppingToken); // Changed to seconds
                }
            }

            private async Task DoWorkAsync(CancellationToken stoppingToken)
            {
                    var ipAddresses = _configuration.GetSection("Settings:IpAddresses").Get<string[]>();

                    if(ipAddresses == null || ipAddresses.Length == 0)
                    {
                        _logger.LogError("No IP addresses found in configuration.");
                        return;
                    }

                    foreach (var ip in ipAddresses)
                    {

                        try
                        {


                            _logger.LogInformation($"Sending POST request to {ip}...");

                            HttpResponseMessage response = await _client.PostAsync(ip, null);

                            if (response.IsSuccessStatusCode)
                            {
                                Console.WriteLine($"✅ Success from: {ip}");

                                string responseContent = await response.Content.ReadAsStringAsync();
                                _logger.LogInformation($"Response: {responseContent}");
                            }
                            else
                            {
                                Console.WriteLine($"❌ Failed from: {ip} - Status: {response.StatusCode}");
                            }
                        }
                        catch (HttpRequestException httpEx)
                        {
                            _logger.LogError($"HTTP error {ip} : {httpEx.Message}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"General error {ip}: {ex.Message}");
                        }
                    }
                
            }

            public override void Dispose()
            {
                _client.Dispose();
                base.Dispose();
            }
        }

    }
}

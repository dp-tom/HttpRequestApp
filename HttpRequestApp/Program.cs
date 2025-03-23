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
            private string _uri;
            private int _maxRetries;
            private int _retryDelaySeconds;

            public Worker(IConfiguration configuration, ILogger<Worker> logger)
            {
                _configuration = configuration;
                _logger = logger;
                _client = new HttpClient();

                // Load configuration settings
                _uri = _configuration["Settings:Uri"]!;
                _interval = int.Parse(_configuration["Settings:IntervalSeconds"]!); // Changed to seconds
                _maxRetries = 3;
                _retryDelaySeconds = 5;
            }

            // This method is called when the service starts and should return immediately
            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await DoWorkAsync(stoppingToken);
                    _logger.LogInformation($"Waiting for {_interval} seconds before next attempt..."); // Updated log message
                    await Task.Delay(TimeSpan.FromSeconds(_interval), stoppingToken); // Changed to seconds
                }
            }

            private async Task DoWorkAsync(CancellationToken stoppingToken)
            {
                int retryCount = 0;
                bool success = false;

                while (retryCount < _maxRetries && !success && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        _logger.LogInformation($"Sending POST request to {_uri}...");

                        HttpResponseMessage response = await _client.PostAsync(_uri, null);

                        if (response.IsSuccessStatusCode)
                        {
                            string responseContent = await response.Content.ReadAsStringAsync();
                            _logger.LogInformation($"Response: {responseContent}");
                            success = true;
                        }
                    }
                    catch (HttpRequestException httpEx)
                    {
                        _logger.LogError($"HTTP error: {httpEx.Message}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"General error: {ex.Message}");
                    }

                    if (!success)
                    {
                        retryCount++;
                        if (retryCount < _maxRetries)
                        {
                            _logger.LogInformation($"Retrying... Attempt {retryCount}/{_maxRetries} in {_retryDelaySeconds} seconds.");
                            await Task.Delay(TimeSpan.FromSeconds(_retryDelaySeconds), stoppingToken);
                        }
                        else
                        {
                            _logger.LogInformation("Max retry attempts reached. Skipping this cycle.");
                        }
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

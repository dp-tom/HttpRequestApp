using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;


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


        /* public class Worker : BackgroundService
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
         }*/


        public class Worker : BackgroundService
        {
            private readonly IConfiguration _configuration;
            private readonly ILogger<Worker> _logger;
            private readonly HttpClient _client;
            private readonly int _scanIntervalMinutes;
            private readonly int _failureThresholdHours;
            private DateTime _lastSuccessTime;
            private List<TargetRange> _targetRanges;
            private string _requestPath;

            public Worker(IConfiguration configuration, ILogger<Worker> logger)
            {
                _configuration = configuration;
                _logger = logger;
                _client = new HttpClient();
                _lastSuccessTime = DateTime.UtcNow; // initialize at service start

                // Load settings
                _scanIntervalMinutes = _configuration.GetValue<int>("Settings:ScanIntervalMinutes");
                _failureThresholdHours = _configuration.GetValue<int>("Settings:FailureThresholdHours");
                _requestPath = _configuration.GetValue<string>("Settings:RequestPath") ?? "/sayHello";

                _targetRanges = _configuration.GetSection("Settings:ScanTargets")
                                    .Get<List<TargetRange>>() ?? new List<TargetRange>();
            }

            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        bool success = await DoWorkAsync(stoppingToken);

                        if (success)
                        {
                            _lastSuccessTime = DateTime.UtcNow;
                        }
                        else
                        {
                            _logger.LogWarning("No successful response this cycle.");
                        }

                        // Check 4-hour failure threshold
                        if (DateTime.UtcNow - _lastSuccessTime > TimeSpan.FromHours(_failureThresholdHours))
                        {
                            _logger.LogError($"❗ No successful responses in the last {_failureThresholdHours} hours!");
                            _lastSuccessTime = DateTime.UtcNow; // reset to avoid spamming
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Unexpected error: {ex.Message}");
                    }

                    _logger.LogInformation($"Waiting {_scanIntervalMinutes} minutes before next attempt...");
                    await Task.Delay(TimeSpan.FromMinutes(_scanIntervalMinutes), stoppingToken);
                }
            }

            private async Task<bool> DoWorkAsync(CancellationToken stoppingToken)
            {
                bool anySuccess = false;

                foreach (var target in _targetRanges)
                {
                    var startIP = IPAddress.Parse(target.IpStart!);
                    var endIP = IPAddress.Parse(target.IpEnd!);

                    foreach (var ip in GetIpRange(startIP, endIP))
                    {
                        string url = $"http://{ip}:{target.Port}{_requestPath}";

                        try
                        {
                            _logger.LogInformation($"Sending POST to {url}...");
                            HttpResponseMessage response = await _client.PostAsync(url, null, stoppingToken);

                            if (response.IsSuccessStatusCode)
                            {
                                anySuccess = true;
                                _logger.LogInformation($"✅ Success from {url}");
                            }
                            else
                            {
                                _logger.LogWarning($"❌ Failed from {url} - Status: {response.StatusCode}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Error contacting {url}: {ex.Message}");
                        }
                    }
                }

                return anySuccess;
            }

            private IEnumerable<IPAddress> GetIpRange(IPAddress start, IPAddress end)
            {
                var startBytes = start.GetAddressBytes();
                var endBytes = end.GetAddressBytes();

                if (startBytes.Length != 4 || endBytes.Length != 4)
                    throw new ArgumentException("Only IPv4 addresses are supported.");

                uint startInt = BitConverter.ToUInt32(startBytes.Reverse().ToArray(), 0);
                uint endInt = BitConverter.ToUInt32(endBytes.Reverse().ToArray(), 0);

                for (uint i = startInt; i <= endInt; i++)
                {
                    yield return new IPAddress(BitConverter.GetBytes(i).Reverse().ToArray());
                }
            }

            public override void Dispose()
            {
                _client.Dispose();
                base.Dispose();
            }

            private class TargetRange
            {
                public string? IpStart { get; set; }
                public string? IpEnd { get; set; }
                public int Port { get; set; }
            }
        }

    }
}

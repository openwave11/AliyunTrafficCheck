using AlibabaCloud.OpenApiClient.Models;
using AlibabaCloud.SDK.CDT20210813.Models;
using AlibabaCloud.SDK.Ecs20140526;
using AlibabaCloud.SDK.Ecs20140526.Models;
using Microsoft.Extensions.Configuration;
using Quartz;
using Quartz.Impl;
using System.Net.Http;
using System.Text;
public class Program
{
    private static string? _accessKeyId = "";
    private static string? _accessKeySecret = "";
    private static string? _instanceId = "";
    private static int _maxTraffic = 180;
    private static string? _regionId = "cn-hongkong";
    private static bool _isClose;
    private static string? _webhookUrl = "";

    public static async Task Main(string[] args)
    {
        #region 配置读取

        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", false, true)
            .AddJsonFile("appsettings.Development.json", true, true)
            .AddEnvironmentVariables();
        IConfiguration configuration = builder.Build();
        _accessKeyId = configuration["Credentials:AccessKeyId"];
        _accessKeySecret = configuration["Credentials:AccessKeySecret"];
        _instanceId = configuration["instanceId"];
        _regionId = configuration["regionId"];
        int.TryParse(configuration["maxTraffic"], out _maxTraffic);
        _webhookUrl = configuration["WeChatWebhookUrl"];
        
       Console.WriteLine($"当前运行环境: {Environment.OSVersion.Platform} - {Environment.OSVersion.VersionString}");
       var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
       Console.WriteLine($"当前运行时环境: {(string.IsNullOrEmpty(env) ? "默认环境" : env)}");

        #endregion

        #region 定时任务

        var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
        await scheduler.Start();
        var startJob = JobBuilder.Create<StartInstanceJob>()
            .WithIdentity("startJob", "group1")
            .Build();
        var startTrigger = TriggerBuilder.Create()
            .WithIdentity("startTrigger", "group1")
            .StartNow()
            .WithSchedule(CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(1, 0, 0))
            .Build();

        var checkJob = JobBuilder.Create<CheckJob>()
            .WithIdentity("checkJob", "group1")
            .Build();
        var checkTrigger = TriggerBuilder.Create()
            .WithIdentity("checkTrigger", "group1")
            .StartNow()
            .WithSimpleSchedule(x => x
                .WithIntervalInHours(1)
                .RepeatForever())
            .Build();
        // 调度任务
        await scheduler.ScheduleJob(startJob, startTrigger);
        await scheduler.ScheduleJob(checkJob, checkTrigger);

        #endregion

        await Task.Delay(-1);
    }
    private static async Task<long> GetTraffic()
    {
        var config = new Config
        {
            AccessKeyId = _accessKeyId,
            AccessKeySecret = _accessKeySecret,
            Endpoint = "cdt.aliyuncs.com"
        };

        var client = new AlibabaCloud.SDK.CDT20210813.Client(config);
        var request = new ListCdtInternetTrafficRequest();
        try
        {
            var response = await client.ListCdtInternetTrafficAsync(request);
            if (response.StatusCode == 200)
            {
                var total = response.Body.TrafficDetails.Sum(n => n.Traffic);
                return total ?? 0;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Unexpected Error: " + e.Message);
        }

        return 0;
    }
    private static async Task Check()
    {
        if (!_isClose)
        {
            string message = "";
            
            double traffic = await GetTraffic();
            var convertTraffic = Math.Round(traffic / 1024 / 1024 / 1024, 2);
            WriteLog($"当前已使用流量:{convertTraffic}GB");
            if (convertTraffic > _maxTraffic)
            {
                WriteLog($"当前已超过{_maxTraffic}GB,执行关机任何操作");
                await ControlInstance("stop");
                message = $"ECS实例已关闭。已使用流量: {convertTraffic}GB，超过限制 {_maxTraffic}GB。";
            }
            else
            {
                WriteLog($"当前未超过{_maxTraffic}GB,不执行任何操作");
                message=$"ECS实例正常运行。已使用流量: {convertTraffic}GB，未超过限制 {_maxTraffic}GB。";
            }
            
            if (!string.IsNullOrEmpty(_webhookUrl))
            {
             
                await SendWeChatNotification(message);
            }
           
        }
    }

    static void WriteLog(string log)
    {
        Console.WriteLine($"{log} {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    }

    private static async Task PerformAction(Client client, string? instanceId, string action)
    {
        var describeRequest = new DescribeInstanceStatusRequest
        {
            InstanceId = new List<string?> {instanceId},
            RegionId = _regionId
        };
        var describeResponse = await client.DescribeInstanceStatusAsync(describeRequest);
        var instanceStatus = describeResponse.Body.InstanceStatuses.InstanceStatus[0].Status;
        if (action.ToLower() == "stop")
        {
            if (instanceStatus == "Stopped") return;

            var request = new StopInstanceRequest
            {
                InstanceId = instanceId
            };
            await client.StopInstanceAsync(request);
        }
        else if (action.ToLower() == "start")
        {
            if (instanceStatus == "Running") return;

            var request = new StartInstanceRequest
            {
                InstanceId = instanceId
            };
            await client.StartInstanceAsync(request);
        }
    }
    private static async Task ControlInstance(string action)
    {
        var config = new Config
        {
            AccessKeyId = _accessKeyId,
            AccessKeySecret = _accessKeySecret,
            Endpoint = $"ecs.{_regionId}.aliyuncs.com"
        };
        var client = new Client(config);
        try
        {
            // 如果指定了 instanceId，则操作指定实例；否则操作所有实例
            if (!string.IsNullOrEmpty(_instanceId))
            {
                await PerformAction(client, _instanceId, action);
                _isClose = true;
            }
            else
            {
                var describeRequest = new DescribeInstancesRequest();
                describeRequest.RegionId = _regionId;
                var describeResponse = await client.DescribeInstancesAsync(describeRequest);
                foreach (var instance in describeResponse.Body.Instances.Instance)
                {
                    var instanceId = instance.InstanceId;
                    await PerformAction(client, instanceId, action);
                }

                _isClose = true;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Unexpected Error: {e.Message}");
        }
    }
    
    private static async Task SendWeChatNotification(string message)
    {
        try
        {
            using var httpClient = new HttpClient();
            var payload = new
            {
                msgtype = "text",
                text = new
                {
                    content = message
                }
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            await httpClient.PostAsync(_webhookUrl, content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"发送微信通知失败: {ex.Message}");
        }
    }
    
    public class StartInstanceJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            WriteLog($"执行开机操作");
            await ControlInstance("start");
            _isClose = false;
            if (!string.IsNullOrEmpty(_webhookUrl))
            {
                await SendWeChatNotification("ECS实例已开启。");
            }
        }
    }
    public class CheckJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            await Check();
        }
    }
}
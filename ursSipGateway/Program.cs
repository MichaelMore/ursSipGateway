using Project;
using Project.Enums;
using Project.Helper;
using Project.AppSetting;
using Project.ProjectCtrl;
using Project.WorkerService;
using Newtonsoft.Json;
using NLog;

//使用 default 的 NLog.config
var nlog = LogManager.GetLogger("Startup");

try {
    IHost host = Host.CreateDefaultBuilder(args)
    // 關於 nlog 使用 appsettings.json 來做設定，以及使用注入的方式，
    // 請參考: https://stackoverflow.com/questions/69594537/nlog-setup-for-net5-worker-service-template
    // 關於 ILogger/LoggerName，此篇也很好: https://stackoverflow.com/questions/67274039/nlog-write-to-multiple-logs-in-net-core-3-1    

    // 使用 nlog 注入, 自動讀取 appsetting.json 的 "NLog" Section <= 但是不一定要用，因為覺得很麻煩，
    //.ConfigureLogging((hostContext, logging) => {
    //    logging.ClearProviders();
    //    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
    //    logging.AddNLog(hostContext.Configuration, new NLog.Extensions.Logging.NLogProviderOptions() {
    //        LoggingConfigurationSectionName = "NLog"
    //    });
    //})

    // service 設定
    .ConfigureServices((hostContext, services) => {        

        // 全域性針對 NewtonSoft 的 Json 序列化物件的設定
        JsonConvert.DefaultSettings = () => new JsonSerializerSettings {
            //Formatting = Formatting.Indented,
            //TypeNameHandling = TypeNameHandling.Objects,
            //ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyyy/MM/dd HH:mm:ss.ffffff"
        };

        // 讀取
        nlog.Info("");
        nlog.Info("");
        nlog.Info("***********************************************");
        nlog.Info("********** program.ConfigureServices **********");
        nlog.Info($"\t {GlobalVar.ProjectName} starting .... version = {GlobalVar.CurrentVersion}");

        #region 載入 AppSettings 至 GlobalVar
        // AppSettings 用 Project 或注入都可以存取
        IConfiguration configuration = hostContext.Configuration;

        //AppSettings 放入 Project.AppSettings
        nlog.Info("\t Project.SetConfiguration ...");
        GlobalVar.SetConfiguration(configuration);
        #endregion

        #region 注入AppSettings
        nlog.Info("\t AppSetting 注入 ...");
        AppSettings settings = configuration.GetSection("AppSettings").Get<AppSettings>();
        // AppSettings注入
        services.AddSingleton(settings);
        nlog.Info($"\t\t ServerID={settings.ServerID}");
        nlog.Info($"\t\t AppID={settings.AppID}");
        nlog.Info($"\t\t Loggings: \r\n{JsonConvert.SerializeObject(settings.Logging, Formatting.Indented)}");
        nlog.Info($"\t\t Recording: \r\n{JsonConvert.SerializeObject(settings.Recording, Formatting.Indented)}");
        #endregion

        #region 註冊IHttpClientFactory
        //註冊IHttpClientFactory的實現到DI容器        
        nlog.Info("\t IHttpClientFactory 注入 ...");
        services.AddHttpClient(); // <= WEB 不用加，但 console/service 要加，不然 AddTransient 會 error

        // 這一段是在處理繞過 SSL 自簽的問題，但還有問題... 之後再看看。
        //services.AddHttpClient("myApi")0
        //        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler {
        //            ServerCertificateCustomValidationCallback = delegate { return true; }
        //        });

        services.AddTransient<HttpClientHelper>();
        #endregion

        // 寫到 EXE 目錄下的 NetworkInterface.txt
        GlobalVar.WriteNetworkInterfaceFile();

        #region 註冊DemoWorker
        // 真正的 worker service
        nlog.Info("\t 註冊 ReadPacket ...");
        services.AddHostedService<MainWorker>();
        services.AddHostedService<CommandWorker>();
        services.AddHostedService<ScanWorker>();
        services.AddHostedService<WebAPIWorker>();
        #endregion
    })

    // 使用 Windows Service 註冊
    .UseWindowsService()
    .Build();
    await host.RunAsync();
}
catch(Exception ex) {
    nlog.Error($"系統(CreateDefaultBuilder)啟動錯誤: {ex.Message}");
}



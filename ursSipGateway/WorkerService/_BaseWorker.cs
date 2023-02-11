using Project.Database;
using Project.Helper;
using Project.Lib;
using Project.AppSetting;
using Project.ProjectCtrl;
using Newtonsoft.Json;
using NLog;

namespace Project {

    /// <summary>
    /// 此 Worker 是用來測試的，
    /// </summary>
    public class BaseWorker : BackgroundService {

        public virtual string className => GetType().Name;
        private BaseDb db => new BaseDb();

        //log物件
        protected Logger nlog;

        //Api呼叫物件
        protected HttpClientHelper _httpClientHelper;        

        //停用服務物件
        protected IHostApplicationLifetime hostLifeTime;

        public BaseWorker(HttpClientHelper httpClientHelper, IHostApplicationLifetime hostLifeTime) {            
            //初始化nlog
            nlog = LogManager.GetLogger(className);

            //初始化httpClientFactory
            this._httpClientHelper = httpClientHelper;

            //初始化hostLifeTime
            this.hostLifeTime = hostLifeTime;
        }

        public override Task StartAsync(CancellationToken cancellationToken) {
            nlog.Info("");
            nlog.Info("");
            nlog.Info("********************************************");
            nlog.Info($"\t {className} 服務啟動 ..., version = {GlobalVar.CurrentVersion}");
            nlog.Info("********************************************");
            //            
            return base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            // 因為每一個 worker 商業邏輯一定不同，幾乎100%會被 override，所以內容不用寫了
            await Task.Delay(1000);
        }

        public override Task StopAsync(CancellationToken cancellationToken) {
            nlog.Info($"{GlobalVar.ProjectName} 服務已正常停止");
            return base.StopAsync(cancellationToken);
        }
        public override void Dispose() {
            nlog.Info($"{GlobalVar.ProjectName} 服務已正常釋放");
            base.Dispose();
        }

        /// <summary>
        /// 更新服務狀態到資料庫
        /// </summary>
        /// <param name="serviceErrorCode"></param>
        /// <param name="msg"></param>
        /// <param name="serviceStatus"></param>
        private void UpdateModuleStaus(int serviceErrorCode, string msg, int serviceStatus) {
            try {
                var err = db.UpdateModuleStatus(GlobalVar.ServiceName_Ch, GlobalVar.ServiceName, serviceErrorCode, msg, serviceStatus);
                if (err.Success)
                    nlog.Info($@"UpdateModuleStaus");
            }
            catch (Exception ex) {
                nlog.Fatal("Update stauts error：" + ex.Message);
            }
        }

        public bool TimeIsUp(int waitSec, ref DateTime checkTime) {
            var ret = false;

            var diffSec = (DateTime.Now - checkTime).TotalSeconds;
            if (diffSec > waitSec) {
                checkTime = DateTime.Now;
                ret = true;
            }
            return ret;
        }
    }
}
using Project.Helper;
using Project.AppSetting;
using Project.ProjectCtrl;
using NLog;
using Project.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using richpod.lib;
using Microsoft.VisualBasic;

namespace Project.WorkerService {
    class WebAPIWorker : BaseWorker {
        public override string className => GetType().Name;
        public WebAPIWorker(HttpClientHelper httpClientHelper, IHostApplicationLifetime hostLifeTime) : base(httpClientHelper, hostLifeTime) {
            //初始化nlog
            nlog = LogManager.GetLogger(className);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {

            //必須給定前置時間給 Batch 檔啟動服務,不然console會判定service啟動錯誤
            await Task.Delay(GlobalVar.AppSettings.WorkerOption.DelayBefroreExecute * 1000, stoppingToken);

            nlog.Info($"{className} ExecuteAsync starting ...");            

            //DateTime checkTime = DateTime.MinValue; // 故意讓流程一開始要先跑
            DateTime checkTime = DateTime.Now;            

            while (!stoppingToken.IsCancellationRequested) {                
                try {
                    GlobalVar.WaitApiSending.WaitOne(); // <= 要注意這裡是否會造成監聽緩慢
                    var obj = GlobalVar.GetApiQueue();
                    await ProcessApiObject(obj);                    
                }
                catch (Exception ex) {
                    nlog.Error($"執行工作發生錯誤：{ex.Message}");
                }                
                //await Task.Delay(1000, stoppingToken);
            }
        }

        private async Task ProcessApiObject(object obj) {
            if (obj == null)
                return;
            var strObj = obj.GetType().Name;            
            if (strObj.ToUpper() == "ChannelStatusModel".ToUpper()) {
                await UpdateChannelStatus(obj);                 
            }
            else if (strObj.ToUpper() == "RecDataModel".ToUpper()) {
                await WriteRecData(obj);
            }
            else if (strObj.ToUpper() == "SystemLogModel".ToUpper()) {
                await WriteSystemLog(obj);
            }
        }

        private async Task<string> GetToken() {
            if (string.IsNullOrEmpty(GlobalVar.AppSettings.WebAPI.GetToken)) {
                nlog.Info($"\t\t cannot get token, the appsettings.WebAPI.GetToken is null/empty");
                return "";
            }
            var body = new {
                AccountID = GlobalVar.AppSettings.WebAPI.ApiAccount,
	            AccountPwd = GlobalVar.AppSettings.WebAPI.ApiPassword
            };
            var token = "";
            try {
                var strBody = JsonConvert.SerializeObject(body);
                var respModel = await _httpClientHelper.PostAsync(GlobalVar.AppSettings.WebAPI.GetToken, strBody);
                if (respModel == null) {
                    nlog.Error($"\t\t GetToken() failed: API return null object(respModel)");
                    return "";
                }
                nlog.Info($"\t\t GetToken() API return respModel = {JsonConvert.SerializeObject(respModel)}");
                if (respModel.ResultCode == 0 && lib_json.GetJsonValue(respModel.Content.ToString(), "ResultCode") == "0") {                    
                    token = lib_json.GetJsonValue(respModel.Content.ToString(), "Content");                    
                }
                else {
                    nlog.Error($"\t\t GetToken() response error.");
                }                
            }
            catch(Exception ex) {
                token = "";
                nlog.Error($"\t\t GetToken() failed: raised an exception: {ex.Message}");
            }
            return token;
        }

        private async Task UpdateChannelStatus(object obj) {
            if (obj == null)
                return;
            var chStatus = obj as ChannelStatusModel;
            nlog.Info($">>> process UpdateChannelStatus: {JsonConvert.SerializeObject(chStatus)}");
            if (string.IsNullOrEmpty(GlobalVar.AppSettings.WebAPI.UpdateChannelStatus)) {
                nlog.Info($"\t AppSettings.WebAPI.UpdateChannelStatus is null/empty, process ignored");
                return;
            }

            nlog.Info($"\t try to get token ...");
            var token = await GetToken();
            if (string.IsNullOrEmpty(token)) {
                nlog.Error($"\t no token, process terminated.");
                return;                
            }

            nlog.Info($"\t try to call API:UpdateChannelStatus={GlobalVar.AppSettings.WebAPI.UpdateChannelStatus}");            
            var strBody = JsonConvert.SerializeObject(chStatus);
            nlog.Info($"\t\t body={strBody}");
            Dictionary<string, string> myHeaders = new Dictionary<string, string>();
            myHeaders.Add("siplogger-token", token);
            var respModel = await _httpClientHelper.PostAsync(GlobalVar.AppSettings.WebAPI.UpdateChannelStatus, strBody, headers:myHeaders);
            if (respModel != null) {
                nlog.Info($"\t UpdateChannelStatus.respModel = {JsonConvert.SerializeObject(respModel)}");
            }
            else {
                nlog.Error($"\t failed to UpdateChannelStatus: respModel is null");
            }
        }

        private async Task WriteRecData(object obj) {
            if (obj == null)
                return;
            var recData = obj as RecDataModel;
            nlog.Info($">>> process WriteRecData: {JsonConvert.SerializeObject(recData)}");
            if (string.IsNullOrEmpty(GlobalVar.AppSettings.WebAPI.WriteRecData)) {
                nlog.Info($"\t AppSettings.WebAPI.WriteRecData is null/empty, process ignored");
                return;
            }

            nlog.Info($"\t try to get token ...");
            var token = await GetToken();
            if (string.IsNullOrEmpty(token)) {
                nlog.Error($"\t no token, process terminated.");
                return;
            }

            nlog.Info($"\t try to call API:WriteRecData={GlobalVar.AppSettings.WebAPI.WriteRecData}");            
            var strBody = JsonConvert.SerializeObject(recData);
            nlog.Info($"\t\t body={strBody}");
            Dictionary<string, string> myHeaders = new Dictionary<string, string>();
            myHeaders.Add("siplogger-token", token);
            var respModel = await _httpClientHelper.PostAsync(GlobalVar.AppSettings.WebAPI.WriteRecData, strBody, headers: myHeaders);
            if (respModel != null) {
                nlog.Info($"\t WriteRecData.respModel = {JsonConvert.SerializeObject(respModel)}");
            }
            else {
                nlog.Error($"\t failed to WriteRecData: respModel is null");
            }
        }

        private async Task WriteSystemLog(object obj) {
            if (obj == null)
                return;
            var sysLog = obj as SystemLogModel;
            nlog.Info($">>> process WriteSystemLog: {JsonConvert.SerializeObject(sysLog)}");
            if (string.IsNullOrEmpty(GlobalVar.AppSettings.WebAPI.WriteSystemLog)) {
                nlog.Info($"\t AppSettings.WebAPI.WriteSystemLog is null/empty, process ignored");
                return;
            }

            nlog.Info($"\t try to get token ...");
            var token = await GetToken();
            if (string.IsNullOrEmpty(token)) {
                nlog.Error($"\t no token, process terminated.");
                return;
            }

            nlog.Info($"\t try to call API:WriteSystemLog={GlobalVar.AppSettings.WebAPI.WriteSystemLog}");
            var strBody = JsonConvert.SerializeObject(sysLog);
            nlog.Info($"\t\t body={strBody}");
            Dictionary<string, string> myHeaders = new Dictionary<string, string>();
            myHeaders.Add("siplogger-token", token);
            var respModel = await _httpClientHelper.PostAsync(GlobalVar.AppSettings.WebAPI.WriteSystemLog, strBody, headers: myHeaders);
            if (respModel != null) {
                nlog.Info($"\t WriteSystemLog.respModel = {JsonConvert.SerializeObject(respModel)}");
            }
            else {
                nlog.Error($"\t failed to WriteSystemLog: respModel is null");
            }
        }



    }
}

using Project.Models;
using Newtonsoft.Json;
using NLog;
using System.Collections.Specialized;
using System.Net;
using System.Text;

namespace Project
    .Helper {

    //**************************************************************************************************
    // 以下程式碼來自: https://www.twblogs.net/a/608ce0b1753b6adabf6e54b0
    //**************************************************************************************************   

    /// <summary>
    /// HTTP幫助類
    /// 注意: 此 HttpClientHelperApp 是 for APP/Service 用的，因為注入方式與寫 nlog 的方式不同。
    /// </summary>
    public class HttpClientHelper {        
        private Logger nlog;
        private IHttpClientFactory _httpClientFactory;
        public HttpClientHelper(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory) {
            _httpClientFactory = httpClientFactory;
            nlog = LogManager.GetLogger("HttpClient");
            //nlog = loggerFactory.CreateLogger("HttpClient");
        }

        /// <summary>
        /// 發起GET異步請求
        /// </summary>
        /// <typeparam name="T">返回類型</typeparam>
        /// <param name="url">請求地址</param>
        /// <param name="headers">請求頭信息</param>
        /// <param name="timeOut">請求超時時間，單位秒</param>
        /// <returns>返回string</returns>
        public async Task<ApiResponseModel> GetAsync(string url, Dictionary<string, string> headers = null, int timeOut = 30) {
            var retModel = new ApiResponseModel();
            nlog.Info($"WebApi.GetAsync: url = {url}");
            var hostName = GetHostName(url);
            try {
                using (HttpClient client = _httpClientFactory.CreateClient(hostName)) {
                    client.Timeout = TimeSpan.FromSeconds(timeOut);
                    if (headers?.Count > 0) {
                        foreach (string key in headers.Keys) {
                            client.DefaultRequestHeaders.Add(key, headers[key]);
                        }
                    }
                    nlog.Info($"\t WEB API is calling(GET) ...");
                    using (HttpResponseMessage response = await client.GetAsync(url)) {                        
                        retModel.HttpCode = (int)response.StatusCode;
                        nlog.Info($"\t statusCode = {response.StatusCode}");
                        if (response.IsSuccessStatusCode) {
                            retModel.ResultCode = 0;                            
                        }
                        else {
                            retModel.ResultCode = -1;                            
                        }
                        
                        retModel.Content = await response.Content.ReadAsStringAsync();
                        nlog.Info($"\t response = {retModel.Content}");
                    }
                }
            }
            catch (Exception ex) {
                retModel.ResultCode = -2;                
                retModel.Exception = ex.Message;
                nlog.Error($"\t error: {ex.Message}");
            }
            return retModel;
        }

        /// <summary>
        /// 發起GET異步請求，但返回的內容為 Byte Array
        /// </summary>        
        /// <param name="url">請求地址</param>
        /// <param name="headers">請求頭信息</param>
        /// <param name="timeOut">請求超時時間，單位秒</param>
        /// <returns>ApiResponseModel, 且 Content = Byte Array</returns>
        public async Task<ApiResponseModel> GetAsyncBytes(string url, Dictionary<string, string> headers = null, int timeOut = 30) {
            var retModel = new ApiResponseModel();
            nlog.Info($"WebApi.GetAsync: url = {url}");
            var hostName = GetHostName(url);
            try {
                using (HttpClient client = _httpClientFactory.CreateClient(hostName)) {
                    client.Timeout = TimeSpan.FromSeconds(timeOut);
                    if (headers?.Count > 0) {
                        foreach (string key in headers.Keys) {
                            client.DefaultRequestHeaders.Add(key, headers[key]);
                        }
                    }
                    nlog.Info($"\t WEB API is calling(GET) ...");
                    using (HttpResponseMessage response = await client.GetAsync(url)) {
                        retModel.HttpCode = (int)response.StatusCode;
                        nlog.Info($"\t statusCode = {response.StatusCode}");
                        if (response.IsSuccessStatusCode) {
                            retModel.ResultCode = 0;                            
                        }
                        else {
                            retModel.ResultCode = -1;
                        }
                        retModel.Content = await ReadContentAsBytes(response);
                        var byteLen = retModel.Content == null ? 0 : (retModel.Content as Byte[]).Length;
                        nlog.Info($"\t response bytes length = {byteLen}");
                    }
                }
            }
            catch (Exception ex) {
                retModel.ResultCode = -2;                
                retModel.Exception = ex.Message;
                nlog.Error($"\t error: {ex.Message}");
            }
            return retModel;
        }

        /// <summary>
        /// 發起POST異步請求
        /// </summary>
        /// <param name="url">請求地址</param>
        /// <param name="body">POST提交的內容</param>
        /// <param name="bodyMediaType">POST內容的媒體類型，如：application/xml、application/json</param>
        /// <param name="responseContentType">HTTP響應上的content-type內容頭的值,如：application/xml、application/json、application/text、application/x-www-form-urlencoded等</param>
        /// <param name="headers">請求頭信息</param>
        /// <param name="timeOut">請求超時時間，單位秒</param>
        /// <returns>返回string</returns>
        public async Task<ApiResponseModel> PostAsync(string url, string body, string bodyMediaType = null, string responseContentType = "application/json",
                                            Dictionary<string, string> headers = null, int timeOut = 30) {
            var retModel = new ApiResponseModel();
            nlog.Info($"WebApi.PostAsync: url = {url}");
            var hostName = GetHostName(url);
            try {
                using (HttpClient client = _httpClientFactory.CreateClient(hostName)) {                
                    client.Timeout = TimeSpan.FromSeconds(timeOut);
                    if (headers?.Count > 0) {
                        foreach (string key in headers.Keys) {
                            client.DefaultRequestHeaders.Add(key, headers[key]);
                        }
                    }
                    StringContent content = new StringContent(body, System.Text.Encoding.UTF8, mediaType: bodyMediaType);
                    if (!string.IsNullOrWhiteSpace(responseContentType)) {
                        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(responseContentType);
                    }
                    nlog.Info($"\t WEB API is calling(POST) ...");
                    using (HttpResponseMessage response = await client.PostAsync(url, content)) {
                        retModel.HttpCode = (int)response.StatusCode;
                        nlog.Info($"\t statusCode = {response.StatusCode}");
                        if (response.IsSuccessStatusCode) {
                            retModel.ResultCode = 0;
                        }
                        else {
                            retModel.ResultCode = -1;
                        }
                        retModel.Content = await ReadContentAsString(response);
                        nlog.Info($"\t response = {retModel.Content}");
                    }
                }
            }
            catch (Exception ex) {
                retModel.ResultCode = -2;
                retModel.Exception = ex.Message;
                nlog.Error($"\t error: {ex.Message}");
            }
            return retModel;
        }

        /// <summary>
        /// 發起POST異步請求(MultipartFormDataContent)
        /// </summary>
        /// <param name="url">請求地址</param>
        /// <param name="formData">POST提交的內容, 但使用 NameValueCollection 來組合 Multipart Form </param>
        /// <param name="bodyMediaType">POST內容的媒體類型，如：application/xml、application/json</param>
        /// <param name="responseContentType">HTTP響應上的content-type內容頭的值,如：application/xml、application/json、application/text、application/x-www-form-urlencoded等</param>
        /// <param name="headers">請求頭信息</param>
        /// <param name="timeOut">請求超時時間，單位秒</param>
        /// <returns>返回string</returns>
        public async Task<ApiResponseModel> PostFormAsync(string url, NameValueCollection formData, string bodyMediaType = null, string responseContentType = "application/json",
                                            Dictionary<string, string> headers = null, int timeOut = 30) {
            var retModel = new ApiResponseModel();
            nlog.Info($"WebApi.PostAsync: url = {url}");
            var hostName = GetHostName(url);
            try {
                using (HttpClient client = _httpClientFactory.CreateClient(hostName)) {
                    client.Timeout = TimeSpan.FromSeconds(timeOut);
                    if (headers?.Count > 0) {
                        foreach (string key in headers.Keys) {
                            client.DefaultRequestHeaders.Add(key, headers[key]);
                        }
                    }
                    
                    var content = new MultipartFormDataContent();
                    if (formData != null) {
                        foreach (string key in formData.Keys) {
                            content.Add(new StringContent(formData[key], Encoding.UTF8), key);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(responseContentType)) {
                        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(responseContentType);
                    }
                    nlog.Info($"\t WEB API is calling(POST) ...");
                    using (HttpResponseMessage response = await client.PostAsync(url, content)) {
                        retModel.HttpCode = (int)response.StatusCode;
                        nlog.Info($"\t statusCode = {response.StatusCode}");
                        if (response.IsSuccessStatusCode) {
                            retModel.ResultCode = 0;
                        }
                        else {
                            retModel.ResultCode = -1;
                        }
                        retModel.Content = await ReadContentAsString(response);
                        nlog.Info($"\t response = {retModel.Content}");
                    }
                }
            }
            catch (Exception ex) {
                retModel.ResultCode = -2;
                retModel.Exception = ex.Message;
                nlog.Error($"\t error: {ex.Message}");
            }
            return retModel;
        }

        /// <summary>
        /// 發起PUT異步請求
        /// </summary>
        /// <param name="url">請求地址</param>
        /// <param name="body">POST提交的內容</param>
        /// <param name="bodyMediaType">POST內容的媒體類型，如：application/xml、application/json</param>
        /// <param name="responseContentType">HTTP響應上的content-type內容頭的值,如：application/xml、application/json、application/text、application/x-www-form-urlencoded等</param>
        /// <param name="headers">請求頭信息</param>
        /// <param name="timeOut">請求超時時間，單位秒</param>
        /// <returns>返回string</returns>
        public async Task<ApiResponseModel> PutAsync(string url, string body, string bodyMediaType = null, string responseContentType = "application/json",
                                            Dictionary<string, string> headers = null, int timeOut = 30) {
            var retModel = new ApiResponseModel();
            nlog.Info($"WebApi.PutAsync: url = {url}");
            var hostName = GetHostName(url);
            try {
                using (HttpClient client = _httpClientFactory.CreateClient(hostName)) {
                    client.Timeout = TimeSpan.FromSeconds(timeOut);
                    if (headers?.Count > 0) {
                        foreach (string key in headers.Keys) {
                            client.DefaultRequestHeaders.Add(key, headers[key]);
                        }
                    }
                    StringContent content = new StringContent(body, System.Text.Encoding.UTF8, mediaType: bodyMediaType);
                    if (!string.IsNullOrWhiteSpace(responseContentType)) {
                        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(responseContentType);
                    }
                    nlog.Info($"\t WEB API is calling(PUT) ...");
                    using (HttpResponseMessage response = await client.PutAsync(url, content)) {
                        retModel.HttpCode = (int)response.StatusCode;
                        nlog.Info($"\t statusCode = {response.StatusCode}");
                        if (response.IsSuccessStatusCode) {
                            retModel.ResultCode = 0;
                        }
                        else {
                            retModel.ResultCode = -1;
                        }
                        retModel.Content = await ReadContentAsString(response);
                        nlog.Info($"\t response = {retModel.Content}");
                    }
                }
            }
            catch (Exception ex) {
                retModel.ResultCode = -2;
                retModel.Exception = ex.Message;
                nlog.Error($"\t error: {ex.Message}");
            }
            return retModel;
        }

        /// <summary>
        /// 發起DELETE異步請求
        /// </summary>
        /// <typeparam name="T">返回類型</typeparam>
        /// <param name="url">請求地址</param>
        /// <param name="headers">請求頭信息</param>
        /// <param name="timeOut">請求超時時間，單位秒</param>
        /// <returns>返回string</returns>
        public async Task<ApiResponseModel> DeleteAsync(string url, Dictionary<string, string> headers = null, int timeOut = 30) {
            var retModel = new ApiResponseModel();
            nlog.Info($"WebApi.DeleteAsync: url = {url}");
            var hostName = GetHostName(url);
            try {
                using (HttpClient client = _httpClientFactory.CreateClient(hostName)) {
                    client.Timeout = TimeSpan.FromSeconds(timeOut);
                    if (headers?.Count > 0) {
                        foreach (string key in headers.Keys) {
                            client.DefaultRequestHeaders.Add(key, headers[key]);
                        }
                    }
                    nlog.Info($"\t WEB API is calling(DELETE) ...");
                    using (HttpResponseMessage response = await client.DeleteAsync(url)) {
                        retModel.HttpCode = (int)response.StatusCode;
                        nlog.Info($"\t statusCode = {response.StatusCode}");
                        if (response.IsSuccessStatusCode) {
                            retModel.ResultCode = 0;                            
                        }
                        else {
                            retModel.ResultCode = -1;                                                        
                        }                                                
                        retModel.Content = await ReadContentAsString(response);
                        nlog.Info($"\t response = {retModel.Content}");
                    }
                }
            }
            catch (Exception ex) {
                retModel.ResultCode = -2;                
                retModel.Exception = ex.Message;
                nlog.Error($"\t error: {ex.Message}");
            }
            return retModel;
        }

        #region 私有函數


        private async static Task<string> ReadContentAsString(HttpResponseMessage resp) {
            var ret = "";
            try {
                if (resp != null) {
                    if (resp.Content != null) {
                        ret = await resp.Content.ReadAsStringAsync();
                    }
                }
            }
            catch (Exception ex) {                
                ret = "";
            }
            return ret;
        }

        private async static Task<byte[]> ReadContentAsBytes(HttpResponseMessage resp) {
            byte[] ret = null;
            try {
                if (resp != null) {
                    if (resp.Content != null) {
                        ret = await resp.Content.ReadAsByteArrayAsync();
                    }
                }
            }
            catch (Exception ex) {                
            }
            return ret;
        }

        /// <summary>
        /// 獲取請求的主機名
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private static string GetHostName(string url) {
            if (!string.IsNullOrWhiteSpace(url)) {
                return url.Replace("https://", "").Replace("http://", "").Split('/')[0];
            }
            else {
                return "AnyHost";
            }
        }

        #endregion
    }
    
}

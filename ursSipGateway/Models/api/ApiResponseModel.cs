using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Project.Models {    

    public class ApiResponseModel {
        public int ResultCode { get; set; } = 0;// 1: ok, < 0: error
        public int HttpCode { get; set; } = (int)HttpStatusCode.OK;
        public int DataCount { get; set; } = 0;
        public string ResponseText { get; set; } = "Ok";
        public string Exception { get; set; } = string.Empty;
        public object Content { get; set; } = null;
        public object ExtraInfo { get; set; } = null;

        public ApiResponseModel() {            
        }


    }
}

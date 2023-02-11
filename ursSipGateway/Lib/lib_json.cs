using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace richpod.lib {
    public static class lib_json {

        public static JObject GetJsonObject(string s) {
            JObject jObj = null;
            try {
                jObj = JObject.Parse(s);
            }
            catch (Exception ex) {
                return null;
            }
            return jObj;
        }


        public static string GetJsonValue(string jsonStr, params string[] keyList) {
            var ret = "";
            var jobj = lib_json.GetJsonObject(jsonStr);
            if (jobj != null) {
                ret = GetJsonValue(jobj, keyList);
            }
            return ret;
        }

        public static string GetJsonValue(JObject jobj, params string[] keyList) {
            var ret = "";
            if (jobj == null || keyList == null)
                return "";

            var list = keyList.ToList();
            JToken tok = jobj;
            foreach (var s in list) {
                tok = tok[s];
                if (tok == null)
                    return "";
                else {
                    var index = list.IndexOf(s);
                    // 是否最後一筆 ?
                    if (index + 1 == list.Count) {
                        ret = tok.ToString().Trim();
                        break;
                    }
                }
            }
            return ret;
        }


    }
}

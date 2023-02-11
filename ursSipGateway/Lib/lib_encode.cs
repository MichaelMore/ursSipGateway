using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Security.Cryptography;

namespace Project.Lib {
    public static class lib_encode
    {

        // <summary>
        /// 使用AES 256 加密
        /// </summary>
        /// <param name="source">本文</param>
        /// <param name="key">因為是256 所以你密碼必須為32英文字=32*8=256</param>
        /// <param name="iv">IV為128 所以為 16 * 8= 128</param>
        /// <returns></returns>
        public static string EncryptAES256(string source, string key, string iv, out string err)
        {
            err = "";

            try {
                byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
                // TODO: 找 RijndaelManaged 的替代
                var aes = new RijndaelManaged();
                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.IV = Encoding.UTF8.GetBytes(iv);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                ICryptoTransform transform = aes.CreateEncryptor();

                return Convert.ToBase64String(transform.TransformFinalBlock(sourceBytes, 0, sourceBytes.Length));
            }
            catch (Exception ex) {
                err = ex.Message;
                return "";
            }
        }


        /// <summary>
        /// 使用AES 256 解密
        /// </summary>
        /// <param name="encryptData">Base64的加密後的字串</param>
        /// <param name="key">因為是256 所以你密碼必須為32英文字=32*8=256</param>
        /// <param name="iv">IV為128 所以為 16 * 8= 128</param>
        /// <returns></returns>
        public static string DecryptAES256(string encryptData, string key, string iv, out string err)
        {
            err = "";

            try {
                var encryptBytes = Convert.FromBase64String(encryptData);
                // TODO: 找 RijndaelManaged 的替代
                var aes = new RijndaelManaged();
                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.IV = Encoding.UTF8.GetBytes(iv);
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                ICryptoTransform transform = aes.CreateDecryptor();

                return Encoding.UTF8.GetString(transform.TransformFinalBlock(encryptBytes, 0, encryptBytes.Length));
            }
            catch (Exception ex) {
                err = ex.Message;
                return "";
            }
        }

        public static string StringToISO_8859_1(string srcText) {
            string dst = "";
            char[] src = srcText.ToCharArray();
            for (int i = 0; i < src.Length; i++) {
                string str = @"&#" + (int)src[i] + ";";
                dst += str;
            }
            return dst;
        }


        /// <summary>
        /// 转换为原始字符串
        /// </summary>
        /// <param name="srcText"></param>
        /// <returns></returns>
        public static string ISO_8859_1ToString(string srcText) {
            string dst = "";
            string[] src = srcText.Split(';');
            for (int i = 0; i < src.Length; i++) {
                if (src[i].Length > 0) {
                    string str = ((char)int.Parse(src[i].Substring(2))).ToString();
                    dst += str;
                }
            }
            return dst;
        }
    }

}
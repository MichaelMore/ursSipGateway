using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Project.Lib {

    internal static class lib_fileEncoding {
        //  Call this function to remove the key from memory after use for security
        [DllImport("KERNEL32.DLL", EntryPoint = "RtlZeroMemory")]
        public static extern bool ZeroMemory(IntPtr Destination, int Length);

        public static byte[] GenerateRandomSalt() {
            byte[] data = new byte[32];

            using (var rng = RandomNumberGenerator.Create()) {
                for (int i = 0; i < 10; i++) {
                    rng.GetBytes(data);
                }
            }
            return data;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="inputFile"></param>
        /// <param name="outputFile"></param>
        /// <param name="password"></param>
        /// <param name="salt"></param>
        /// <param name="errMsg"></param>
        /// <returns></returns>
        // 參考網站: http://stackoverflow.com/questions/27645527/aes-encryption-on-large-files
        public static bool FileEncrypt(string inputFile, string outputFile, string password, byte[] salt, out string errMsg) {
            var ret = false;
            errMsg = "";

            if (!File.Exists(inputFile)) {
                errMsg = $"inputFile({inputFile}) not exists";
                return ret;
            }

            if (!CheckFileNameValidChar(outputFile)) {
                errMsg = $"outputFile({outputFile}) 檔名有不合法的字元";
                return ret;
            }

            //// 有問題，暫時先不過濾
            //var invalid = Path.GetInvalidFileNameChars();
            //foreach(var ch in invalid) {
            //    if (outputFile.Contains(ch)) {
            //        errMsg = $"outputFile({outputFile}) 檔名有不合法的字元";
            //        return ret;
            //    }
            //}

            #region 建立 AES
            //convert password string to byte arrray
            byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
            //RijndaelManaged AES = new RijndaelManaged(); //Set Rijndael symmetric encryption algorithm
            var AES = Aes.Create("AesManaged");
            try {
                AES.KeySize = 256;
                AES.BlockSize = 128;
                AES.Padding = PaddingMode.PKCS7;

                //http://stackoverflow.com/questions/2659214/why-do-i-need-to-use-the-rfc2898derivebytes-class-in-net-instead-of-directly
                //"What it does is repeatedly hash the user password along with the salt." High iteration counts.
                var key = new Rfc2898DeriveBytes(passwordBytes, salt, 50000);
                AES.Key = key.GetBytes(AES.KeySize / 8);
                AES.IV = key.GetBytes(AES.BlockSize / 8);

                //Cipher modes: http://security.stackexchange.com/questions/52665/which-is-the-best-cipher-mode-and-padding-mode-for-aes-encryption
                AES.Mode = CipherMode.CFB;
            }
            catch (Exception ex) {
                if (AES != null)
                    AES.Dispose();
                errMsg = $"AES setup error: {ex.Message}";
                return ret;
            }
            #endregion

            FileStream fsIn;
            #region open/read inputFile 檔案            
            try {
                fsIn = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            catch (Exception ex) {
                if (AES != null)
                    AES.Dispose();
                errMsg = $"open/read inputFile({inputFile}) error: {ex.Message}";
                return ret;
            }
            #endregion

            FileStream fsCrypt = null;
            CryptoStream cs = null;
            #region 檢查 outputFile 檔案
            try {
                fsCrypt = new FileStream(outputFile, FileMode.Create);
                fsCrypt.Write(salt, 0, salt.Length);
                cs = new CryptoStream(fsCrypt, AES.CreateEncryptor(), CryptoStreamMode.Write);
            }
            catch (Exception ex) {
                errMsg = $"create outputFile({outputFile}) error: {ex.Message}";
            }
            #endregion

            if (errMsg == "") {
                #region 加密: 每一 MB 加密 ...                
                int read;
                byte[] buffer = new byte[1048576];
                try {
                    while ((read = fsIn.Read(buffer, 0, buffer.Length)) > 0) { // 讀取 fsIn...，每一 MB 做加密寫入
                        Thread.Sleep(1);
                        cs.Write(buffer, 0, read);
                    }
                }
                catch (Exception ex) {
                    errMsg = $"encrypt data error: {ex.Message}";
                }
                #endregion
            }

            try {
                cs.Close();
            }
            catch (Exception ex) {
                Console.WriteLine("Error by closing CryptoStream: " + ex.Message);
            }
            finally {
                if (fsCrypt != null)
                    fsCrypt.Close();
                fsIn.Close();
                AES.Dispose();
            }
            return errMsg == "";
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="inputFile"></param>
        /// <param name="outputFile"></param>
        /// <param name="password"></param>
        /// <param name="salt"></param>
        /// <param name="errMsg"></param>
        public static bool FileDecrypt(string inputFile, string outputFile, string password, byte[] salt, out string errMsg) {
            errMsg = "";
            var ret = false;

            if (!File.Exists(inputFile)) {
                errMsg = $"inputFile({inputFile}) not exists";
                return ret;
            }

            if (!CheckFileNameValidChar(outputFile)) {
                errMsg = $"outputFile({outputFile}) 檔名有不合法的字元";
                return ret;
            }
            // 有問題，暫時先不過濾
            //if (outputFile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
            //    errMsg = $"outputFile({outputFile}) 檔名有不合法的字元";
            //    return ret;
            //}

            #region 建立 AES
            byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
            //RijndaelManaged AES = new RijndaelManaged();
            var AES = Aes.Create("AesManaged");
            try {
                AES.KeySize = 256;
                AES.BlockSize = 128;
                AES.Padding = PaddingMode.PKCS7;
                var key = new Rfc2898DeriveBytes(passwordBytes, salt, 50000);
                AES.Key = key.GetBytes(AES.KeySize / 8);
                AES.IV = key.GetBytes(AES.BlockSize / 8);
                AES.Mode = CipherMode.CFB;
            }
            catch (Exception ex) {
                if (AES != null)
                    AES.Dispose();
                errMsg = $"AES setup error: {ex.Message}";
                return ret;
            }
            #endregion

            FileStream fsOut;
            #region 檢查 outputFile
            try {
                fsOut = new FileStream(outputFile, FileMode.Create);
            }
            catch (Exception ex) {
                if (AES != null)
                    AES.Dispose();
                errMsg = $"create outputFile({outputFile}) error: {ex.Message}";
                return ret;
            }
            #endregion

            FileStream fsCrypt = null;
            CryptoStream cs = null;
            #region 檢查 inputFile
            try {
                fsCrypt = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                fsCrypt.Read(salt, 0, salt.Length);
                cs = new CryptoStream(fsCrypt, AES.CreateDecryptor(), CryptoStreamMode.Read);
            }
            catch (Exception ex) {
                errMsg = $"read/open inputFile({inputFile}) error: {ex.Message}";
            }
            #endregion             

            // 處理解密
            if (errMsg == "") {
                #region 解密: 每一 MB 解密 ...                
                int read;
                byte[] buffer = new byte[1048576];
                try {
                    while ((read = cs.Read(buffer, 0, buffer.Length)) > 0) {
                        Thread.Sleep(1);
                        fsOut.Write(buffer, 0, read);
                    }
                }
                catch (Exception ex) {
                    errMsg = $"decrypt data error: {ex.Message}";
                }
                #endregion
            }

            try {
                cs.Close();
            }
            catch (Exception ex) {
                //不需要寫 error log
            }
            finally {
                if (fsCrypt != null)
                    fsCrypt.Close();
                fsOut.Close();
                AES.Dispose();
            }
            return errMsg == "";
        }

        public static bool CheckFileNameValidChar(string fullFileName) {

            // 檢查 PATH
            var invalidPathChar = Path.GetInvalidPathChars();
            var path = Path.GetDirectoryName(fullFileName);
            if (string.IsNullOrEmpty(path))
                return true;
            if (path.IndexOfAny(invalidPathChar) >= 0)
                return false;
            // 檢查 file name
            var invalidFileChar = Path.GetInvalidFileNameChars();
            var fileName = Path.GetFileName(fullFileName);
            if (string.IsNullOrEmpty(fileName))
                return true;
            if (fileName.IndexOfAny(invalidFileChar) >= 0)
                return false;
            //
            return true;
        }



    }

}


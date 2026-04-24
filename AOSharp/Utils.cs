using System;
using System.IO;
using System.Security.Cryptography;

namespace AOSharp
{
    public static class Utils
    {
        public static string HashFromFile(string filepath)
        {
            byte[] encodedBytes;
            MD5 md5 = new MD5CryptoServiceProvider();

            using (Stream fileStream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                encodedBytes = md5.ComputeHash(fileStream);
            }

            string hash = BitConverter.ToString(encodedBytes);
            return hash.Replace("-", string.Empty);
        }

        public static string HashFromString(string value)
        {
            MD5 md5 = new MD5CryptoServiceProvider();
            byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(value);
            byte[] encodedBytes = md5.ComputeHash(inputBytes);
            return BitConverter.ToString(encodedBytes).Replace("-", string.Empty);
        }
    }
}

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using PokemonGo.RocketAPI;

namespace PoGo.NecroBot.Logic.Utils
{
    public class WebClient : HttpClient
    {
        private static readonly HttpClientHandler Handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false,
            UseProxy = Client.Proxy != null,
            Proxy = Client.Proxy
        };


        public WebClient() : base(new RetryHandler(Handler))
        {
            DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/535.2 (KHTML, like Gecko) Chrome/15.0.874.121 Safari/535.2");
            //DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");
            //DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            //DefaultRequestHeaders.TryAddWithoutValidation("Content-Type", "application/x-www-form-urlencoded");
            
            //DefaultRequestHeaders.ExpectContinue = false;
            this.Timeout = TimeSpan.FromMilliseconds(10000);
        }

        public string DownloadString(string url)
        {
            return DownloadString(new Uri(url));
        }

        public string DownloadString(Uri uri)
        {
            try
            {
                var bytesResponse = base.GetByteArrayAsync(uri).Result;
                return Encoding.UTF8.GetString(bytesResponse);
            }
            catch (WebException e)
            {
               
            }
            return string.Empty;
        }

        public void DownloadFile(string url, string destPath)
        {
            using (var stream = base.GetStreamAsync(url).Result)
            {
                using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
            }
        }

        protected virtual WebRequest GetWebRequest(Uri uri)
        {
            var request = WebRequest.Create(uri);
            return request;
        }
    }

    public static class WebExtension
    {
        public static void SetTimeout(this WebRequest request, int milliseconds)
        {
            ((HttpWebRequest) request).ContinueTimeout = milliseconds;
        }

        public static int GetTimeout(this WebRequest request)
        {
            return ((HttpWebRequest)request).ContinueTimeout;
        }
    }
}
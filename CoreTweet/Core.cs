// The MIT License (MIT)
//
// CoreTweet - A .NET Twitter Library supporting Twitter API 1.1
// Copyright (c) 2013 lambdalice
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using CoreTweet.Core;
using Alice.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

/// <summary>
/// The twitter library.
/// </summary>
namespace CoreTweet
{
    /// <summary>
    /// The type of the HTTP method.
    /// </summary>
    public enum MethodType
    {
        /// <summary>
        /// GET method.
        /// </summary>
        Get,
        /// <summary>
        /// POST method.
        /// </summary>
        Post,
        /// <summary>
        /// POST method without any response.
        /// </summary>
        PostNoResponse
    }

    public static class OAuth
    {
        public class OAuthSession
        {
            public string ConsumerKey { get; set; }
            public string ConsumerSecret { get; set; }
            public string RequestToken { get; set; }
            public string RequestTokenSecret { get; set; }
            public Uri AuthorizeUri
            {
                get
                {
                    return new Uri(AuthorizeUrl + "?oauth_token=" + RequestToken);
                }
            }
        }

        /// <summary>
        /// The request token URL.
        /// </summary>
        static readonly string RequestTokenUrl = "https://api.twitter.com/oauth/request_token";
        /// <summary>
        /// The access token URL.
        /// </summary>
        static readonly string AccessTokenUrl = "https://api.twitter.com/oauth/access_token";
        /// <summary>
        /// The authorize URL.
        /// </summary>
        static readonly string AuthorizeUrl = "https://api.twitter.com/oauth/authorize";

        /// <summary>
        ///     Generates the authorize URI.
        ///     Then call GetTokens(string) after get the pin code.
        /// </summary>
        /// <returns>
        ///     The authorize URI.
        /// </returns>
        /// <param name='consumer_key'>
        ///     Consumer key.
        /// </param>
        /// <param name='consumer_secret'>
        ///     Consumer secret.
        /// </param>
        public static OAuthSession Authorize(string consumerKey, string consumerSecret)
        {
            var header = Tokens.Create(consumerKey, consumerSecret, null, null)
                .CreateAuthorizationHeader(MethodType.Get, RequestTokenUrl, null);
            var dic = from x in Request.HttpGet(RequestTokenUrl, null, header).Use()
                      from y in new StreamReader(x).Use()
                      select y.ReadToEnd()
                              .Split('&')
                              .Where(z => z.Contains('='))
                              .Select(z => z.Split('='))
                              .ToDictionary(z => z[0], z => z[1]);
            return new OAuthSession()
            {
                RequestToken = dic["oauth_token"],
                RequestTokenSecret = dic["oauth_token_secret"],
                ConsumerKey = consumerKey,
                ConsumerSecret = consumerSecret
            };
        }

        /// <summary>
        ///     Gets the OAuth tokens.
        ///     Be sure to call GenerateAuthUri(string,string) before call this.
        /// </summary>
        /// <param name='pin'>
        ///     Pin code.
        /// </param>
        /// <param name~'session'>
        ///     OAuth session.
        /// </para>
        /// <returns>
        ///     The tokens.
        /// </returns>
        public static Tokens GetTokens(this OAuthSession session, string pin)
        {
            var prm = new Dictionary<string, object>() { { "oauth_verifier", pin } };
            var header = Tokens.Create(session.ConsumerKey, session.ConsumerSecret, session.RequestToken, session.RequestTokenSecret)
                .CreateAuthorizationHeader(MethodType.Get, AccessTokenUrl, prm);
            var dic = from x in Request.HttpGet(AccessTokenUrl, prm, header).Use()
                      from y in new StreamReader(x).Use()
                      select y.ReadToEnd()
                              .Split('&')
                              .Where(z => z.Contains('='))
                              .Select(z => z.Split('='))
                              .ToDictionary(z => z[0], z => z[1]);
            return Tokens.Create(session.ConsumerKey, session.ConsumerSecret,
                dic["oauth_token"], dic["oauth_token_secret"], long.Parse(dic["user_id"]), dic["screen_name"]);
        }
    }

    public static class OAuth2
    {
        /// <summary>
        /// The access token URL.
        /// </summary>
        static readonly string AccessTokenUrl = "https://api.twitter.com/oauth2/token";
        /// <summary>
        /// The URL to revoke a OAuth2 Bearer Token.
        /// </summary>
        static readonly string InvalidateTokenUrl = "https://api.twitter.com/oauth2/invalidate_token";

        private static string CreateCredentials(string consumerKey, string consumerSecret)
        {
            return "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(consumerKey + ":" + consumerSecret));
        }

        /// <summary>
        /// Gets the OAuth 2 Bearer Token.
        /// </summary>
        /// <param name="consumerKey">Consumer key.</param>
        /// <param name="consumerSecret">Consumer secret.</param>
        /// <returns>The tokens.</returns>
        public static OAuth2Tokens GetToken(string consumerKey, string consumerSecret)
        {
            var token = from x in Request.HttpPost(
                            AccessTokenUrl,
                            new Dictionary<string, object>() { { "grant_type", "client_credentials" } }, //  At this time, only client_credentials is allowed.
                            CreateCredentials(consumerKey, consumerSecret),
                            true).Use()
                        from y in new StreamReader(x).Use()
                        select (string)JObject.Parse(y.ReadToEnd())["access_token"];
            return OAuth2Tokens.Create(consumerKey, consumerSecret, token);
        }

        /// <summary>
        /// Invalidates the OAuth 2 Bearer Token.
        /// </summary>
        /// <param name="tokens">An instance of OAuth2Tokens.</param>
        /// <returns>Invalidated token.</returns>
        public static string InvalidateToken(this OAuth2Tokens tokens)
        {
            return from x in Request.HttpPost(
                       InvalidateTokenUrl,
                       new Dictionary<string, object>() { { "access_token", Uri.UnescapeDataString(tokens.BearerToken) } },
                       CreateCredentials(tokens.ConsumerKey, tokens.ConsumerSecret),
                       true).Use()
                   from y in new StreamReader(x).Use()
                   select (string)JObject.Parse(y.ReadToEnd())["access_token"];
        }
    }

    /// <summary>
    /// Sends a request to Twitter and some other web services.
    /// </summary>
    internal static class Request
    {
        private static void ConfigureServerPointManager()
        {
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.ServerCertificateValidationCallback
                  = (_, __, ___, ____) => true;
        }

        /// <summary>
        /// Sends a GET request.
        /// </summary>
        /// <returns>The response.</returns>
        /// <param name="url">URL.</param>
        /// <param name="prm">Parameters.</param>
        internal static Stream HttpGet(string url, IDictionary<string, object> prm, string authorizationHeader)
        {
            ConfigureServerPointManager();
            if(prm == null) prm = new Dictionary<string, object>();
            var req = WebRequest.Create(url + '?' +
                string.Join("&", prm.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value.ToString())))
            );
            req.Headers.Add(HttpRequestHeader.Authorization, authorizationHeader);
            return req.GetResponse().GetResponseStream();
        }

        /// <summary>
        /// Sends a POST request.
        /// </summary>
        /// <returns>The response.</returns>
        /// <param name="url">URL.</param>
        /// <param name="prm">Parameters.</param>
        /// <param name="response">If it set false, won't try to get any responses and will return null.</param>
        internal static Stream HttpPost(string url, IDictionary<string, object> prm, string authorizationHeader, bool response)
        {
            if(prm == null) prm = new Dictionary<string, object>();
            var data = Encoding.UTF8.GetBytes(
                string.Join("&", prm.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value.ToString()))));
            ConfigureServerPointManager();
            var req = WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            req.ContentLength = data.Length;
            req.Headers.Add(HttpRequestHeader.Authorization, authorizationHeader);
            using(var reqstr = req.GetRequestStream())
                reqstr.Write(data, 0, data.Length);
            return response ? req.GetResponse().GetResponseStream() : null;
        }

        /// <summary>
        /// Sends a POST request with multipart/form-data.
        /// </summary>
        /// <returns>The response.</returns>
        /// <param name="url">URL.</param>
        /// <param name="prm">Parameters.</param>
        /// <param name="response">If it set false, won't try to get any responses and will return null.</param>
        internal static Stream HttpPostWithMultipartFormData(string url, IDictionary<string, object> prm, string authorizationHeader, bool response)
        {
            ConfigureServerPointManager();
            var boundary = Guid.NewGuid().ToString();
            var req = WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "multipart/form-data;boundary=" + boundary;
            req.Headers.Add(HttpRequestHeader.Authorization, authorizationHeader);
            using(var reqstr = req.GetRequestStream())
            {
                Action<string> writeStr = s =>
                {
                    var bytes = Encoding.UTF8.GetBytes(s);
                    reqstr.Write(bytes, 0, bytes.Length);
                };

                prm.ForEach(x =>
                {
                    var valueStream = x.Value as Stream;
                    var valueBytes = x.Value as IEnumerable<byte>;
                    var valueFile = x.Value as FileInfo;
                    var valueString = x.Value.ToString();

                    writeStr("--" + boundary + "\r\n");
                    if(valueStream != null || valueBytes != null || valueFile != null)
                        writeStr("Content-Type: application/octet-stream\r\n");
                    writeStr(String.Format(@"Content-Disposition: form-data; name=""{0}""", x.Key));
                    if(valueFile != null)
                        writeStr(String.Format(@"; filename=""{0}""", valueFile.Name));
                    else if(valueStream != null || valueBytes != null)
                        writeStr(@"; filename=""file""");
                    writeStr("\r\n\r\n");

                    if(valueFile != null)
                        valueStream = valueFile.OpenRead();
                    if(valueStream != null)
                    {
                        while(true)
                        {
                            var buffer = new byte[4096];
                            var count = valueStream.Read(buffer, 0, buffer.Length);
                            if (count == 0) break;
                            reqstr.Write(buffer, 0, count);
                        }
                    }
                    else if(valueBytes != null)
                        valueBytes.ForEach(b => reqstr.WriteByte(b));
                    else
                        writeStr(valueString);

                    if(valueFile != null)
                        valueStream.Close();

                    writeStr("\r\n");
                });
                writeStr("--" + boundary + "--");
            }
            return response ? req.GetResponse().GetResponseStream() : null;
        }

        /// <summary>
        /// Generates the signature.
        /// </summary>
        /// <returns>The signature.</returns>
        /// <param name="t">Tokens.</param>
        /// <param name="httpMethod">The http method.</param>
        /// <param name="url">the URL.</param>
        /// <param name="prm">Parameters.</param>
        internal static string GenerateSignature(Tokens t, string httpMethod, string url, SortedDictionary<string, string> prm)
        {
            using(var hs1 = new HMACSHA1())
            {
                hs1.Key = Encoding.UTF8.GetBytes(
                    string.Format("{0}&{1}", UrlEncode(t.ConsumerSecret),
                                  UrlEncode(t.AccessTokenSecret) ?? ""));
                var hash = hs1.ComputeHash(
                    System.Text.Encoding.UTF8.GetBytes(
                    string.Format("{0}&{1}&{2}", httpMethod, UrlEncode(url),
                                      UrlEncode(prm.Select(x => string.Format("{0}={1}", UrlEncode(x.Key), UrlEncode(x.Value)))
                                         .JoinToString("&")))));
                return Convert.ToBase64String(hash);
            }
        }

        /// <summary>
        /// Generates the parameters.
        /// </summary>
        /// <returns>The parameters.</returns>
        /// <param name="ConsumerKey ">Consumer key.</param>
        /// <param name="token">Token.</param>
        internal static SortedDictionary<string, string> GenerateParameters(string consumerKey, string token)
        {
            var ret = new SortedDictionary<string, string>() {
                {"oauth_consumer_key", consumerKey},
                {"oauth_signature_method", "HMAC-SHA1"},
                {"oauth_timestamp", ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0))
                    .TotalSeconds).ToString()},
                {"oauth_nonce", new Random().Next(int.MinValue, int.MaxValue).ToString("X")},
                {"oauth_version", "1.0"}
            };
            if(!string.IsNullOrEmpty(token))
                ret.Add("oauth_token", token);
            return ret;
        }

        /// <summary>
        /// Encodes the specified text.
        /// </summary>
        /// <returns>The encoded text.</returns>
        /// <param name="text">Text.</param>
        internal static string UrlEncode(string text)
        {
            if(string.IsNullOrEmpty(text))
                return null;
            return Encoding.UTF8.GetBytes(text)
                .Select(x => x < 0x80 && "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~"
                        .Contains((char)x) ? ((char)x).ToString() : ('%' + x.ToString("X2")))
                .JoinToString();
        }
    }

    /// <summary>
    /// Properties of CoreTweet.
    /// </summary>
    public class Property
    {
        static string _apiversion = "1.1";
        /// <summary>
        /// The version of the Twitter API.
        /// To change this value is not recommended but allowed. 
        /// </summary>
        public static string ApiVersion { get { return _apiversion; } set { _apiversion = value; } }
    }
}


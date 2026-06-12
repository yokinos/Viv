using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Viv.Delusion;
using Viv.Delusion.Extension;

namespace Viv.Echo.Http
{
    public class VivHttpClient : IVivHttpService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public VivHttpClient(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        public async Task<HttpResult<T>> GetAsync<T>(string url, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            return await SendAsync<T>(HttpMethod.Get, url, null, headers, cancellationToken);
        }

        public async Task<HttpResult<T>> GetAsync<T>(string url, Dictionary<string, string>? query = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            if (query != null && query.Count > 0)
            {
                url = BuildQueryUrl(url, query);
            }

            return await SendAsync<T>(HttpMethod.Get, url, null, headers, cancellationToken);
        }

        public async Task<HttpResult<T>> GetAsync<T>(string url, object query, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            var properties = VivTypeReflectionCache.GetPropertieList(query.GetType());
            var queryDict = new Dictionary<string, string>();

            foreach (var prop in properties)
            {
                var value = prop.GetValue(query);
                queryDict[prop.Name] = value?.ToString() ?? string.Empty;
            }

            return await GetAsync<T>(url, queryDict, headers, cancellationToken);
        }

        public async Task<HttpResult<T>> PostAsync<T>(string url, object data, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            return await SendAsync<T>(HttpMethod.Post, url, data, headers, cancellationToken);
        }

        public async Task<HttpResult<T>> UploadFileAsync<T>(string url, string filePath, string fieldName = "file", Dictionary<string, string>? formData = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            var fileName = Path.GetFileName(filePath);
            return await UploadFileAsync<T>(url, fileBytes, fileName, fieldName, formData, headers, cancellationToken);
        }

        public async Task<HttpResult<T>> UploadFileAsync<T>(string url, byte[] fileBytes, string fileName, string fieldName = "file", Dictionary<string, string>? formData = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream(fileBytes);
            return await UploadFileStreamAsync<T>(url, ms, fileName, fieldName, formData, headers, cancellationToken);
        }

        public async Task<HttpResult<T>> UploadFileAsync<T>(string url, Stream stream, string fileName, string fieldName = "file", Dictionary<string, string>? formData = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        {
            return await UploadFileStreamAsync<T>(url, stream, fileName, fieldName, formData, headers, cancellationToken);
        }

        #region Core

        private async Task<HttpResult<T>> UploadFileStreamAsync<T>(string url, Stream stream, string fileName, string fieldName, Dictionary<string, string>? formData, Dictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var client = _httpClientFactory.CreateClient();

                using var content = new MultipartFormDataContent();
                using var streamContent = new StreamContent(stream);

                streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                content.Add(streamContent, fieldName, fileName);

                if (formData != null)
                {
                    foreach (var (key, value) in formData)
                    {
                        content.Add(new StringContent(value ?? string.Empty), key);
                    }
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                ApplyHeaders(request, headers);

                var response = await client.SendAsync(request, cancellationToken);
                return await BuildResultAsync<T>(response, stopwatch, cancellationToken);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new HttpResult<T>
                {
                    IsSuccess = false,
                    StatusCode = HttpStatusCode.InternalServerError,
                    Message = ex.Message,
                    ElapsedTime = stopwatch.ElapsedMilliseconds
                };
            }
        }

        private async Task<HttpResult<T>> SendAsync<T>(HttpMethod method, string url, object? body, Dictionary<string, string>? headers, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var client = _httpClientFactory.CreateClient();

                using var request = new HttpRequestMessage(method, url);
                ApplyHeaders(request, headers);

                if (body != null)
                {
                    var json = body.ToJson();
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                var response = await client.SendAsync(request, cancellationToken);
                return await BuildResultAsync<T>(response, stopwatch, cancellationToken);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new HttpResult<T>
                {
                    IsSuccess = false,
                    StatusCode = HttpStatusCode.InternalServerError,
                    Message = ex.Message,
                    ElapsedTime = stopwatch.ElapsedMilliseconds
                };
            }
        }

        private static void ApplyHeaders(HttpRequestMessage request, Dictionary<string, string>? headers)
        {
            if (headers == null) return;

            foreach (var (key, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        private static async Task<HttpResult<T>> BuildResultAsync<T>(HttpResponseMessage response, Stopwatch sw, CancellationToken cancellationToken)
        {
            var result = new HttpResult<T>
            {
                StatusCode = response.StatusCode
            };

            try
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                result.ResponseJson = json;

                if (response.IsSuccessStatusCode)
                {
                    result.IsSuccess = true;
                    result.Response = json.As<T>();
                }
                else
                {
                    result.IsSuccess = false;
                    result.Message = $"请求失败，状态码：{(int)response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = ex.Message;
            }
            finally
            {
                sw.Stop();
                result.ElapsedTime = sw.ElapsedMilliseconds;
            }

            return result;
        }

        private static string BuildQueryUrl(string url, Dictionary<string, string> query)
        {
            var uriBuilder = new UriBuilder(url);
            var existingQuery = uriBuilder.Query.TrimStart('?');

            var newQuery = string.Join("&", query.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value ?? string.Empty)}"));

            uriBuilder.Query = string.IsNullOrEmpty(existingQuery) ? newQuery : $"{existingQuery}&{newQuery}";
            return uriBuilder.ToString();
        }

        #endregion
    }
}

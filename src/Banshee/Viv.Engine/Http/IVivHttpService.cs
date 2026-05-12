using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Viv.Engine.Http
{
    public interface IVivHttpService
    {
        Task<HttpResult<T>> GetAsync<T>(string url, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default);

        Task<HttpResult<T>> GetAsync<T>(string url, Dictionary<string, string>? query = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default);

        Task<HttpResult<T>> GetAsync<T>(string url, object query, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default);

        Task<HttpResult<T>> PostAsync<T>(string url, object data, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default);

        Task<HttpResult<T>> UploadFileAsync<T>(string url, string filePath, string fieldName = "file", Dictionary<string, string>? formData = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default);

        Task<HttpResult<T>> UploadFileAsync<T>(string url, byte[] fileBytes, string fileName, string fieldName = "file", Dictionary<string, string>? formData = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default);

        Task<HttpResult<T>> UploadFileAsync<T>(string url, Stream stream, string fileName, string fieldName = "file", Dictionary<string, string>? formData = null, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default);
    }
}

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Viv.Contracts.Interface
{
    /// <summary>
    /// S3 对象存储服务接口。
    /// </summary>
    public interface IS3Service
    {
        /// <summary>
        /// 生成预签名上传 URL，前端可用此 URL 直接 PUT 文件到 S3，不经过后端。
        /// </summary>
        /// <param name="objectKey">文件在桶中的路径，如 "images/avatar/123.jpg"</param>
        /// <param name="contentType">MIME 类型，如 "image/jpeg"</param>
        /// <param name="expirationSeconds">URL 有效时间（秒），不传使用默认值</param>
        /// <param name="cancellationToken"></param>
        Task<string> GetPresignedUploadUrlAsync(string objectKey, string contentType, int? expirationSeconds = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 生成预签名下载 URL，用于临时授权访问私有文件。
        /// </summary>
        /// <param name="objectKey">文件在桶中的路径</param>
        /// <param name="expirationSeconds">URL 有效时间（秒），不传使用默认值</param>
        /// <param name="cancellationToken"></param>
        Task<string> GetPresignedDownloadUrlAsync(string objectKey, int? expirationSeconds = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 后端直接上传文件流到 S3。
        /// </summary>
        /// <param name="objectKey">文件在桶中的路径</param>
        /// <param name="stream">文件流</param>
        /// <param name="contentType">MIME 类型</param>
        /// <param name="cancellationToken"></param>
        /// <returns>是否上传成功</returns>
        Task<bool> UploadFileAsync(string objectKey, Stream stream, string contentType, CancellationToken cancellationToken = default);

        /// <summary>
        /// 后端直接从 S3 下载文件到内存流，返回的 Stream 由调用方负责释放。
        /// </summary>
        /// <param name="objectKey">文件在桶中的路径</param>
        /// <param name="cancellationToken"></param>
        Task<Stream> DownloadFileAsync(string objectKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除 S3 中的文件。
        /// </summary>
        /// <param name="objectKey">文件在桶中的路径</param>
        /// <param name="cancellationToken"></param>
        /// <returns>是否删除成功</returns>
        Task<bool> DeleteFileAsync(string objectKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查文件是否存在。
        /// </summary>
        /// <param name="objectKey">文件在桶中的路径</param>
        /// <param name="cancellationToken"></param>
        /// <returns>文件存在返回 true，不存在返回 false</returns>
        Task<bool> FileExistsAsync(string objectKey, CancellationToken cancellationToken = default);
    }
}

using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Viv.Contracts.Interface;
using Viv.Contracts.Options;
using Viv.Delusion;

namespace Viv.Sandrone.Impl
{
    /// <summary>
    /// S3 文件服务实现。
    /// </summary>
    public sealed class VivS3Service : IS3Service, IDisposable
    {
        private readonly AmazonS3Client _s3Client;
        private readonly S3Options _options;

        public VivS3Service()
        {
            _options = VivConfigRegistry.Get<S3Options>()
                        ?? throw new InvalidOperationException("未找到 S3Options 配置，请检查 viv.config.json 中 S3Option 节点。");
            var config = CreateS3Config(_options);
            _s3Client = new AmazonS3Client(_options.AccessKey, _options.SecretKey, config);
        }

        /// <summary>
        /// 创建 S3 客户端配置。
        /// </summary>
        private static AmazonS3Config CreateS3Config(S3Options options)
        {
            var endpoint = BuildEndpoint(options);

            var config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true,
                UseHttp = endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            };

            if (!string.IsNullOrWhiteSpace(options.Region))
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
            }

            return config;
        }

        /// <summary>
        /// 构造完整的 S3 Endpoint。
        /// </summary>
        private static string BuildEndpoint(S3Options options)
        {
            var rawEndpoint = options.Endpoint.Trim();

            if (!rawEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !rawEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                rawEndpoint = $"{(options.UseHttps ? "https" : "http")}://{rawEndpoint}";
            }

            if (!Uri.TryCreate(rawEndpoint, UriKind.Absolute, out var endpointUri))
            {
                throw new InvalidOperationException($"S3 Endpoint 格式不正确：{options.Endpoint}");
            }

            var builder = new UriBuilder(endpointUri);

            // 如果 Endpoint 没有明确指定端口，则使用配置中的端口。
            if (endpointUri.IsDefaultPort)
            {
                builder.Port = options.Port;
            }

            return builder.Uri.ToString().TrimEnd('/');
        }

        /// <summary>
        /// 生成预签名上传 URL。
        /// </summary>
        public async Task<string> GetPresignedUploadUrlAsync(string objectKey, string contentType, int? expirationSeconds = null, CancellationToken cancellationToken = default)
        {
            ValidateObjectKey(objectKey);

            if (string.IsNullOrWhiteSpace(contentType))
            {
                throw new ArgumentException("contentType 不能为空。", nameof(contentType));
            }

            var expireSeconds = expirationSeconds ?? _options.UploadPresignExpireSeconds;
            ValidateExpiration(expireSeconds);

            var request = new GetPreSignedUrlRequest
            {
                BucketName = _options.UploadBucket,
                Key = objectKey,
                Verb = HttpVerb.PUT,
                ContentType = contentType,
                Expires = DateTime.UtcNow.AddSeconds(expireSeconds)
            };

            return await _s3Client.GetPreSignedURLAsync(request).ConfigureAwait(false);
        }

        /// <summary>
        /// 生成预签名下载 URL。
        /// </summary>
        public async Task<string> GetPresignedDownloadUrlAsync(string objectKey, int? expirationSeconds = null, CancellationToken cancellationToken = default)
        {
            ValidateObjectKey(objectKey);

            var expireSeconds = expirationSeconds ?? _options.DownloadPresignExpireSeconds;
            ValidateExpiration(expireSeconds);

            var request = new GetPreSignedUrlRequest
            {
                BucketName = _options.UploadBucket,
                Key = objectKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddSeconds(expireSeconds)
            };

            return await _s3Client.GetPreSignedURLAsync(request).ConfigureAwait(false);
        }

        /// <summary>
        /// 后端直接上传文件流。
        /// </summary>
        public async Task<bool> UploadFileAsync(string objectKey, Stream stream, string contentType, CancellationToken cancellationToken = default)
        {
            ValidateObjectKey(objectKey);

            ArgumentNullException.ThrowIfNull(stream);

            if (!stream.CanRead)
            {
                throw new ArgumentException("上传流必须支持读取。", nameof(stream));
            }

            if (string.IsNullOrWhiteSpace(contentType))
            {
                throw new ArgumentException("contentType 不能为空。", nameof(contentType));
            }

            var request = new PutObjectRequest
            {
                BucketName = _options.UploadBucket,
                Key = objectKey,
                InputStream = stream,
                ContentType = contentType,
                AutoCloseStream = false,
                AutoResetStreamPosition = false
            };

            var response = await _s3Client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
            return response.HttpStatusCode == HttpStatusCode.OK;
        }

        /// <summary>
        /// 后端直接下载文件流。
        /// </summary>
        public async Task<Stream> DownloadFileAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            ValidateObjectKey(objectKey);

            var request = new GetObjectRequest
            {
                BucketName = _options.UploadBucket,
                Key = objectKey
            };

            using var response = await _s3Client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
            var memoryStream = new MemoryStream();
            await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            memoryStream.Position = 0;
            return memoryStream;
        }

        /// <summary>
        /// 删除指定文件。
        /// </summary>
        public async Task<bool> DeleteFileAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            ValidateObjectKey(objectKey);

            var request = new DeleteObjectRequest
            {
                BucketName = _options.UploadBucket,
                Key = objectKey
            };

            var response = await _s3Client.DeleteObjectAsync(request, cancellationToken).ConfigureAwait(false);

            return response.HttpStatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;
        }

        /// <summary>
        /// 检查文件是否存在。
        /// </summary>
        public async Task<bool> FileExistsAsync(string objectKey, CancellationToken cancellationToken = default)
        {
            ValidateObjectKey(objectKey);

            var request = new GetObjectMetadataRequest
            {
                BucketName = _options.UploadBucket,
                Key = objectKey
            };

            try
            {
                await _s3Client.GetObjectMetadataAsync(request, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        /// <summary>
        /// 验证对象 Key。
        /// </summary>
        private static void ValidateObjectKey(string objectKey)
        {
            if (string.IsNullOrWhiteSpace(objectKey))
            {
                throw new ArgumentException("objectKey 不能为空。", nameof(objectKey));
            }

            if (objectKey.Length > 1024)
            {
                throw new ArgumentException("objectKey 长度不能超过 1024 个字符。", nameof(objectKey));
            }
        }

        /// <summary>
        /// 验证预签名 URL 有效时间。
        /// </summary>
        private static void ValidateExpiration(int expirationSeconds)
        {
            if (expirationSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expirationSeconds), "有效时间必须大于 0 秒。");
            }
        }

        /// <summary>
        /// 释放 S3 客户端（应用关闭时由 DI 容器调用）。
        /// </summary>
        public void Dispose()
        {
            _s3Client.Dispose();
        }
    }
}

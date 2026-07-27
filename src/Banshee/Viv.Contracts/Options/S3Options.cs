namespace Viv.Contracts.Options
{
    /// <summary>
    /// RustFS S3 兼容对象存储配置。
    /// </summary>
    public sealed class S3Options
    {
        /// <summary>
        /// RustFS 访问地址，例如：
        /// http://127.0.0.1:9000
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// 是否使用 HTTPS。
        /// </summary>
        public bool UseHttps { get; set; } = false;

        /// <summary>
        /// RustFS 端口。
        /// </summary>
        public int Port { get; set; } = 9000;

        /// <summary>
        /// RustFS Access Key。
        /// </summary>
        public string AccessKey { get; set; } = string.Empty;

        /// <summary>
        /// RustFS Secret Key。
        /// </summary>
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>
        /// S3 区域。
        /// </summary>
        public string Region { get; set; } = "us-east-1";

        /// <summary>
        /// 默认存储桶。
        /// </summary>
        public string UploadBucket { get; set; } = string.Empty;

        /// <summary>
        /// 上传预签名 URL 有效时间，单位：秒。
        /// </summary>
        public int UploadPresignExpireSeconds { get; set; } = 900;

        /// <summary>
        /// 下载预签名 URL 有效时间，单位：秒。
        /// </summary>
        public int DownloadPresignExpireSeconds { get; set; } = 900;
    }
}

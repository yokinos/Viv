namespace Viv.Herta.Core.Models
{
    public enum MediaFileType
    {
        Image = 1,
        Video = 2,
        Audio = 3,
        Document = 4
    }

    public class MediaFileInfo
    {
        public string Url { get; set; } = string.Empty;
        public MediaFileType FileType { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
    }
}

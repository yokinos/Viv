using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MiniExcelLibs;
using Viv.Delusion.Extension;

namespace Viv.Sandrone.Magic
{
    /// <summary>
    /// MiniExcel 封装 — 导入导出
    /// </summary>
    public static class MiniExcelMagic
    {
        /// <summary>
        /// 保存到本地文件（自动创建目录,自动覆盖文件）
        /// </summary>
        public static async Task SaveAsAsync<T>(string path,
            IEnumerable<T> data,
            bool printHeader = true,
            string sheetName = "Sheet1",
            ExcelType excelType = ExcelType.XLSX,
            CancellationToken cancellationToken = default) where T : class, new()
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await MiniExcel.SaveAsAsync(path, data, printHeader, sheetName, excelType, null, true, cancellationToken);
        }

        /// <summary>
        /// 直接返回文件流供 Controller 下载（不落盘）
        /// </summary>
        /// <param name="fileName">前端下载显示文件名（含 .xlsx 后缀）</param>
        /// <param name="data">数据源</param>
        /// <param name="printHeader"></param>
        /// <param name="sheetName"></param>
        /// <param name="excelType"></param>
        /// <param name="cancellationToken"></param>
        public static async Task<FileStreamResult> CreateDownloadResultAsync<T>(string fileName,
            IEnumerable<T> data,
            bool printHeader = true,
            string sheetName = "Sheet1",
            ExcelType excelType = ExcelType.XLSX,
            CancellationToken cancellationToken = default) where T : class, new()
        {
            var ms = new MemoryStream();
            await MiniExcel.SaveAsAsync(ms, data, printHeader, sheetName, excelType, null, cancellationToken);
            ms.Seek(0, SeekOrigin.Begin);

            return new FileStreamResult(ms, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                FileDownloadName = fileName
            };
        }

        /// <summary>
        /// 读取本地 Excel 全部数据
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="sheetName">工作表名，null 取第一个 sheet</param>
        /// <param name="excelType"></param>
        /// <param name="startCell"></param>
        /// <param name="cancellationToken"></param>
        public static async Task<List<T>> QueryAsync<T>(string filePath,
            string? sheetName = null,
            ExcelType excelType = ExcelType.UNKNOWN,
            string startCell = "A1",
            CancellationToken cancellationToken = default) where T : class, new()
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("文件不存在", filePath);

            using var fs = File.OpenRead(filePath);
            var result = await MiniExcel.QueryAsync<T>(fs, sheetName, excelType, startCell, null, cancellationToken);
            return result?.ToList() ?? [];
        }

        /// <summary>
        /// 分页读取本地 Excel
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页条数</param>
        /// <param name="sheetName">工作表名</param>
        /// <param name="excelType"></param>
        /// <param name="startCell"></param>
        /// <param name="cancellationToken"></param>
        public static async Task<List<T>> QueryAsync<T>(string filePath,
            int pageIndex = 1,
            int pageSize = 100,
            string? sheetName = null,
            ExcelType excelType = ExcelType.UNKNOWN,
            string startCell = "A1",
            CancellationToken cancellationToken = default) where T : class, new()
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("文件不存在", filePath);
            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1) pageSize = 100;

            using var fs = File.OpenRead(filePath);
            var all = await MiniExcel.QueryAsync<T>(fs, sheetName, excelType, startCell, null, cancellationToken);
            if (all.IsNullOrEmpty()) return [];

            return all.Page(pageSize, pageIndex).ToList();
        }

        /// <summary>
        /// 读取上传文件全部数据
        /// </summary>
        /// <param name="file">前端上传文件</param>
        /// <param name="sheetName">工作表名</param>
        /// <param name="excelType"></param>
        /// <param name="startCell"></param>
        /// <param name="cancellationToken"></param>
        public static async Task<List<T>> QueryAsync<T>(IFormFile file,
            string? sheetName = null,
            ExcelType excelType = ExcelType.UNKNOWN,
            string startCell = "A1",
            CancellationToken cancellationToken = default) where T : class, new()
        {
            using var stream = file.OpenReadStream();
            var result = await MiniExcel.QueryAsync<T>(stream, sheetName, excelType, startCell, null, cancellationToken);
            return result?.ToList() ?? [];
        }

        /// <summary>
        /// 分页读取上传文件
        /// </summary>
        /// <param name="file">前端上传文件</param>
        /// <param name="pageIndex">页码，从 1 开始</param>
        /// <param name="pageSize">每页条数</param>
        /// <param name="sheetName">工作表名</param>
        /// <param name="excelType"></param>
        /// <param name="startCell"></param>
        /// <param name="cancellationToken"></param>
        public static async Task<List<T>> QueryAsync<T>(IFormFile file,
            int pageIndex = 1,
            int pageSize = 100,
            string? sheetName = null,
            ExcelType excelType = ExcelType.UNKNOWN,
            string startCell = "A1",
            CancellationToken cancellationToken = default) where T : class, new()
        {
            if (pageIndex < 1) pageIndex = 1;
            if (pageSize < 1) pageSize = 100;

            using var stream = file.OpenReadStream();
            var all = await MiniExcel.QueryAsync<T>(stream, sheetName, excelType, startCell, null, cancellationToken);
            if (all.IsNullOrEmpty()) return [];

            return all.Page(pageSize, pageIndex).ToList();
        }
    }
}

using System;

namespace ContractManager.Models
{
    /// <summary>
    /// GitHub Release 解析出的更新信息。
    /// </summary>
    public class UpdateInfo
    {
        /// <summary>版本号（已去除 tag 的 v 前缀，如 "2.3.0"）。</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>Release body（changelog），可能为空。</summary>
        public string Changelog { get; set; } = string.Empty;

        /// <summary>首个 asset 的 browser_download_url。</summary>
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>是否预发布版本。</summary>
        public bool IsPrerelease { get; set; }

        /// <summary>Release 发布时间（UTC），解析失败为 DateTime.MinValue。</summary>
        public DateTime ReleaseDate { get; set; }
    }
}

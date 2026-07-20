using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OCRbyLLoneBot
{
    /// <summary>黑白名单持久化JSON模型（合并单黑名单）</summary>
    public class BotBlackConfig
    {
        /// <summary>管理员唯一QQ</summary>
        public long AdminQQ { get; set; }
        /// <summary>统一黑名单：机器人账号 + 被封禁普通用户全部存在这里</summary>
        public List<long> AllBlackQqList { get; set; } = new List<long>();
    }
}

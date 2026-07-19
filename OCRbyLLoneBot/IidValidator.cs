using System.Text.RegularExpressions;

/// <summary>
/// IID 合法性校验工具类（整合JS校验逻辑，原有功能全部保留，新增中文错误解析）
/// </summary>
public static class IidValidator
{
    #region 常量定义
    public const int Length54 = 54;
    public const int Length63 = 63;
    public const int BlockCount = 9;
    #endregion

    #region 对外主校验（原有逻辑完整保留，内置自动清洗）
    /// <summary>
    /// 完整校验IID（自动清洗原始输入：剔除所有非数字字符）
    /// </summary>
    /// <param name="rawInput">原始IID文本，允许带空格、横杠、文字、换行</param>
    /// <returns>完整校验结果（含清洗后纯数字iid、错误码、失败区块）</returns>
    public static IidValidationResult ValidateIID(string rawInput)
    {
        // 1. 内置清洗逻辑：统一去除所有非数字
        string cleaned = string.IsNullOrEmpty(rawInput)
            ? string.Empty
            : Regex.Replace(rawInput, @"\D", "");

        // 空输入
        if (string.IsNullOrEmpty(cleaned))
            return new IidValidationResult(false) { Error = "empty_iid", CleanedIid = string.Empty };

        // 非纯数字兜底（清洗后理论不会触发，兼容外部直接传入未清洗字符串场景）
        if (!IsAllDigits(cleaned))
        {
            return new IidValidationResult(false)
            {
                Error = "not_numeric",
                CleanedIid = cleaned
            };
        }

        // 长度校验
        if (cleaned.Length != Length54 && cleaned.Length != Length63)
            return new IidValidationResult(false)
            {
                Error = "invalid_length",
                Length = cleaned.Length,
                CleanedIid = cleaned
            };

        // 拆分9个区块逐块校验
        int blockSize = cleaned.Length / BlockCount;
        var failedBlocks = new List<FailedBlock>();
        for (int i = 0; i < BlockCount; i++)
        {
            string block = cleaned.Substring(i * blockSize, blockSize);
            if (!CheckBlock(block))
            {
                failedBlocks.Add(new FailedBlock
                {
                    Index = i + 1,
                    Value = block
                });
            }
        }

        // 合法结果携带清洗后的纯数字IID
        return new IidValidationResult(failedBlocks.Count == 0)
        {
            FailedBlocks = failedBlocks,
            CleanedIid = cleaned
        };
    }
    #endregion

    /// <summary>
    /// 提取带横杠/空格混合分隔的9段式IID，兼容横杠、空格混用
    /// </summary>
    public static List<string> ExtractSplitIidWithSeparator(string raw)
    {
        HashSet<string> temp = new HashSet<string>();
        // 63位：7位一段，分隔符允许 - / 空格
        var match63Split = Regex.Matches(raw, @"(?:\d{7}[- ]){8}\d{7}");
        foreach (Match m in match63Split)
        {
            string numOnly = Regex.Replace(m.Value, @"[- ]", "");
            if (numOnly.Length == Length63)
                temp.Add(numOnly);
        }
        // 54位：6位一段，分隔符允许 - / 空格
        var match54Split = Regex.Matches(raw, @"(?:\d{6}[- ]){8}\d{6}");
        foreach (Match m in match54Split)
        {
            string numOnly = Regex.Replace(m.Value, @"[- ]", "");
            if (numOnly.Length == Length54)
                temp.Add(numOnly);
        }
        return temp.ToList();
    }

    #region 区块校验、辅助工具（原有逻辑完全保留）
    /// <summary>
    /// 校验单个IID区块合法性（与JS checkBlock逻辑1:1对齐）
    /// </summary>
    /// <param name="block">单个区块纯数字字符串</param>
    /// <returns>区块校验是否通过</returns>
    private static bool CheckBlock(string block)
    {
        if (string.IsNullOrEmpty(block) || block.Length < 2 || !IsAllDigits(block))
            return false;

        int checkDigit = block[^1] - '0';
        int sum = 0;
        for (int i = 0; i < block.Length - 1; i++)
        {
            int digit = block[i] - '0';
            sum += i % 2 == 0 ? digit : digit * 2;
        }
        return sum % 7 == checkDigit;
    }

    /// <summary>
    /// 判断字符串是否全部为数字
    /// </summary>
    private static bool IsAllDigits(string input)
    {
        foreach (char c in input)
            if (!char.IsDigit(c)) return false;
        return true;
    }
    #endregion

    #region 新增：对标JS getErrorText 错误码转中文提示（全新功能，不删旧代码）
    /// <summary>
    /// 错误码转可读中文提示（与JS getErrorText逻辑对齐，覆盖全部错误场景）
    /// </summary>
    /// <param name="validateResult">ValidateIID返回的校验结果</param>
    /// <returns>主提示文本 + 详情说明</returns>
    public static IidErrorText GetErrorText(IidValidationResult validateResult)
    {
        var res = new IidErrorText();

        // 无错误直接返回空提示
        if (validateResult.Valid)
        {
            res.MainText = string.Empty;
            res.DetailText = string.Empty;
            return res;
        }

        // 1. 空IID
        if (validateResult.Error == "empty_iid")
        {
            res.MainText = "⚠️ IID内容为空";
            res.DetailText = "未识别到有效IID数字内容，请检查输入";
        }
        // 2. 包含非数字字符
        else if (validateResult.Error == "not_numeric")
        {
            res.MainText = "⚠️ IID包含非数字字符";
            res.DetailText = "IID必须全部由数字组成，不能包含字母、符号、空格、横线等";
        }
        // 3. 长度错误
        else if (validateResult.Error == "invalid_length")
        {
            res.MainText = "⚠️ IID长度不正确";
            res.DetailText = $"当前清洗后长度：{validateResult.Length}位，合法IID仅支持54位或63位";
        }
        // 4. 区块校验失败
        else if (validateResult.FailedBlocks != null && validateResult.FailedBlocks.Any())
        {
            var blockDesc = string.Join("、", validateResult.FailedBlocks
                .Select(b => $"第{b.Index}区块({b.Value})"));

            res.MainText = $"⚠️ IID不合法，共{validateResult.FailedBlocks.Count}个区块校验失败";
            res.DetailText = $"校验失败区块：{blockDesc}，区块末尾校验码计算不匹配";
        }
        // 兜底未知错误
        else
        {
            res.MainText = "IID校验失败";
            res.DetailText = "未知校验错误，请检查输入内容";
        }

        return res;
    }
    #endregion



    #region 数据模型（原有模型保留，新增IidErrorText）
    /// <summary>
    /// IID完整校验结果模型（原有字段全部保留）
    /// </summary>
    public class IidValidationResult
    {
        public IidValidationResult(bool valid)
        {
            Valid = valid;
            FailedBlocks = new List<FailedBlock>();
            CleanedIid = string.Empty;
        }

        /// <summary>整体是否合法</summary>
        public bool Valid { get; set; }
        /// <summary>错误码：empty_iid / not_numeric / invalid_length</summary>
        public string? Error { get; set; }
        /// <summary>清洗后字符串长度，仅长度错误时有值</summary>
        public int Length { get; set; }
        /// <summary>校验失败的区块集合</summary>
        public List<FailedBlock> FailedBlocks { get; set; }
        /// <summary>清洗完成的标准纯数字IID</summary>
        public string CleanedIid { get; set; }
    }

    /// <summary>
    /// 单个失败区块信息
    /// </summary>
    public class FailedBlock
    {
        /// <summary>区块序号 1~9</summary>
        public int Index { get; set; }
        /// <summary>区块原始数字内容</summary>
        public string? Value { get; set; }
    }

    /// <summary>
    /// 新增：中文错误提示载体，对标JS返回结构 {mainText, detailText}
    /// </summary>
    public class IidErrorText
    {
        /// <summary>简短主错误提示（弹窗/Toast标题）</summary>
        public string MainText { get; set; } = string.Empty;
        /// <summary>详细错误说明（弹窗详情）</summary>
        public string DetailText { get; set; } = string.Empty;
    }
    #endregion


    #region 新增 OCR 脏文本清洗 & IID 提取（完全对齐JS逻辑，增量新增，不改动旧代码）


    /// <summary>
    /// 清洗OCR识别脏文本：剔除汉字、字母、符号、短数字碎片、客服电话等干扰内容
    /// 对齐 JS cleanOcrText 逻辑
    /// </summary>
    /// <param name="rawText">OCR原始识别多行文本</param>
    /// <returns>仅保留长数字片段的干净文本</returns>
    public static string CleanOcrText(string rawText)
    {
        if (string.IsNullOrEmpty(rawText))
            return string.Empty;

        string txt = rawText;
        // 1. 删除图片后缀标识 test jpg png jpeg
        txt = Regex.Replace(txt, @"test|jpg|png|jpeg", "", RegexOptions.IgnoreCase);
        // 2. 删除大小写英文字母 + 全部中文汉字
        txt = Regex.Replace(txt, @"[a-zA-Z\u4e00-\u9fa5]", "");
        // 3. 除数字、空白外，所有符号替换为空格
        txt = Regex.Replace(txt, @"[^\d\s]", " ");
        // 4. 连续多个空格/换行/制表统一替换为单个空格
        txt = Regex.Replace(txt, @"\s+", " ");
        // 5. 移除 400 / 800 开头客服完整号码
        txt = Regex.Replace(txt, @"\b(800|400)\d+\b", " ");
        // 6. 过滤1~6位孤立短数字碎片（日期、零散序列号干扰）
        txt = Regex.Replace(txt, @"\b\d{1,6}\b", " ");
        // 首尾去空格
        txt = txt.Trim();

        return txt;
    }

    /// <summary>
    /// 从OCR清洗后的文本中批量提取所有候选IID（54/63位纯数字）
    /// 对齐 JS extractIIDs 完整逻辑，自动去重
    /// </summary>
    /// <param name="ocrRawText">OCR原始识别文本</param>
    /// <returns>去重后的候选IID纯数字列表（未校验区块，仅格式筛选）</returns>
    public static List<string> ExtractIIDsFromOCR(string ocrRawText)
    {
        var results = new HashSet<string>(); // 自动去重
        string filteredText = CleanOcrText(ocrRawText);

        // 匹配带空格分段格式：7位空格分隔9段 / 6位空格分隔9段
        MatchCollection match63 = Regex.Matches(filteredText, @"(?:\d{7}\s){8}\d{7}");
        MatchCollection match54 = Regex.Matches(filteredText, @"(?:\d{6}\s){8}\d{6}");

        // 处理63位分段IID
        foreach (Match m in match63)
        {
            string noSpace = Regex.Replace(m.Value, @"\s", "");
            if (noSpace.Length == Length63)
                results.Add(noSpace);
        }
        // 处理54位分段IID
        foreach (Match m in match54)
        {
            string noSpace = Regex.Replace(m.Value, @"\s", "");
            if (noSpace.Length == Length54)
                results.Add(noSpace);
        }

        // 直接匹配连续54~63位长数字串
        MatchCollection longDigitMatch = Regex.Matches(filteredText, @"\d{54,63}");
        foreach (Match m in longDigitMatch)
        {
            string val = m.Value;
            if (val.Length == Length54 || val.Length == Length63)
                results.Add(val);
        }

        // 全文提取全部纯数字，滑动窗口截取63/54位片段
        string allPureDigit = Regex.Replace(filteredText, @"\D", "");
        // 63位滑动窗口
        for (int i = 0; i <= allPureDigit.Length - Length63; i++)
        {
            string seg = allPureDigit.Substring(i, Length63);
            results.Add(seg);
        }
        // 54位滑动窗口
        for (int i = 0; i <= allPureDigit.Length - Length54; i++)
        {
            string seg = allPureDigit.Substring(i, Length54);
            results.Add(seg);
        }

        // 再次严格过滤长度，转为列表返回
        return results.Where(x => x.Length == Length54 || x.Length == Length63).ToList();
    }

    /// <summary>
    /// 一站式：OCR文本清洗 → 提取候选IID → 全量校验返回合法IID
    /// </summary>
    /// <param name="ocrRawText">OCR原始识别内容</param>
    /// <returns>全部校验通过的标准IID列表</returns>
    public static List<string> GetValidIidFromOcr(string ocrRawText)
    {
        var candidateList = ExtractIIDsFromOCR(ocrRawText);
        var validIids = new List<string>();

        foreach (string candidate in candidateList)
        {
            var validateRes = ValidateIID(candidate);
            if (validateRes.Valid)
            {
                validIids.Add(validateRes.CleanedIid);
            }
        }
        return validIids;
    }
    #endregion
}
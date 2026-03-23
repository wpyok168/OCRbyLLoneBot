using System;
using System.Collections.Generic;

/// <summary>
/// IID 合法性校验工具类
/// </summary>
public static class IidValidator
{
    /// <summary>
    /// 校验IID单个区块合法性
    /// </summary>
    /// <param name="block">IID的单个区块</param>
    /// <returns>区块是否合法</returns>
    public static bool CheckBlock(string block)
    {
        // 空值检查 + 验证是否全为数字 + 长度至少2位
        if (string.IsNullOrEmpty(block) || !IsAllDigits(block) || block.Length < 2)
            return false;

        // 获取校验位（最后一位）
        int checkDigit = int.Parse(block[block.Length - 1].ToString());
        int sum = 0;

        // 遍历除校验位外的所有数字计算总和
        for (int i = 0; i < block.Length - 1; i++)
        {
            int digit = int.Parse(block[i].ToString());
            // 偶数索引直接加，奇数索引乘2再加
            sum += i % 2 == 0 ? digit : digit * 2;
        }

        // 校验总和模7是否等于校验位
        return sum % 7 == checkDigit;
    }

    /// <summary>
    /// 校验整个IID的合法性（增强版，返回具体错误信息）
    /// </summary>
    /// <param name="iid">待校验的IID字符串</param>
    /// <returns>校验结果对象</returns>
    public static IidValidationResult ValidateIID(string iid)
    {
        // 空值检查
        if (string.IsNullOrEmpty(iid))
            return new IidValidationResult(false) { Error = "empty_iid" };

        // 检查是否全为数字
        if (!IsAllDigits(iid))
            return new IidValidationResult(false) { Error = "not_numeric" };

        // 检查长度是否为54或63位
        if (iid.Length != 54 && iid.Length != 63)
            return new IidValidationResult(false)
            {
                Error = "invalid_length",
                Length = iid.Length
            };

        // 计算每个区块的大小（54位=6位/区块，63位=7位/区块）
        int blockSize = iid.Length / 9;
        var failedBlocks = new List<FailedBlock>();

        // 检查9个区块的合法性
        for (int i = 0; i < 9; i++)
        {
            // 截取当前区块
            string block = iid.Substring(i * blockSize, blockSize);

            // 校验区块并记录失败的区块
            if (!CheckBlock(block))
            {
                failedBlocks.Add(new FailedBlock
                {
                    Index = i + 1, // 区块索引从1开始
                    Value = block
                });
            }
        }

        // 返回校验结果（合法时 Error 为 null）
        return new IidValidationResult(failedBlocks.Count == 0)
        {
            FailedBlocks = failedBlocks
        };
    }

    /// <summary>
    /// 辅助方法：检查字符串是否全为数字
    /// </summary>
    /// <param name="input">输入字符串</param>
    /// <returns>是否全为数字</returns>
    private static bool IsAllDigits(string input)
    {
        foreach (char c in input)
        {
            if (!char.IsDigit(c))
                return false;
        }
        return true;
    }

    #region 嵌套数据模型类
    /// <summary>
    /// IID校验结果模型
    /// </summary>
    public class IidValidationResult
    {
        /// <summary>
        /// 构造函数（基础版，仅指定是否合法）
        /// </summary>
        /// <param name="valid">是否合法</param>
        public IidValidationResult(bool valid)
        {
            Valid = valid;
            // Error 默认为 null，符合可空类型语义
            FailedBlocks = new List<FailedBlock>();
        }

        /// <summary>
        /// 是否合法
        /// </summary>
        public bool Valid { get; set; }

        /// <summary>
        /// 错误码（empty_iid/not_numeric/invalid_length）
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// 实际长度（仅当error为invalid_length时有值）
        /// </summary>
        public int Length { get; set; }

        /// <summary>
        /// 失败的区块列表
        /// </summary>
        public List<FailedBlock> FailedBlocks { get; set; }
    }

    /// <summary>
    /// 失败的区块信息
    /// </summary>
    public class FailedBlock
    {
        /// <summary>
        /// 区块索引（1-9）
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 区块值
        /// </summary>
        public string? Value { get; set; }
    }
    #endregion
}
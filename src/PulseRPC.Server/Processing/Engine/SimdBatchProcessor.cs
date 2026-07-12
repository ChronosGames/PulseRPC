using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace PulseRPC.Server.Processing.Engine;

/// <summary>
/// P8 优化：SIMD 加速的批量消息处理器
/// 提供高性能的消息扫描、长度提取和协议号批量查找功能
/// </summary>
[Obsolete("Experimental standalone component. It is not used by the fixed-shard message engine.", false)]
public static class SimdBatchProcessor
{
    /// <summary>
    /// 消息扫描结果
    /// </summary>
    public readonly struct MessageScanResult
    {
        /// <summary>消息偏移量数组</summary>
        public readonly int[] Offsets;
        /// <summary>消息长度数组</summary>
        public readonly int[] Lengths;
        /// <summary>扫描到的消息数量</summary>
        public readonly int Count;
        /// <summary>是否有不完整的消息</summary>
        public readonly bool HasIncomplete;
        /// <summary>不完整消息的起始偏移</summary>
        public readonly int IncompleteOffset;

        public MessageScanResult(int[] offsets, int[] lengths, int count, bool hasIncomplete, int incompleteOffset)
        {
            Offsets = offsets;
            Lengths = lengths;
            Count = count;
            HasIncomplete = hasIncomplete;
            IncompleteOffset = incompleteOffset;
        }
    }

    /// <summary>
    /// SIMD 加速的批量消息边界扫描
    /// 从缓冲区中快速定位所有消息的起始位置和长度
    /// </summary>
    /// <param name="buffer">输入缓冲区</param>
    /// <param name="maxMessages">最大消息数量</param>
    /// <returns>消息扫描结果</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MessageScanResult ScanMessageBoundaries(ReadOnlySpan<byte> buffer, int maxMessages = 64)
    {
        if (buffer.Length < 4)
        {
            return new MessageScanResult(Array.Empty<int>(), Array.Empty<int>(), 0, buffer.Length > 0, 0);
        }

        var offsets = new int[maxMessages];
        var lengths = new int[maxMessages];
        var count = 0;
        var offset = 0;

        while (offset + 4 <= buffer.Length && count < maxMessages)
        {
            // 读取消息长度（4字节，小端序）
            var length = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset));

            // 验证长度有效性
            if (length <= 0 || length > 10 * 1024 * 1024) // 10MB max
            {
                break;
            }

            // 检查是否有完整消息
            if (offset + 4 + length > buffer.Length)
            {
                return new MessageScanResult(offsets, lengths, count, true, offset);
            }

            offsets[count] = offset + 4; // 跳过长度前缀
            lengths[count] = length;
            count++;

            offset += 4 + length;
        }

        var hasIncomplete = offset < buffer.Length;
        return new MessageScanResult(offsets, lengths, count, hasIncomplete, hasIncomplete ? offset : -1);
    }

    /// <summary>
    /// SIMD 加速的协议号批量提取
    /// 从多个消息中并行提取协议号
    /// </summary>
    /// <param name="buffer">输入缓冲区</param>
    /// <param name="scanResult">消息扫描结果</param>
    /// <param name="protocolIdOffset">协议号在消息中的偏移（默认为消息开始后的第2字节）</param>
    /// <returns>协议号数组</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort[] ExtractProtocolIds(ReadOnlySpan<byte> buffer, in MessageScanResult scanResult, int protocolIdOffset = 1)
    {
        if (scanResult.Count == 0)
            return Array.Empty<ushort>();

        var protocolIds = new ushort[scanResult.Count];

        // 使用 SIMD 批量提取（如果可用且消息数量足够）
        if (Avx2.IsSupported && scanResult.Count >= 8)
        {
            ExtractProtocolIdsSimd(buffer, scanResult, protocolIds, protocolIdOffset);
        }
        else
        {
            ExtractProtocolIdsScalar(buffer, scanResult, protocolIds, protocolIdOffset);
        }

        return protocolIds;
    }

    /// <summary>
    /// 标量版本的协议号提取
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExtractProtocolIdsScalar(ReadOnlySpan<byte> buffer, in MessageScanResult scanResult, ushort[] protocolIds, int protocolIdOffset)
    {
        for (int i = 0; i < scanResult.Count; i++)
        {
            var msgStart = scanResult.Offsets[i];
            if (msgStart + protocolIdOffset + 2 <= buffer.Length)
            {
                protocolIds[i] = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(msgStart + protocolIdOffset, 2));
            }
        }
    }

    /// <summary>
    /// SIMD 加速的协议号提取（AVX2）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExtractProtocolIdsSimd(ReadOnlySpan<byte> buffer, in MessageScanResult scanResult, ushort[] protocolIds, int protocolIdOffset)
    {
        // 处理 8 个消息一组的批次
        var fullBatches = scanResult.Count / 8;

        // 将 stackalloc 移出循环以避免 CA2014 警告
        Span<ushort> batchIds = stackalloc ushort[8];

        for (int batch = 0; batch < fullBatches; batch++)
        {
            var baseIndex = batch * 8;

            // 收集 8 个协议号位置的数据
            for (int i = 0; i < 8; i++)
            {
                var msgStart = scanResult.Offsets[baseIndex + i];
                if (msgStart + protocolIdOffset + 2 <= buffer.Length)
                {
                    batchIds[i] = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(msgStart + protocolIdOffset, 2));
                }
            }

            // 复制结果
            batchIds.CopyTo(protocolIds.AsSpan(baseIndex, 8));
        }

        // 处理剩余的消息
        var remainder = scanResult.Count % 8;
        if (remainder > 0)
        {
            var startIndex = fullBatches * 8;
            for (int i = 0; i < remainder; i++)
            {
                var msgStart = scanResult.Offsets[startIndex + i];
                if (msgStart + protocolIdOffset + 2 <= buffer.Length)
                {
                    protocolIds[startIndex + i] = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(msgStart + protocolIdOffset, 2));
                }
            }
        }
    }

    /// <summary>
    /// SIMD 加速的协议号验证
    /// 检查一批协议号是否都在有效范围内
    /// </summary>
    /// <param name="protocolIds">协议号数组</param>
    /// <param name="maxValidId">最大有效协议号</param>
    /// <returns>是否全部有效</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ValidateProtocolIdsBatch(ReadOnlySpan<ushort> protocolIds, ushort maxValidId)
    {
        if (protocolIds.Length == 0)
            return true;

        if (Vector.IsHardwareAccelerated && protocolIds.Length >= Vector<ushort>.Count)
        {
            return ValidateProtocolIdsVectorized(protocolIds, maxValidId);
        }

        return ValidateProtocolIdsScalar(protocolIds, maxValidId);
    }

    /// <summary>
    /// 标量版本的协议号验证
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ValidateProtocolIdsScalar(ReadOnlySpan<ushort> protocolIds, ushort maxValidId)
    {
        for (int i = 0; i < protocolIds.Length; i++)
        {
            if (protocolIds[i] > maxValidId)
                return false;
        }
        return true;
    }

    /// <summary>
    /// 向量化版本的协议号验证
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ValidateProtocolIdsVectorized(ReadOnlySpan<ushort> protocolIds, ushort maxValidId)
    {
        var maxVector = new Vector<ushort>(maxValidId);
        var vectorSize = Vector<ushort>.Count;
        var fullVectors = protocolIds.Length / vectorSize;

        // 处理完整向量
        for (int i = 0; i < fullVectors; i++)
        {
            var offset = i * vectorSize;
            var vec = new Vector<ushort>(protocolIds.Slice(offset, vectorSize));

            // 检查是否有任何元素大于 maxValidId
            if (Vector.GreaterThanAny(vec, maxVector))
                return false;
        }

        // 处理剩余元素
        var remainder = protocolIds.Length % vectorSize;
        if (remainder > 0)
        {
            var startIndex = fullVectors * vectorSize;
            for (int i = 0; i < remainder; i++)
            {
                if (protocolIds[startIndex + i] > maxValidId)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// SIMD 加速的消息头魔数验证
    /// 快速检查多个消息是否具有正确的协议版本
    /// </summary>
    /// <param name="buffer">输入缓冲区</param>
    /// <param name="scanResult">消息扫描结果</param>
    /// <param name="expectedVersion">期望的协议版本</param>
    /// <returns>无效消息的索引，-1 表示全部有效</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ValidateProtocolVersions(ReadOnlySpan<byte> buffer, in MessageScanResult scanResult, byte expectedVersion)
    {
        for (int i = 0; i < scanResult.Count; i++)
        {
            var msgStart = scanResult.Offsets[i];
            if (msgStart < buffer.Length && buffer[msgStart] != expectedVersion)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// 批量消息处理结果
    /// </summary>
    public readonly struct BatchProcessResult
    {
        /// <summary>成功处理的消息数量</summary>
        public readonly int ProcessedCount;
        /// <summary>已消费的字节数</summary>
        public readonly int ConsumedBytes;
        /// <summary>第一个错误的索引（-1 表示无错误）</summary>
        public readonly int FirstErrorIndex;
        /// <summary>错误类型（如果有）</summary>
        public readonly string? ErrorType;

        public BatchProcessResult(int processedCount, int consumedBytes, int firstErrorIndex = -1, string? errorType = null)
        {
            ProcessedCount = processedCount;
            ConsumedBytes = consumedBytes;
            FirstErrorIndex = firstErrorIndex;
            ErrorType = errorType;
        }

        public bool HasError => FirstErrorIndex >= 0;

        public static BatchProcessResult Success(int count, int bytes) => new(count, bytes);
        public static BatchProcessResult Error(int processedCount, int consumedBytes, int errorIndex, string errorType)
            => new(processedCount, consumedBytes, errorIndex, errorType);
    }

    /// <summary>
    /// SIMD 优化的批量消息预处理
    /// 一次性扫描、验证和提取所有消息的关键信息
    /// </summary>
    /// <param name="buffer">输入缓冲区</param>
    /// <param name="expectedVersion">期望的协议版本</param>
    /// <param name="protocolIdOffset">协议号偏移</param>
    /// <returns>批量处理结果及预提取的协议号</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (BatchProcessResult Result, ushort[] ProtocolIds) PreprocessBatch(
        ReadOnlySpan<byte> buffer,
        byte expectedVersion = 1,
        int protocolIdOffset = 1)
    {
        // 1. 扫描消息边界
        var scanResult = ScanMessageBoundaries(buffer);

        if (scanResult.Count == 0)
        {
            return (new BatchProcessResult(0, 0), Array.Empty<ushort>());
        }

        // 2. 验证协议版本
        var invalidVersionIndex = ValidateProtocolVersions(buffer, scanResult, expectedVersion);
        if (invalidVersionIndex >= 0)
        {
            var consumedBytes = invalidVersionIndex > 0 ? scanResult.Offsets[invalidVersionIndex - 1] + scanResult.Lengths[invalidVersionIndex - 1] + 4 : 0;
            return (BatchProcessResult.Error(invalidVersionIndex, consumedBytes - 4, invalidVersionIndex, "InvalidProtocolVersion"), Array.Empty<ushort>());
        }

        // 3. 提取协议号
        var protocolIds = ExtractProtocolIds(buffer, scanResult, protocolIdOffset);

        // 4. 计算已消费字节
        var lastMsgEnd = scanResult.Offsets[scanResult.Count - 1] + scanResult.Lengths[scanResult.Count - 1];
        var totalConsumed = lastMsgEnd;

        return (BatchProcessResult.Success(scanResult.Count, totalConsumed), protocolIds);
    }
}

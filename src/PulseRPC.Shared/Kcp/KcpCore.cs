using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PulseRPC.Shared.Kcp;

/// <summary>
/// KCP协议核心实现
/// </summary>
public sealed class KcpCore : IDisposable
{
    private readonly uint _conv;
    private readonly Action<byte[], int> _output;
    private readonly ILogger _logger;

    // 协议状态
    private uint _mtu = 1400;
    private uint _mss;
    private uint _state;

    // 发送控制
    private uint _sndUna;    // 第一个未确认的包
    private uint _sndNxt;    // 下一个发送的包
    private uint _sndWnd = 32; // 发送窗口
    private uint _rcvWnd = 128; // 接收窗口
    private uint _rmtWnd = 128; // 远端窗口
    private uint _cwnd;      // 拥塞窗口
    private uint _ssthresh = 2; // 慢启动阈值

    // 接收控制
    private uint _rcvNxt;    // 下一个接收的包

    // 定时器
    private uint _current;
    private uint _interval = 100;
    private uint _tsFlush;
    private uint _xmit;

    // 拥塞控制
    private uint _nodelay;
    private uint _updated;
    private uint _tsProbe;
    private uint _probeWait;
    private uint _incr;

    // RTT计算
    private int _rxSrtt;
    private int _rxRttval;
    private int _rxRto = 200;
    private int _rxMinrto = 100;

    // 队列
    private readonly List<KcpSegment> _sndQueue = new();
    private readonly List<KcpSegment> _rcvQueue = new();
    private readonly List<KcpSegment> _sndBuf = new();
    private readonly List<KcpSegment> _rcvBuf = new();

    // ACK列表
    private readonly List<uint> _ackList = new();

    private bool _disposed;

    /// <summary>
    /// 构造函数
    /// </summary>
    public KcpCore(uint conv, Action<byte[], int> output, ILogger? logger = null)
    {
        _conv = conv;
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _logger = logger ?? NullLogger.Instance;

        _mss = _mtu - KcpSegmentHeader.HeaderSize;
        _rcvWnd = 128;
        _sndWnd = 32;
        _rmtWnd = 128;
        _cwnd = 1;
        _incr = _mss;
        _rxRto = 200;
        _rxMinrto = 100;
        _interval = 100;
        _tsFlush = 100;
    }

    /// <summary>
    /// 发送数据
    /// </summary>
    public int Send(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length == 0)
            return -1;

        var count = buffer.Length <= _mss ? 1 : (int)((buffer.Length + _mss - 1) / _mss);
        if (count >= 255)
            return -2;

        if (count == 0)
            count = 1;

        var offset = 0;
        for (var i = 0; i < count; i++)
        {
            var size = Math.Min((int)_mss, buffer.Length - offset);
            var seg = KcpSegment.Rent();

            seg.Header.Conv = _conv;
            seg.Header.Cmd = (byte)KcpCommand.Push;
            seg.Header.Frg = (byte)(count - i - 1);
            seg.Header.Wnd = (ushort)WndUnused();
            seg.Header.Ts = _current;
            seg.Header.Sn = _sndNxt++;
            seg.Header.Una = _rcvNxt;

            seg.SetData(buffer.Slice(offset, size));
            _sndQueue.Add(seg);

            offset += size;
        }

        return 0;
    }

    /// <summary>
    /// 接收数据
    /// </summary>
    public int Recv(Span<byte> buffer)
    {
        if (_rcvQueue.Count == 0)
            return -1;

        var peekSize = PeekSize();
        if (peekSize < 0)
            return -2;

        if (peekSize > buffer.Length)
            return -3;

        var fastRecover = _rcvQueue.Count >= _rcvWnd;
        var len = 0;

        // 合并所有数据
        for (var i = 0; i < _rcvQueue.Count; i++)
        {
            var seg = _rcvQueue[i];
            if (seg.Header.Frg > 0)
                continue;

            // 找到完整的消息
            var msgLen = 0;
            for (var j = i; j < _rcvQueue.Count; j++)
            {
                var s = _rcvQueue[j];
                msgLen += (int)s.Header.Len;
                if (s.Header.Frg == 0)
                    break;
            }

            if (msgLen > buffer.Length)
                break;

            // 复制数据
            var pos = 0;
            for (var j = i; j < _rcvQueue.Count && pos < msgLen; j++)
            {
                var s = _rcvQueue[j];
                s.Data.Span.CopyTo(buffer.Slice(pos, (int)s.Header.Len));
                pos += (int)s.Header.Len;

                KcpSegment.Return(s);
                _rcvQueue.RemoveAt(j);
                j--;

                if (s.Header.Frg == 0)
                    break;
            }

            len = msgLen;
            break;
        }

        // 移动数据
        if (_rcvQueue.Count < _rcvWnd && fastRecover)
        {
            // 快速恢复，立即发送窗口更新
            _probeWait = 0;
            _tsProbe = _current + _probeWait;
        }

        return len;
    }

    /// <summary>
    /// 输入数据包
    /// </summary>
    public int Input(ReadOnlySpan<byte> data)
    {
        var oldUna = _sndUna;
        uint maxAck = 0;
        uint latestTs = 0;
        var flag = false;

        if (data.Length < KcpSegmentHeader.HeaderSize)
        {
            _logger.LogError("[KCP.Input] 数据包太短，无法包含完整头部: Length={Length}, RequiredSize={RequiredSize}",
                data.Length, KcpSegmentHeader.HeaderSize);
            return -1;
        }

        var offset = 0;
        var segmentCount = 0;
        while (offset + KcpSegmentHeader.HeaderSize <= data.Length)
        {
            segmentCount++;

            var segment = KcpSegment.Decode(data.Slice(offset));
            if (segment == null)
            {
                _logger.LogError("[KCP.Input] 第{SegmentIndex}个数据段解码失败", segmentCount);
                break;
            }

            if (segment.Header.Conv != _conv)
            {
                _logger.LogError("[KCP.Input] 会话ID不匹配: Expected={ExpectedConv}, Actual={ActualConv}",
                    _conv, segment.Header.Conv);
                KcpSegment.Return(segment);
                return -1;
            }

            offset += KcpSegmentHeader.HeaderSize + (int)segment.Header.Len;

            if (segment.Header.Cmd != (byte)KcpCommand.Push &&
                segment.Header.Cmd != (byte)KcpCommand.Ack &&
                segment.Header.Cmd != (byte)KcpCommand.WindowsProbe &&
                segment.Header.Cmd != (byte)KcpCommand.WindowsResponse)
            {
                _logger.LogError("[KCP.Input] 不支持的命令类型: Cmd={Cmd}", segment.Header.Cmd);
                KcpSegment.Return(segment);
                return -2;
            }

            _rmtWnd = segment.Header.Wnd;
            ParseUna(segment.Header.Una);
            ShrinkBuf();

            if (segment.Header.Cmd == (byte)KcpCommand.Ack)
            {
                if (TimeDiff(_current, segment.Header.Ts) >= 0)
                {
                    UpdateRtt(TimeDiff(_current, segment.Header.Ts));
                }
                ParseAck(segment.Header.Sn);
                ShrinkBuf();
                if (!flag)
                {
                    flag = true;
                    maxAck = segment.Header.Sn;
                    latestTs = segment.Header.Ts;
                }
                else if (TimeDiff(segment.Header.Sn, maxAck) > 0)
                {
                    maxAck = segment.Header.Sn;
                    latestTs = segment.Header.Ts;
                }
            }
            else if (segment.Header.Cmd == (byte)KcpCommand.Push)
            {
                var seqDiff = TimeDiff(segment.Header.Sn, _rcvNxt + _rcvWnd);

                if (TimeDiff(segment.Header.Sn, _rcvNxt + _rcvWnd) < 0)
                {
                    AckPush(segment.Header.Sn, segment.Header.Ts);

                    var nextDiff = TimeDiff(segment.Header.Sn, _rcvNxt);

                    if (TimeDiff(segment.Header.Sn, _rcvNxt) >= 0)
                    {
                        // 序列号同步检查和修复逻辑
                        var seqGap = (int)(segment.Header.Sn - _rcvNxt);
                        if (seqGap > 0 && seqGap <= 10) // 合理的序列号跳跃范围
                        {
                            _logger.LogWarning("[KCP.Input] 检测到序列号跳跃，自动同步: 期望={ExpectedSn}, 实际={ActualSn}, 差距={SeqGap}",
                                _rcvNxt, segment.Header.Sn, seqGap);
                            _rcvNxt = segment.Header.Sn;
                            _logger.LogInformation("[KCP.Input] 序列号已同步: RcvNxt={RcvNxt}", _rcvNxt);
                        }
                        else if (seqGap > 10)
                        {
                            _logger.LogError("[KCP.Input] 序列号跳跃过大，可能存在数据丢失: 期望={ExpectedSn}, 实际={ActualSn}, 差距={SeqGap}",
                                _rcvNxt, segment.Header.Sn, seqGap);
                        }

                        ParseData(segment);
                        segment = null; // 已被ParseData处理
                    }
                }
                else
                {
                    _logger.LogWarning("[KCP.Input] 序列号超出接收窗口，丢弃段: Sn={Sn}, Window={Window}",
                        segment.Header.Sn, _rcvNxt + _rcvWnd);
                }
            }
            else if (segment.Header.Cmd == (byte)KcpCommand.WindowsProbe)
            {
                // 窗口探测包
                _probeWait = 0;
                _tsProbe = _current + _probeWait;
            }
            else if (segment.Header.Cmd == (byte)KcpCommand.WindowsResponse)
            {
                // 窗口响应包
                // 不需要特别处理
            }

            segment?.Dispose();
        }

        if (flag)
        {
            ParseFastAck(maxAck, latestTs);
        }

        if (TimeDiff(_sndUna, oldUna) > 0)
        {
            if (_cwnd < _rmtWnd)
            {
                var mss = _mss;
                if (_cwnd < _ssthresh)
                {
                    _cwnd++;
                    _incr += mss;
                }
                else
                {
                    if (_incr < mss)
                        _incr = mss;
                    _incr += (mss * mss) / _incr + (mss / 16);
                    if ((_cwnd + 1) * mss <= _incr)
                        _cwnd++;
                }
                if (_cwnd > _rmtWnd)
                {
                    _cwnd = _rmtWnd;
                    _incr = _rmtWnd * mss;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// 更新KCP状态
    /// </summary>
    public void Update(uint current)
    {
        _current = current;

        if (_updated == 0)
        {
            _updated = 1;
            _tsFlush = _current;
        }

        var slap = TimeDiff(_current, _tsFlush);

        if (slap >= 10000 || slap < -10000)
        {
            _tsFlush = _current;
            slap = 0;
        }

        if (slap >= 0)
        {
            _tsFlush += _interval;
            if (TimeDiff(_current, _tsFlush) >= 0)
                _tsFlush = _current + _interval;
            Flush();
        }
    }

    /// <summary>
    /// 检查下次更新时间
    /// </summary>
    public uint Check(uint current)
    {
        var tsFlush = _tsFlush;
        var tmFlush = int.MaxValue;
        var tmPacket = int.MaxValue;

        if (_updated == 0)
            return current;

        if (TimeDiff(current, tsFlush) >= 10000 || TimeDiff(current, tsFlush) < -10000)
            tsFlush = current;

        if (TimeDiff(current, tsFlush) >= 0)
            return current;

        tmFlush = (int)TimeDiff(tsFlush, current);

        foreach (var seg in _sndBuf)
        {
            var diff = (int)TimeDiff(seg.ResendTs, current);
            if (diff <= 0)
                return current;
            if (diff < tmPacket)
                tmPacket = diff;
        }

        var minimal = Math.Min(tmPacket, tmFlush);
        if (minimal >= _interval)
            minimal = (int)_interval;

        return current + (uint)minimal;
    }

    /// <summary>
    /// 获取下一个数据包大小
    /// </summary>
    public int PeekSize()
    {
        if (_rcvQueue.Count == 0)
            return -1;

        var length = 0;
        foreach (var seg in _rcvQueue)
        {
            length += (int)seg.Header.Len;
            if (seg.Header.Frg == 0)
                break;
        }

        return length;
    }

    /// <summary>
    /// 设置最大传输单元
    /// </summary>
    public int SetMtu(int mtu)
    {
        if (mtu < 50 || mtu < KcpSegmentHeader.HeaderSize)
            return -1;

        _mtu = (uint)mtu;
        _mss = _mtu - KcpSegmentHeader.HeaderSize;
        return 0;
    }

    /// <summary>
    /// 设置窗口大小
    /// </summary>
    public int SetWindowSize(int sndwnd, int rcvwnd)
    {
        if (sndwnd > 0)
            _sndWnd = (uint)sndwnd;
        if (rcvwnd > 0)
            _rcvWnd = (uint)rcvwnd;
        return 0;
    }

    /// <summary>
    /// 设置无延迟模式
    /// </summary>
    public int NoDelay(int nodelay, int interval, int resend, bool nc)
    {
        if (nodelay >= 0)
        {
            _nodelay = (uint)nodelay;
            if (nodelay != 0)
                _rxMinrto = 30;
            else
                _rxMinrto = 100;
        }

        if (interval >= 0)
        {
            if (interval > 5000)
                interval = 5000;
            else if (interval < 10)
                interval = 10;
            _interval = (uint)interval;
        }

        if (resend >= 0)
        {
            // 暂不实现resend参数
        }

        // nc参数控制是否关闭拥塞控制
        if (nc)
        {
            _cwnd = _sndWnd;
            _incr = _sndWnd * _mss;
        }

        return 0;
    }

    #region Private Methods

    private void Flush()
    {
        var current = _current;
        var change = false;
        var lost = false;

        if (_updated == 0)
            return;

        var seg = KcpSegment.Rent();
        seg.Header.Conv = _conv;
        seg.Header.Cmd = (byte)KcpCommand.Ack;
        seg.Header.Wnd = (ushort)WndUnused();
        seg.Header.Una = _rcvNxt;

        // 发送ACK
        var count = _ackList.Count;
        for (var i = 0; i < count; i += 2)
        {
            if (i + 1 < count)
            {
                seg.Header.Sn = _ackList[i];
                seg.Header.Ts = _ackList[i + 1];
                OutputSeg(seg);
            }
        }
        _ackList.Clear();

        KcpSegment.Return(seg);

        // 计算窗口大小
        var cwnd = Math.Min(_sndWnd, _rmtWnd);
        if (_nodelay == 0)
            cwnd = Math.Min(cwnd, _cwnd);

        // 移动数据从发送队列到发送缓冲区
        while (TimeDiff(_sndNxt, _sndUna + cwnd) < 0)
        {
            if (_sndQueue.Count == 0)
                break;

            var newseg = _sndQueue[0];
            _sndQueue.RemoveAt(0);
            _sndBuf.Add(newseg);

            newseg.Header.Conv = _conv;
            newseg.Header.Cmd = (byte)KcpCommand.Push;
            newseg.Header.Wnd = (ushort)WndUnused();
            newseg.Header.Ts = current;
            newseg.Header.Sn = _sndNxt++;
            newseg.Header.Una = _rcvNxt;
            newseg.ResendTs = current;
            newseg.FastAck = 0;
            newseg.Xmit = 0;
        }

        // 计算重传超时
        var resent = (_nodelay == 0) ? 8u : 2u;
        var rtomin = (_nodelay == 0) ? (uint)_rxRto >> 3 : (uint)_rxMinrto;

        // 发送所有等待发送的段
        foreach (var segment in _sndBuf)
        {
            var needsend = false;
            var debug = TimeDiff(current, segment.ResendTs);

            if (segment.Xmit == 0)
            {
                needsend = true;
                segment.Xmit++;
                segment.ResendTs = current + (uint)_rxRto;
            }
            else if (TimeDiff(current, segment.ResendTs) >= 0)
            {
                needsend = true;
                segment.Xmit++;
                _xmit++;
                if (_nodelay == 0)
                    segment.ResendTs = current + (uint)_rxRto;
                else
                    segment.ResendTs = current + Math.Max((uint)_rxRto, rtomin);
                lost = true;
            }
            else if (segment.FastAck >= resent)
            {
                if (segment.Xmit <= 5)
                {
                    needsend = true;
                    segment.Xmit++;
                    segment.FastAck = 0;
                    segment.ResendTs = current + (uint)_rxRto;
                    change = true;
                }
            }

            if (needsend)
            {
                segment.Header.Ts = current;
                segment.Header.Wnd = (ushort)WndUnused();
                segment.Header.Una = _rcvNxt;

                OutputSeg(segment);

                if (segment.Xmit >= 3)
                {
                    _ssthresh = Math.Max(_cwnd / 2, 2);
                    _cwnd = _ssthresh + resent;
                    _incr = _cwnd * _mss;
                }
            }
        }

        // 更新拥塞窗口
        if (change)
        {
            var inflight = _sndNxt - _sndUna;
            _ssthresh = Math.Max(inflight / 2, 2);
            _cwnd = _ssthresh + resent;
            _incr = _cwnd * _mss;
        }

        if (lost)
        {
            _ssthresh = Math.Max(cwnd / 2, 2);
            _cwnd = 1;
            _incr = _mss;
        }

        if (_cwnd < 1)
        {
            _cwnd = 1;
            _incr = _mss;
        }
    }

    private void OutputSeg(KcpSegment seg)
    {
        var buffer = new byte[KcpSegmentHeader.HeaderSize + seg.Header.Len];
        var encoded = seg.Encode(buffer);
        if (encoded > 0)
        {
            _output(buffer, encoded);
        }
    }

    private uint WndUnused()
    {
        if (_rcvQueue.Count < _rcvWnd)
            return _rcvWnd - (uint)_rcvQueue.Count;
        return 0;
    }

    private void AckPush(uint sn, uint ts)
    {
        _ackList.Add(sn);
        _ackList.Add(ts);
    }

    private void ParseData(KcpSegment newseg)
    {
        var sn = newseg.Header.Sn;

        if (TimeDiff(sn, _rcvNxt + _rcvWnd) >= 0 ||
            TimeDiff(sn, _rcvNxt) < 0)
        {
            _logger.LogWarning("[KCP.ParseData] 数据段序列号超出范围，丢弃: Sn={Sn}, RcvNxt={RcvNxt}, RcvWnd={RcvWnd}",
                sn, _rcvNxt, _rcvWnd);
            KcpSegment.Return(newseg);
            return;
        }

        var after = -1;
        var repeat = false;

        for (var i = _rcvBuf.Count - 1; i >= 0; i--)
        {
            var seg = _rcvBuf[i];
            if (seg.Header.Sn == sn)
            {
                repeat = true;
                break;
            }

            if (TimeDiff(sn, seg.Header.Sn) > 0)
            {
                after = i;
                break;
            }
        }

        if (!repeat)
        {
            if (after == -1)
            {
                _rcvBuf.Insert(0, newseg);
            }
            else
            {
                _rcvBuf.Insert(after + 1, newseg);
            }
        }
        else
        {
            KcpSegment.Return(newseg);
        }

        while (_rcvBuf.Count > 0)
        {
            var seg = _rcvBuf[0];

            if (seg.Header.Sn == _rcvNxt && _rcvQueue.Count < _rcvWnd)
            {
                _rcvBuf.RemoveAt(0);
                _rcvQueue.Add(seg);
                _rcvNxt++;
            }
            else
            {
                if (seg.Header.Sn != _rcvNxt)
                {
                    _logger.LogDebug("[KCP.ParseData] 段序列号不连续，停止移动: ExpectedSn={ExpectedSn}, ActualSn={ActualSn}",
                        _rcvNxt, seg.Header.Sn);
                }
                if (_rcvQueue.Count >= _rcvWnd)
                {
                    _logger.LogDebug("[KCP.ParseData] 接收队列已满，停止移动: QueueCount={QueueCount}, RcvWnd={RcvWnd}",
                        _rcvQueue.Count, _rcvWnd);
                }
                break;
            }
        }
    }

    private void ParseUna(uint una)
    {
        for (var i = 0; i < _sndBuf.Count; i++)
        {
            if (TimeDiff(una, _sndBuf[i].Header.Sn) > 0)
            {
                KcpSegment.Return(_sndBuf[i]);
                _sndBuf.RemoveAt(i);
                i--;
            }
            else
            {
                break;
            }
        }
    }

    private void ParseAck(uint sn)
    {
        if (TimeDiff(sn, _sndUna) < 0 || TimeDiff(sn, _sndNxt) >= 0)
            return;

        for (var i = 0; i < _sndBuf.Count; i++)
        {
            var seg = _sndBuf[i];
            if (sn == seg.Header.Sn)
            {
                KcpSegment.Return(seg);
                _sndBuf.RemoveAt(i);
                break;
            }

            if (TimeDiff(sn, seg.Header.Sn) < 0)
                break;
        }
    }

    private void ParseFastAck(uint sn, uint ts)
    {
        if (TimeDiff(sn, _sndUna) < 0 || TimeDiff(sn, _sndNxt) >= 0)
            return;

        foreach (var seg in _sndBuf)
        {
            if (TimeDiff(sn, seg.Header.Sn) < 0)
                break;
            else if (sn != seg.Header.Sn)
                seg.FastAck++;
        }
    }

    private void ShrinkBuf()
    {
        if (_sndBuf.Count > 0)
            _sndUna = _sndBuf[0].Header.Sn;
        else
            _sndUna = _sndNxt;
    }

    private void UpdateRtt(int rtt)
    {
        if (_rxSrtt == 0)
        {
            _rxSrtt = rtt;
            _rxRttval = rtt / 2;
        }
        else
        {
            var delta = rtt - _rxSrtt;
            if (delta < 0)
                delta = -delta;
            _rxRttval = (3 * _rxRttval + delta) / 4;
            _rxSrtt = (7 * _rxSrtt + rtt) / 8;
            if (_rxSrtt < 1)
                _rxSrtt = 1;
        }

        var rto = _rxSrtt + Math.Max((int)_interval, 4 * _rxRttval);
        _rxRto = Math.Max(_rxMinrto, Math.Min(rto, 60000));
    }

    private static int TimeDiff(uint later, uint earlier)
    {
        return (int)(later - earlier);
    }

    #endregion

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // 清理所有队列
        foreach (var seg in _sndQueue)
            KcpSegment.Return(seg);
        _sndQueue.Clear();

        foreach (var seg in _rcvQueue)
            KcpSegment.Return(seg);
        _rcvQueue.Clear();

        foreach (var seg in _sndBuf)
            KcpSegment.Return(seg);
        _sndBuf.Clear();

        foreach (var seg in _rcvBuf)
            KcpSegment.Return(seg);
        _rcvBuf.Clear();

        _ackList.Clear();
    }
}

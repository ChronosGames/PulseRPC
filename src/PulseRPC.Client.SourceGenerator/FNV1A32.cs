using System.Text;

namespace PulseRPC;

// ReSharper disable once UnusedType.Global
// ReSharper disable once InconsistentNaming
public static class FNV1A32
{
    public static int GetHashCode(string str)
    {
        return GetHashCode(Encoding.UTF8.GetBytes(str));
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public static int GetHashCode(byte[]? obj)
    {
        uint hash = 0;
        if (obj != null)
        {
            hash = obj.Aggregate(2166136261, (current, t) => unchecked((t ^ current) * 16777619));
        }

        return unchecked((int)hash);
    }
}

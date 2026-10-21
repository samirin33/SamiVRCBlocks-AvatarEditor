using System;
using System.Collections.Generic;

namespace SamiVRCBlocksAvatar.Editor
{
    /// <summary>
    /// 直接参照できない型（MA / NDMF / VRC SDK / Assembly-CSharp 等）を名前から探す。
    /// 結果はドメインリロードまでキャッシュする（OnGUI やメニュー検証から毎回全アセンブリを走査しない）。
    /// </summary>
    internal static class ReflectionUtil
    {
        static readonly Dictionary<string, Type> Cache = new Dictionary<string, Type>(StringComparer.Ordinal);

        public static Type FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            lock (Cache)
            {
                if (Cache.TryGetValue(fullName, out var cached))
                    return cached;
            }

            Type found = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    found = assembly.GetType(fullName, false);
                }
                catch
                {
                    // 動的アセンブリ等は GetType で例外になることがある
                }

                if (found != null)
                    break;
            }

            lock (Cache)
                Cache[fullName] = found;
            return found;
        }
    }
}

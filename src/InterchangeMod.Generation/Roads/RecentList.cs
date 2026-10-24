using System;
using System.Collections.Generic;
using System.Linq;

namespace InterchangeMod.Generation.Roads
{
    /// <summary>最近使用列表：新用的放最前，重复的提到最前，超过容量丢掉最旧的。可序列化成一行文本存进设置。</summary>
    public sealed class RecentList
    {
        private const char Separator = '|';
        private readonly List<string> m_Items = new List<string>();

        public RecentList(int capacity = 8)
        {
            Capacity = capacity;
        }

        public int Capacity { get; }

        public IReadOnlyList<string> Items => m_Items;

        /// <summary>返回列表是否有变化。</summary>
        public bool Add(string item)
        {
            if (string.IsNullOrEmpty(item) || (m_Items.Count > 0 && m_Items[0] == item))
                return false;
            m_Items.Remove(item);
            m_Items.Insert(0, item);
            if (m_Items.Count > Capacity)
                m_Items.RemoveRange(Capacity, m_Items.Count - Capacity);
            return true;
        }

        public string Serialize() => string.Join(Separator.ToString(), m_Items);

        public static RecentList Deserialize(string? text, int capacity = 8)
        {
            var list = new RecentList(capacity);
            if (string.IsNullOrEmpty(text))
                return list;
            // 反向添加，保持原来的先后顺序
            foreach (var item in text!.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries).Reverse())
                list.Add(item);
            return list;
        }
    }
}

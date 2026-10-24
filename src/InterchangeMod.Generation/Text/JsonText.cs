using System.Globalization;
using System.Text;

namespace InterchangeMod.Generation.Text
{
    /// <summary>
    /// 极简 JSON 拼接：面板状态只有几十个字段，用字符串绑定传给 UI，UI 端 JSON.parse。
    /// 调用方负责逗号：每个字段用 Field 系列方法写，写完一个对象调用 End。
    /// </summary>
    public sealed class JsonText
    {
        private readonly StringBuilder m_Builder = new StringBuilder(1024);
        private bool m_NeedComma;

        public JsonText BeginObject(string? name = null) => Open(name, '{');

        public JsonText BeginArray(string? name = null) => Open(name, '[');

        public JsonText EndObject() => Close('}');

        public JsonText EndArray() => Close(']');

        public JsonText Field(string name, string? value)
        {
            Key(name);
            WriteString(value);
            return this;
        }

        public JsonText Field(string name, float value)
        {
            Key(name);
            m_Builder.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
            m_NeedComma = true;
            return this;
        }

        public JsonText Field(string name, int value)
        {
            Key(name);
            m_Builder.Append(value.ToString(CultureInfo.InvariantCulture));
            m_NeedComma = true;
            return this;
        }

        public JsonText Field(string name, bool value)
        {
            Key(name);
            m_Builder.Append(value ? "true" : "false");
            m_NeedComma = true;
            return this;
        }

        /// <summary>数组里的字符串元素。</summary>
        public JsonText Item(string? value)
        {
            Comma();
            WriteString(value);
            return this;
        }

        public override string ToString() => m_Builder.ToString();

        private JsonText Open(string? name, char bracket)
        {
            if (name != null)
                Key(name);
            else
                Comma();
            m_Builder.Append(bracket);
            m_NeedComma = false;
            return this;
        }

        private JsonText Close(char bracket)
        {
            m_Builder.Append(bracket);
            m_NeedComma = true;
            return this;
        }

        private void Key(string name)
        {
            Comma();
            WriteString(name);
            m_Builder.Append(':');
            m_NeedComma = false;
        }

        private void Comma()
        {
            if (m_NeedComma)
                m_Builder.Append(',');
            m_NeedComma = true;
        }

        private void WriteString(string? value)
        {
            m_Builder.Append('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': m_Builder.Append("\\\""); break;
                    case '\\': m_Builder.Append("\\\\"); break;
                    case '\n': m_Builder.Append("\\n"); break;
                    case '\r': m_Builder.Append("\\r"); break;
                    case '\t': m_Builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            m_Builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            m_Builder.Append(c);
                        break;
                }
            }
            m_Builder.Append('"');
            m_NeedComma = true;
        }
    }
}

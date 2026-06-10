using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WinterMP.Tools
{
    /// <summary>
    /// Minimal streaming JSON writer (no external dependencies inside the game process).
    /// Tracks nesting to insert commas; caller is responsible for balanced Begin/End calls.
    /// </summary>
    internal sealed class JsonWriter
    {
        private readonly StringBuilder _sb = new StringBuilder(64 * 1024);
        private readonly Stack<bool> _hasItems = new Stack<bool>();
        private int _indent;

        public override string ToString() => _sb.ToString();

        public void BeginObject()
        {
            Prefix();
            _sb.Append('{');
            Push();
        }

        public void EndObject()
        {
            Pop();
            NewlineIndent();
            _sb.Append('}');
        }

        public void BeginArray()
        {
            Prefix();
            _sb.Append('[');
            Push();
        }

        public void EndArray()
        {
            Pop();
            NewlineIndent();
            _sb.Append(']');
        }

        public void Key(string name)
        {
            Prefix();
            WriteEscaped(name);
            _sb.Append(": ");
            // A key consumes the comma slot; the following value must not add one.
            _suppressNextPrefix = true;
        }

        public void Value(string? value)
        {
            Prefix();
            if (value == null) _sb.Append("null");
            else WriteEscaped(value);
        }

        public void Value(bool value)
        {
            Prefix();
            _sb.Append(value ? "true" : "false");
        }

        public void Value(long value)
        {
            Prefix();
            _sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        public void Value(double value)
        {
            Prefix();
            _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private bool _suppressNextPrefix;

        private void Prefix()
        {
            if (_suppressNextPrefix)
            {
                _suppressNextPrefix = false;
                return;
            }

            if (_hasItems.Count > 0)
            {
                if (_hasItems.Peek()) _sb.Append(',');
                ReplaceTop(true);
                NewlineIndent();
            }
        }

        private void Push()
        {
            _hasItems.Push(false);
            _indent++;
        }

        private void Pop()
        {
            _hasItems.Pop();
            _indent--;
        }

        private void ReplaceTop(bool value)
        {
            _hasItems.Pop();
            _hasItems.Push(value);
        }

        private void NewlineIndent()
        {
            _sb.Append('\n');
            _sb.Append(' ', _indent * 2);
        }

        private void WriteEscaped(string text)
        {
            _sb.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            _sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            _sb.Append(c);
                        break;
                }
            }

            _sb.Append('"');
        }
    }
}

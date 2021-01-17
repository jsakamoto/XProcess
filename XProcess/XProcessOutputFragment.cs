namespace Toolbelt.Diagnostics
{
    public class XProcessOutputFragment
    {
        public XProcessOutputType Type { get; }

        public string Data { get; }

        public XProcessOutputFragment(XProcessOutputType type, string data)
        {
            Type = type;
            Data = data;
        }

        public override string ToString() => $"{Type} \"{Data}\"";
    }
}

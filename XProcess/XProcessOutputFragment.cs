namespace Toolbelt.Diagnostics
{
    public class XProcessOutputFragment
    {
        public XProcessOutputType Type { get; }

        public string Data { get; }

        internal long Sequence { get; set; }

        public XProcessOutputFragment(XProcessOutputType type, string data)
        {
            Type = type;
            Data = data;
        }

        public override string ToString() => $"{Type} \"{Data}\"";
    }
}

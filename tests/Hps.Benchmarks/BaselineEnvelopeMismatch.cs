namespace Hps.Benchmarks
{
    internal sealed class BaselineEnvelopeMismatch
    {
        public BaselineEnvelopeMismatch(string code, string field, string expected, string actual)
        {
            Code = code;
            Field = field;
            Expected = expected;
            Actual = actual;
        }

        public string Code { get; }

        public string Field { get; }

        public string Expected { get; }

        public string Actual { get; }
    }
}

namespace Viv.Nana.Core
{
    public class NanaConsumeException : Exception
    {
        public NanaConsumeException(string message) : base(message) { }

        public NanaConsumeException(string message, Exception inner) : base(message, inner) { }
    }
}

namespace Viv.Nana.Core
{
    public class VivMessageConsumeException : Exception
    {
        public VivMessageConsumeException(string message) : base(message) { }

        public VivMessageConsumeException(string message, Exception inner) : base(message, inner) { }
    }
}

namespace Viv.Echo.Grpc
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class GrpcClientAttribute : Attribute
    {
        public GrpcClientAttribute(string name, string address)
        {
            Name = name;
            Address = address;
        }

        public string Name { get; }
        public string Address { get; }
    }
}

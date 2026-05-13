namespace Viv.Sayu
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class VivCronAttribute : Attribute
    {
        public string Cron { get; }

        public VivCronAttribute(string cron)
        {
            Cron = cron;
        }
    }
}

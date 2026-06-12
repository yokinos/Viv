namespace Viv.Sdk.Rpc.Tenant.Response
{
    public class GetTenantResponse
    {
        public long TenantId { get; set; }
        public string Name { get; set; } = default!;
    }
}

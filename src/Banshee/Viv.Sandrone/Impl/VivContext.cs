using Viv.Contracts.Interface;
using Viv.Contracts.Models;

namespace Viv.Sandrone.Impl
{
    public class VivContext : IVivContext
    {
        private readonly IVivContextAccessor _accessor;

        public VivContext(IVivContextAccessor accessor)
        {
            _accessor = accessor;
        }

        private VivContextContent? Snapshot => _accessor.Current;

        public long AppId => Snapshot?.AppId ?? 0;

        public long SubjectId => Snapshot?.SubjectId ?? 0;

        public long UserId => Snapshot?.UserId ?? 0;

        public string RequestId => Snapshot?.RequestId ?? string.Empty;

        public VivContextContent? GetRawSnapshot()
        {
            return Snapshot;
        }

        public void SetSnapshot(VivContextContent model)
        {
            _accessor.Current = model;
        }

        public void Clear()
        {
            _accessor.Current = null;
        }
    }
}
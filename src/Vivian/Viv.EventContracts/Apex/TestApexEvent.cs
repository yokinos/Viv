using System;
using System.Collections.Generic;
using System.Text;
using Viv.Nana.Core;

namespace Viv.EventContracts.Apex
{
    public class TestApexEvent : NanaEvent
    {
        public DateTime TestTime { get; set; }
    }
}

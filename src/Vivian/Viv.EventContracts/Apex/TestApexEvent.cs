using System;
using System.Collections.Generic;
using System.Text;
using Viv.Nana;

namespace Viv.EventContracts.Apex
{
    public class TestApexEvent : NanaEvent
    {
        public DateTime TestTime { get; set; }
    }
}

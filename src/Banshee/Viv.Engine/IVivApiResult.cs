using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Engine
{
    public interface IVivApiResult : IActionResult
    {
        public string RequestId { get; set; }
        public int Code { get; set; }
        public string Message { get; set; }
        public object? Data { get; set; }
    }
}

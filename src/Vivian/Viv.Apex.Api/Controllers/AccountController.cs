using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Viv.Apex.Core.IService;
using Viv.Apex.Entity.ViewModel.Account.Request;

namespace Viv.Apex.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly IUserService _userService;

        public AccountController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpPost("apexLogin")]
        public async Task<IActionResult> ApexLoginAsync(ApexLoginRequest request)
        {
            return await _userService.LoginAsync(request);
        }
    }
}

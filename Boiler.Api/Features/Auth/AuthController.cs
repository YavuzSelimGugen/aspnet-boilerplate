﻿using Boiler.Api.Features.Auth;
using Boiler.Api.Features.Auth.Helpers;
using Boiler.Api.Features.Auth.Request;
using Boiler.Domain.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Boiler.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService m_AuthService;

        public AuthController(IAuthService authService)
        {
            m_AuthService = authService;
        }

        [HttpPost("Register")]
        public IActionResult Register([FromBody] RegisterRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var response = m_AuthService.Register(model);
            return Ok(response);
        }

        [HttpPost("Login")]
        public IActionResult Login([FromBody] LoginRequest model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var response = m_AuthService.Login(model, HttpContext.Connection.RemoteIpAddress.ToString());
            return Ok(response);
        }

        [HttpGet("Secret"), Authorize]
        public IActionResult Secret()
        {
            return Ok("Secret 😎");
        }
        [HttpGet("Secret")]
        [Authorize(Role.SuperAdmin)]
        public IActionResult SecretSuperAdmin()
        {
            return Ok("Secret 🐱‍🏍");
        }
    }
}

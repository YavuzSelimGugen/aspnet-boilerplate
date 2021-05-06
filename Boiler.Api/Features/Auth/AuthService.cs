﻿using BC = BCrypt.Net.BCrypt;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Security.Claims;
using System.Collections.Generic;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Boiler.Api.Features.Auth.Response;
using Boiler.Api.Features.Auth.Request;
using Boiler.Domain.Auth;
using Boiler.Api.Features.Auth.Helpers;
using Boiler.Infrastructure.Interfaces;
using Boiler.Api.Exceptions;

namespace Boiler.Api.Features.Auth
{
    internal class AuthService : IAuthService
    {
        private readonly IAuthContext   m_Context;
        private readonly AuthSettings   m_AuthSettings;

        public AuthService(IAuthContext context, IOptions<AuthSettings> settings)
        {
            m_AuthSettings = settings.Value;
            m_Context = context;
        }

        public AuthResponse Login(LoginRequest model, string ipAddress)
        {
            var account = m_Context.Accounts.FirstOrDefault(x => x.Email == model.Email);
            if (account == null || (!account.IsVerified && m_AuthSettings.RequiresVerification) || !BC.Verify(model.Password, account.PasswordHash))
                throw new ApiException("Invalid email or password");

            var jwtToken     = GenerateJwtToken(account);
            var refreshToken = GenerateRefreshToken(ipAddress);
            account.RefreshTokens.Add(refreshToken);
            RemoveOldRefreshTokens(account);

            m_Context.Accounts.Update(account);
            m_Context.SaveChanges();

            return new AuthResponse
            {
                Email        = account.Email,
                Role         = account.Role,
                JwtToken     = jwtToken,
                RefreshToken = refreshToken.Token,
            };
        }

        public void Logout()
        {
            throw new NotImplementedException();
        }

        public AuthResponse Register(RegisterRequest model)
        {
            if (m_Context.Accounts.Any(x => x.Email == model.Email))
                throw new ApiException("User is already registered!");

            var account = new Account
            {
                CreationDate      = DateTime.UtcNow,
                Email             = model.Email,
                PasswordHash      = BC.HashPassword(model.Password),
                Role              = Role.User,
                VerificationToken = GenerateTokenString(),
            };

            m_Context.Accounts.Add(account);
            m_Context.SaveChanges();

            // TODO(selim): Send verification mail
            return new AuthResponse
            {
                Email = account.Email,
                Role  = account.Role,
                JwtToken = GenerateJwtToken(account),
            };
        }

        public static Account CreateTestAccount(string email, string password, Role role) => new Account
        {
            Email            = email,
            Role             = role,
            CreationDate     = DateTime.Now,
            PasswordHash     = BC.HashPassword(password),
            VerificationDate = DateTime.Now,
        };

        public int GetIdFromToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(m_AuthSettings.Secret);
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ClockSkew                = TimeSpan.Zero,
                IssuerSigningKey         = new SymmetricSecurityKey(key),
                ValidateIssuer           = false,
                ValidateAudience         = false,
                ValidateIssuerSigningKey = true,
            }, out _);
            return int.Parse(principal.FindFirst(ClaimTypes.NameIdentifier).Value);
        }

        private static string GenerateTokenString()
        {
            using var rngCryptoServiceProvider = new RNGCryptoServiceProvider();
            var randomBytes = new byte[40];
            rngCryptoServiceProvider.GetBytes(randomBytes);
            return BitConverter.ToString(randomBytes).Replace("-", "");
        }

        private string GenerateJwtToken(Account account)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            };

            var key             = Encoding.ASCII.GetBytes(m_AuthSettings.Secret);
            var tokenHandler    = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = m_AuthSettings.AccessTokenTTL == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddMinutes(m_AuthSettings.AccessTokenTTL),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private int? ValidateJwtToken(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(m_AuthSettings.Secret);
            try
            {
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero,
                }, out SecurityToken validatedToken);
                var jwtToken = (JwtSecurityToken)validatedToken;
                var accountId = int.Parse(jwtToken.Claims.First(x => x.Type == ClaimTypes.NameIdentifier).Value);
                return accountId;
            }
            catch
            {
                return null;
            }
        }

        private RefreshToken GenerateRefreshToken(string ipAddress) => new()
        {
            Token          = GenerateTokenString(),
            ExpirationDate = DateTime.UtcNow.AddDays(m_AuthSettings.RefreshTokenTTL),
            CreationDate   = DateTime.UtcNow,
            CreatedByIp    = ipAddress
        };

        private void RemoveOldRefreshTokens(Account account) 
        {
            account.RefreshTokens.RemoveAll(x =>
                !x.IsActive &&
                x.CreationDate.AddDays(m_AuthSettings.RefreshTokenTTL) <= DateTime.UtcNow);
        }
    }
}

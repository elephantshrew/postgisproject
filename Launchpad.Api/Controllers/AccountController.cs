﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using IdentityModel.Client; //IdentityModel is a .NET standard helper library for claims-based identity, OAuth 2.0 and OpenID Connect
using Launchpad.App;
using Launchpad.App.Repositories.Interfaces;
using Launchpad.Models.Entities;
using Launchpad.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Launchpad.Api.Controllers
{
    [Route("api")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly SignInManager<User> _signInManager;
        private readonly UserManager<User> _userManager;
        private readonly IConfiguration _configuration;
        private readonly IUserRepository _userRepository;
        private readonly ILookupNormalizer _normalizer;
        private readonly ApplicationDbContext _context;
        private readonly ICategoryRepository _categoryRepository;
        private readonly ICityRepository _cityRepository;


        public AccountController(SignInManager<User> signInManager, UserManager<User> userManager, IConfiguration configuration, IUserRepository userRepository, ILookupNormalizer normalizer, ApplicationDbContext context, ICategoryRepository categoryRepository, ICityRepository cityRepository)
        {
            _signInManager = signInManager;
            _configuration = configuration;
            _userRepository = userRepository;
            _userManager = userManager;
            _normalizer = normalizer;
            _context = context;
            _categoryRepository = categoryRepository;
            _cityRepository = cityRepository;
        }

        [HttpPost("login")]

        public async Task<ActionResult<LoginResponseVM>> Login([FromBody] LoginVM login)
        {
            //validate model
            if (!ModelState.IsValid)
                return BadRequest("Invalid data");

            //validate user login (AspNetCore.Identity does this)
            var result = await _signInManager.PasswordSignInAsync(login.Email, login.Password, false, false).ConfigureAwait(false);

            //don't lockout user for now
            if (!result.Succeeded)
            {
                return BadRequest("Username/password invalid");
            }

            //get user profile
            var user = await _userRepository.GetUserByEmail(login.Email).ConfigureAwait(false);

            //get a token from identity server
            using (var httpClient = new HttpClient())
            {
                var authority = _configuration.GetSection("Identity").GetValue<string>("Authority");

                //call identity server
                var tokenResponse = await httpClient.RequestPasswordTokenAsync(new PasswordTokenRequest
                {
                    Address = authority + "/connect/token",
                    UserName = login.Email,
                    Password = login.Password,
                    ClientId = "mobile",
                    ClientSecret = "oVfHFTfwM3wjH826GN6fgIJUJ370cmzj",
                    Scope = "launchpadapi.scope"
                }).ConfigureAwait(false);

                if (tokenResponse.IsError)
                {
                    return BadRequest("Cannot grant access to user account");
                }



                return Ok(new LoginResponseVM(tokenResponse, user));

            }

        }
        //ok what do I need
        //need to take in registration fields from the request body, return response code and response body in json, asynchronously

        [HttpPost("register")]
        public async Task<ActionResult<UserRegisterResponseVM>> Register([FromBody] UserRegisterVM vm)
        {
            if (!ModelState.IsValid || vm.Password != vm.PasswordConfirmation)
                return BadRequest("Invalid Data");

            //var user = new User(vm);
            var user = new User
            {
                UserName = vm.Email,
                Email = vm.Email,
                FirstName = vm.FirstName,
                LastName = vm.LastName,
                NormalizedEmail = _normalizer.NormalizeEmail(vm.Email),
                Tos = vm.Tos,
                PhoneNumber = vm.Phone
            };
            var result = await _userManager.CreateAsync(user, vm.Password);
            //result = await _userManager.AddPasswordAsync(user, vm.Password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, "user");
                //normalize
                await _userManager.UpdateNormalizedEmailAsync(user);

                //generate email confirmation token
                var confirmation = await _userManager.GenerateEmailConfirmationTokenAsync(user);

                //email token to user
                var apiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");
                var client = new SendGridClient(apiKey);
                var from = new EmailAddress("willcho128@gmail.com", "Example User");
                var subject = "You are now registered!";
                var to = new EmailAddress(vm.Email, vm.FirstName);
                var plainTextContent = "Confirmation: " + confirmation;
                var htmlContent = "Confirmation: " + confirmation;
                var msg =  MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
                var response = await client.SendEmailAsync(msg);

                return Ok(new UserRegisterResponseVM("200 ok, awesome!"));



            }
            else
                return BadRequest("Register failed :(");
        }

        [HttpPatch("Verification")]
        public async Task<IActionResult> Verification([FromBody] VerifyVM vm)
        {
            var user = await _userManager.FindByEmailAsync(vm.Email);
            if (user == null)
                return BadRequest("Cannot find email");
            var result = await _userManager.ConfirmEmailAsync(user, vm.Token);
            if (result.Succeeded)
                return Ok("Verified");
            else
                return BadRequest("Email confirmation failed");
        }

        [HttpPost("ForgotPassword")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordVM vm)
        {
            var user = await _userManager.FindByEmailAsync(vm.Email);
            if (user == null)
                return BadRequest("Cannot find email");
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);

            //email token to user
            var apiKey = Environment.GetEnvironmentVariable("SENDGRID_API_KEY");
            var client = new SendGridClient(apiKey);
            var from = new EmailAddress("willcho128@gmail.com", "Example User");
            var subject = "You are now registered!";
            var to = new EmailAddress(user.Email, user.FirstName);
            var plainTextContent = "Reset token: " + resetToken;
            var htmlContent = "Reset token : " + resetToken;
            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainTextContent, htmlContent);
            var response = await client.SendEmailAsync(msg);
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                return Ok("Verified");
            else
                return BadRequest(response.StatusCode);
                   
        }

        [HttpPost("Reset")]
        public async Task<IActionResult> Reset([FromBody] ResetPasswordVM vm)
        {
            var user = await _userManager.FindByEmailAsync(vm.Email);
            if (user == null)
                return BadRequest("Cannot find email");
            var result = await _userManager.ResetPasswordAsync(user, vm.ResetToken, vm.NewPassword);
            if (result.Succeeded)
                return Ok("Password successfully reset");
            else
                return BadRequest("Could not reset password");

        }

        [HttpGet("Category")]
        public async Task<ActionResult<List<CategoryVM>>> ListAllCategories()
        {
            try
            {
                var result = await _categoryRepository.GetAll();
                return result;

            }
            catch
            {
                return StatusCode(500);
            }
        }

        [HttpGet("City")]
        public async Task<ActionResult<List<CityVM>>> ListAllCities()
        {
            try
            {
                var result = await _cityRepository.GetAll();
                return result;
            }
            catch
            {
                return StatusCode(500);
            }

        }



    }
}
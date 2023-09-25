// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Threading.Tasks;
using CopilotChat.WebApi.Auth;
using CopilotChat.WebApi.Models.Storage;
using CopilotChat.WebApi.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace CopilotChat.WebApi.Controllers;
public class UserConsentController : Controller
{
    private readonly ILogger<UserConsentController> _logger;
    private readonly UserConsentRepository _userConsentRepository;


    /// <summary>
    /// Constructs the consent controller
    /// </summary>
    /// <param name="userConsentRepository">The user consent repository.</param>
    /// <param name="logger">The logger.</param>
    public UserConsentController(UserConsentRepository userConsentRepository, ILogger<UserConsentController> logger)
    {
        this._logger = logger;
        this._userConsentRepository = userConsentRepository;
    }


    /// <summary>
    /// Upsert the User Consent
    /// </summary>
    /// <param name="userConsent">User consent information : Accept or not and version of the consent</param>
    /// <returns></returns>
    [HttpPost]
    [Route("UserConsent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UserConsentAsync([FromServices] IAuthInfo authInfo, [FromBody] UserConsent userConsent)
    {
        //Create UserConsent object, add to the repository
        if (userConsent == null)
        {
            return this.BadRequest("User consent does not exist.");
        }

        userConsent.Id = authInfo.UserId;
        userConsent.ConsentOn = DateTime.UtcNow;

        await this._userConsentRepository.CreateAsync(userConsent);
        return this.Ok();
    }
}

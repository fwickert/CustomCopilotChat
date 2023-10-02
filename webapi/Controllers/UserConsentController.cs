// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CopilotChat.WebApi.Auth;
using CopilotChat.WebApi.Models.Storage;
using CopilotChat.WebApi.Storage;
using Microsoft.AspNetCore.Authorization;
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
    [Route("consent/updateUserConsent")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UserConsentAsync([FromBody] UserConsent userConsent)
    {
        //Create UserConsent object, add to the repository
        if (userConsent == null)
        {
            return this.BadRequest("User consent does not exist.");
        }

        //userConsent.Id = user.UserId;
        //userConsent.ConsentOn = DateTime.UtcNow;

        await this._userConsentRepository.CreateAsync(userConsent);
        return this.Ok();
    }

    //Create function to get the user consent

    /// <summary>
    /// Get the User Consent
    /// </summary>
    ///<param name="userId">The user id.</param>
    ///
    [HttpGet]
    [Route("consent/getUserConsent/{userId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserConsentAsync(string userId)
    {
        var userConsent = await this._userConsentRepository.FindByUserIdAsync(userId);
        return this.Ok(userConsent);
    }

   
}

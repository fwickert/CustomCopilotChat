﻿// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using CopilotChat.WebApi.Models.Response;
using CopilotChat.WebApi.Options;
using CopilotChat.WebApi.Services.MemoryMigration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CopilotChat.WebApi.Controllers;

/// <summary>
/// Controller for reporting the status of chat migration.
/// </summary>
[ApiController]
public class MaintenanceController : ControllerBase
{
    internal const string GlobalSiteMaintenance = "GlobalSiteMaintenance";

    private readonly ILogger<MaintenanceController> _logger;
    private readonly IOptions<ServiceOptions> _serviceOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="MaintenanceController"/> class.
    /// </summary>
    public MaintenanceController(
        ILogger<MaintenanceController> logger,
        IOptions<ServiceOptions> serviceOptions)
    {
        this._logger = logger;
        this._serviceOptions = serviceOptions;
    }

    /// <summary>
    /// Route for reporting the status of site maintenance.
    /// </summary>
    [Route("maintenanceStatus")]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MaintenanceResult?>> GetMaintenanceStatusAsync(
        [FromServices] IChatMigrationMonitor migrationMonitor,
        CancellationToken cancellationToken = default)
    {
        MaintenanceResult? result = null;

        var migrationStatus = await migrationMonitor.GetCurrentStatusAsync(cancellationToken);

        if (migrationStatus != ChatMigrationStatus.None)
        {
            result =
                new MaintenanceResult
                {
                    Title = "Migrating Chat Memory",
                    Message = "An upgrade requires that all non-document memories be migrated.  This may take several minutes...",
                    Note = "Note: All document memories will need to be re-imported.",
                };
        }

        if (this._serviceOptions.Value.InMaintenance)
        {
            result = new MaintenanceResult(); // Default maintenance message
        }

        if (result != null)
        {
            return this.Ok(result);
        }

        return this.Ok();
    }
}

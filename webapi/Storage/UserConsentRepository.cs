// Copyright (c) Microsoft. All rights reserved.

using System.Threading.Tasks;
using CopilotChat.WebApi.Models.Storage;

namespace CopilotChat.WebApi.Storage;

public class UserConsentRepository : Repository<UserConsent>
{
    public UserConsentRepository(IStorageContext<UserConsent> storageContext)
        : base(storageContext)
    {
    }

    /// <summary>
    /// Finds User consent by userID.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>a user consent object</returns>
    public Task<UserConsent> FindByUserIdAsync(string userId)
    {
        return base.StorageContext.ReadAsync(userId, userId);
    }
}

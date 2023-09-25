// Copyright (c) Microsoft. All rights reserved.

using CopilotChat.WebApi.Models.Storage;

namespace CopilotChat.WebApi.Storage;

public class UserConsentRepository : Repository<UserConsent>
{
    public UserConsentRepository(IStorageContext<UserConsent> storageContext)
        : base(storageContext)
    {
    }
}

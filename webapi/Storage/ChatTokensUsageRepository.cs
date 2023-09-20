using CopilotChat.WebApi.Models.Storage;

namespace CopilotChat.WebApi.Storage;

public class ChatTokensUsageRepository : Repository<ChatTokensUsage>
{
    public ChatTokensUsageRepository(IStorageContext<ChatTokensUsage> storageContext)
        : base(storageContext)
    {
    }
}

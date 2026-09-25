using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ballknower.AI;

public interface IAiClient
{
    Task<OpenRouterMessage> SendWithToolsAsync(
        string model,
        List<OpenRouterMessage> messages);
}
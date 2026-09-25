
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Ballknower.AI;

public static class ConversationHistoryManager
{
    // A conservative approximation, not a model tokenizer.
    // Counts serialized message content and tool-call arguments.
    private const int CharactersPerToken = 3;

    private const int MessageOverheadTokens = 12;

    public static List<OpenRouterMessage> BuildRequest(
        IReadOnlyList<OpenRouterMessage> conversation,
        int historyTokenBudget)
    {
        var request = new List<OpenRouterMessage>();

        if (conversation.Count == 0)
            return request;

        // System instructions are always included.
        int start = 0;

        if (conversation[0].Role == "system")
        {
            request.Add(conversation[0]);
            start = 1;
        }

        // Group the conversation into complete exchanges.
        // A user message starts a new exchange; any assistant
        // messages and tool results belong to that exchange.
        var exchanges = new List<List<OpenRouterMessage>>();
        List<OpenRouterMessage>? current = null;

        for (int i = start; i < conversation.Count; i++)
        {
            var message = conversation[i];

            if (message.Role == "user")
            {
                current = new List<OpenRouterMessage>();
                exchanges.Add(current);
            }

            if (current is null)
            {
                // Preserve any unusual leading messages as
                // their own group rather than discarding them.
                current = new List<OpenRouterMessage>();
                exchanges.Add(current);
            }

            current.Add(message);
        }

        if (exchanges.Count == 0)
            return request;

        // Always retain the newest exchange, even if it exceeds
        // the selected budget. This preserves the active request
        // and its tool-call / tool-result sequence.
        int newestIndex = exchanges.Count - 1;

        var selected = new List<List<OpenRouterMessage>>
        {
            exchanges[newestIndex]
        };

        int usedTokens =
            EstimateExchangeTokens(exchanges[newestIndex]);

        int budget = Math.Max(1000, historyTokenBudget);

        // Add older complete exchanges from newest to oldest.
        // Stop at the first exchange that would exceed budget.
        for (int i = newestIndex - 1; i >= 0; i--)
        {
            int exchangeTokens =
                EstimateExchangeTokens(exchanges[i]);

            if (usedTokens + exchangeTokens > budget)
                break;

            selected.Insert(0, exchanges[i]);

            usedTokens += exchangeTokens;
        }

        foreach (var exchange in selected)
        {
            request.AddRange(exchange);
        }

        return request;
    }

    public static int EstimateRequestTokens(
        IReadOnlyList<OpenRouterMessage> messages)
    {
        int total = 0;

        foreach (var message in messages)
        {
            total += EstimateMessageTokens(message);
        }

        return total;
    }

    private static int EstimateExchangeTokens(
        IReadOnlyList<OpenRouterMessage> exchange)
    {
        return EstimateRequestTokens(exchange);
    }

    private static int EstimateMessageTokens(
        OpenRouterMessage message)
    {
        int characters =
            message.Role?.Length ?? 0;

        characters +=
            message.Content?.Length ?? 0;

        characters +=
            message.ToolCallId?.Length ?? 0;

        if (message.ToolCalls is not null)
        {
            foreach (var call in message.ToolCalls)
            {
                characters += call.Id?.Length ?? 0;
                characters += call.Type?.Length ?? 0;
                characters +=
                    call.Function?.Name?.Length ?? 0;
                characters +=
                    call.Function?.Arguments?.Length ?? 0;
            }
        }

        return MessageOverheadTokens +
            (int)Math.Ceiling(
                characters / (double)CharactersPerToken);
    }
}
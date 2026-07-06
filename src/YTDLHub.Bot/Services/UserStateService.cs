using System.Collections.Concurrent;
using YTDLHub.Core.Models;

namespace YTDLHub.Bot.Services;

/// <summary>
/// In-memory store that tracks per-user conversation state.
/// Used to associate a URL with a chat while the user picks a quality.
/// </summary>
public sealed class UserStateService
{
    private readonly ConcurrentDictionary<long, UserState> _states = new();

    public void SetPendingUrl(long chatId, string url, VideoInfo info)
    {
        _states[chatId] = new UserState(url, info, DateTime.UtcNow);
    }

    public bool TryGetPendingUrl(long chatId, out UserState state)
    {
        if (_states.TryGetValue(chatId, out state!) &&
            DateTime.UtcNow - state.CreatedAt < TimeSpan.FromMinutes(10))
        {
            return true;
        }

        _states.TryRemove(chatId, out _);
        state = default!;
        return false;
    }

    public void Clear(long chatId) => _states.TryRemove(chatId, out _);
}

public record UserState(string Url, VideoInfo Info, DateTime CreatedAt);

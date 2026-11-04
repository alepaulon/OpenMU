// <copyright file="LostConnectionDetectionPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns;

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Disconnects players whose client stopped to report that it's alive.
/// </summary>
/// <remarks>
/// A connection can get lost without being closed, e.g. when the network of a mobile client drops or a
/// browser tab is frozen. The server doesn't notice that by itself, so the player would stay connected, and its
/// account would stay registered at the login server - every further login of the account would be rejected.
/// The game client reports periodically that it's alive (the ping, every 20 seconds), even when the player
/// doesn't do anything. When these reports stop for longer than the configured timeout while the player is in
/// the world, the connection is considered lost and the player is disconnected.
/// Players whose client didn't report it since entering the world, e.g. because it doesn't support it, are not
/// affected. Neither are players outside of the world, because not every client reports it there.
/// <para>
/// The periodic tasks of a game are called one after another, so a check must not wait for a disconnection
/// which doesn't complete, e.g. because the player is stuck in an action which holds its state. The players
/// are disconnected at the same time, and the check waits only a limited time for them. A player whose
/// disconnection is still running is not disconnected again.
/// </para>
/// </remarks>
[PlugIn]
[Display(Name = nameof(PlugInResources.LostConnectionDetectionPlugIn_Name), Description = nameof(PlugInResources.LostConnectionDetectionPlugIn_Description), ResourceType = typeof(PlugInResources))]
[Guid("18632228-3E40-4C75-839D-AF7843089E82")]
public class LostConnectionDetectionPlugIn : IPeriodicTaskPlugIn, ISupportCustomConfiguration<LostConnectionDetectionConfiguration>, ISupportDefaultCustomConfiguration
{
    /// <summary>
    /// The interval in which the players are checked. It's shorter than any sensible timeout,
    /// so that a lost connection is detected shortly after the timeout elapsed.
    /// </summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The maximum time a check waits for the disconnections it started.
    /// </summary>
    private static readonly TimeSpan DisconnectionWaitTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The players whose disconnection was started, but didn't complete yet.
    /// </summary>
    private readonly ConcurrentDictionary<Player, byte> _disconnectingPlayers = new();

    private DateTime _nextCheckUtc = DateTime.UtcNow;

    /// <inheritdoc />
    public LostConnectionDetectionConfiguration? Configuration { get; set; }

    /// <inheritdoc />
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        var now = DateTime.UtcNow;
        var timeout = (this.Configuration ??= new LostConnectionDetectionConfiguration()).Timeout;
        if (now < this._nextCheckUtc || timeout <= TimeSpan.Zero)
        {
            return;
        }

        this._nextCheckUtc = now + CheckInterval;
        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        List<(Player Player, Task Disconnection)>? disconnections = null;
        foreach (var player in players)
        {
            if (player.LastAliveReport is not { } lastAliveReport
                || now - lastAliveReport <= timeout
                || !player.IsConnected
                || !this._disconnectingPlayers.TryAdd(player, 0))
            {
                continue;
            }

            player.Logger.LogInformation("Disconnecting {Player}, because its client didn't report since {LastAliveReport} that it's alive.", player, lastAliveReport);
            (disconnections ??= []).Add((player, this.DisconnectAsync(player).AsTask()));
        }

        if (disconnections is null)
        {
            return;
        }

        try
        {
            await Task.WhenAll(disconnections.Select(d => d.Disconnection)).WaitAsync(DisconnectionWaitTime).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            foreach (var (player, _) in disconnections.Where(d => !d.Disconnection.IsCompleted))
            {
                player.Logger.LogWarning("The disconnection of {Player} didn't complete within {WaitTime}. It may be stuck.", player, DisconnectionWaitTime);
            }
        }
    }

    /// <inheritdoc />
    public void ForceStart()
    {
        this._nextCheckUtc = DateTime.MinValue;
    }

    /// <inheritdoc />
    public object CreateDefaultConfig()
    {
        return new LostConnectionDetectionConfiguration();
    }

    private async ValueTask DisconnectAsync(Player player)
    {
        try
        {
            await player.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            player.Logger.LogError(ex, "Error while disconnecting {Player}.", player);
        }
        finally
        {
            this._disconnectingPlayers.TryRemove(player, out _);
        }
    }
}

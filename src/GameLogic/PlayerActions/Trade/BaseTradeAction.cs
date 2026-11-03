// <copyright file="BaseTradeAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Trade;

using System.Diagnostics.Metrics;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Interfaces;

/// <summary>
/// Action to cancel a trade.
/// </summary>
public class BaseTradeAction
{
    private static readonly Meter TradeMeter = new(MeterName);

    /// <summary>
    /// Gets the name of the meter of this class.
    /// </summary>
    internal static string MeterName => typeof(BaseTradeAction).Namespace ?? nameof(BaseTradeAction);

    /// <summary>
    /// Gets the counter for finished trades.
    /// </summary>
    protected static Counter<int> FinishedTrades { get; } = TradeMeter.CreateCounter<int>("FinishedTradesCount");

    /// <summary>
    /// Gets the counter for cancelled trades.
    /// </summary>
    private static Counter<int> CancelledTrades { get; } = TradeMeter.CreateCounter<int>("CancelledTradesCount");

    /// <summary>
    /// Cancels the trade.
    /// </summary>
    /// <param name="trader">The trader.</param>
    /// <param name="checkState">If set to <c>true</c>, the player state is checked. Otherwise, it's ignored.</param>
    /// <remarks>
    /// The offer (the items in the trade and the offered money) goes back to the trader. The inventory is
    /// not rolled back to its state when the trade was opened: while the trade is open, the inventory and
    /// the money can change by other means (e.g. dropping an item, a sale in the personal store or a paid
    /// command), and a rollback would undo these changes. A dropped item which another player picked up
    /// already would be restored, for example, which duplicates it.
    /// </remarks>
    protected async ValueTask CancelTradeAsync(ITrader trader, bool checkState = true)
    {
        CancelledTrades.Add(1);
        await using (var context = await trader.PlayerState.TryBeginAdvanceToAsync(PlayerState.EnteredWorld).ConfigureAwait(false))
        {
            if (checkState && !context.Allowed)
            {
                return;
            }

            await this.ReturnOfferAsync(trader).ConfigureAwait(false);
        }

        this.ResetTradeState(trader);
    }

    /// <summary>
    /// Resets the trade.
    /// </summary>
    /// <param name="trader">The trader.</param>
    protected void ResetTradeState(ITrader trader)
    {
        trader.TradingPartner = null;
        trader.TradingMoney = 0;
        trader.BackupInventory = null;
        trader.TemporaryStorage!.Clear();
    }

    /// <summary>
    /// Returns the offer to the trader: the offered money and the items in the trade storage.
    /// </summary>
    /// <param name="trader">The trader.</param>
    private async ValueTask ReturnOfferAsync(ITrader trader)
    {
        if (trader.TradingMoney > 0)
        {
            // The offered money was taken from the inventory when it was offered. It's limited, because the
            // trader may have got more money in the meantime.
            trader.Money = (int)Math.Min(int.MaxValue, (long)trader.Money + trader.TradingMoney);
        }

        trader.TradingMoney = 0;
        if (trader.TemporaryStorage is not { } temporaryStorage
            || trader.Inventory is not { } inventory
            || !temporaryStorage.Items.Any())
        {
            return;
        }

        var items = temporaryStorage.Items.ToList();
        if (await inventory.TryTakeAllAsync(temporaryStorage).ConfigureAwait(false))
        {
            return;
        }

        // The items came from the inventory, so they usually fit back. Otherwise, as many as possible are returned.
        foreach (var item in items)
        {
            try
            {
                await temporaryStorage.RemoveItemAsync(item).ConfigureAwait(false);
                if (!await inventory.AddItemAsync(item).ConfigureAwait(false))
                {
                    trader.Logger.LogError("Failed to return item {item} of the cancelled trade to the inventory of {trader}. The item is lost.", item, trader.Name);
                }
            }
            catch (Exception ex)
            {
                trader.Logger.LogError(ex, "Error returning item {item} of the cancelled trade to the inventory of {trader}.", item, trader.Name);
            }
        }
    }

    /// <summary>
    /// Sends the message to the trader.
    /// </summary>
    /// <param name="trader">The trader.</param>
    /// <param name="messageKey">The key of the message in <see cref="PlayerMessage"/>.</param>
    protected async ValueTask SendMessageAsync(ITrader trader, string messageKey)
    {
        if (trader is Player player)
        {
            try
            {
                await player.ShowLocalizedBlueMessageAsync(messageKey).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                trader.Logger.LogError(ex, "Error sending a message");
            }
        }
    }
}

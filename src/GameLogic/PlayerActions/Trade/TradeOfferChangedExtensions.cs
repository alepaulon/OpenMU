// <copyright file="TradeOfferChangedExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Trade;

using MUnique.OpenMU.GameLogic.Views.Trade;

/// <summary>
/// Extensions for <see cref="ITrader"/> to handle a change of the offer during an ongoing trade.
/// </summary>
internal static class TradeOfferChangedExtensions
{
    /// <summary>
    /// Resets the trade button state of both traders after the offer (items or money) of the
    /// <paramref name="trader"/> changed, and notifies both of them about it.
    /// Otherwise, a trading partner who already accepted would accept an offer which he didn't see.
    /// </summary>
    /// <param name="trader">The trader whose offer changed.</param>
    public static async ValueTask ResetTradeButtonsAfterOfferChangeAsync(this ITrader trader)
    {
        await trader.PlayerState.TryAdvanceToAsync(PlayerState.TradeOpened).ConfigureAwait(false);
        await trader.InvokeViewPlugInAsync<IChangeTradeButtonStatePlugIn>(p => p.ChangeTradeButtonStateAsync(TradeButtonState.Red)).ConfigureAwait(false);
        if (trader.TradingPartner is { } tradingPartner)
        {
            await tradingPartner.PlayerState.TryAdvanceToAsync(PlayerState.TradeOpened).ConfigureAwait(false);
            await tradingPartner.InvokeViewPlugInAsync<IChangeTradeButtonStatePlugIn>(p => p.ChangeTradeButtonStateAsync(TradeButtonState.Red)).ConfigureAwait(false);
        }
    }
}

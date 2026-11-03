// <copyright file="TradeButtonAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Trade;

using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.GameLogic.Views.Trade;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Action to change the trade button state.
/// </summary>
public class TradeButtonAction : BaseTradeAction
{
    /// <summary>
    /// Tries to change the trade button change to the new <paramref name="tradeButtonState"/>.
    /// </summary>
    /// <param name="trader">The trader.</param>
    /// <param name="tradeButtonState">The new state of the trade button.</param>
    public async ValueTask TradeButtonChangedAsync(ITrader trader, TradeButtonState tradeButtonState)
    {
        using var loggerScope = (trader as Player)?.Logger.BeginScope(this.GetType());
        var success = (tradeButtonState == TradeButtonState.Checked && await trader.PlayerState.TryAdvanceToAsync(PlayerState.TradeButtonPressed).ConfigureAwait(false))
                      || (tradeButtonState == TradeButtonState.Unchecked && await trader.PlayerState.TryAdvanceToAsync(PlayerState.TradeOpened).ConfigureAwait(false));
        if (!success)
        {
            return;
        }

        var tradingPartner = trader.TradingPartner;

        if (trader.PlayerState.CurrentState == PlayerState.TradeButtonPressed
            && tradingPartner is not null
            && tradingPartner.PlayerState.CurrentState == PlayerState.TradeButtonPressed)
        {
            TradeResult result = await this.InternalFinishTradeAsync(trader, tradingPartner).ConfigureAwait(false);
            if (result != TradeResult.Success)
            {
                await this.CancelTradeAsync(tradingPartner, false).ConfigureAwait(false);
                await this.CancelTradeAsync(trader, false).ConfigureAwait(false);
                (trader as Player)?.Logger.LogDebug($"Cancelled the trade because of unfinished state. trader: {trader.Name}, partner:{tradingPartner.Name}");
            }
            else
            {
                // The offered money was handed over. It's kept until the traders entered the world again, so that
                // plugins which observe this state change still see what was traded. Kept beyond, a cancellation
                // of the next trade request would return it a second time.
                trader.TradingMoney = 0;
                tradingPartner.TradingMoney = 0;
            }

            await trader.InvokeViewPlugInAsync<ITradeFinishedPlugIn>(p => p.TradeFinishedAsync(result)).ConfigureAwait(false);
            await tradingPartner.InvokeViewPlugInAsync<ITradeFinishedPlugIn>(p => p.TradeFinishedAsync(result)).ConfigureAwait(false);
        }
        else if (tradingPartner is not null)
        {
            await tradingPartner.InvokeViewPlugInAsync<IChangeTradeButtonStatePlugIn>(p => p.ChangeTradeButtonStateAsync(TradeButtonState.Checked)).ConfigureAwait(false);
        }
        else
        {
            // nothing to do.
        }
    }

    private static async ValueTask<bool> TryAddItemsOfTradingPartnerAsync(ITrader trader)
    {
        if (trader.TradingPartner?.TemporaryStorage?.Items.Any() ?? false)
        {
            return await trader.Inventory!.TryTakeAllAsync(trader.TradingPartner.TemporaryStorage!).ConfigureAwait(false);
        }

        return true;
    }

    private async ValueTask<TradeResult> InternalFinishTradeAsync(ITrader trader, ITrader tradingPartner)
    {
        await using var context = await trader.PlayerState.TryBeginAdvanceToAsync(PlayerState.EnteredWorld).ConfigureAwait(false);
        await using var partnerContext = await tradingPartner.PlayerState.TryBeginAdvanceToAsync(PlayerState.EnteredWorld).ConfigureAwait(false);
        if (!context.Allowed || !partnerContext.Allowed)
        {
            context.Allowed = false;
            partnerContext.Allowed = false;
            (trader as Player)?.Logger.LogDebug($"Unexpected player states. {trader.Name}:{trader.PlayerState}, {tradingPartner.Name}:{tradingPartner.PlayerState}");
            return TradeResult.Cancelled;
        }

        using var itemContext = trader.GameContext.PersistenceContextProvider.CreateNewTradeContext();
        var traderItems = trader.TemporaryStorage!.Items.ToList();
        var tradePartnerItems = tradingPartner.TemporaryStorage!.Items.ToList();

        // Checked here, and not only when an item is moved into the trade: the temporary storage
        // is shared with other windows (e.g. the chaos machine), so it may already contain items.
        // Both are checked, so that each trader gets the message about their own items.
        var traderItemsAreTradable = await this.CheckItemsAreTradableAsync(trader, traderItems).ConfigureAwait(false);
        var partnerItemsAreTradable = await this.CheckItemsAreTradableAsync(tradingPartner, tradePartnerItems).ConfigureAwait(false);
        if (!traderItemsAreTradable || !partnerItemsAreTradable)
        {
            return TradeResult.Cancelled;
        }

        if (!CanReceiveMoney(trader, tradingPartner.TradingMoney) || !CanReceiveMoney(tradingPartner, trader.TradingMoney))
        {
            (trader as Player)?.Logger.LogWarning(
                "Trade between {0} and {1} cancelled, because the traded money would exceed the maximum inventory money.",
                trader.Name,
                tradingPartner.Name);
            await this.SendInventoryFullMessagesAsync(trader, tradingPartner).ConfigureAwait(false);
            return TradeResult.FailedByFullInventory;
        }

        this.AttachItemsToPersistenceContext(traderItems, itemContext);
        this.AttachItemsToPersistenceContext(tradePartnerItems, itemContext);

        // The slots in the trade storages, to put the items back if the trade can't be finished.
        var traderItemSlots = traderItems.Select(item => (Item: item, Slot: item.ItemSlot)).ToList();
        var tradePartnerItemSlots = tradePartnerItems.Select(item => (Item: item, Slot: item.ItemSlot)).ToList();

        if (!await TryAddItemsOfTradingPartnerAsync(trader).ConfigureAwait(false))
        {
            // Nothing was moved, the failed transfer was already rolled back.
            await this.SendInventoryFullMessagesAsync(trader, tradingPartner).ConfigureAwait(false);
            return TradeResult.FailedByFullInventory;
        }

        if (!await TryAddItemsOfTradingPartnerAsync(tradingPartner).ConfigureAwait(false))
        {
            // The trader already took the items of the partner. A cancelled trade returns the items of
            // the trade storages to their owners, so they have to be put back there.
            await PutBackIntoTradeAsync(trader.Inventory!, tradingPartner.TemporaryStorage!, tradePartnerItemSlots).ConfigureAwait(false);
            await this.SendInventoryFullMessagesAsync(trader, tradingPartner).ConfigureAwait(false);
            return TradeResult.FailedByFullInventory;
        }

        try
        {
            this.DetachItemsFromPersistenceContext(traderItems, trader.PersistenceContext);
            this.DetachItemsFromPersistenceContext(tradePartnerItems, tradingPartner.PersistenceContext);
            await itemContext.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Nothing was saved, so the items go back to the trade storages and to the persistence contexts
            // of their owners. The cancellation of the trade returns them.
            await PutBackIntoTradeAsync(tradingPartner.Inventory!, trader.TemporaryStorage!, traderItemSlots).ConfigureAwait(false);
            await PutBackIntoTradeAsync(trader.Inventory!, tradingPartner.TemporaryStorage!, tradePartnerItemSlots).ConfigureAwait(false);
            this.AttachItemsToPersistenceContext(traderItems, trader.PersistenceContext);
            this.AttachItemsToPersistenceContext(tradePartnerItems, tradingPartner.PersistenceContext);

            await this.SendMessageAsync(trader, nameof(PlayerMessage.UnexpectedErrorDuringClosingTrade)).ConfigureAwait(false);
            await this.SendMessageAsync(tradingPartner, nameof(PlayerMessage.UnexpectedErrorDuringClosingTrade)).ConfigureAwait(false);
            context.Allowed = false;
            partnerContext.Allowed = false;
            (trader as Player)?.Logger.LogError(exception, $"An unexpected error occured during closing the trade. trader: {trader.Name}, partner:{tradingPartner.Name}");
            return TradeResult.Cancelled;
        }

        // From here on, the trade is saved and can't be undone.
        this.AttachItemsToPersistenceContext(traderItems, tradingPartner.PersistenceContext);
        this.AttachItemsToPersistenceContext(tradePartnerItems, trader.PersistenceContext);
        var moneyOfTrader = trader.TradingMoney;
        var moneyOfPartner = tradingPartner.TradingMoney;
        this.ResetTradeState(tradingPartner);
        this.ResetTradeState(trader);
        trader.Money += moneyOfPartner;
        tradingPartner.Money += moneyOfTrader;
        FinishedTrades.Add(1);

        try
        {
            await tradingPartner.InvokeViewPlugInAsync<IChangeTradeButtonStatePlugIn>(p => p.ChangeTradeButtonStateAsync(TradeButtonState.Checked)).ConfigureAwait(false);
            this.CallPlugIn(traderItems, trader, tradingPartner);
            this.CallPlugIn(tradePartnerItems, tradingPartner, trader);
        }
        catch (Exception exception)
        {
            (trader as Player)?.Logger.LogError(exception, $"An unexpected error occured after closing the trade. trader: {trader.Name}, partner:{tradingPartner.Name}");
        }

        return TradeResult.Success;
    }

    private static bool CanReceiveMoney(ITrader receiver, int amount)
    {
        var maximum = receiver.GameContext.Configuration?.MaximumInventoryMoney ?? int.MaxValue;
        return (long)receiver.Money + amount <= maximum;
    }

    private static async ValueTask PutBackIntoTradeAsync(IStorage inventory, IStorage tradeStorage, IEnumerable<(Item Item, byte Slot)> itemSlots)
    {
        foreach (var (item, slot) in itemSlots)
        {
            await inventory.RemoveItemAsync(item).ConfigureAwait(false);
            if (!await tradeStorage.AddItemAsync(slot, item).ConfigureAwait(false))
            {
                await tradeStorage.AddItemAsync(item).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask SendInventoryFullMessagesAsync(ITrader trader, ITrader tradingPartner)
    {
        // TODO: Maybe notify which player has the full inventory.
        await this.SendMessageAsync(trader, nameof(PlayerMessage.InventoryFull)).ConfigureAwait(false);
        await this.SendMessageAsync(tradingPartner, nameof(PlayerMessage.InventoryFull)).ConfigureAwait(false);
    }

    private async ValueTask<bool> CheckItemsAreTradableAsync(ITrader trader, IEnumerable<Item> items)
    {
        var notTradable = items.FirstOrDefault(item => item.Definition is { IsTradable: false });
        if (notTradable is null)
        {
            return true;
        }

        (trader as Player)?.Logger.LogWarning(
            "Trader {0} offered {1}, which its item definition doesn't allow to trade. The trade is cancelled.",
            trader.Name,
            notTradable);
        await this.SendMessageAsync(trader, nameof(PlayerMessage.ItemCannotBeTraded)).ConfigureAwait(false);
        return false;
    }

    private void CallPlugIn(IEnumerable<Item> items, ITrader source, ITrader target)
    {
        var point = target.GameContext.PlugInManager.GetPlugInPoint<IItemTradedToOtherPlayerPlugIn>();
        if (point is null)
        {
            return;
        }

        foreach (var item in items)
        {
            point.ItemTraded(source, target, item);
        }
    }

    private void AttachItemsToPersistenceContext(IEnumerable<Item> items, IContext itemContext)
    {
        foreach (var item in items)
        {
            itemContext.Attach(item);
        }
    }

    private void DetachItemsFromPersistenceContext(IEnumerable<Item> items, IContext itemContext)
    {
        foreach (var item in items)
        {
            itemContext.Detach(item);
        }
    }
}
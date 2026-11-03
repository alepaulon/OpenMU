// <copyright file="TradeTest.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MUnique.OpenMU.DataModel;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlayerActions.Items;
using MUnique.OpenMU.GameLogic.PlayerActions.Trade;
using MUnique.OpenMU.GameLogic.Views.Trade;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Tests for the trade actions.
/// </summary>
[TestFixture]
public class TradeTest
{
    /// <summary>
    /// Tests the trade request.
    /// </summary>
    [Test]
    public async ValueTask TestTradeRequestAsync()
    {
        var player = this.CreateTrader(PlayerState.EnteredWorld); // The player which will send the trade request
        var tradePartner = this.CreateTrader(PlayerState.EnteredWorld); // The player which will receive the trade request

        var packetHandler = new TradeRequestAction();
        var success = await packetHandler.RequestTradeAsync(player, tradePartner).ConfigureAwait(false);
        Assert.AreEqual(true, success);
        Assert.AreSame(tradePartner, player.TradingPartner);
        Assert.AreSame(player, tradePartner.TradingPartner);
        Assert.AreEqual(PlayerState.TradeRequested, player.PlayerState.CurrentState);
        Assert.AreEqual(PlayerState.TradeRequested, tradePartner.PlayerState.CurrentState);
        Mock.Get(tradePartner.ViewPlugIns.GetPlugIn<IShowTradeRequestPlugIn>()!).Verify(view => view!.ShowTradeRequestAsync(player), Times.Once);
    }

    /// <summary>
    /// Tests the trade response.
    /// </summary>
    [Test]
    public async ValueTask TestTradeResponseAsync()
    {
        var requester = this.CreateTrader(PlayerState.TradeRequested);
        var responder = this.CreateTrader(PlayerState.TradeRequested);
        requester.TradingPartner = responder;
        responder.TradingPartner = requester;
        var responseHandler = new TradeAcceptAction();
        await responseHandler.HandleTradeAcceptAsync(responder, true).ConfigureAwait(false);
        Assert.AreEqual(requester.PlayerState.CurrentState, PlayerState.TradeOpened);
        Assert.AreEqual(responder.PlayerState.CurrentState, PlayerState.TradeOpened);
        Mock.Get(requester.ViewPlugIns.GetPlugIn<IShowTradeRequestAnswerPlugIn>()!).Verify(view => view!.ShowTradeRequestAnswerAsync(true), Times.Once);
    }

    /// <summary>
    /// Tests the cancellation of a trade.
    /// </summary>
    [Test]
    public async ValueTask TradeCancelTestAsync()
    {
        var trader1 = this.CreateTrader(PlayerState.TradeOpened);
        var trader2 = this.CreateTrader(PlayerState.TradeOpened);
        trader1.TradingPartner = trader2;
        trader2.TradingPartner = trader1;
        var cancelTrader = new TradeCancelAction();
        await cancelTrader.CancelTradeAsync(trader1).ConfigureAwait(false);
        Assert.AreEqual(PlayerState.EnteredWorld, trader1.PlayerState.CurrentState);
        Assert.AreEqual(PlayerState.EnteredWorld, trader2.PlayerState.CurrentState);

        Mock.Get(trader1.ViewPlugIns.GetPlugIn<ITradeFinishedPlugIn>()!).Verify(view => view!.TradeFinishedAsync(TradeResult.Cancelled), Times.Once);
        Mock.Get(trader2.ViewPlugIns.GetPlugIn<ITradeFinishedPlugIn>()!).Verify(view => view!.TradeFinishedAsync(TradeResult.Cancelled), Times.Once);
    }

    /// <summary>
    /// Tests the finishing of a trade.
    /// </summary>
    [Test]
    public async ValueTask TradeFinishTestAsync()
    {
        var trader1 = this.CreateTrader(PlayerState.TradeOpened);
        var trader2 = this.CreateTrader(PlayerState.TradeOpened);
        trader1.TradingPartner = trader2;
        trader2.TradingPartner = trader1;

        var gameContext = new Mock<IGameContext>();
        gameContext.Setup(c => c.PlugInManager).Returns(new PlugInManager(null, new NullLoggerFactory(), null, null));
        gameContext.Setup(c => c.PersistenceContextProvider).Returns(new InMemoryPersistenceContextProvider());

        Mock.Get(trader1).Setup(m => m.GameContext).Returns(gameContext.Object);
        Mock.Get(trader2).Setup(m => m.GameContext).Returns(gameContext.Object);

        var tradeButtonHandler = new TradeButtonAction();
        await tradeButtonHandler.TradeButtonChangedAsync(trader1, TradeButtonState.Unchecked).ConfigureAwait(false);
        Assert.AreEqual(trader1.PlayerState.CurrentState, PlayerState.TradeOpened);
        await tradeButtonHandler.TradeButtonChangedAsync(trader1, TradeButtonState.Checked).ConfigureAwait(false);
        Assert.AreEqual(trader1.PlayerState.CurrentState, PlayerState.TradeButtonPressed);
        Assert.AreEqual(trader2.PlayerState.CurrentState, PlayerState.TradeOpened);
        await tradeButtonHandler.TradeButtonChangedAsync(trader2, TradeButtonState.Checked).ConfigureAwait(false);
        Assert.AreEqual(trader1.PlayerState.CurrentState, PlayerState.EnteredWorld);
        Assert.AreEqual(trader2.PlayerState.CurrentState, PlayerState.EnteredWorld);
        Mock.Get(trader1.ViewPlugIns.GetPlugIn<ITradeFinishedPlugIn>()!).Verify(view => view!.TradeFinishedAsync(TradeResult.Success), Times.Once);
        Mock.Get(trader2.ViewPlugIns.GetPlugIn<ITradeFinishedPlugIn>()!).Verify(view => view!.TradeFinishedAsync(TradeResult.Success), Times.Once);
    }

    /// <summary>
    /// Tests a trade of items.
    /// </summary>
    [Test]
    public async ValueTask TradeItemsAsync()
    {
        var trader1 = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        var trader2 = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        var tradeRequestAction = new TradeRequestAction();
        var tradeResponseAction = new TradeAcceptAction();

        var item1 = this.GetItem();
        var item2 = this.GetItem();
        await trader1.Inventory!.AddItemAsync(20, item1).ConfigureAwait(false);
        await trader1.Inventory.AddItemAsync(21, item2).ConfigureAwait(false);
        await tradeRequestAction.RequestTradeAsync(trader1, trader2).ConfigureAwait(false);
        await tradeResponseAction.HandleTradeAcceptAsync(trader2, true).ConfigureAwait(false);
        var itemMoveAction = new MoveItemAction();
        await itemMoveAction.MoveItemAsync(trader1, 20, Storages.Inventory, 0, Storages.Trade).ConfigureAwait(false);
        await itemMoveAction.MoveItemAsync(trader1, 21, Storages.Inventory, 2, Storages.Trade).ConfigureAwait(false);
        Assert.That(trader1.TemporaryStorage!.Items.First(), Is.SameAs(item1));

        var tradeButtonHandler = new TradeButtonAction();
        await tradeButtonHandler.TradeButtonChangedAsync(trader1, TradeButtonState.Checked).ConfigureAwait(false);
        await tradeButtonHandler.TradeButtonChangedAsync(trader2, TradeButtonState.Checked).ConfigureAwait(false);
        Assert.That(trader1.Inventory.ItemStorage.Items, Is.Empty);
        Assert.That(trader2.Inventory!.ItemStorage.Items.First(), Is.SameAs(item1));
    }

    /// <summary>
    /// Tests a trade of items, when it failes due to missing inventory space.
    /// </summary>
    [Test]
    public async ValueTask TradeFailedItemsNotFitAsync()
    {
        var trader1 = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        var trader2 = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        var tradeRequestAction = new TradeRequestAction();
        var tradeResponseAction = new TradeAcceptAction();

        // Fill up inventory of the receiving player
        for (byte i = (byte)(InventoryConstants.LastEquippableItemSlotIndex + 1); i < 64 + InventoryConstants.LastEquippableItemSlotIndex; i++)
        {
            var item = this.GetItem();
            await trader2.Inventory!.AddItemAsync(i, item).ConfigureAwait(false);
        }

        // Create items which should be traded.
        var item1 = this.GetItem();
        var item2 = this.GetItem();
        await trader1.Inventory!.AddItemAsync(20, item1).ConfigureAwait(false);
        await trader1.Inventory.AddItemAsync(21, item2).ConfigureAwait(false);

        // Set up the trade
        await tradeRequestAction.RequestTradeAsync(trader1, trader2).ConfigureAwait(false);
        await tradeResponseAction.HandleTradeAcceptAsync(trader2, true).ConfigureAwait(false);
        var itemMoveAction = new MoveItemAction();
        await itemMoveAction.MoveItemAsync(trader1, 20, Storages.Inventory, 0, Storages.Trade).ConfigureAwait(false);
        await itemMoveAction.MoveItemAsync(trader1, 21, Storages.Inventory, 2, Storages.Trade).ConfigureAwait(false);
        Assert.That(trader1.TemporaryStorage!.Items.First(), Is.SameAs(item1));

        // Accept the trade on both ends
        var tradeButtonHandler = new TradeButtonAction();
        await tradeButtonHandler.TradeButtonChangedAsync(trader1, TradeButtonState.Checked).ConfigureAwait(false);
        await tradeButtonHandler.TradeButtonChangedAsync(trader2, TradeButtonState.Checked).ConfigureAwait(false);

        // Check result
        Assert.That(trader1.Inventory.ItemStorage.Items, Is.Not.Empty);
        Assert.That(trader1.Inventory!.ItemStorage.Items.First(), Is.SameAs(item1));
        Assert.That(trader1.Inventory!.ItemStorage.Items.Last(), Is.SameAs(item2));
    }

    /// <summary>
    /// Tests that a cancelled trade returns the offered items and the offered money to the trader.
    /// </summary>
    [Test]
    public async ValueTask TradeCancelReturnsTheOfferAsync()
    {
        var (trader1, trader2) = await this.CreateTradersAsync().ConfigureAwait(false);
        trader1.Money = 1000;
        var item = this.GetItem();
        await trader1.Inventory!.AddItemAsync(20, item).ConfigureAwait(false);
        await this.OpenTradeAsync(trader1, trader2).ConfigureAwait(false);
        await new MoveItemAction().MoveItemAsync(trader1, 20, Storages.Inventory, 0, Storages.Trade).ConfigureAwait(false);
        await new TradeMoneyAction().TradeMoneyAsync(trader1, 300).ConfigureAwait(false);
        Assert.That(trader1.Money, Is.EqualTo(700));
        Assert.That(trader1.TradingMoney, Is.EqualTo(300));

        await new TradeCancelAction().CancelTradeAsync(trader1).ConfigureAwait(false);

        Assert.That(trader1.PlayerState.CurrentState, Is.EqualTo(PlayerState.EnteredWorld));
        Assert.That(trader2.PlayerState.CurrentState, Is.EqualTo(PlayerState.EnteredWorld));
        Assert.That(trader1.Inventory.ItemStorage.Items, Is.EquivalentTo(new[] { item }));
        Assert.That(trader1.TemporaryStorage!.Items, Is.Empty);
        Assert.That(trader1.Money, Is.EqualTo(1000));
        Assert.That(trader1.TradingMoney, Is.EqualTo(0));
    }

    /// <summary>
    /// Tests that a cancelled trade keeps the changes of the inventory and the money which happened outside
    /// of the trade while it was open, e.g. an item which was sold in the personal store.
    /// Rolling back the inventory to its state when the trade was opened would restore such an item,
    /// which means that it's duplicated.
    /// </summary>
    [Test]
    public async ValueTask TradeCancelKeepsChangesOutsideOfTheTradeAsync()
    {
        var (trader1, trader2) = await this.CreateTradersAsync().ConfigureAwait(false);
        trader1.Money = 1000;
        var offeredItem = this.GetItem();
        var soldItem = this.GetItem();
        await trader1.Inventory!.AddItemAsync(20, offeredItem).ConfigureAwait(false);
        await trader1.Inventory.AddItemAsync(21, soldItem).ConfigureAwait(false);
        await this.OpenTradeAsync(trader1, trader2).ConfigureAwait(false);
        await new MoveItemAction().MoveItemAsync(trader1, 20, Storages.Inventory, 0, Storages.Trade).ConfigureAwait(false);

        // The item leaves the inventory and money is spent, while the trade is open.
        await trader1.Inventory.RemoveItemAsync(soldItem).ConfigureAwait(false);
        Assert.That(trader1.TryRemoveMoney(400), Is.True);

        await new TradeCancelAction().CancelTradeAsync(trader1).ConfigureAwait(false);

        Assert.That(trader1.Inventory.ItemStorage.Items, Is.EquivalentTo(new[] { offeredItem }));
        Assert.That(trader1.Money, Is.EqualTo(600));
    }

    /// <summary>
    /// Tests that the items go back to their owners, when the trader who accepted last could take the items
    /// of the partner, but the partner can't take the items of the trader.
    /// </summary>
    [Test]
    public async ValueTask TradeFailedPartnerInventoryFullReturnsAllItemsAsync()
    {
        var (trader1, trader2) = await this.CreateTradersAsync().ConfigureAwait(false);
        var bigItem = this.GetItem(2, 2);
        var smallItem = this.GetItem();
        await trader1.Inventory!.AddItemAsync(20, bigItem).ConfigureAwait(false);
        await trader2.Inventory!.AddItemAsync(12, smallItem).ConfigureAwait(false);

        // Fill up the inventory of trader2, so that it has just the space of the small item.
        for (byte i = 13; i < 12 + 64; i++)
        {
            await trader2.Inventory.AddItemAsync(i, this.GetItem()).ConfigureAwait(false);
        }

        await this.OpenTradeAsync(trader1, trader2).ConfigureAwait(false);
        var itemMoveAction = new MoveItemAction();
        await itemMoveAction.MoveItemAsync(trader1, 20, Storages.Inventory, 0, Storages.Trade).ConfigureAwait(false);
        await itemMoveAction.MoveItemAsync(trader2, 12, Storages.Inventory, 0, Storages.Trade).ConfigureAwait(false);

        // trader1 accepts last, so it takes the small item first. Then trader2 can't take the big item.
        var tradeButtonHandler = new TradeButtonAction();
        await tradeButtonHandler.TradeButtonChangedAsync(trader2, TradeButtonState.Checked).ConfigureAwait(false);
        await tradeButtonHandler.TradeButtonChangedAsync(trader1, TradeButtonState.Checked).ConfigureAwait(false);

        Mock.Get(trader1.ViewPlugIns.GetPlugIn<ITradeFinishedPlugIn>()!).Verify(view => view.TradeFinishedAsync(TradeResult.FailedByFullInventory), Times.Once);
        Assert.That(trader1.Inventory.ItemStorage.Items, Is.EquivalentTo(new[] { bigItem }));
        Assert.That(trader2.Inventory.ItemStorage.Items, Does.Contain(smallItem));
        Assert.That(trader2.Inventory.ItemStorage.Items, Does.Not.Contain(bigItem));
        Assert.That(trader2.Inventory.ItemStorage.Items, Has.Count.EqualTo(64));
        Assert.That(trader1.TemporaryStorage!.Items, Is.Empty);
        Assert.That(trader2.TemporaryStorage!.Items, Is.Empty);
    }

    /// <summary>
    /// Tests that a trade is not finished, when the received money would exceed the maximum inventory money.
    /// Otherwise, the money of the receiver would overflow.
    /// </summary>
    [Test]
    public async ValueTask TradeFailedMoneyExceedsMaximumAsync()
    {
        var (trader1, trader2) = await this.CreateTradersAsync().ConfigureAwait(false);
        trader1.Money = 1000;
        trader2.Money = int.MaxValue - 100;
        await this.OpenTradeAsync(trader1, trader2).ConfigureAwait(false);
        await new TradeMoneyAction().TradeMoneyAsync(trader1, 500).ConfigureAwait(false);

        var tradeButtonHandler = new TradeButtonAction();
        await tradeButtonHandler.TradeButtonChangedAsync(trader1, TradeButtonState.Checked).ConfigureAwait(false);
        await tradeButtonHandler.TradeButtonChangedAsync(trader2, TradeButtonState.Checked).ConfigureAwait(false);

        Mock.Get(trader2.ViewPlugIns.GetPlugIn<ITradeFinishedPlugIn>()!).Verify(view => view.TradeFinishedAsync(TradeResult.FailedByFullInventory), Times.Once);
        Assert.That(trader1.Money, Is.EqualTo(1000));
        Assert.That(trader2.Money, Is.EqualTo(int.MaxValue - 100));
    }

    /// <summary>
    /// Tests that the money of a finished trade isn't returned when the next trade request is declined.
    /// </summary>
    [Test]
    public async ValueTask TradedMoneyIsNotReturnedByTheNextTradeAsync()
    {
        var (trader1, trader2) = await this.CreateTradersAsync().ConfigureAwait(false);
        trader1.Money = 1000;
        await this.OpenTradeAsync(trader1, trader2).ConfigureAwait(false);
        await new TradeMoneyAction().TradeMoneyAsync(trader1, 300).ConfigureAwait(false);
        var tradeButtonHandler = new TradeButtonAction();
        await tradeButtonHandler.TradeButtonChangedAsync(trader1, TradeButtonState.Checked).ConfigureAwait(false);
        await tradeButtonHandler.TradeButtonChangedAsync(trader2, TradeButtonState.Checked).ConfigureAwait(false);
        Assert.That(trader1.Money, Is.EqualTo(700));
        Assert.That(trader2.Money, Is.EqualTo(300));

        await new TradeRequestAction().RequestTradeAsync(trader1, trader2).ConfigureAwait(false);
        await new TradeAcceptAction().HandleTradeAcceptAsync(trader2, false).ConfigureAwait(false);

        Assert.That(trader1.Money, Is.EqualTo(700));
        Assert.That(trader2.Money, Is.EqualTo(300));
    }

    /// <summary>
    /// Tests that an item can't be dropped while a trade is open, but afterwards.
    /// </summary>
    [Test]
    public async ValueTask ItemCantBeDroppedWhileTradingAsync()
    {
        var (trader1, trader2) = await this.CreateTradersAsync().ConfigureAwait(false);
        var item = this.GetItem();
        await trader1.Inventory!.AddItemAsync(20, item).ConfigureAwait(false);
        await this.OpenTradeAsync(trader1, trader2).ConfigureAwait(false);

        await new DropItemAction().DropItemAsync(trader1, 20, trader1.Position).ConfigureAwait(false);
        Assert.That(trader1.Inventory.ItemStorage.Items, Does.Contain(item));

        await new TradeCancelAction().CancelTradeAsync(trader1).ConfigureAwait(false);
        await new DropItemAction().DropItemAsync(trader1, 20, trader1.Position).ConfigureAwait(false);
        Assert.That(trader1.Inventory.ItemStorage.Items, Does.Not.Contain(item));
    }

    /// <summary>
    /// Tests that a change of the traded items resets the acceptance of the trading partner,
    /// so that the trade isn't finished with items which the partner didn't accept.
    /// </summary>
    /// <param name="fromSlot">The slot from which an item is moved after the partner accepted.</param>
    /// <param name="fromStorage">The storage from which an item is moved after the partner accepted.</param>
    /// <param name="toSlot">The slot to which an item is moved after the partner accepted.</param>
    /// <param name="toStorage">The storage to which an item is moved after the partner accepted.</param>
    [TestCase((byte)21, Storages.Inventory, (byte)2, Storages.Trade, Description = "Item added to the trade")]
    [TestCase((byte)0, Storages.Trade, (byte)20, Storages.Inventory, Description = "Item removed from the trade")]
    [TestCase((byte)0, Storages.Trade, (byte)4, Storages.Trade, Description = "Item moved within the trade")]
    public async ValueTask TradeItemChangeResetsAcceptanceOfPartnerAsync(byte fromSlot, Storages fromStorage, byte toSlot, Storages toStorage)
    {
        var trader1 = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        var trader2 = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        var itemMoveAction = new MoveItemAction();
        var tradeButtonHandler = new TradeButtonAction();

        await trader1.Inventory!.AddItemAsync(20, this.GetItem()).ConfigureAwait(false);
        await trader1.Inventory.AddItemAsync(21, this.GetItem()).ConfigureAwait(false);
        await new TradeRequestAction().RequestTradeAsync(trader1, trader2).ConfigureAwait(false);
        await new TradeAcceptAction().HandleTradeAcceptAsync(trader2, true).ConfigureAwait(false);
        await itemMoveAction.MoveItemAsync(trader1, 20, Storages.Inventory, 0, Storages.Trade).ConfigureAwait(false);

        // The partner accepts the offer, then the offer changes.
        await tradeButtonHandler.TradeButtonChangedAsync(trader2, TradeButtonState.Checked).ConfigureAwait(false);
        Assert.That(trader2.PlayerState.CurrentState, Is.EqualTo(PlayerState.TradeButtonPressed));
        var partnerButtonView = Mock.Get(trader2.ViewPlugIns.GetPlugIn<IChangeTradeButtonStatePlugIn>()!);
        partnerButtonView.Invocations.Clear();
        await itemMoveAction.MoveItemAsync(trader1, fromSlot, fromStorage, toSlot, toStorage).ConfigureAwait(false);

        Assert.That(trader2.PlayerState.CurrentState, Is.EqualTo(PlayerState.TradeOpened));
        partnerButtonView.Verify(view => view.ChangeTradeButtonStateAsync(TradeButtonState.Red), Times.Once);

        // Accepting the changed offer alone must not finish the trade.
        await tradeButtonHandler.TradeButtonChangedAsync(trader1, TradeButtonState.Checked).ConfigureAwait(false);
        Assert.That(trader1.PlayerState.CurrentState, Is.EqualTo(PlayerState.TradeButtonPressed));
        Assert.That(trader2.Inventory!.ItemStorage.Items, Is.Empty);
        Mock.Get(trader2.ViewPlugIns.GetPlugIn<ITradeFinishedPlugIn>()!).Verify(view => view.TradeFinishedAsync(It.IsAny<TradeResult>()), Times.Never);
    }

    private Item GetItem(byte width = 1, byte height = 1)
    {
        var item = new Mock<Item>();
        item.SetupAllProperties();
        item.Object.Definition = new ItemDefinition { Width = width, Height = height };
        item.Setup(i => i.ItemOptions).Returns(new List<ItemOptionLink>());
        return item.Object;
    }

    private async ValueTask<(Player Trader1, Player Trader2)> CreateTradersAsync()
    {
        var trader1 = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        var trader2 = await PlayerTestHelper.CreatePlayerAsync().ConfigureAwait(false);
        trader1.GameContext.Configuration.MaximumInventoryMoney = int.MaxValue;
        trader2.GameContext.Configuration.MaximumInventoryMoney = int.MaxValue;
        return (trader1, trader2);
    }

    private async ValueTask OpenTradeAsync(Player trader1, Player trader2)
    {
        await new TradeRequestAction().RequestTradeAsync(trader1, trader2).ConfigureAwait(false);
        await new TradeAcceptAction().HandleTradeAcceptAsync(trader2, true).ConfigureAwait(false);
        Assert.That(trader1.PlayerState.CurrentState, Is.EqualTo(PlayerState.TradeOpened));
        Assert.That(trader2.PlayerState.CurrentState, Is.EqualTo(PlayerState.TradeOpened));
    }

    private ITrader CreateTrader(State playerState)
    {
        var trader = new Mock<ITrader>();
        trader.SetupAllProperties();
        trader.Setup(t => t.PlayerState).Returns(new StateMachine(playerState));
        var inventory = new Mock<IInventoryStorage>();
        var itemStorage = new Mock<ItemStorage>();
        itemStorage.Setup(i => i.Items).Returns(new List<Item>());
        inventory.Setup(i => i.ItemStorage).Returns(itemStorage.Object);
        trader.Setup(t => t.Inventory).Returns(inventory.Object);
        trader.Object.BackupInventory = new BackupItemStorage(itemStorage.Object) { Items = new List<Item>() };
        var temporaryStorage = new Mock<IStorage>();
        temporaryStorage.Setup(t => t.Items).Returns(new List<Item>());
        trader.Setup(t => t.TemporaryStorage).Returns(temporaryStorage.Object);
        trader.Setup(t => t.ViewPlugIns).Returns(new MockViewPlugInContainer());

        var contextMock = new Mock<IPlayerContext>();
        trader.Setup(t => t.PersistenceContext).Returns(contextMock.Object);
        return trader.Object;
    }

    //// TODO: Test fail scenarios
}

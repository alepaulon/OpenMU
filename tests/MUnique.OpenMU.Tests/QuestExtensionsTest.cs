// <copyright file="QuestExtensionsTest.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Moq;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlayerActions.Quests;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Tests the <see cref="QuestExtensions"/>.
/// </summary>
[TestFixture]
public class QuestExtensionsTest
{
    /// <summary>
    /// Deleting a requirement state which isn't saved yet makes the entity framework remove it from the
    /// collection right away. Clearing must survive that, and still delete every state.
    /// </summary>
    [Test]
    public async ValueTask ClearSurvivesDeleteRemovingTheStateFromTheCollectionAsync()
    {
        var state = new TestQuestState();
        var first = new QuestMonsterKillRequirementState();
        var second = new QuestMonsterKillRequirementState();
        state.RequirementStates.Add(first);
        state.RequirementStates.Add(second);

        var context = new Mock<IContext>();
        context
            .Setup(c => c.DeleteAsync(It.IsAny<QuestMonsterKillRequirementState>()))
            .Returns((QuestMonsterKillRequirementState deleted) => new ValueTask<bool>(state.RequirementStates.Remove(deleted)));

        await state.ClearAsync(context.Object).ConfigureAwait(false);

        Assert.That(state.RequirementStates, Is.Empty);
        context.Verify(c => c.DeleteAsync(first), Times.Once);
        context.Verify(c => c.DeleteAsync(second), Times.Once);
    }

    private sealed class TestQuestState : CharacterQuestState
    {
        public TestQuestState()
        {
            this.RequirementStates = new List<QuestMonsterKillRequirementState>();
        }
    }
}

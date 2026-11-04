// <copyright file="SkillUseRestrictionExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Skills;

using System.ComponentModel;
using MUnique.OpenMU.GameLogic.PlugIns;

/// <summary>
/// Extensions to check the <see cref="ISkillUseRestrictionPlugIn"/>s.
/// </summary>
public static class SkillUseRestrictionExtensions
{
    /// <summary>
    /// Determines whether a plugin prevents the player from using the skill.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="skill">The skill.</param>
    /// <returns><c>true</c>, if the use of the skill is prevented; otherwise, <c>false</c>.</returns>
    public static bool IsSkillUsePreventedByPlugIns(this Player player, Skill skill)
    {
        if (player.GameContext.PlugInManager.GetPlugInPoint<ISkillUseRestrictionPlugIn>() is not { } plugInPoint)
        {
            return false;
        }

        var eventArgs = new CancelEventArgs();
        plugInPoint.CheckSkillUse(player, skill, eventArgs);
        if (eventArgs.Cancel)
        {
            player.Logger.LogDebug("The use of skill {skill} by player {player} was prevented by a plugin.", skill.Number, player);
        }

        return eventArgs.Cancel;
    }
}

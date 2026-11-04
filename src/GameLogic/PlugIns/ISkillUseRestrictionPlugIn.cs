// <copyright file="ISkillUseRestrictionPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns;

using System.ComponentModel;
using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Plugins which can prevent a player from using a skill which is in its skill list,
/// e.g. a skill which is granted by an equipped item, but not allowed by a custom rule.
/// </summary>
[Guid("6E0B3C55-2F1A-4D8E-9B7C-3A5D1E8F2C40")]
[PlugInPoint("Skill use restriction", "Plugins which can prevent a player from using a skill of its skill list.")]
public interface ISkillUseRestrictionPlugIn
{
    /// <summary>
    /// Checks if the player may use the skill.
    /// </summary>
    /// <param name="player">The player who wants to use the skill.</param>
    /// <param name="skill">The skill.</param>
    /// <param name="eventArgs">The event args. A plugin sets <see cref="CancelEventArgs.Cancel"/> to prevent the use of the skill.</param>
    void CheckSkillUse(Player player, Skill skill, CancelEventArgs eventArgs);
}

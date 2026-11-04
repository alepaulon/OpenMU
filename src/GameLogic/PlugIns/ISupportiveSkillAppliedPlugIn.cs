// <copyright file="ISupportiveSkillAppliedPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns;

using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A plugin interface which is called after a player applied a buff or regeneration skill
/// (<see cref="SkillType.Buff"/>, <see cref="SkillType.Regeneration"/>) to a target, e.g. a heal.
/// </summary>
[Guid("6C1D9A47-3E25-4B8F-A0D6-2F7E4C9B1A83")]
[PlugInPoint("Supportive skill applied", "Plugins which will be executed after a player applied a buff or regeneration skill to a target.")]
public interface ISupportiveSkillAppliedPlugIn
{
    /// <summary>
    /// Is called after a player applied a buff or regeneration skill to a target.
    /// </summary>
    /// <param name="player">The player which applied the skill.</param>
    /// <param name="target">The target of the skill, which may be the player itself.</param>
    /// <param name="skill">The skill.</param>
    /// <param name="restoredHealth">The health which the skill restored, without overhealing; 0 for buffs.</param>
    void SupportiveSkillApplied(Player player, IAttackable target, Skill skill, float restoredHealth);
}

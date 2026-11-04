// <copyright file="INpcTargetSelectionPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A plugin interface which is called when a monster with the <see cref="BasicMonsterIntelligence"/>
/// (or one derived from it) determines the target it attacks.
/// </summary>
/// <remarks>
/// It allows game features to replace the default target selection - keeping the current target as long
/// as it's valid, otherwise taking the nearest one - e.g. by a threat (aggro) table.
/// </remarks>
[Guid("0B6F2E5C-8A43-4D2F-9C71-5E3A8B1D4F60")]
[PlugInPoint("NPC target selection", "Plugins which can select the target which a monster attacks.")]
public interface INpcTargetSelectionPlugIn
{
    /// <summary>
    /// Is called when the monster determines its target, once per tick of its intelligence.
    /// </summary>
    /// <param name="monster">The monster.</param>
    /// <param name="args">The arguments, which take the selected target.</param>
    ValueTask SelectTargetAsync(Monster monster, NpcTargetSelectionArgs args);
}

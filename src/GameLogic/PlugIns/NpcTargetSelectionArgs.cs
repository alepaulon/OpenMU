// <copyright file="NpcTargetSelectionArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns;

/// <summary>
/// The arguments of the <see cref="INpcTargetSelectionPlugIn"/>. Plugin points can't return
/// values, so the selected target is passed back through this object.
/// </summary>
public sealed class NpcTargetSelectionArgs
{
    private readonly Func<IAttackable, bool> _isValidTarget;

    /// <summary>
    /// Initializes a new instance of the <see cref="NpcTargetSelectionArgs"/> class.
    /// </summary>
    /// <param name="previousTarget">The target of the previous tick.</param>
    /// <param name="defaultTarget">The target which the monster selected by its default logic.</param>
    /// <param name="isValidTarget">Determines whether the monster may attack a target.</param>
    public NpcTargetSelectionArgs(IAttackable? previousTarget, IAttackable? defaultTarget, Func<IAttackable, bool> isValidTarget)
    {
        this.PreviousTarget = previousTarget;
        this.DefaultTarget = defaultTarget;
        this._isValidTarget = isValidTarget;
    }

    /// <summary>
    /// Gets the target of the previous tick, which may not be valid anymore.
    /// </summary>
    public IAttackable? PreviousTarget { get; }

    /// <summary>
    /// Gets the target which the monster selected by its default logic: the previous target
    /// while it's valid, otherwise the nearest one. It's <see langword="null"/> when there is none.
    /// </summary>
    public IAttackable? DefaultTarget { get; }

    /// <summary>
    /// Gets or sets the target which the monster should attack. When it stays <see langword="null"/>,
    /// or the selected target is not valid (see <see cref="IsValidTarget"/>), the <see cref="DefaultTarget"/> is attacked.
    /// </summary>
    /// <remarks>
    /// A plugin should not overwrite a target which another plugin already selected.
    /// </remarks>
    public IAttackable? Target { get; set; }

    /// <summary>
    /// Determines whether the monster may attack the target: it's alive, on the same map, visible,
    /// not in a safezone and within the view range of the monster, with a clear line of sight.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <returns><see langword="true"/>, if the monster may attack the target; otherwise, <see langword="false"/>.</returns>
    public bool IsValidTarget(IAttackable target) => this._isValidTarget(target);
}

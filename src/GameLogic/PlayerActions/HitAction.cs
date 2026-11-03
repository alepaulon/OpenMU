// <copyright file="HitAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions;

using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlayerActions.Skills;
using MUnique.OpenMU.GameLogic.Views.World;

/// <summary>
/// Action to hit targets without a skill with pure melee damage.
/// </summary>
public class HitAction
{
    /// <summary>
    /// The maximum distance, in tiles, at which a hit without a ranged weapon is accepted.
    /// </summary>
    /// <remarks>
    /// A melee hit reaches about two tiles. Two more are tolerated, because the positions of the game client
    /// and the server can differ a bit, like for skills.
    /// </remarks>
    internal const int MaximumMeleeHitDistance = 4;

    /// <summary>
    /// The maximum distance, in tiles, at which a hit with a ranged weapon (bow or crossbow) is accepted.
    /// </summary>
    /// <remarks>
    /// A ranged hit reaches about six tiles, plus the same tolerance as for melee hits.
    /// </remarks>
    internal const int MaximumRangedHitDistance = 8;

    /// <summary>
    /// Hits the specified target by the specified player.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="target">The target.</param>
    /// <param name="attackAnimation">The attack animation.</param>
    /// <param name="lookingDirection">The looking direction.</param>
    public async ValueTask HitAsync(Player player, IAttackable target, byte attackAnimation, Direction lookingDirection)
    {
        if (!await AttackActionValidator.CanAttackAsync(player, null).ConfigureAwait(false))
        {
            return;
        }

        if (target.IsAtSafezone())
        {
            return;
        }

        if (target is IObservable targetAsObservable)
        {
            using var readerLock = await targetAsObservable.ObserverLock.ReaderLockAsync().ConfigureAwait(false);
            if (!targetAsObservable.Observers.Contains(player))
            {
                // Target out of range
                return;
            }
        }

        if (!player.IsInRange(target.Position, GetMaximumHitDistance(player)))
        {
            // The observer check above allows hits from almost the whole visible range.
            player.Logger.LogWarning("Possible hacker - player {player} at {position} hit {target} at {targetPosition}, which is out of range.", player, player.Position, target, target.Position);
            return;
        }

        player.Rotation = lookingDirection;
        var hitInfo = await target.AttackByAsync(player, null, false).ConfigureAwait(false);
        if (hitInfo is { SkillEntry.Skill: not null } hit)
        {
            var success = await target.TryApplyElementalEffectsAsync(player, hit.SkillEntry, hit).ConfigureAwait(false);
            await player.ForEachWorldObserverAsync<IShowSkillAnimationPlugIn>(p => p.ShowSkillAnimationAsync(player, target, hit.SkillEntry.Skill, success), true).ConfigureAwait(false);
            return;
        }

        if (player.Attributes?[Stats.TransformationSkin] is { } skin and not 0
            && await this.ApplySkinnedMonstersSkillAsync(player, target, (short)skin).ConfigureAwait(false) is var (skill, effectApplied))
        {
            await player.ForEachWorldObserverAsync<IShowSkillAnimationPlugIn>(p => p.ShowSkillAnimationAsync(player, target, skill, effectApplied), true).ConfigureAwait(false);
            return;
        }

        await player.ForEachWorldObserverAsync<IShowAnimationPlugIn>(p => p.ShowAnimationAsync(player, attackAnimation, target, lookingDirection), false).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the maximum distance at which the player may hit a target without a skill.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The maximum distance in tiles.</returns>
    /// <remarks>
    /// Ranged weapons are the ones which consume ammunition (arrows and bolts).
    /// </remarks>
    internal static int GetMaximumHitDistance(Player player)
    {
        return player.Attributes?[Stats.AmmunitionConsumptionRate] > 0
            ? MaximumRangedHitDistance
            : MaximumMeleeHitDistance;
    }

    private async ValueTask<(Skill Skill, bool EffectApplied)?> ApplySkinnedMonstersSkillAsync(Player player, IAttackable target, short skin)
    {
        var effectApplied = false;
        if (player.GameContext.Configuration.Monsters.FirstOrDefault(m => m.Number == skin)?.AttackSkill
            is not { ElementalModifierTarget: not null } skill)
        {
            return null;
        }

        var modifier = skill.ElementalModifierTarget!;
        var resistance = target.Attributes[modifier];
        if (resistance >= 255 || !Rand.NextRandomBool(1 / (resistance + 1)))
        {
            return (skill, effectApplied);
        }

        // Currently, we just support one effect for monsters.
        // E.g. Poison for Poison BullFighters.
        if (skill.MagicEffectDef is { Duration: not null } effectDefinition
            && !target.MagicEffectList.ContainsEffect(effectDefinition.Number)
            && effectDefinition.PowerUpDefinitions.FirstOrDefault() is { Boost: not null } powerUpDef)
        {
            var powerUp = target.Attributes.CreateElement(powerUpDef);
            var powerUpDuration = target.Attributes.CreateDurationElement(effectDefinition.Duration);
            var magicEffect = powerUpDef.TargetAttribute == Stats.IsPoisoned
                ? new PoisonMagicEffect(powerUp, effectDefinition, TimeSpan.FromSeconds(powerUpDuration.Value), player, target)
                : new MagicEffect(powerUp, effectDefinition, TimeSpan.FromSeconds(powerUpDuration.Value));
            await target.MagicEffectList.AddEffectAsync(magicEffect).ConfigureAwait(false);
            effectApplied = true;
        }

        if (modifier == Stats.LightningResistance)
        {
            await target.MoveRandomlyAsync().ConfigureAwait(false);
        }

        return (skill, effectApplied);
    }
}

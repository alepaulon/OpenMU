// <copyright file="StateMachine.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic;

using System.ComponentModel;
using System.Threading;
using MUnique.OpenMU.PlugIns;
using Nito.AsyncEx;

/// <summary>
/// A state machine.
/// </summary>
public class StateMachine
{
    /// <summary>
    /// The last assigned <see cref="_lockOrder"/>.
    /// </summary>
    private static long _lastLockOrder;

    /// <summary>
    /// The lock object for state transitions.
    /// </summary>
    /// <remarks>
    /// Invariant: whoever holds the locks of two state machines at the same time (e.g. the two traders
    /// when a trade is finished) acquires them in the order of <see cref="_lockOrder"/>, through
    /// <see cref="TryBeginAdvanceToAsync(StateMachine, State, StateMachine, State)"/>.
    /// Otherwise, two such callers with the opposite order deadlock each other.
    /// </remarks>
    private readonly AsyncLock _asyncLock = new();

    /// <summary>
    /// The position of <see cref="_asyncLock"/> in the global lock order. It's unique and doesn't change.
    /// </summary>
    private readonly long _lockOrder = Interlocked.Increment(ref _lastLockOrder);

    /// <summary>
    /// A cancel event args object, which is getting reused.
    /// </summary>
    private readonly StateChangeEventArgs _cachedCancelEventArgs = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="StateMachine"/> class.
    /// </summary>
    /// <param name="initialState">The initial state.</param>
    public StateMachine(State initialState)
    {
        this.CurrentState = initialState;
    }

    /// <summary>
    /// Event that fires just before the state changes.
    /// </summary>
    public event AsyncEventHandler<StateChangeEventArgs>? StateChanges;

    /// <summary>
    /// Event that fires after the state have changed.
    /// </summary>
    public event AsyncEventHandler<StateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Gets the current state.
    /// </summary>
    public State CurrentState { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the state machine is in a finished state, that means that no further state changes are possible.
    /// </summary>
    public bool Finished => this.CurrentState?.PossibleTransitions is null || this.CurrentState.PossibleTransitions.Count == 0;

    /// <summary>
    /// Tries to advance the state to <paramref name="nextState"/>.
    /// </summary>
    /// <param name="nextState">The state to advance to.</param>
    /// <returns>The success.</returns>
    public async ValueTask<bool> TryAdvanceToAsync(State nextState)
    {
        if (this.CurrentState?.PossibleTransitions is null)
        {
            return false;
        }

        using var l = await this._asyncLock.LockAsync().ConfigureAwait(false);

        if (this.CurrentState?.PossibleTransitions is not { } possibleTransitions)
        {
            return false;
        }

        if (possibleTransitions.Contains(nextState) && await this.OnStateChangingAsync(nextState).ConfigureAwait(false))
        {
            var previousState = this.CurrentState;
            this.CurrentState = nextState;
            await this.OnStateChangedAsync(previousState, nextState).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tries to start a "transaction" to advance the state to <paramref name="nextState"/>.
    /// </summary>
    /// <param name="nextState">The state to advance to.</param>
    /// <returns>The state change context. On disposal of this object, the state change is getting completed.</returns>
    public async ValueTask<StateChangeContext> TryBeginAdvanceToAsync(State nextState)
    {
        var lockRelease = await this._asyncLock.LockAsync().ConfigureAwait(false);
        var context = new StateChangeContext(lockRelease, async () =>
        {
            var previousState = this.CurrentState;
            this.CurrentState = nextState;
            await this.OnStateChangedAsync(previousState, nextState).ConfigureAwait(false);
        })
        {
            Allowed = (this.CurrentState.PossibleTransitions?.Contains(nextState) ?? false) && await this.OnStateChangingAsync(nextState).ConfigureAwait(false),
        };

        return context;
    }

    /// <summary>
    /// Tries to start a "transaction" to advance the states of two state machines at the same time.
    /// The locks of both are acquired in the global lock order, regardless of the order of the parameters.
    /// </summary>
    /// <param name="first">The first state machine.</param>
    /// <param name="firstNextState">The state to advance the first state machine to.</param>
    /// <param name="second">The second state machine.</param>
    /// <param name="secondNextState">The state to advance the second state machine to.</param>
    /// <returns>The state change contexts of both state machines, in the order of the parameters.</returns>
    public static async ValueTask<(StateChangeContext First, StateChangeContext Second)> TryBeginAdvanceToAsync(StateMachine first, State firstNextState, StateMachine second, State secondNextState)
    {
        if (ReferenceEquals(first, second))
        {
            throw new ArgumentException("The state machines must be different, because the lock isn't reentrant.", nameof(second));
        }

        if (first._lockOrder < second._lockOrder)
        {
            var firstContext = await first.TryBeginAdvanceToAsync(firstNextState).ConfigureAwait(false);
            return (firstContext, await BeginWhileHoldingAsync(firstContext, second, secondNextState).ConfigureAwait(false));
        }

        var secondContext = await second.TryBeginAdvanceToAsync(secondNextState).ConfigureAwait(false);
        return (await BeginWhileHoldingAsync(secondContext, first, firstNextState).ConfigureAwait(false), secondContext);

        static async ValueTask<StateChangeContext> BeginWhileHoldingAsync(StateChangeContext heldContext, StateMachine stateMachine, State nextState)
        {
            try
            {
                return await stateMachine.TryBeginAdvanceToAsync(nextState).ConfigureAwait(false);
            }
            catch
            {
                heldContext.Allowed = false;
                await heldContext.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    /// <summary>
    /// Calls the StateChanged-Event.
    /// </summary>
    private async ValueTask OnStateChangedAsync(State previousState, State currentState)
    {
        await this.StateChanged.SafeInvokeAsync(new StateChangedEventArgs(previousState, currentState)).ConfigureAwait(false);
    }

    /// <summary>
    /// Calls the StateChanges-Event.
    /// </summary>
    /// <param name="nextState">The next state.</param>
    /// <returns><c>True</c>, if all event handlers did not set <see cref="CancelEventArgs"/> to <c>true</c>; Otherwise, <c>false</c>.</returns>
    private async ValueTask<bool> OnStateChangingAsync(State nextState)
    {
        if (this.StateChanges != null)
        {
            this._cachedCancelEventArgs.Cancel = false;
            this._cachedCancelEventArgs.NextState = nextState;
            await this.StateChanges.SafeInvokeAsync(this._cachedCancelEventArgs).ConfigureAwait(false);
            return !this._cachedCancelEventArgs.Cancel;
        }

        return true;
    }

    /// <summary>
    /// The state change context for more complex state changes.
    /// On disposal of this object, the state change is getting completed.
    /// </summary>
    public sealed class StateChangeContext : IAsyncDisposable
    {
        /// <summary>
        /// The lock release of the acquired lock of the state machine.
        /// </summary>
        private readonly IDisposable _lockRelease;

        /// <summary>
        /// The action which gets executed when the state change is completed.
        /// </summary>
        private readonly Func<ValueTask> _finishAction;

        /// <summary>
        /// Initializes a new instance of the <see cref="StateChangeContext"/> class.
        /// </summary>
        /// <param name="lockRelease">The lock object of the state machine, which is in the locked state.</param>
        /// <param name="finishAction">The action which should get executed when the state change is completed.</param>
        public StateChangeContext(IDisposable lockRelease, Func<ValueTask> finishAction)
        {
            this._lockRelease = lockRelease;
            this._finishAction = finishAction;
        }

        /// <summary>
        /// Gets a value indicating whether a state change is allowed.
        /// </summary>
        public bool Allowed { get; internal set; }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (this.Allowed)
                {
                    await this._finishAction().ConfigureAwait(false);
                }
            }
            finally
            {
                this._lockRelease.Dispose();
            }
        }
    }

    /// <summary>
    /// The state change event args, including the next state.
    /// </summary>
    public class StateChangeEventArgs : CancelEventArgs
    {
        /// <summary>
        /// Gets or sets the next state.
        /// </summary>
        public State? NextState { get; set; }
    }

    /// <summary>
    /// The event args for <see cref="StateMachine.StateChanged"/>.
    /// </summary>
    public class StateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StateChangedEventArgs"/> class.
        /// </summary>
        /// <param name="previousState">State of the previous.</param>
        /// <param name="currentStateState">State of the current state.</param>
        public StateChangedEventArgs(State previousState, State currentStateState)
        {
            this.PreviousState = previousState;
            this.CurrentStateState = currentStateState;
        }

        /// <summary>
        /// Gets the state of the previous state.
        /// </summary>
        public State PreviousState { get; }

        /// <summary>
        /// Gets the state of the current state.
        /// </summary>
        public State CurrentStateState { get; }
    }
}
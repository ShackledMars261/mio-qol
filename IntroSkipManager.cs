using MioGame;
using QoL.Util;
using IntroState = MioGame.GW_tuto_jump.Intro.State;
using ActionKind = MioGame.Mio.Action.Kind;
using MioFns = MioGame.GlobalFunctions.game.mio;

namespace QoL.Managers
{
    /// <summary>
    /// Skips the new-game intro: the opening cutscene and the glitch-world jump tutorial
    /// (zone GW_intro_jump_P1). The run starts at the Samsk cutscene instead.
    /// </summary>
    /// <remarks>
    /// Both halves are driven by one zone script, <see cref="GW_tuto_jump"/>. A trace of a normal
    /// playthrough showed that when Mio takes the tutorial's exit gate, the game does exactly this:
    /// <code>
    /// loot 'CODE:TUTO_JUMP_DONE' x1
    /// loot 'UNLOCK:SLASH' (NO_LOG)            (not replicated, see GrantSlash)
    /// loot 'CODE:TW_FALL_DONE' x1
    /// exit_glitch_to (-1229.6, 1424.5, 0)   → zone ST_security_fall_P1, Samsk cutscene starts
    /// tuto_jump_rt.intro_done = true          (set last; it means "whole intro done")
    /// </code>
    /// None of it depends on the cutscene or tutorial having played, so the skip performs those
    /// steps itself:
    /// <list type="bullet">
    /// <item><b>New game (normal path):</b> the intro puts Mio in the glitch world during loading,
    /// from inside the glitch manager (<c>glitch_state_update</c>). Just before its first run, Mio's
    /// teleport to the Samsk spot is queued, and the glitch manager is held until the teleport
    /// lands, so the glitch world is never entered and the game opens on the Samsk cutscene
    /// without the glitch exit's fade.</item>
    /// <item><b>Fallback</b> (skip turned on mid-intro, Mio already in the glitch world): the same
    /// grants, then <see cref="Game.exit_glitch_to"/>, which fades out and back in.</item>
    /// </list>
    /// Either way the intro script is no longer updated afterwards: its zone stays loaded and
    /// would otherwise start the cutscene on Mio in his new location.
    ///
    /// Earlier attempts and why they failed, for reference:
    /// <list type="bullet">
    /// <item>Forcing the cutscene state to Done: the cutscene is an animation that keeps moving Mio
    /// and re-applies <see cref="ActionKind.Intro_drama"/>. Setting <c>intro_done</c> early also
    /// made the gate's exit skip its loot and teleport, so Mio rose forever.</item>
    /// <item>Teleporting Mio onto the gate after that: native crash in the gate's exit.</item>
    /// </list>
    /// </remarks>
    internal static unsafe class IntroSkipManager
    {
        /// <summary>Whether the feature is on. Set through <see cref="SetEnabled"/>.</summary>
        internal static bool Enabled { get; private set; } = true;

        /// <summary>
        /// Grant UNLOCK:SLASH like the vanilla exit gate does. Off for now: the Samsk cutscene
        /// appears to grant it as well. Turn back on if slash turns out to be missing.
        /// </summary>
        private static readonly bool GrantSlash = false;

        /// <summary>
        /// Where the exit gate sends Mio (start of the Samsk cutscene in ST_security_fall_P1),
        /// recorded from a normal playthrough on build patch3-rc3 r21756.
        /// </summary>
        private static readonly (float x, float y, float z) ExitPosition = (-1229.606445f, 1424.457275f, 0f);

        private enum Phase
        {
            /// <summary>The intro room isn't active, or the skip is off.</summary>
            Idle,
            /// <summary>The intro room is active and hasn't been skipped yet.</summary>
            Armed,
            /// <summary>Skipped. The intro script is no longer updated while its zone stays loaded.</summary>
            Skipped,
        }

        private static Phase phase = Phase.Idle;
        private static bool introRoomActive;
        private static GW_tuto_jump* intro;

        /// <summary>Max frames to hold the glitch manager while Mio's teleport is pending.</summary>
        private const int MaxHoldFrames = 60;
        /// <summary>Frames after the hold during which a glitch entry triggers the fallback exit.</summary>
        private const int WatchFrames = 60;
        private static int holdFramesLeft;
        private static int watchFramesLeft;

        internal static void Initialize()
        {
            On.MioGame.On_GW_tuto_jump.activate.Prefix += OnIntroActivate;
            On.MioGame.On_GW_tuto_jump.deactivate_all.Prefix += OnIntroDeactivateAll;
            On.MioGame.On_GW_tuto_jump.update.Hook += OnIntroUpdate;
            // Game.enter_glitch can't be used: the intro's entry into the glitch world doesn't call
            // it as a function (likely inlined), so hooks on it never fire. glitch_state_update is
            // the per-frame glitch manager, so the skip runs just before it instead.
            On.MioGame.GlobalFunctions.game.On_game.glitch_state_update.Hook += OnGlitchStateUpdate;
        }

        /// <summary>Turns the feature on or off. Called by the mod menu toggle.</summary>
        internal static void SetEnabled(bool enabled)
        {
            Enabled = enabled;

            // Turned on while the intro is already playing: skip from where it is now.
            if (enabled && introRoomActive && phase == Phase.Idle)
                phase = Phase.Armed;
        }

        // ---- Hooks ------------------------------------------------------------------------

        private static void OnIntroActivate(GW_tuto_jump* self, Node2* n)
        {
            intro = self;
            introRoomActive = true;
            holdFramesLeft = 0;
            watchFramesLeft = 0;
            phase = Enabled ? Phase.Armed : Phase.Idle;
            Log($"intro room activated (skip {(Enabled ? "armed" : "off")})");
        }

        private static void OnIntroDeactivateAll(Zone* z)
        {
            if (!introRoomActive) return;
            Log($"intro room deactivated (phase {phase})");
            intro = null;
            introRoomActive = false;
            phase = Phase.Idle;
        }

        /// <summary>
        /// Normal path. The intro's room has just activated and Mio is not in the glitch world
        /// yet: send him straight to the Samsk spot before the glitch manager can put him there.
        /// </summary>
        /// <remarks>
        /// <see cref="MioFns.mio_teleport"/> only queues the move; Mio's own update applies it a
        /// few frames later. Until then Mio still stands in the intro room, and the glitch manager
        /// would enter the glitch world on that frame (seen in testing as a one-frame flash of
        /// the glitch area). So the glitch manager is held until the teleport has landed.
        /// </remarks>
        private static void OnGlitchStateUpdate(On.MioGame.GlobalFunctions.game.On_game.orig_glitch_state_update orig)
        {
            ref Game game = ref Globals.game;

            if (Enabled && introRoomActive && phase == Phase.Armed && intro != null && !game.glitch.is_inside())
            {
                Log("intro is about to enter the glitch world, starting at the Samsk cutscene instead");
                GrantExitLoot(ref game);
                MarkIntroDone(intro, ref game);
                MioFns.mio_teleport(ExitVector());
                phase = Phase.Skipped;
                holdFramesLeft = MaxHoldFrames;
                Log($"intro skipped, Mio's teleport to {NodeTransforms.Format(ExitVector())} queued; holding the glitch manager until it lands");
            }

            if (holdFramesLeft > 0)
            {
                if (TeleportLanded(ref game))
                {
                    Log($"teleport landed after {MaxHoldFrames - holdFramesLeft} frames, resuming the glitch manager");
                    holdFramesLeft = 0;
                    watchFramesLeft = WatchFrames;
                }
                else
                {
                    if (--holdFramesLeft == 0)
                    {
                        Log($"teleport hadn't landed after {MaxHoldFrames} frames, resuming the glitch manager anyway");
                        watchFramesLeft = WatchFrames;
                    }
                    return; // Hold: don't run the glitch manager this frame.
                }
            }

            bool insideBefore = game.glitch.is_inside();
            orig();

            if (introRoomActive && !insideBefore && game.glitch.is_inside())
            {
                Log($"glitch_state_update entered the glitch world (phase {phase})");

                // Right after the skip: the hold didn't stop the entry. Leave the normal way (with
                // its fade) rather than leaving Mio at the Samsk spot in glitch-world state.
                if (watchFramesLeft > 0)
                {
                    Log("entered anyway after the skip, exiting with exit_glitch_to");
                    game.exit_glitch_to(ExitVector());
                }
            }
            if (watchFramesLeft > 0) watchFramesLeft--;
        }

        /// <summary>Whether Mio has actually arrived at the exit position.</summary>
        private static bool TeleportLanded(ref Game game)
        {
            if (game.mio.node == null) return false;
            var p = NodeTransforms.WorldPosition(game.mio.node);
            float dx = p.x - ExitPosition.x, dy = p.y - ExitPosition.y;
            return dx * dx + dy * dy < 50f * 50f;
        }

        private static void OnIntroUpdate(On.MioGame.On_GW_tuto_jump.orig_update orig, GW_tuto_jump* self, Node2* n)
        {
            // Fallback path: only reached when the skip was armed after the glitch was entered.
            if (phase == Phase.Armed && Enabled && TrySkipFromInside(self))
                phase = Phase.Skipped;

            // Once skipped, the intro script stays frozen: running it would start the cutscene.
            if (phase == Phase.Skipped) return;

            orig(self, n);
        }

        // ---- Skip steps ---------------------------------------------------------------------

        /// <summary>
        /// Fallback for a skip armed after Mio is already in the glitch world: does what the exit
        /// gate does. Returns false if it's not time yet.
        /// </summary>
        private static bool TrySkipFromInside(GW_tuto_jump* self)
        {
            ref Game game = ref Globals.game;
            if (!game.glitch.is_inside()) return false;

            // The player already reached the gate on their own: let the game's exit finish.
            if (game.mio.action.kind == ActionKind.Gw_exit)
            {
                Log("Mio is already taking the exit gate, not skipping");
                phase = Phase.Idle;
                return false;
            }

            Log($"skipping intro from inside the glitch world (intro state {self->intro_rt.state._value}, mio action {game.mio.action.kind})");

            // With the script frozen from here on, nothing re-applies the action, so cancelling sticks.
            if (game.mio.action.kind == ActionKind.Intro_drama)
                game.mio.cancel_action();

            GrantExitLoot(ref game);
            MarkIntroDone(self, ref game);
            game.exit_glitch_to(ExitVector());
            Log($"intro skipped, exiting the glitch world to {NodeTransforms.Format(ExitVector())}");
            return true;
        }

        /// <summary>What the exit gate grants, in the same order and with the same flags.</summary>
        private static void GrantExitLoot(ref Game game)
        {
            game.loot(GameStrings.Constant("CODE:TUTO_JUMP_DONE"), 1, Loot_flags.None);
            if (GrantSlash)
                game.loot(GameStrings.Constant("UNLOCK:SLASH"), Loot_flags.NO_LOG);
            game.loot(GameStrings.Constant("CODE:TW_FALL_DONE"), 1, Loot_flags.None);
        }

        /// <summary>Leaves the intro's runtime state as the exit gate leaves it.</summary>
        private static void MarkIntroDone(GW_tuto_jump* self, ref Game game)
        {
            ref var tracked = ref self->intro_rt.state;
            tracked._value = IntroState.Done;
            tracked._changed_frame = game.frame;
            tracked._changed_gameplay_frame = game.gameplay_frame;
            self->tuto_jump_rt.intro_done = true;
        }

        private static Vec_float_3 ExitVector()
        {
            Vec_float_3 v = default;
            v.x = ExitPosition.x; v.y = ExitPosition.y; v.z = ExitPosition.z;
            return v;
        }

        private static void Log(string message) => QoLMod.Log($"[IntroSkip] {message}");
    }
}
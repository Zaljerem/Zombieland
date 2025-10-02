using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
    public class JobDriver_Stumble : JobDriver
    {
        public IntVec3 destination;

        public Thing eatTarget;
        public Pawn lastEatTarget;
        public IntVec3 lastEatTargetPosition;
        public int eatDelayCounter;
        public int eatDelay;

        void InitAction()
        {
            destination = IntVec3.Invalid;
            lastEatTargetPosition = IntVec3.Invalid;
        }

        void TickAction()
        {
            var zombie = pawn as Zombie;
            if (zombie == null)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            // Critical safety checks
            if (pawn.Dead || pawn.Downed || !pawn.Spawned)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            if (pawn.pather.Moving && pawn.pather.Destination.IsValid)
                return;
            if (zombie.state == ZombieState.Emerging || zombie.state == ZombieState.Floating)
                return;

            ZombieStateHandler.CheckEndRage(zombie);

            if (this.ShouldDie(zombie))
                return;

            if (ZombieStateHandler.WallPushing(zombie))
                return;

            if (this.Roping(zombie))
            {
                this.ExecuteMove(zombie, zombie.Map.GetGrid());
                return;
            }

            if (ZombieStateHandler.DownedOrUnconsciousness(zombie))
                return;

            if (this.Attack(zombie))
                return;

            var grid = zombie.Map.GetGrid();

            if (ZombieStateHandler.CheckWallPushing(zombie, grid))
                return;

            if (this.ValidDestination(zombie))
                return;

            ZombieStateHandler.ApplyFire(zombie);

            var bodyType = zombie.story.bodyType;
            if (zombie.isMiner && (bodyType == BodyTypeDefOf.Fat || bodyType == BodyTypeDefOf.Hulk))
                if (this.Mine(zombie, true))
                    return;

            if (this.Eat(zombie, grid))
                return;

            bool smashTime;
            if (zombie.IsTanky)
            {
                if (this.Smash(zombie, true, false))
                    return;
                smashTime = true;
            }
            else
            {
                smashTime = this.Track(zombie, grid);
                if (smashTime)
                {
                    if (!zombie.checkSmashable)
                        smashTime = false;
                    zombie.checkSmashable = false;
                }
                if (this.Smash(zombie, smashTime, true))
                    return;
            }

            var possibleMoves = this.PossibleMoves(zombie);
            if (possibleMoves.Count > 0)
            {
                if (zombie.raging > 0 || zombie.IsTanky || zombie.isAlbino || zombie.isDarkSlimer ||
                    (zombie.wasMapPawnBefore && zombie.state != ZombieState.Tracking))
                {
                    if (this.RageMove(zombie, grid, possibleMoves, smashTime))
                        return;
                }

                if (zombie.raging <= 0)
                {
                    if (zombie.isMiner && this.Mine(zombie, false))
                        return;

                    this.Wander(zombie, grid, possibleMoves);
                }
            }

            // Fallback: If no valid destination, try to wander randomly
            if (!destination.IsValid)
            {
                var root = pawn.Position;
                for (int i = 0; i < 8; i++)
                {
                    var cell = root + GenRadial.RadialPattern[i];
                    if (cell.InBounds(pawn.Map) && cell.Walkable(pawn.Map))
                    {
                        destination = cell;
                        break;
                    }
                }
            }

            this.ExecuteMove(zombie, grid);
            ZombieStateHandler.BeginRage(zombie, grid);
        }

        public override void Notify_PatherArrived()
        {
            // Guard against downed/dead arrival
            if (pawn.Dead || pawn.Downed)
                return;

            base.Notify_PatherArrived();
            destination = IntVec3.Invalid;

            var zombie = (Zombie)pawn;
            zombie.checkSmashable = true;

            if (zombie.IsActiveElectric)
                ZombieStateHandler.Electrify(zombie);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return new Toil()
            {
                initAction = new Action(InitAction),
                tickAction = new Action(TickAction),
                defaultCompleteMode = ToilCompleteMode.Never
            };
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
    }

}

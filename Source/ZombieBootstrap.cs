using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace ZombieLand
{
	static class ZombieBootstrap
	{
		// Capture only. It must run before ordinary finalizers that may clear the
		// exception; real recovery stays at Priority.Last and returns the exception unchanged.
		public const int CaptureFinalizerPriority = int.MaxValue;

		static readonly HashSet<string> loggedRepairs = new();
		static readonly HashSet<string> loggedFailures = new();
		static readonly HashSet<string> loggedPassthroughs = new();
		static readonly MethodInfo canExistOnLayerMethod = AccessTools.Method(typeof(FactionGenerator), "CanExistOnLayer", new[] { typeof(PlanetLayer), typeof(FactionDef) });
		[ThreadStatic] static Dictionary<string, Stack<Exception>> capturedFinalizerExceptions;

		public static void ResetLogDedupers()
		{
			loggedRepairs.Clear();
			loggedFailures.Clear();
			loggedPassthroughs.Clear();
			capturedFinalizerExceptions?.Clear();
		}

		public static void RunSafely(string phase, string action, Action callback)
		{
			try
			{
				callback();
			}
			catch (Exception ex)
			{
				LogFailureOnce($"{phase}: {action}", ex);
			}
		}

		public static Exception CaptureFinalizerException(string phase, Exception exception)
		{
			try
			{
				if (exception == null)
					return exception;

				capturedFinalizerExceptions ??= new Dictionary<string, Stack<Exception>>();
				if (capturedFinalizerExceptions.TryGetValue(phase, out var exceptions) == false)
				{
					exceptions = new Stack<Exception>();
					capturedFinalizerExceptions[phase] = exceptions;
				}
				exceptions.Push(exception);
				return exception;
			}
			catch
			{
				return exception;
			}
		}

		public static bool ShouldRunFinalizerRecovery(string phase, Exception exception, bool runOriginal, out Exception observedException, bool runWhenOriginalSkipped = false, bool runWhenOriginalSucceeded = false)
		{
			try
			{
				observedException = exception;
				if (capturedFinalizerExceptions == null || capturedFinalizerExceptions.TryGetValue(phase, out var exceptions) == false)
					return observedException != null || (runOriginal ? runWhenOriginalSucceeded : runWhenOriginalSkipped);

				if (exception == null && exceptions.Count > 0)
				{
					observedException = exceptions.Pop();
					if (exceptions.Count == 0)
						capturedFinalizerExceptions.Remove(phase);
					return true;
				}

				if (exceptions.Count > 0)
					_ = exceptions.Pop();
				if (exceptions.Count == 0)
					capturedFinalizerExceptions.Remove(phase);

				return observedException != null || (runOriginal ? runWhenOriginalSucceeded : runWhenOriginalSkipped);
			}
			catch
			{
				observedException = exception;
				return exception != null;
			}
		}

		public static Exception RecoveryPassthrough(string phase, Exception exception, Exception observedException, bool recovered)
		{
			try
			{
				if (observedException == null || recovered == false)
					return exception;

				var context = Context(phase, null);
				try
				{
					var key = $"{context}\n{observedException.GetType().FullName}\n{observedException.Message}";
					if (loggedPassthroughs.Add(key))
					{
						var disposition = exception == null
							? "the exception had already been marked handled by another finalizer"
							: "the exception passed to Zombieland's recovery finalizer remains unchanged";
						Log.Warning($"Initialization recovery note: Zombieland observed an exception during {context} while RimWorld/Harmony was already unwinding it. Zombieland only ran minimal Zombieland-state repair; this log line is not saying the original exception was caused by Zombieland, and {disposition}. The root cause is usually the first earlier error or exception.");
					}
				}
				catch
				{
				}

				return exception;
			}
			catch
			{
				return exception;
			}
		}

		public static Faction EnsureZombieFaction(string phase, PlanetLayer layer = null, List<Faction> factions = null, bool addThroughManager = true, bool createIfMissing = false)
			=> EnsureZombieFaction(phase, out _, layer, factions, addThroughManager, createIfMissing);

		public static Faction EnsureZombieFaction(string phase, out bool changed, PlanetLayer layer = null, List<Faction> factions = null, bool addThroughManager = true, bool createIfMissing = false)
		{
			changed = false;
			try
			{
				var def = ZombieDefOf.Zombies;
				if (def == null)
					return null;
				if (ZombieFactionCanExistOnLayer(phase, layer, def) == false)
					return null;

				var factionManager = GetFactionManager();
				factions ??= factionManager?.AllFactionsListForReading;
				if (factions == null)
					return null;

				var zombies = factions.FirstOrDefault(faction => faction?.def == def);
				if (zombies != null)
					return zombies;
				// Only worldgen/load recovery should create the emergency faction. Later map
				// bootstrap paths consume it and fail closed if those boundaries did not repair it.
				if (createIfMissing == false)
					return null;

				zombies = CreateZombieFactionThroughVanilla(def, layer, factions, addThroughManager, factionManager);
				if (zombies == null)
					return null;

				changed = true;
				LogFactionRepairOnce(phase, layer);
				return zombies;
			}
			catch (Exception ex)
			{
				LogFailureOnce(phase, ex);
				return null;
			}
		}

		public static bool EnsureMapStateAfterFinalize(string phase, Map map, bool includeRuntime = true)
		{
			try
			{
				var context = MapContext(phase, map);
				var zombieFaction = EnsureZombieFaction(context, out var changed);
				_ = EnsureZombieDestinationReservations(context, map, zombieFaction, out var reservationsChanged);
				changed |= reservationsChanged;
				if (includeRuntime)
					changed |= EnsureTickManagerRuntime(context, map);
				return changed;
			}
			catch (Exception ex)
			{
				LogFailureOnce($"{phase}: map bootstrap", ex);
				return false;
			}
		}

		public static void EnsureZombieFactionAfterPostLoad(FactionManager factionManager, List<Faction> factions)
		{
			const string phase = "FactionManager.ExposeData PostLoadInit";
			if (factionManager == null || factions == null)
				return;
			if (ReferenceEquals(factionManager, GetFactionManager()) == false)
				return;

			_ = EnsureZombieFaction(phase, out _, factions: factions, addThroughManager: true, createIfMissing: true);
		}

		public static bool EnsureZombieDestinationReservations(string phase, Map map, Faction zombieFaction = null)
			=> EnsureZombieDestinationReservations(phase, map, zombieFaction, out _);

		public static bool EnsureZombieDestinationReservations(string phase, Map map, Faction zombieFaction, out bool changed)
		{
			changed = false;
			try
			{
				if (map?.pawnDestinationReservationManager == null)
					return false;

				var destinations = map.pawnDestinationReservationManager.reservedDestinations;
				if (destinations == null)
					return false;

				zombieFaction ??= EnsureZombieFaction(phase);
				if (zombieFaction == null)
					return false;

				if (destinations.ContainsKey(zombieFaction) == false)
				{
					_ = map.pawnDestinationReservationManager.GetPawnDestinationSetFor(zombieFaction);
					changed = true;
				}
				return true;
			}
			catch (Exception ex)
			{
				LogFailureOnce($"{phase}: zombie destination reservations", ex);
				return false;
			}
		}

		public static void ResetZombieGrid(string phase, Map map, bool createIfMissing = true, bool rebuildLiveZombieCounts = false)
		{
			RunSafely(phase, "zombie count grid reset", () =>
			{
				var grid = createIfMissing ? map?.GetGrid() : map?.GetComponent<PheromoneGrid>();
				if (grid == null)
					return;

				grid.IterateCellsQuick(cell => cell.zombieCount = 0);
				if (rebuildLiveZombieCounts)
					RebuildLiveZombieCounts(map, grid);
			});
		}

		static void RebuildLiveZombieCounts(Map map, PheromoneGrid grid)
		{
			if (map?.mapPawns?.AllPawnsSpawned == null)
				return;

			foreach (var zombie in map.mapPawns.AllPawnsSpawned.OfType<Zombie>())
			{
				if (zombie?.Spawned != true || zombie.Dead || zombie.lastGotoPosition.IsValid == false || zombie.lastGotoPosition.InBounds(map) == false)
					continue;
				grid.ChangeZombieCount(zombie.lastGotoPosition, 1);
			}
		}

		static bool EnsureTickManagerRuntime(string phase, Map map)
		{
			var changed = false;
			RunSafely(phase, "TickManager runtime init", () =>
			{
				if (map == null)
					return;

				var tickManager = map.GetComponent<TickManager>();
				if (tickManager == null)
					return;
				if (tickManager.isInitialized == 0)
					return;

				_ = tickManager.EnsureRuntimeInitialized(phase, out var runtimeChanged);
				changed |= runtimeChanged;
			});
			return changed;
		}

		static FactionManager GetFactionManager()
		{
			try
			{
				return Find.FactionManager;
			}
			catch
			{
				return null;
			}
		}

		static bool ZombieFactionCanExistOnLayer(string phase, PlanetLayer layer, FactionDef def)
		{
			if (layer == null)
				return true;

			try
			{
				if (canExistOnLayerMethod == null)
					return false;
				return (bool)canExistOnLayerMethod.Invoke(null, new object[] { layer, def });
			}
			catch (Exception ex)
			{
				LogFailureOnce($"{phase}: zombie faction layer gate", ex);
				return false;
			}
		}

		static Faction CreateZombieFactionThroughVanilla(FactionDef def, PlanetLayer layer, List<Faction> factions, bool addThroughManager, FactionManager factionManager)
		{
			// Creation must remain on the vanilla public path so other mods that patch
			// faction initialization still participate in this recovery attempt.
			if (addThroughManager == false || factionManager == null)
				return null;

			if (layer == null)
				FactionGenerator.CreateFactionAndAddToManager(def);
			else
				FactionGenerator.CreateFactionAndAddToManager(layer, def);

			return factionManager.FirstFactionOfDef(def)
				?? factions.FirstOrDefault(faction => faction?.def == def);
		}

		static void LogFactionRepairOnce(string phase, PlanetLayer layer)
		{
			var context = Context(phase, layer);
			if (loggedRepairs.Add(context))
				Log.Warning($"Zombieland repaired the missing zombie faction during {context} by re-entering RimWorld's public faction creation path after the normal faction generation step did not finish.");
		}

		static void LogFailureOnce(string phase, Exception ex)
		{
			var context = Context(phase, null);
			var key = $"{context}\n{ex.GetType().FullName}\n{ex.Message}";
			if (loggedFailures.Add(key))
				Log.Error($"Zombieland bootstrap failed during {context}: {ex}");
		}

		static string Context(string phase, PlanetLayer layer)
		{
			var context = phase.NullOrEmpty() ? "unknown phase" : phase;
			return layer == null ? context : $"{context} ({layer})";
		}

		static string MapContext(string phase, Map map)
		{
			var context = phase.NullOrEmpty() ? "unknown phase" : phase;
			if (map == null)
				return context;
			var label = map.Parent?.Label ?? map.ToString();
			return $"{context} ({label}, map {map.uniqueID})";
		}
	}

}

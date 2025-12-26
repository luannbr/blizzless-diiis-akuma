using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using DiIiS_NA.Core.Logging;

namespace DiIiS_NA.GameServer.GSSystem.GameSystem
{
	public class GameUpdateThread
	{
		[DllImport("kernel32.dll")]
		public static extern int GetCurrentThreadId();

		[DllImport("libc.so.6")]
		private static extern int getpid();

		[DllImport("libc.so.6")]
		private static extern int sched_setaffinity(int pid, IntPtr cpusetsize, ref ulong cpuset);

		private int CurrentTId
		{
			get
			{
				return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? GetCurrentThreadId() : getpid();
			}
		}

		private static readonly Logger Logger = LogManager.CreateLogger();
		public List<Game> Games = new List<Game>();

		private readonly object _lock = new object();

		public ulong CPUAffinity = 0;

		public void Run()
		{
			var inactiveGames = new List<Game>();
			var gameSnapshot = new List<Game>();
			var stopwatch = new Stopwatch();
			var lastOverrunLog = Stopwatch.StartNew();

			Thread.BeginThreadAffinity();
			if (CPUAffinity != 0)
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
					CurrentThread.ProcessorAffinity = new IntPtr((int)CPUAffinity);
				else
					sched_setaffinity(0, new IntPtr(sizeof(ulong)), ref CPUAffinity);
			}

			while (true)
			{
				stopwatch.Restart();

				// Snapshot the games list quickly under lock, then update outside the lock.
				gameSnapshot.Clear();
				inactiveGames.Clear();
				lock (_lock)
				{
					for (int i = 0; i < Games.Count; i++)
					{
						var game = Games[i];
						if (!game.Working)
							inactiveGames.Add(game);
						else
							gameSnapshot.Add(game);
					}

					// Remove inactive games while we already hold the lock.
					for (int i = 0; i < inactiveGames.Count; i++)
						Games.Remove(inactiveGames[i]);
				}

				// Update each active game on this dedicated update thread.
				// This avoids per-tick Task.Run allocations and threadpool contention, which were a major source of lag/stutter.
				for (int i = 0; i < gameSnapshot.Count; i++)
				{
					var game = gameSnapshot[i];
					try
					{
						game.Update();
					}
					catch (Exception ex)
					{
						Logger.ErrorException(ex, "Error in Game.Update()");
					}
				}

				stopwatch.Stop();
				var elapsedMs = stopwatch.ElapsedMilliseconds;

				// Sleep to maintain a consistent 100ms update cadence.
				int compensation = (int)(100 - elapsedMs);
				if (elapsedMs > 100)
				{
					// Throttle this log to avoid flooding when the server is under load.
					if (lastOverrunLog.ElapsedMilliseconds >= 5000)
					{
						Logger.Trace("Game.Update() loop took [{0}ms] more than Game.UpdateFrequency [{1}ms].", elapsedMs, 100);
						lastOverrunLog.Restart();
					}
					compensation = (int)(100 - (elapsedMs % 100));
				}

				Thread.Sleep(Math.Max(0, compensation));
			}

			//Thread.EndThreadAffinity();
		}

		public void AddGame(Game game)
		{
			lock (_lock)
			{
				Games.Add(game);
			}
		}

		private ProcessThread CurrentThread
		{
			get
			{
				int id = CurrentTId;
				return
					(from ProcessThread th in Process.GetCurrentProcess().Threads
					 where th.Id == id
					 select th).Single();
			}
		}
	}
}

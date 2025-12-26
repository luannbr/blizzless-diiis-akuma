using DiIiS_NA.Core.Logging;
using DiIiS_NA.Core.Schedulers;
using System;
using System.Collections.Generic;

namespace DiIiS_NA.LoginServer
{
    public class Watchdog
    {
		private static readonly Logger Logger = LogManager.CreateLogger();

		private uint Seconds = 0;

		private List<ScheduledTask> ScheduledTasks = new List<ScheduledTask>();
		private List<ScheduledTask> TasksToRemove = new List<ScheduledTask>();

		private List<ScheduledEvent> ScheduledEvents = new List<ScheduledEvent>();

		public void Run()
		{
			ScheduledEvents.Add(new SpawnDensityRegen());
			
			while (true)
			{
				System.Threading.Thread.Sleep(1000);
				this.Seconds++;
				try
				{
					lock (this.ScheduledTasks)
					{
						foreach (var task in this.TasksToRemove)
						{
							if (this.ScheduledTasks.Contains(task))
								this.ScheduledTasks.Remove(task);
						}
						this.TasksToRemove.Clear();

						// Avoid LINQ allocations in this 1Hz loop.
						for (int i = 0; i < this.ScheduledTasks.Count; i++)
						{
							var task = this.ScheduledTasks[i];
							if (task.Delay == 0)
								continue;
							if (this.Seconds % task.Delay != 0)
								continue;
							try
							{
								task.Task.Invoke();
							}
							catch
							{
								//this.TasksToRemove.Add(task);
							}
						}
					}

					foreach (var s_event in this.ScheduledEvents)
					{
						if (s_event.TimedOut)
							s_event.ExecuteEvent();
					}
				}
				catch { }
			}
		}

		public void AddTask(uint Delay, Action Task)
		{
			lock (this.ScheduledTasks)
			{
				this.ScheduledTasks.Add(new ScheduledTask { Delay = Delay, Task = Task });
			}
		}

		public class ScheduledTask
		{
			public Action Task;
			public uint Delay;
		}
	}
}

﻿using JocysCom.ClassLibrary.IO;
using SharpDX.DirectInput;
using SharpDX.XInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using x360ce.Engine;

namespace x360ce.App.DInput
{
	public partial class DInputHelper : IDisposable
	{

		public DInputHelper()
		{
			TimerSemaphore = new SemaphoreSlim(0);
			Manager = new DirectInput();
			InitDeviceDetector();
			CombinedXInputStates = new State[4];
			LiveXInputStates = new State[4];
			XiControllers = new Controller[4];
			XiControllerConnected = new bool[4];
			for (int i = 0; i < 4; i++)
			{
				CombinedXInputStates[i] = new State();
				LiveXInputStates[i] = new State();
				XiControllers[i] = new Controller((UserIndex)i);
			}
			watch = new System.Diagnostics.Stopwatch();
		}

		// Where current DInput device state is stored:
		//
		//    UserDevice.Device - DirectInput Device (Joystick)
		//    UserDevice.State - DirectInput Device (JoystickState)
		//
		// Process 1
		// limited to [125, 250, 500, 1000Hz]
		// Lock
		// {
		//    Aquire:
		//    DiDevices - when device is detected.
		//	  DiCapabilities - when device is detected.
		//	  JoStates - from mapped devices.
		//	  DiStates - from converted JoStates.
		//	  XiStates - from converted DiStates
		// }
		//
		// Process 2
		// limited to [30Hz] (only when visible).
		// Lock
		// {
		//	  DiDevices, DiCapabilities, DiStates, XiStates
		//	  Update DInput and XInput forms.
		// }

		public event EventHandler<EventArgs> FrequencyUpdated;
		public event EventHandler<EventArgs> DevicesUpdated;
		public event EventHandler<EventArgs> StatesUpdated;
		public event EventHandler<EventArgs> StatesRetrieved;
		public event EventHandler<EventArgs> UpdateCompleted;

		DirectInput Manager;

		JocysCom.ClassLibrary.HiResTimer _timer;

		public void Start()
		{
			watch.Restart();
			var ts = new System.Threading.ThreadStart(TimerProcess);
			var t = new System.Threading.Thread(ts);
			t.IsBackground = true;
			t.Start();
		}

		public void Stop()
		{
			// Unlock EventArgsSemaphore.Wait() line.
			TimerSemaphore.Release();
		}

		SemaphoreSlim TimerSemaphore;
		object EventArgsSemaphoreLock = new object();


		void TimerProcess()
		{
			_timer = new JocysCom.ClassLibrary.HiResTimer();
			_timer.Interval = (int)Frequency;
			_timer.Elapsed += Timer_Elapsed;
			_timer.Start();
			// Wait here until all items returns to the pool.
			TimerSemaphore.Wait();
			_timer.Dispose();
		}

		public Exception LastException = null;

		private void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			try
			{
				RefreshAll();
			}
			catch (Exception ex)
			{
				LastException = ex;
			}
		}

		object DiUpdatesLock = new object();

		void RefreshAll()
		{
			lock (DiUpdatesLock)
			{
				// Update information about connected devices.
				UpdateDiDevices();
				// Update JoystickStates from devices.
				UpdateDiStates();
				// Update XInput states from Custom DirectInput states.
				UpdateXiStates();
				// Combine XInput states of controllers.
				CombineXiStates();
				// Update virtual devices from combined states.
				UpdateVirtualDevices();
				// Retrieve XInput states from XInput controllers.
				RetrieveXiStates();
				// Update pool frequency value and sleep if necessary.
				UpdateDelayFrequency();
				// Fire event.
				var ev = UpdateCompleted;
				if (ev != null)
					ev(this, new EventArgs());
			}
		}

		/// <summary>
		/// Watch to monitor update frequency.
		/// </summary>
		System.Diagnostics.Stopwatch watch;
		long lastTime;
		long currentTick;
		public long CurrentUpdateFrequency;

		UpdateFrequency Frequency
		{
			get			{ return _Frequency;  }
			set
			{
				_Frequency = value;
				var t = _timer;
				if (t != null && t.Interval != (int)value)
					t.Interval = (int)value;
			}
		}
		UpdateFrequency _Frequency = UpdateFrequency.ms1_1000Hz;

		void UpdateDelayFrequency()
		{
			// Calculate update frequency.
			currentTick++;
			var currentTime = watch.ElapsedMilliseconds;
			// If one second elapsed then...
			if ((currentTime - lastTime) > 1000)
			{
				CurrentUpdateFrequency = currentTick;
				currentTick = 0;
				lastTime = currentTime;
				var ev = FrequencyUpdated;
				if (ev != null)
					ev(this, new EventArgs());
			}
		}

		#region IDisposable

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		// The bulk of the clean-up code is implemented in Dispose(bool)
		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				Stop();
				UnInitDeviceDetector();
				if (Manager != null)
				{
					Manager.Dispose();
					Manager = null;
				}
			}
		}

		#endregion

	}
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InspectorDelegateCaller;

/// <summary>
/// Keeps an object in memory until it hasn't been accessed for a certain number of seconds. Defaults to <see cref="defaultStorageTimeSeconds"/> seconds.
/// </summary>
class TemporaryObjectStore
{
	/// <summary>
	/// Optional debug logging callback
	/// There will be no logging if this is null
	/// </summary>
	public static Action<string> DebugLogger = null;

	/// <summary>
	/// Whether or not to suppress exceptions from being thrown (applies to all instances)
	/// </summary>
	public static bool SuppressExceptions;

	/// <summary>
	/// Indicates if this instance is currently being used.
	/// Instances that are being used cannot be re-initialized.
	/// Will become false when the stored object is freed.
	/// </summary>
	public bool IsBeingUsed
	{
		get
		{
			lock (lockObj)
			{
				if (storedObj != null)
					return true;
				return false;
			}
		}
	}

	/// <summary>
	/// Time when the stored object is currently expected to be freed.
	/// This can change if the object is accessed.
	/// </summary>
	public DateTime ExpectedReleaseTime
	{
		get
		{
			lock (lockObj)
			{
				return lastAccessTime + TimeSpan.FromMilliseconds(DelayMs);
			}
		}
	}

	private double storageTimeSeconds;
	private object storedObj = null;
	private DateTime lastAccessTime;
	private const double defaultStorageTimeSeconds = 5.0;
	private int DelayMs => (int)(storageTimeSeconds * 1000) + 1;
	private static ulong globalId;
	private ulong id;
	private object lockObj = new();
	
	private static void DebugLog(Func<string> messageProducer)
	{
		if (DebugLogger != null)
			DebugLogger($"{nameof(TemporaryObjectStore)}: {messageProducer()}");
	}

	private static void TryThrow(string msg)
	{
		if (!SuppressExceptions)
			throw new Exception($"{nameof(TemporaryObjectStore)}: {msg}");
		else
			DebugLog(() => msg);
	}

	/// <summary>
	/// Provides a way to access the stored object.
	/// This uses a lock statement to ensure only one thread can access it at a time.
	/// </summary>
	/// <returns>The stored object</returns>
	public object Access()
	{
		lock (lockObj)
		{
			lastAccessTime = DateTime.UtcNow;
			DebugLog(() => $"Accessed id {id} at {lastAccessTime}, new expected release time: {ExpectedReleaseTime}");
			return storedObj;
		}
	}

	/// <summary>
	/// Initialize the instance of <see cref="TemporaryObjectStore"/> with the given <see cref="object"/>
	/// Will throw an exception if the instance is already being used
	/// </summary>
	/// <param name="_obj">The object to keep in memory for a minimum time of <see cref="defaultStorageTimeSeconds"/> seconds.</param>
	public void InitializeAndStart(object _obj, double? _storageTimeSeconds = null)
	{
		if (storedObj != null)
		{
			TryThrow($"Tried to re-initialize instance with id {id} at {DateTime.UtcNow}, expected release time: {ExpectedReleaseTime}");
		}

		storedObj = _obj;
		id = globalId++;
		lastAccessTime = DateTime.UtcNow;
		storageTimeSeconds = _storageTimeSeconds ?? defaultStorageTimeSeconds;

		DebugLog(() => $"Initialized instance with Id {id} at {lastAccessTime}, expected to release at {ExpectedReleaseTime}");

		Task.Run(async () =>
		{
			await Task.Delay(DelayMs);
			Update(this);
		});
	}

	private static async void Update(TemporaryObjectStore objMemAccess)
	{
		int delayMs = 0;
		bool released = false;
		lock (objMemAccess.lockObj)
		{
			if (!objMemAccess.TryRelease())
			{
				DebugLog(() => $"Could not release instance with id {objMemAccess.id} at {DateTime.UtcNow}, expected release time: {objMemAccess.ExpectedReleaseTime}.");
				delayMs = objMemAccess.DelayMs;
			}
			else
			{
				DebugLog(() => $"Released instance with id {objMemAccess.id} at {DateTime.UtcNow}, expected release time: {objMemAccess.ExpectedReleaseTime}");
				released = true;
			}
		}
		if (!released)
		{
			await Task.Delay(delayMs);
			Update(objMemAccess);
		}
	}

	private bool TryRelease()
	{
		if (storedObj == null)
		{
			TryThrow("Stored object is already null in TryRelease!");
		}
		if ((DateTime.UtcNow - lastAccessTime).TotalSeconds > defaultStorageTimeSeconds)
		{
			storedObj = null;
			return true;
		}
		return false;
	}
}

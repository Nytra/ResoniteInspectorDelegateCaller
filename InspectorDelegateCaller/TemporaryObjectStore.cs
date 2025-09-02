using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
	/// Set true to free the object on the next update even if the time is not up.
	/// </summary>
	public bool ForceFree;

	/// <summary>
	/// Indicates if this instance is currently being used.
	/// Instances that are being used cannot be re-initialized.
	/// Will become false when the stored object is freed.
	/// </summary>
	public bool IsBeingUsed
	{
		get
		{
			bool result = false;
			RunActionWithLock(() => 
			{
				if (storedObj != null)
					result = true;
				result = false;
			});
			return result;
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
			DateTime result = default;
			RunActionWithLock(() => 
			{
				result = expectedReleaseTimeInternal;
			});
			return result;
		}
	}

	private DateTime expectedReleaseTimeInternal
	{
		get
		{
			return lastAccessTime + TimeSpan.FromMilliseconds(DelayMs);
		}
	}

	private double storageTimeSeconds;
	private object storedObj = null;
	private DateTime lastAccessTime;
	private const double defaultStorageTimeSeconds = 5.0;
	private int DelayMs => (int)(storageTimeSeconds * 1000) + 100;
	private static ulong globalId;
	private ulong id;
	private object lockObj = new();

	private void RunActionWithLock(Action act)
	{
		lock (lockObj)
		{
			act();
		}
	}
	
	private void DebugLog(Func<string> messageProducer, bool showReleaseTime=false)
	{
		if (DebugLogger != null)
			DebugLogger($"{nameof(TemporaryObjectStore)} id {id} {(showReleaseTime ? $"({expectedReleaseTimeInternal})" : "")}: {messageProducer()}");
	}

	private void TryThrow(Func<string> messageProducer)
	{
		if (!SuppressExceptions)
			throw new Exception($"{nameof(TemporaryObjectStore)} {id}: {messageProducer()}");
		else
			DebugLog(messageProducer);
	}

	/// <summary>
	/// Provides a way to access the stored object.
	/// This uses a lock statement to ensure only one thread can access it at a time.
	/// </summary>
	/// <returns>The stored object</returns>
	public object Access()
	{
		object stored = null;
		RunActionWithLock(() =>
		{
			lastAccessTime = DateTime.UtcNow;
			DebugLog(() => $"This instance was accessed.", true);
			stored = storedObj;
		});
		return stored;
	}

	/// <summary>
	/// Initialize the instance of <see cref="TemporaryObjectStore"/> with the given <see cref="object"/>
	/// Will throw an exception if the instance is already being used
	/// </summary>
	/// <param name="_obj">The object to store.</param>
	/// <param name="_storageTimeSeconds">Optional: number of seconds before the object is freed. Uses a default value otherwise. <see cref="defaultStorageTimeSeconds"/></param>
	public void InitializeAndStart(object _obj, double? _storageTimeSeconds = null)
	{
		if (_obj is null)
		{
			TryThrow(() => $"ERROR: The object to store is already null in {nameof(InitializeAndStart)}.");
		}
		if (storedObj != null)
		{
			TryThrow(() => $"ERROR: Wrongly tried to re-initialize this instance.");
		}

		storedObj = _obj;
		id = globalId++;
		lastAccessTime = DateTime.UtcNow;
		storageTimeSeconds = _storageTimeSeconds ?? defaultStorageTimeSeconds;

		DebugLog(() => $"New initialization.", true);

		Task.Run(async () =>
		{
			await Task.Delay(DelayMs).ConfigureAwait(continueOnCapturedContext: false);
			Update(this);
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static async void Update(TemporaryObjectStore objStore)
	{
		int delayMs = 0;
		bool released = false;
		objStore.RunActionWithLock(() =>
		{
			if (!objStore.TryRelease())
			{
				objStore.DebugLog(() => $"Could not release this instance, it is still being used.", true);
				delayMs = objStore.DelayMs;
			}
			else
			{
				objStore.DebugLog(() => $"Released this instance.");
				released = true;
			}
		});
		if (!released)
		{
			await Task.Delay(delayMs).ConfigureAwait(continueOnCapturedContext: false);
			Update(objStore);
		}
	}

	private bool TryRelease()
	{
		if (storedObj == null)
		{
			TryThrow(() => $"ERROR: Stored object is already null in {nameof(TryRelease)}!");
		}
		if (ForceFree || (DateTime.UtcNow - lastAccessTime).TotalSeconds > storageTimeSeconds)
		{
			storedObj = null;
			return true;
		}
		return false;
	}
}

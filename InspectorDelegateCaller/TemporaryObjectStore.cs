using Elements.Assets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InspectorDelegateCaller;

/// <summary>
/// Keeps a reference to an object until it hasn't been accessed for a certain number of seconds. Defaults to <see cref="defaultStorageTimeSeconds"/> seconds.
/// Useful if you have data you want to store while it's needed, and then be released when it is not being used anymore
/// Originally made by Nytra
/// </summary>
public class TemporaryObjectStore
{
	/// <summary>
	/// Data sent to the <see cref="onReleaseCallback"/> which contains the object that was released and the instance that released it
	/// Will be immediately disposed after the callback, meaning all references inside will become null
	/// </summary>
	public class OnReleaseCallbackData : IDisposable
	{
		/// <summary>
		/// The object that was released
		/// </summary>
		public object releasedObject;

		/// <summary>
		/// The store that released the object
		/// </summary>
		public TemporaryObjectStore releasingStore;

		private bool disposed = false;

		/// <summary>
		/// Constructs the <see cref="OnReleaseCallbackData"/>
		/// </summary>
		/// <param name="releasedObject">The object that was released</param>
		/// <param name="releasingStore">The store that released the object</param>
		public OnReleaseCallbackData(object releasedObject, TemporaryObjectStore releasingStore)
		{
			this.releasedObject = releasedObject;
			this.releasingStore = releasingStore;
		}

		public void Dispose()
		{
			Dispose(disposing: true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposed)
			{
				if (disposing)
				{
					releasedObject = null;
					releasingStore = null;
				}

				disposed = true;
			}
		}

		~OnReleaseCallbackData()
		{
			Dispose(disposing: false);
		}
	}

	/// <summary>
	/// Optional debug logging callback
	/// There will be no logging if this is null
	/// Defaults to null
	/// </summary>
	public static Action<string> DebugLoggingCallback = null;

	/// <summary>
	/// Indicates if this instance is currently being used to store something.
	/// Instances that are currently storing something cannot be re-initialized until they release their stored object.
	/// Will become false when the stored object is freed.
	/// </summary>
	public bool HasStoredObject => storedObj != null;

	/// <summary>
	/// Time when the stored object is currently expected to be freed.
	/// This can change if the object is accessed.
	/// </summary>
	public DateTime ExpectedReleaseTime => lastAccessTime + TimeSpan.FromSeconds(storageTimeSeconds);

	private double storageTimeSeconds;
	private object storedObj = null;
	private DateTime lastAccessTime;
	private const double DEFAULT_STORAGE_TIME_SECONDS = 60;
	private static ulong globalId;
	private ulong id;
	private CancellationTokenSource cancellation = null;
	private bool suppressExceptions;
	private Action<OnReleaseCallbackData> onReleaseCallback = null;
	
	private void Debug(string msg)
	{
		if (DebugLoggingCallback != null)
			DebugLoggingCallback($"{nameof(TemporaryObjectStore)} id {id} ({ExpectedReleaseTime}): {msg}");
	}

	private void ThrowOrDebug(string msg)
	{
		if (!suppressExceptions)
			throw new Exception($"{nameof(TemporaryObjectStore)} {id} ({ExpectedReleaseTime}): {msg}");
		else
			Debug(msg);
	}

	/// <summary>
	/// Provides a way to access the stored object.
	/// </summary>
	/// <returns>The stored object</returns>
	public object Access()
	{
		if (storedObj != null)
		{
			lastAccessTime = DateTime.UtcNow;
			Debug($"Accessed.");
		}
		else
		{
			Debug($"Accessed but the stored object is null.");
		}
		return storedObj;
	}

	/// <summary>
	/// Requests a cancellation of the update task for this instance
	/// Results in the stored object being freed as soon as possible.
	/// Does not happen immediately
	/// Will potentially throw if there is nothing being stored
	/// </summary>
	/// <param name="_onReleaseCallback">Optional: Action callback which gets called immediately after the stored object has been released and the instance is ready to store something else</param>
	public bool RequestRelease(Action<OnReleaseCallbackData> _onReleaseCallback = null)
	{
		if (storedObj == null)
		{
			ThrowOrDebug("Cannot request cancellation because nothing is stored.");
			return false;
		}
		Debug($"Requesting cancellation of update task.");
		onReleaseCallback ??= _onReleaseCallback;
		cancellation?.Cancel();
		return true;
	}

	/// <summary>
	/// Initialize this instance with the given object
	/// Might throw an exception if the instance is already storing something, see <paramref name="_suppressExceptions"/>
	/// </summary>
	/// <param name="objectToStore">The object to store.</param>
	/// <param name="_storageTimeSeconds">Optional: number of seconds before the object is potentially freed. Uses a default value otherwise. <see cref="DEFAULT_STORAGE_TIME_SECONDS"/></param>
	/// <param name="_suppressExceptions">Optional: Should exceptions be suppressed. This is useful if you really don't want to crash the program in any circumstances.</param>
	/// <param name="_onReleaseCallback">Optional: Action callback which gets called immediately after the stored object has been released and the instance is ready to store something else</param>
	public void StoreTemporarily(object objectToStore, double? _storageTimeSeconds = null, bool _suppressExceptions = false, Action<OnReleaseCallbackData> _onReleaseCallback = null)
	{
		if (objectToStore is null)
		{
			ThrowOrDebug($"ERROR: The object to store is already null in {nameof(StoreTemporarily)}.");
			return;
		}
		if (storedObj != null)
		{
			ThrowOrDebug($"ERROR: Wrongly tried to store a new object when there is already an object being stored.");
			return;
		}

		storedObj = objectToStore;
		id = globalId++;
		lastAccessTime = DateTime.UtcNow;
		storageTimeSeconds = _storageTimeSeconds ?? DEFAULT_STORAGE_TIME_SECONDS;
		cancellation = new();
		suppressExceptions = _suppressExceptions;
		onReleaseCallback = _onReleaseCallback;

		Debug($"Stored new object.");

		Task.Run(() =>
		{
			Update(this);
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static async void Update(TemporaryObjectStore objStore)
	{
		int delayMs = (int)(objStore.ExpectedReleaseTime - DateTime.UtcNow).TotalMilliseconds + 100; // Wait a tiny bit longer than the actual storage time because otherwise it thinks it's too soon to release it
		try
		{
			await Task.Delay(delayMs, objStore.cancellation.Token).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (TaskCanceledException)
		{
		}
		if (objStore.cancellation.IsCancellationRequested)
		{
			objStore.Debug($"Update task was cancelled. Releasing stored object.");
			objStore.Release();
			return;
		}
		if (!objStore.TryRelease())
		{
			objStore.Debug($"Could not release stored object, it is still being used.");
			Update(objStore);
		}
		else
		{
			objStore.Debug($"Released stored object.");
		}
	}

	private bool TryRelease()
	{
		if (storedObj == null)
		{
			ThrowOrDebug($"ERROR: Stored object is already null in {nameof(TryRelease)}!");
			return true; // return true because the object is released
		}
		else if ((DateTime.UtcNow - lastAccessTime).TotalSeconds > storageTimeSeconds)
		{
			Release();
			return true;
		}
		return false;
	}

	private void Release()
	{
		OnReleaseCallbackData data = null;
		if (onReleaseCallback != null)
		{
			data = new OnReleaseCallbackData(storedObj, this);
		}
		storedObj = null;
		onReleaseCallback?.Invoke(data);
		data.Dispose();
		onReleaseCallback = null;
		cancellation = null;
	}
}

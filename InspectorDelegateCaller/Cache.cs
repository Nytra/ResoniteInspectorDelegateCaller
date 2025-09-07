using System;
using System.Collections.Generic;

namespace Caching;

/// <summary>
/// Class to control all static instances of <see cref="Cache{TKey, TObject}"/>
/// </summary>
public static class CacheManager
{
	private static HashSet<Action> cleanupActions = new();
	internal static void RegisterCleanupAction(Action cleanupAction)
	{
		cleanupActions.Add(cleanupAction);
	}

	/// <summary>
	/// Clears all caches, which releases all data held by them
	/// </summary>
	public static void ClearAllCaches()
	{
		foreach (var cleanupAction in cleanupActions)
		{
			cleanupAction();
		}
	}
}

/// <summary>
/// Cache which stores data temporarily using <see cref="TemporaryObjectStore"/>
/// </summary>
/// <typeparam name="TKey">The type to use as a key</typeparam>
/// <typeparam name="TObject">The type of the data that is being stored</typeparam>
public static class Cache<TKey, TObject>
{
	private static Dictionary<TKey, TemporaryObjectStore> stores = new();

	static Cache()
	{
		CacheManager.RegisterCleanupAction(Clear);
	}

	/// <summary>
	/// Get data already stored in the cache, or construct new data and then store it
	/// </summary>
	/// <param name="key">The key which is used to get the stored data</param>
	/// <param name="constructor">The function that constructs the data if it is not already stored</param>
	/// <param name="onReleaseCallback">Optional: Callback which gets called immediately after the stored object has been released, containing <see cref="TemporaryObjectStore.OnReleaseCallbackData"/></param>
	/// <returns></returns>
	public static TObject Get(TKey key, Func<TKey, TObject> constructor, Action<TemporaryObjectStore.OnReleaseCallbackData> onReleaseCallback = null)
	{
		TemporaryObjectStore store;
		if (!stores.TryGetValue(key, out store))
		{
			store = new();
			stores.Add(key, store);
		}
		if (store.HasStoredObject)
		{
			return (TObject)store.Access();
		}
		TObject res = constructor(key);
		store.StoreTemporarily(res, onReleaseCallback: onReleaseCallback);
		return res;
	}


	/// <summary>
	/// Clears this singular cache instance
	/// </summary>
	public static void Clear()
	{
		foreach (var objectStore in stores.Values)
		{
			if (objectStore.HasStoredObject)
				objectStore.RequestRelease();
		}
		stores.Clear();
	}
}
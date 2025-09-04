using Elements.Core;
using FrooxEngine;
using ResoniteModLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static InspectorDelegateCaller.InspectorDelegateCaller;

namespace InspectorDelegateCaller;

public static class Helper
{
	// adapted from ShowDelegates by art0007i
	public static void GetAllMethods(Type t, HashSet<MethodDataCache> set, Func<MethodInfo, MethodDataCache> dataConstructor)
	{
		var type = t;
		while (type != null)
		{
			foreach (var m in type.GetMethods(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
			{
				if (set.Any(v => v.method.MethodHandle == m.MethodHandle)) continue;
				var data = dataConstructor(m);
				if (data != null)
					set.Add(data);
			}
			type = type.BaseType;
		}
	}

	public static bool HasSyncMethod(MethodInfo info)
	{
		return info.CustomAttributes.Any(a => a.AttributeType == typeof(SyncMethod));
	}

	public static bool IsButtonEventHandler(ParameterInfo[] param)
	{
		return param[1].ParameterType == typeof(ButtonEventData) && (param[0].ParameterType == typeof(IButton) || param[0].ParameterType.GetInterfaces().Contains(typeof(IButton)));
	}

	public static bool ClassifyType(Type t, out bool isPrimitive, out bool isWorldElement, out bool isDelegate)
	{
		isWorldElement = false;
		isDelegate = false;
		isPrimitive = false;
		if (typeof(Delegate).IsAssignableFrom(t))
		{
			isDelegate = true;
			return true;
		}
		else if (t == typeof(IWorldElement) || t.GetInterfaces().Contains(typeof(IWorldElement)))
		{
			isWorldElement = true;
			return true;
		}
		else
		{
			try
			{
				if (Coder.IsEnginePrimitive(t))
				{
					isPrimitive = true;
					return true;
				}
			}
			catch (Exception e)
			{
				InspectorDelegateCaller.Error($"ERROR: Type {t.GetNiceName()} threw in IsEnginePrimitive!\n{e}");
			}
		}
		return false;
	}

	public static string GetParamString(ParameterInfo[] param = null)
	{
		if (param == null) return "";
		return string.Join(", ", param.Select(p => p.ParameterType).Select(t => t.GetNiceName()));
	}

	public static void DebugPrintAllSyncMethods(Predicate<MethodDataCache> filter = null)
	{
		var asses = AppDomain.CurrentDomain.GetAssemblies();

		foreach (var ass in asses)
		{
			IEnumerable<Type> workerTypes;
			try
			{
				workerTypes = ass.GetTypes().Where(t => typeof(Worker).IsAssignableFrom(t));
			}
			catch
			{
				continue;
			}
			foreach (var workerType in workerTypes)
			{
				var syncMethodsData = InspectorDelegateCaller.GetAllValidSyncMethods(workerType).Where(data => filter == null || filter(data));
				foreach (var syncMethodData in syncMethodsData)
				{
					try
					{
						var paramsSupported = syncMethodData.parameters.Length == 0 || syncMethodData.parameters.All(p => p.ParameterType.IsDataModelType());
						var returnTypeSupported = syncMethodData.method.ReturnType == typeof(void);
						var supportedText = returnTypeSupported && paramsSupported ? "supported" : "invalid";
						ResoniteMod.Debug($"{ass.GetName().Name}.{workerType.GetNiceName()} {syncMethodData.method.ReturnType.GetNiceName()} {syncMethodData.method.Name}({GetParamString(syncMethodData.parameters)}) - {supportedText}");
					}
					catch
					{
						InspectorDelegateCaller.Error($"ERROR in {ass.GetName().Name}.{workerType.GetNiceName()} {syncMethodData.method.ReturnType.GetNiceName()} {syncMethodData.method.Name} ({GetParamString(syncMethodData.parameters)})");
						throw;
					}
				}
			}
		}
	}
}
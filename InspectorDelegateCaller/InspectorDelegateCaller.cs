﻿using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
//using ResoniteHotReloadLib;

namespace InspectorDelegateCaller
{
	public class InspectorDelegateCaller : ResoniteMod
	{
		public override string Name => "InspectorDelegateCaller";
		public override string Author => "eia485 / Nytra";
		public override string Version => "1.4.2";
		public override string Link => "https://github.com/Nytra/ResoniteInspectorDelegateCaller";

		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_Action = new("actions", "show callable direct actions in inspectors", () => true);
		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_SubAction = new("subActions", "show callable non direct actions in inspectors, this is mainly protoflux calls", () => true);
		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_ArgAction = new("argActions", "show any action with arguments in inspectors", () => true);
		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_Buttons = new("buttons", "show callable buttons in inspectors", () => false);
		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_ArgButtons = new("argButtons", "show any button with arguments in inspectors", () => true);

		// hide the slot destroy buttons because they are easy to accidentally click and they are not undoable
		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_ShowSlotDestroy = new("showSlotDestroy", "show the slot destroy buttons in inspectors", () => false);

		// skip generating buttons if they were already generated by some other method
		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_SkipDuplicates = new("skipDuplicates", "skip duplicates", () => true);
		//[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_SkipIndirect = new("skipIndirect", "if skip duplicates is true, skip methods passed as arguments to other methods (may not always be appropriate)", () => false, internalAccessOnly: true);

		static ModConfiguration config;

		static Dictionary<Worker, Dictionary<UIBuilder, Slot>> workerUiRootSlots = new Dictionary<Worker, Dictionary<UIBuilder, Slot>>();

		public override void OnEngineInit()
		{
			config = GetConfiguration();
			//HotReloader.RegisterForHotReload(this);
			PatchStuff();
		}

		//static void BeforeHotReload()
		//{
		//	workerUiRootSlots.Clear();
		//	Harmony harmony = new Harmony("net.eia485 / Nytra.InspectorDelegateCaller");
		//	harmony.UnpatchAll("net.eia485 / Nytra.InspectorDelegateCaller");
		//}

		//static void OnHotReload(ResoniteMod modInstance)
		//{
		//	config = modInstance.GetConfiguration();
		//	PatchStuff();
		//}

		static void PatchStuff()
		{
			Harmony harmony = new Harmony("owo.Nytra.InspectorDelegateCaller");
			harmony.PatchAll();
		}

		static bool ButtonAlreadyGenerated(Worker worker, MethodInfo m, ParameterInfo[] param, UIBuilder ui)
		{
			// Don't hide buttons on member editors or ref editors
			if (worker is MemberEditor || worker is RefEditor)
			{
				return false;
			}

			Slot s = null;
			if (workerUiRootSlots.ContainsKey(worker) && workerUiRootSlots[worker].ContainsKey(ui))
			{
				s = workerUiRootSlots[worker][ui];
			}
			if (s != null && m != null)
			{
				
				if (param.Length == 2 && isButtonDelegate(param) && hasSyncMethod(m) && s.GetComponentsInChildren<Button>().Any((Button btn) => btn.Pressed.Target != null && btn.Pressed.Target.Method.MethodHandle == m.MethodHandle))
				{
					Debug($"Button found for method {m.Name} on worker {worker.Name}");
					return true;
				}
				if ((param.Length == 3 || param.Length == 2) && isButtonDelegate(param) && hasSyncMethod(m) && s.GetComponentsInChildren<ButtonRelayBase>().Any((ButtonRelayBase btnRelay) => btnRelay.GetSyncMember("ButtonPressed") is ISyncDelegate syncDelegate && syncDelegate.Method != null && syncDelegate.Method.Method.MethodHandle == m.MethodHandle))
				{
					Debug($"ButtonRelay found for method {m.Name} on worker {worker.Name}");
					return true;
				}

				// In some cases the component will generate ButtonRelays which provide the method as an argument to another method
				// One example is AudioReverbZone
				// I could skip if these are found but it might not always be appropriate to do so?

				//if (config.GetValue(Key_SkipIndirect) && param.Length == 0 && hasSyncMethod(m) && s.GetComponentsInChildren<ButtonRelayBase>().Any((ButtonRelayBase btnRelay) => btnRelay.GetSyncMember("Argument") is ISyncDelegate syncDelegate && syncDelegate.Method != null && syncDelegate.Method.Method.MethodHandle == m.MethodHandle))
				//{
				//	Debug($"ButtonRelay found with Argument for method {m.Name} on worker {worker.Name}");
				//	return true;
				//}
			}
			return false;
		}

		// from ShowDelegates by art0007i
		public static MethodInfo[] GetAllMethodsForRealThisTime(Type t)
		{
			var set = Pool.BorrowHashSet<MethodInfo>();

			var type = t;
			while (type != null)
			{
				foreach (var m in type.GetMethods(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
				{
					// i hate this but it works?
					if (set.Any(v => v.MethodHandle == m.MethodHandle)) continue;
					set.Add(m);
				}
				type = type.BaseType;
			}

			var arr = set.ToArray();

			Pool.Return(ref set);
			return arr;
		}

		[HarmonyPatch(typeof(WorkerInspector), "BuildInspectorUI")]
		class InspectorDelegateCallerPatch
		{
			static void Postfix(Worker worker, UIBuilder ui)
			{
				Debug("Worker Name: " + worker.Name + " Worker Type Name: " + worker.GetType().Name);

				Slot workerUiRoot = ui.CurrentRect?.Slot?.Parent;

				if (workerUiRoot == null)
				{
					// if the workerUiRoot is null then something is wrong
					Error("workerUiRoot is null!");
					return;
				}

				//Debug($"currentRect Slot parent hierarchy:\n{ui.CurrentRect.Slot.ParentHierarchyToString()}");

				if (!workerUiRootSlots.ContainsKey(worker))
				{
					workerUiRootSlots.Add(worker, new Dictionary<UIBuilder, Slot>());
				}

				// add the uibuilder and the worker ui root slot
				workerUiRootSlots[worker].Add(ui, workerUiRoot);

				// run this later so that the worker ui can finish generating fully, then it becomes possible to check for duplicate buttons
				worker.World.RunSynchronously(() =>
				{
					RectTransform origRect = ui.CurrentRect;
					ui.NestInto(workerUiRootSlots[worker][ui]);
					var origHeight = ui.Style.MinHeight;
					ui.Style.MinHeight = 24f;
					int count = 0;

					// worker.GetType().GetMethods(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
					foreach (var m in GetAllMethodsForRealThisTime(worker.GetType()))
					{
						if (m.ReturnType == typeof(void))
						{
							var param = m.GetParameters();

							if (config.GetValue(Key_SkipDuplicates) && ButtonAlreadyGenerated(worker, m, param, ui)) continue;

							switch (param.Length)
							{
								case 0: //could have some branching mess here. may be marginally faster
									if (m.CustomAttributes.Any((a) => (a.AttributeType == typeof(SyncMethod) && config.GetValue(Key_Action)) || (a.AttributeType.BaseType == typeof(SyncMethod) && config.GetValue(Key_SubAction))))
									{
										// check for the slot destroy buttons
										if ((m.Name == "Destroy" || m.Name == "DestroyPreservingAssets") && worker.GetType().FullName == "FrooxEngine.Slot" && config.GetValue(Key_ShowSlotDestroy) == false) break;

										LocaleString str = m.Name;

										var b = ui.Button(in str);
										b.Slot.AttachComponent<ButtonActionTrigger>().OnPressed.Target = (Action)m.CreateDelegate(typeof(Action), worker);
										count++;
									}
									break;
								case 1:
									if (config.GetValue(Key_ArgAction) && hasSyncMethod(m))
									{
										var p = param[0];
										var pt = p.ParameterType;
										if (pt == typeof(IWorldElement) || pt.GetInterfaces().Contains(typeof(IWorldElement)))
										{
											actionCallbackwitharg(true, worker, ui, m, p, pt);
											count++;
										}
										else if (Coder.IsEnginePrimitive(pt))
										{
											actionCallbackwitharg(false, worker, ui, m, p, pt);
											count++;
										}
									}
									break;
								case 2:
									if (config.GetValue(Key_Buttons) && isButtonDelegate(param) && hasSyncMethod(m))
									{
										LocaleString str = m.Name;
										var b = ui.Button(in str).Pressed.Target = (ButtonEventHandler)m.CreateDelegate(typeof(ButtonEventHandler), worker);
										count++;
									}
									break;
								case 3:
									if (config.GetValue(Key_ArgButtons) && isButtonDelegate(param) && hasSyncMethod(m))
									{
										var p = param[2];
										var pt = p.ParameterType;
										if (pt == typeof(IWorldElement) || pt.GetInterfaces().Contains(typeof(IWorldElement)))
										{
											buttonCallbackwitharg(typeof(ButtonRefRelay<>), worker, ui, m, p, pt);
											count++;
										}
										else if (Coder.IsEnginePrimitive(pt))
										{
											buttonCallbackwitharg(typeof(ButtonRelay<>), worker, ui, m, p, pt);
											count++;
										}
										else if (typeof(Delegate).IsAssignableFrom(pt))
										{
											buttonCallbackwitharg(typeof(ButtonDelegateRelay<>), worker, ui, m, p, pt);
											count++;
										}
									}
									break;
							}
						}
					}
					ui.Style.MinHeight = origHeight;
					if (count > 0)
					{
						ui.Panel();
						ui.NestOut();
					}
					if (origRect != null) ui.NestInto(origRect);
					workerUiRootSlots[worker].Remove(ui);
					if (workerUiRootSlots[worker].Count == 0) workerUiRootSlots.Remove(worker);
				});
			}
		}
		static bool hasSyncMethod(MethodInfo info) => info.CustomAttributes.Any((a) => a.AttributeType == typeof(SyncMethod) || a.AttributeType.BaseType == typeof(SyncMethod));
		static bool isButtonDelegate(ParameterInfo[] param) => (param[1].ParameterType == typeof(ButtonEventData) || param[1].ParameterType.BaseType == typeof(ButtonEventData) && param[0].ParameterType.GetInterfaces().Contains(typeof(IButton)));
		static void actionCallbackwitharg(bool isRef, Worker worker, UIBuilder ui, MethodInfo m, ParameterInfo p, Type pt)
		{
			ui.HorizontalLayout();
			LocaleString str = m.Name;
			var b = ui.Button(in str);
			var apt = typeof(Action<>).MakeGenericType(pt);
			Type t = (isRef ? typeof(CallbackRefArgument<>) : typeof(CallbackValueArgument<>)).MakeGenericType(pt);
			var c = b.Slot.AttachComponent(t);
			Type rt = typeof(SyncDelegate<>).MakeGenericType(apt);
			rt.GetProperty("Target").SetValue(t.GetField("Callback").GetValue(c), m.CreateDelegate(apt, worker));
			var cbrvn = isRef ? "Reference" : "Value";
			SyncMemberEditorBuilder.Build(c.GetSyncMember(cbrvn), p.Name, t.GetField(cbrvn), ui);
			b.Slot.AttachComponent<ButtonActionTrigger>().OnPressed.Target = (Action)t.GetMethod("Call").CreateDelegate(typeof(Action), c);
			ui.NestOut();
		}
		static void buttonCallbackwitharg(Type genType, Worker worker, UIBuilder ui, MethodInfo m, ParameterInfo p, Type pt)
		{
			ui.HorizontalLayout();
			LocaleString str = m.Name;
			var b = ui.Button(in str);
			var bpt = typeof(ButtonEventHandler<>).MakeGenericType(pt);
			Type t = genType.MakeGenericType(pt);
			var c = b.Slot.AttachComponent(t);
			Type rt = typeof(SyncDelegate<>).MakeGenericType(bpt);
			rt.GetProperty("Target").SetValue(t.GetField("ButtonPressed").GetValue(c), m.CreateDelegate(bpt, m.IsStatic ? null : worker));
			SyncMemberEditorBuilder.Build(c.GetSyncMember("Argument"), p.Name, t.GetField("Argument"), ui);
			ui.NestOut();
		}
	}
}
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using FrooxEngine.UIX;
using Elements.Core;
using System.Collections.Generic;

namespace InspectorDelegateCaller
{
	public class InspectorDelegateCaller : ResoniteMod
	{
		public override string Name => "InspectorDelegateCaller";
		public override string Author => "eia485 / Nytra";
		public override string Version => "1.3.0";
		public override string Link => "https://github.com/Nytra/ResoniteInspectorDelegateCaller";

		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_Action = new("actions", "show callable direct actions in inspectors", () => true);
		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_SubAction = new("subActions", "show callable non direct actions in inspectors, this is mainly protoflux calls", () => true);
		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_ArgAction = new("argActions", "show any action with arguments in inspectors", () => true);
		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_Buttons = new("buttons", "show callable buttons in inspectors", () => false);
		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_ArgButtons = new("argButtons", "show any button with arguments in inspectors", () => true);

		// new config key added by Nytra
		// hide the slot destroy buttons because they are easy to accidentally click and they are not undoable
		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_ShowSlotDestroy = new("showSlotDestroy", "show the slot destroy buttons in inspectors", () => false);

		[AutoRegisterConfigKey] static ModConfigurationKey<bool> Key_SkipDuplicates = new("skipDuplicates", "skip duplicates", () => true);

		static ModConfiguration config;

		//static Dictionary<Worker, Slot> workerUiSlots = new Dictionary<Worker, Slot>();
		static Dictionary<Worker, Dictionary<UIBuilder, Slot>> workerUiSlots = new Dictionary<Worker, Dictionary<UIBuilder, Slot>>();

		//static MethodInfo uiPanelMethod = AccessTools.Method(typeof(UIBuilder), "Panel", new Type[] { });

		public override void OnEngineInit()
		{
			config = GetConfiguration();
			Harmony harmony = new Harmony("net.eia485 / Nytra.InspectorDelegateCaller");
			harmony.PatchAll();
		}

		static bool ButtonAlreadyGenerated(Worker worker, MethodInfo m, ParameterInfo[] param, UIBuilder ui)
		{
			Slot s = null;
			if (workerUiSlots.ContainsKey(worker) && workerUiSlots[worker].ContainsKey(ui))
			{
				s = workerUiSlots[worker][ui];
			}
			if (s != null && m != null)
			{
				if (param.Length == 2 && isButtonDelegate(param) && hasSyncMethod(m) && s.GetComponentsInChildren<Button>().Any((Button btn) => btn.Pressed.Target != null && btn.Pressed.Target.Method.MethodHandle == m.MethodHandle))
				{
					Msg($"Button found for method {m.Name} on worker {worker.Name}");
					return true;
				}
				if (param.Length >= 2 && isButtonDelegate(param) && hasSyncMethod(m) && s.GetComponentsInChildren<ButtonRelayBase>().Any((ButtonRelayBase btnRelay) => ((ISyncDelegate)(btnRelay.GetSyncMember("ButtonPressed"))).Method.Method.MethodHandle == m.MethodHandle)) 
				{
					Msg($"ButtonRelay found for method {m.Name} on worker {worker.Name}");
					return true;
				}
				
				// In some cases the component will generate ButtonRelays with the method as an argument to another method
				// One example is AudioReverbZone
				// I could skip if these are found but it might not always be appropriate to do so

				//if (param.Length == 0 && hasSyncMethod(m) && s.GetComponentsInChildren<ButtonRelayBase>().Any((ButtonRelayBase btnRelay) => btnRelay.GetSyncMember("Argument") is ISyncDelegate syncDelegate && syncDelegate.Method.Method.MethodHandle == m.MethodHandle))
				//{
				//	Msg($"ButtonRelay found with Argument for method {m.Name} on worker {worker.Name}");
				//	return true;
				//}
			}
			return false;
		}

		//[HarmonyPatch(typeof(WorkerInspector), "BuildUIForComponent")]
		//class InspectorDelegateCallerPatch2
		//{
		//	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		//	{
		//		var codes = new List<CodeInstruction>(instructions);
		//		bool found = false;
		//		for (var i = 0; i < codes.Count; i++)
		//		{
		//			if (codes[i].Calls(uiPanelMethod))
		//			{
		//				Msg("Found panel call! Removing the following instructions:");
		//				found = true;
		//				//var instructionsToInsert = new List<CodeInstruction>
		//				//{
		//				//    new CodeInstruction(OpCodes.Ldarg_0),
		//				//    new CodeInstruction(OpCodes.Ldfld, typeof(DevToolTip).GetField("_currentGizmo", BindingFlags.NonPublic | BindingFlags.Instance)),
		//				//    new CodeInstruction(OpCodes.Ldnull),
		//				//    new CodeInstruction(OpCodes.Callvirt, typeof(SyncRef).GetMethod("set_Target", BindingFlags.Public | BindingFlags.Instance))
		//				//};
		//				//codes.InsertRange(i, instructionsToInsert);
		//				for (int j = 0; j < 5; j++)
		//				{
		//					Msg(codes[i - 1 + j].ToString());
		//				}
  //                      codes.RemoveRange(i - 1, 5);
		//				break;
		//			}
		//		}
		//		if (!found)
		//		{
		//			Msg("Did not find pattern! Something went wrong!");
		//		}
		//		return codes.AsEnumerable();
		//	}
		//}

		[HarmonyPatch(typeof(WorkerInspector), "BuildInspectorUI")]
		class InspectorDelegateCallerPatch
		{
			static void Postfix(Worker worker, UIBuilder ui)
			{
				//Slot workerInspectorSlot = ui.CurrentRect?.Slot.GetComponentInParents<WorkerInspector>()?.Slot;
				Slot currentRectSlot = ui.CurrentRect?.Slot;
				if (currentRectSlot == null)
				{
					// if the slot is null then something is very badly wrong
					Error("current rect slot is null!");
					return;
				}

				Msg($"current rect slot parent hierarchy:\n{currentRectSlot.ParentHierarchyToString()}");

				if (!workerUiSlots.ContainsKey(worker))
				{
					workerUiSlots.Add(worker, new Dictionary<UIBuilder, Slot>());
				}

				// add the uibuilder and the containing slot
				workerUiSlots[worker].Add(ui, currentRectSlot.Parent);

				worker.World.RunSynchronously(() => 
				{
					var origHeight = ui.Style.MinHeight;
					ui.Style.MinHeight = 24f;
					int count = 0;

					foreach (var m in worker.GetType().GetMethods(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
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
					}
					workerUiSlots[worker].Remove(ui);
					if (workerUiSlots[worker].Count == 0) workerUiSlots.Remove(worker);
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
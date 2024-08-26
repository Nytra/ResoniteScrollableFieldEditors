using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using System.Reflection;
using Elements.Core;
using System;
using FrooxEngine.UIX;
using FrooxEngine.ProtoFlux.CoreNodes;
using System.Globalization;

namespace ScrollableFieldEditors
{
	public class ScrollableFieldEditors : ResoniteMod
	{
		public override string Name => "ScrollableFieldEditors";
		public override string Author => "Nytra";
		public override string Version => "1.0.0";
		public override string Link => "https://github.com/Nytra/ResoniteScrollableFieldEditors";

		public static ModConfiguration Config;

		[AutoRegisterConfigKey]
		private static ModConfigurationKey<bool> MOD_ENABLED = new ModConfigurationKey<bool>("MOD_ENABLED", "Mod Enabled:", () => true);
		[AutoRegisterConfigKey]
		public static ModConfigurationKey<float> SCROLL_SPEED = new ModConfigurationKey<float>("Scroll Speed", "How fast you scroll values.", () => 1f);

		private static MethodInfo _addMethod = AccessTools.Method(typeof(ScrollableFieldEditors), nameof(Add));
		private static MethodInfo _primitiveToStringMethod = AccessTools.Method(typeof(PrimitiveMemberEditor), "PrimitiveToString");
		private static MethodInfo _getEulerValueMethod = AccessTools.Method(typeof(QuaternionMemberEditor), "GetEulerValue");

		private static MemberEditor _currentMemberEditor;
		
		public override void OnEngineInit()
		{
			Config = GetConfiguration();
			Harmony harmony = new Harmony("owo.Nytra.ScrollableFieldEditors");
			harmony.PatchAll();
		}

		private static T Add<T>(T val, float addVal)
		{
			if (Coder<T>.SupportsAddSub)
			{
				return Coder<T>.Add(val, (T)Convert.ChangeType(addVal, typeof(T)));
			}
			return val;
		}

		[HarmonyPatch(typeof(PrimitiveMemberEditor))]
		[HarmonyPatch("EditingStarted")]
		class PrimitiveMemberEditor_EditingStarted_Patch1
		{
			public static void Postfix(PrimitiveMemberEditor __instance)
			{
				_currentMemberEditor = __instance;
			}
		}

		[HarmonyPatch(typeof(PrimitiveMemberEditor))]
		[HarmonyPatch("EditingFinished")]
		class PrimitiveMemberEditor_EditingFinished_Patch
		{
			public static void Postfix(PrimitiveMemberEditor __instance)
			{
				_currentMemberEditor = null;
			}
		}

		[HarmonyPatch(typeof(QuaternionMemberEditor))]
		[HarmonyPatch("EditingStarted")]
		class QuaternionMemberEditor_EditingStarted_Patch
		{
			public static void Postfix(QuaternionMemberEditor __instance)
			{
				_currentMemberEditor = __instance;
			}
		}

		[HarmonyPatch(typeof(QuaternionMemberEditor))]
		[HarmonyPatch("EditingFinished")]
		class QuaternionMemberEditor_EditingFinished_Patch
		{
			public static void Postfix(QuaternionMemberEditor __instance)
			{
				_currentMemberEditor = null;
			}
		}

		private static void UpdateText(Text text, Type memberType, object newVal)
		{
			string newString = null;
			if (_currentMemberEditor is PrimitiveMemberEditor)
			{
				newString = (string)_primitiveToStringMethod.Invoke(_currentMemberEditor, [memberType, newVal]);
			}
			else if (_currentMemberEditor is QuaternionMemberEditor)
			{
				double3? eulerValue = null;

				if (_currentMemberEditor.GetSyncMember("_editingValue") is Sync<double3?> editingValueField)
				{
					eulerValue = editingValueField.Value;
				}

				eulerValue ??= (double3)_getEulerValueMethod.Invoke(_currentMemberEditor, []);

				TextEditor xEditor = null, yEditor = null, zEditor = null;

				if (_currentMemberEditor.GetSyncMember("_xEditor") is RelayRef<TextEditor> xEditorRef)
				{
					xEditor = xEditorRef.Target;
				}
				if (_currentMemberEditor.GetSyncMember("_yEditor") is RelayRef<TextEditor> yEditorRef)
				{
					yEditor = yEditorRef.Target;
				}
				if (_currentMemberEditor.GetSyncMember("_zEditor") is RelayRef<TextEditor> zEditorRef)
				{
					zEditor = zEditorRef.Target;
				}

				if (xEditor.IsEditing)
				{
					newString = eulerValue.Value.x.ToString("0.###", CultureInfo.InvariantCulture);
				}
				else if (yEditor.IsEditing)
				{
					newString = eulerValue.Value.y.ToString("0.###", CultureInfo.InvariantCulture);
				}
				else if (zEditor.IsEditing)
				{
					newString = eulerValue.Value.z.ToString("0.###", CultureInfo.InvariantCulture);
				}
			}
			if (newString != null)
			{
				text.Content.Value = newString;
			}
		}

		[HarmonyPatch(typeof(InteractionHandler))]
		[HarmonyPatch("OnInputUpdate")]
		class InteractionHandler_OnInputUpdate_Patch
		{
			public static void Postfix(InteractionHandler __instance)
			{
				if (Config.GetValue(MOD_ENABLED))
				{
					var activeFocus = __instance.LocalUser.GetActiveFocus();
					if (activeFocus is TextEditor textEditor && textEditor.Text.Target is Text text && _currentMemberEditor.FilterWorldElement() != null && _currentMemberEditor.World == __instance.World)
					{
						var memberType = _currentMemberEditor.Accessor?.TargetType;
						if (memberType != null && memberType.IsEnginePrimitive())
						{
							float yAxis;
							if (__instance.InputInterface.ScreenActive)
							{
								yAxis = __instance.InputInterface.Mouse.NormalizedScrollWheelDelta.Value.y;
								//Msg(__instance.InputInterface.Mouse.NormalizedScrollWheelDelta.Value.ToString());
							}
							else
							{
								yAxis = __instance.Inputs.Axis.Value.Value.y;
								//Msg(__instance.Inputs.Axis.Value.Value.ToString());
							}
							
							if (MathX.Approximately(yAxis, 0))
							{
								return;
							}

							Msg("Member type: " + memberType.Name);

							Msg("scroll Y axis: " + yAxis.ToString());

							object currentVal;
							if (_currentMemberEditor is PrimitiveMemberEditor)
							{
								currentVal = _currentMemberEditor.GetMemberValue();
							}
							else if (_currentMemberEditor is QuaternionMemberEditor)
							{
								currentVal = _getEulerValueMethod.Invoke(_currentMemberEditor, []);
							}
							else
							{
								// it should never get here because no other member editors use text editors
								return;
							}

							Msg("current val: " + currentVal.ToString());

							var amountToAdd = yAxis * Config.GetValue(SCROLL_SPEED);
							Msg("amount to add: " + amountToAdd.ToString());

							object newVal;
							if (_currentMemberEditor is PrimitiveMemberEditor)
							{
								newVal = _addMethod.MakeGenericMethod(memberType).Invoke(null, [currentVal, amountToAdd]);
								_currentMemberEditor.SetMemberValue(newVal);
							}
							else if (_currentMemberEditor is QuaternionMemberEditor)
							{

							}
							else
							{
								return;
							}

							Msg("new val: " + newVal.ToString());



							UpdateText(text, memberType, newVal);
						}
					}
				}
			}
		}
	}
}
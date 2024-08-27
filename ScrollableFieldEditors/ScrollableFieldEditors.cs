using HarmonyLib;
using ResoniteModLoader;
using FrooxEngine;
using System.Reflection;
using Elements.Core;
using System;
using FrooxEngine.UIX;
using System.Globalization;
using System.Collections.Generic;
using static Elements.Core.Number;

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
		private static ModConfigurationKey<bool> MOD_ENABLED = new ModConfigurationKey<bool>("Mod Enabled", "Should the effects of the mod be enabled.", () => true);
		[AutoRegisterConfigKey]
		private static ModConfigurationKey<float> SCROLL_SPEED_VR = new ModConfigurationKey<float>("VR Scroll Speed", "Speed of scrolling values in VR.", () => 0.1f);
		[AutoRegisterConfigKey]
		private static ModConfigurationKey<float> SCROLL_SPEED_DESKTOP = new ModConfigurationKey<float>("Desktop Scroll Speed", "Speed of scrolling values in desktop.", () => 1f);
		[AutoRegisterConfigKey]
		private static ModConfigurationKey<float> SPEED_MULT_QUATERNION = new ModConfigurationKey<float>("Quaternion Speed Multiplier", "Multiplier applied to Base Scroll Speed when editing quaternions.", () => 10f);
		[AutoRegisterConfigKey]
		private static ModConfigurationKey<float> SPEED_MULT_INTEGER = new ModConfigurationKey<float>("Integer Speed Multiplier", "Multiplier applied to Base Scroll Speed when editing integers.", () => 0.75f);
		[AutoRegisterConfigKey]
		private static ModConfigurationKey<bool> DEBUG_LOGGING = new ModConfigurationKey<bool>("Enable Debug Logging", "Enables debug logging (Warning: very spammy when value scrolling!)", () => false);

		private static MethodInfo _addMethod = AccessTools.Method(typeof(ScrollableFieldEditors), nameof(Add));
		private static MethodInfo _subMethod = AccessTools.Method(typeof(ScrollableFieldEditors), nameof(Sub));
		private static MethodInfo _primitiveToStringMethod = AccessTools.Method(typeof(PrimitiveMemberEditor), "PrimitiveToString");
		private static MethodInfo _getEulerValueMethod = AccessTools.Method(typeof(QuaternionMemberEditor), "GetEulerValue");

		private static Dictionary<Type, MethodInfo> _typedAddMethodCache = new();
		private static Dictionary<Type, MethodInfo> _typedSubMethodCache = new();

		private static MemberEditor _currentMemberEditor;
		private static bool _blockScroll = false;
		private static TouchSource _lastTouchSource = null;

		private enum QuaternionField
		{
			X,
			Y,
			Z
		}
		
		public override void OnEngineInit()
		{
			Config = GetConfiguration();
			Harmony harmony = new Harmony("owo.Nytra.ScrollableFieldEditors");
			harmony.PatchAll();
		}

		private static void ExtraDebug(string str)
		{
			if (Config.GetValue(DEBUG_LOGGING))
			{
				Debug(str);
			}
		}

		private static T Add<T>(T val, T addVal)
		{
			if (Coder<T>.SupportsAddSub)
			{
				return Coder<T>.Add(val, addVal);
			}
			return val;
		}

		private static T Sub<T>(T val, T subVal)
		{
			if (Coder<T>.SupportsAddSub)
			{
				return Coder<T>.Sub(val, subVal);
			}
			return val;
		}

		private static QuaternionField? GetEditingField()
		{
			if (_currentMemberEditor.GetSyncMember("_xEditor") is RelayRef<TextEditor> xEditorRef)
			{
				if (xEditorRef.Target != null && xEditorRef.Target.IsEditing)
				{
					return QuaternionField.X;
				}
			}
			if (_currentMemberEditor.GetSyncMember("_yEditor") is RelayRef<TextEditor> yEditorRef)
			{
				if (yEditorRef.Target != null && yEditorRef.Target.IsEditing)
				{
					return QuaternionField.Y;
				}
			}
			if (_currentMemberEditor.GetSyncMember("_zEditor") is RelayRef<TextEditor> zEditorRef)
			{
				if (zEditorRef.Target != null && zEditorRef.Target.IsEditing)
				{
					return QuaternionField.Z;
				}
			}
			return null;
		}

		private static double3 GetEulerValue()
		{
			double3? eulerValue = null;

			if (_currentMemberEditor.GetSyncMember("_editingValue") is Sync<double3?> editingValueField)
			{
				eulerValue = editingValueField.Value;
			}

			eulerValue ??= (double3)_getEulerValueMethod.Invoke(_currentMemberEditor, []);

			return eulerValue.Value;
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
				double3 eulerValue = GetEulerValue();

				double? val = null;
				switch (GetEditingField())
				{
					case QuaternionField.X:
						val = eulerValue.x;
						break;
					case QuaternionField.Y:
						val = eulerValue.y;
						break;
					case QuaternionField.Z:
						val = eulerValue.z;
						break;
					default:
						break;
				}

				newString = val?.ToString("0.###", CultureInfo.InvariantCulture);
			}
			if (newString != null)
			{
				text.Content.Value = newString;
			}
		}

		private static object GetCurrentMemberValue()
		{
			object currentVal;
			if (_currentMemberEditor is PrimitiveMemberEditor)
			{
				currentVal = _currentMemberEditor.GetMemberValue();
			}
			else if (_currentMemberEditor is QuaternionMemberEditor)
			{
				var eulerValue = GetEulerValue();
				currentVal = doubleQ.Euler(eulerValue.x, eulerValue.y, eulerValue.z);
			}
			else
			{
				// it should never get here because no other member editors use text editors
				return null;
			}
			return currentVal;
		}

		private static MethodInfo GetTypedAddMethod(Type type)
		{
			if (_typedAddMethodCache.ContainsKey(type))
			{
				return _typedAddMethodCache[type];
			}
			var method = _addMethod.MakeGenericMethod(type);
			_typedAddMethodCache.Add(type, method);
			return method;
		}

		private static MethodInfo GetTypedSubMethod(Type type)
		{
			if (_typedSubMethodCache.ContainsKey(type))
			{
				return _typedSubMethodCache[type];
			}
			var method = _subMethod.MakeGenericMethod(type);
			_typedSubMethodCache.Add(type, method);
			return method;
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

		[HarmonyPatch(typeof(PointerInteractionController))]
		[HarmonyPatch("UpdatePointer")]
		class PointerInteractionController_UpdatePointer_Patch
		{
			// blocks desktop mode scrolling UIX in userspace
			public static bool Prefix(PointerInteractionController __instance, FrooxEngine.Pointer pointer, ref float2 axisDelta)
			{
				if (Config.GetValue(MOD_ENABLED))
				{
					if (_currentMemberEditor.FilterWorldElement() != null && _currentMemberEditor.World.IsUserspace() && __instance.LocalUser.GetActiveFocus() != null)
					{
						axisDelta = float2.Zero;
					}
				}

				return true;
			}
		}

		[HarmonyPatch(typeof(Button))]
		[HarmonyPatch("OnPressBegin")]
		class Button_OnPressBegin_Patch
		{
			// Store the touch source when a button connected to an IFocusable is pressed
			public static void Postfix(Button __instance, Canvas.InteractionData eventData)
			{
				if (Config.GetValue(MOD_ENABLED))
				{
					ExtraDebug($"Touch source event from: {eventData.source.Name} {eventData.source.ReferenceID}");
					var activeFocus = __instance.LocalUser.GetActiveFocus();
					if (activeFocus != null)
					{
						_lastTouchSource = eventData.source;
					}
				}
			}
		}

		[HarmonyPatch(typeof(InteractionHandler))]
		[HarmonyPatch("OnInputUpdate")]
		class InteractionHandler_OnInputUpdate_Patch
		{
			public static bool Prefix(InteractionHandler __instance)
			{
				if (Config.GetValue(MOD_ENABLED))
				{
					if (_blockScroll)
					{
						// blocks desktop mode scrolling UIX in worldspace
						__instance.Inputs.TouchAxis.Value.ValueRef.Value = float2.Zero;
					}
				}
				return true;
			}
		}

		[HarmonyPatch(typeof(InteractionHandler))]
		[HarmonyPatch("BeforeInputUpdate")]
		[HarmonyAfter("owo.Nytra.NoTankControls", "U-xyla.XyMod")]
		class InteractionHandler_BeforeInputUpdate_Patch
		{
			public static void Postfix(InteractionHandler __instance)
			{
				if (Config.GetValue(MOD_ENABLED))
				{
					_blockScroll = false;
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
							}
							else
							{
								yAxis = __instance.Inputs.Axis.Value.Value.y;
							}

							if (MathX.Approximately(yAxis, 0))
							{
								return;
							}

							_blockScroll = true;

							bool correctSide = false;
							if (_lastTouchSource is RelayTouchSource relayTouchSource)
							{
								if (relayTouchSource.Slot.GetComponent<InteractionLaser>() is InteractionLaser interactionLaser)
								{
									if (__instance.Laser == interactionLaser)
									{
										correctSide = true;
									}
								}
								// Only update for one interaction handler if using pointer interaction controller
								else if (relayTouchSource.Slot.GetComponent<PointerInteractionController>() is not null && __instance.Side.Value == Chirality.Right)
								{
									correctSide = true;
								}
							}

							if (!correctSide)
							{
								ExtraDebug("Interaction laser is not the correct side");
								return;
							}

							// block thumbstick move/rotate while value scrolling on non-Index controllers
							if (__instance.InputInterface.GetControllerNode(__instance.Side).GetType() != typeof(IndexController) && !__instance.InputInterface.ScreenActive)
							{
								// sorry NoTankControls...
								__instance.Inputs.Axis.RegisterBlocks = true;
							}

							ExtraDebug("Member type: " + memberType.Name);

							ExtraDebug("scroll Y axis: " + yAxis.ToString());

							object currentVal = GetCurrentMemberValue();

							ExtraDebug("current val: " + currentVal.ToString());

							float amountToAdd = 0;
							if (__instance.InputInterface.ScreenActive)
							{
								amountToAdd = yAxis * Config.GetValue(SCROLL_SPEED_DESKTOP);
							}
							else
							{
								amountToAdd = yAxis * Config.GetValue(SCROLL_SPEED_VR);
							}

							if (memberType.IsInteger())
							{
								amountToAdd *= Config.GetValue(SPEED_MULT_INTEGER);
								if (amountToAdd > 0)
								{
									amountToAdd = MathX.Ceil(amountToAdd);
								}
								else if (amountToAdd < 0)
								{
									amountToAdd = MathX.Floor(amountToAdd);
								}
							}
							else if (memberType == typeof(floatQ) ||  memberType == typeof(doubleQ))
							{
								amountToAdd *= Config.GetValue(SPEED_MULT_QUATERNION);	
							}

							ExtraDebug("amount to add: " + amountToAdd.ToString());

							object newVal = null;
							if (_currentMemberEditor is PrimitiveMemberEditor)
							{
								if (amountToAdd < 0)
								{
									newVal = GetTypedSubMethod(memberType).Invoke(null, [currentVal, Convert.ChangeType(Math.Abs(amountToAdd), memberType)]);
								}
								else
								{
									newVal = GetTypedAddMethod(memberType).Invoke(null, [currentVal, Convert.ChangeType(amountToAdd, memberType)]);
								}
								_currentMemberEditor.SetMemberValue(newVal);
							}
							else if (_currentMemberEditor is QuaternionMemberEditor)
							{
								var eulerValue = GetEulerValue();
								double x = eulerValue.x;
								double y = eulerValue.y;
								double z = eulerValue.z;

								switch (GetEditingField())
								{
									case QuaternionField.X:
										x += amountToAdd;
										break;
									case QuaternionField.Y:
										y += amountToAdd;
										break;
									case QuaternionField.Z:
										z += amountToAdd;
										break;
									default:
										break;
								}

								var doubleQuat = doubleQ.Euler(x, y, z);

								if (!doubleQuat.IsNaN)
								{
									if (memberType == typeof(floatQ))
									{
										newVal = (floatQ)doubleQuat;
									}
									else if (memberType == typeof(doubleQ))
									{
										newVal = doubleQuat;
									}

									_currentMemberEditor.SetMemberValue(newVal);

									if (_currentMemberEditor.GetSyncMember("_editingValue") is Sync<double3?> editingValueField)
									{
										if (editingValueField.Value.HasValue)
										{
											editingValueField.Value = new double3(x, y, z);
										}
									}
								}
							}
							else
							{
								return;
							}

							if (newVal != null)
							{
								ExtraDebug("new val: " + newVal.ToString());
								UpdateText(text, memberType, newVal);
							}
						}
					}
				}
			}
		}
	}
}
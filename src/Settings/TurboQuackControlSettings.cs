using System.Collections.Generic;
using System.Reflection;
using DuckGame;
using HarmonyLib;
using qUAckzak.Mod.Modes;

namespace qUAckzak.Mod.Settings
{
    [HarmonyPatch(
        typeof(UIControlConfig),
        MethodType.Constructor,
        new[]
        {
            typeof(UIMenu),
            typeof(string),
            typeof(float),
            typeof(float),
            typeof(float),
            typeof(float),
            typeof(string),
            typeof(InputProfile)
        })]
    internal static class TurboQuackControlSettings
    {
        private static readonly FieldInfo ControlBoxField =
            AccessTools.Field(typeof(UIControlConfig), "_controlBox");

        private static readonly FieldInfo ControlElementsField =
            AccessTools.Field(typeof(UIControlConfig), "_controlElements");

        [HarmonyPostfix]
        private static void AddTurboQuackControl(UIControlConfig __instance)
        {
            UIBox controlBox = (UIBox)ControlBoxField.GetValue(__instance);
            List<UIControlElement> controlElements =
                (List<UIControlElement>)ControlElementsField.GetValue(__instance);

            if (controlBox is null || controlElements is null ||
                controlElements.Exists(control => control._trigger == TurboQuackMode.ToggleTrigger))
            {
                return;
            }

            UIControlElement ragdollControl =
                controlElements.Find(control => control._trigger == Triggers.Ragdoll);
            int controlIndex = ragdollControl is null
                ? controlElements.Count
                : controlElements.IndexOf(ragdollControl) + 1;
            int boxIndex = ragdollControl is null
                ? controlBox.components.Count
                : controlBox.components.IndexOf(ragdollControl) + 1;

            UIControlElement turboQuackControl = new(
                "|LIME|TURBO QUACK",
                TurboQuackMode.ToggleTrigger,
                new DeviceInputMapping(),
                field: new FieldBinding(Options.Data, "sfxVolume"));

            controlElements.Insert(controlIndex, turboQuackControl);
            controlBox.Insert(turboQuackControl, boxIndex, true);
        }
    }
}

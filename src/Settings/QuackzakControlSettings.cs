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
    internal static class QuackzakControlSettings
    {
        private static readonly FieldInfo ControlBoxField =
            AccessTools.Field(typeof(UIControlConfig), "_controlBox");

        private static readonly FieldInfo ControlElementsField =
            AccessTools.Field(typeof(UIControlConfig), "_controlElements");

        [HarmonyPostfix]
        private static void AddQuackzakControls(UIControlConfig __instance)
        {
            UIBox controlBox = (UIBox)ControlBoxField.GetValue(__instance);
            List<UIControlElement> controlElements =
                (List<UIControlElement>)ControlElementsField.GetValue(__instance);

            if (controlBox is null || controlElements is null)
            {
                return;
            }

            AddControl(
                controlBox,
                controlElements,
                "|LIME|TURBO QUACK",
                TurboQuackMode.ToggleTrigger,
                Triggers.Ragdoll);
            AddControl(
                controlBox,
                controlElements,
                "|LIME|KONUAMI",
                KonuamiMode.Trigger,
                TurboQuackMode.ToggleTrigger);
        }

        private static void AddControl(
            UIBox controlBox,
            List<UIControlElement> controlElements,
            string label,
            string trigger,
            string precedingTrigger)
        {
            if (controlElements.Exists(control => control._trigger == trigger))
            {
                return;
            }

            UIControlElement precedingControl =
                controlElements.Find(control => control._trigger == precedingTrigger);
            int controlIndex = precedingControl is null
                ? controlElements.Count
                : controlElements.IndexOf(precedingControl) + 1;
            int boxIndex = precedingControl is null
                ? controlBox.components.Count
                : controlBox.components.IndexOf(precedingControl) + 1;

            UIControlElement control = new(
                label,
                trigger,
                new DeviceInputMapping(),
                field: new FieldBinding(Options.Data, "sfxVolume"));

            controlElements.Insert(controlIndex, control);
            controlBox.Insert(control, boxIndex, true);
        }
    }
}

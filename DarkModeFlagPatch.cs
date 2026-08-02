using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CompanionStart
{
    // Baking a recolored copy of the flag texture (via Graphics.Blit or Graphics.CopyTexture)
    // turned out unreliable - the source flag textures are GPU-compressed (DXT5/BC3), and
    // CopyTexture doesn't do format conversion, so a plain uncompressed destination silently
    // failed to receive any data (confirmed via Log.log: "Graphics.CopyTexture called with
    // incompatible formats"). Tinting the UI Image that renders the flag instead sidesteps GPU
    // texture manipulation entirely - it's a per-instance rendering property Unity applies
    // normally regardless of the source texture's format or render pipeline.
    [HarmonyPatch(typeof(SelectTribe), nameof(SelectTribe.Run))]
    internal static class DarkModeFlagPatch
    {
        private static readonly Color DarkTint = new Color(0.5f, 0.5f, 0.65f, 1f);
        private static readonly FieldInfo BaseColourField = typeof(ButtonAnimator).GetField("baseColour", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo BaseColourSetField = typeof(ButtonAnimator).GetField("baseColourSet", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void Postfix(SelectTribe __instance)
        {
            Traverse instance = Traverse.Create(__instance);
            List<ClassData> tribes = instance.Field("tribes").GetValue<List<ClassData>>();
            Transform tribeFlagGroup = instance.Field("tribeFlagGroup").GetValue<Transform>();

            for (int i = 0; i < tribes.Count && i < tribeFlagGroup.childCount; i++)
            {
                if (!tribes[i].name.StartsWith(CompanionStart.NamePrefix))
                {
                    continue;
                }

                Transform flagTransform = tribeFlagGroup.GetChild(i);
                TribeFlagDisplay display = flagTransform.GetComponent<TribeFlagDisplay>();
                if (display == null || display.flagImage == null)
                {
                    continue;
                }

                display.flagImage.color = DarkTint;

                // ButtonAnimator caches whatever color the image had the first time it hovers
                // into a private baseColour field, then restores that cached value on unhover -
                // which happens the instant the flag is instantiated (before this postfix runs),
                // so it was caching the original un-tinted color and reverting to it on unhover.
                // Force the cache to our tint, and tint its highlight color too so hovering
                // doesn't flash back to near-white.
                ButtonAnimator buttonAnimator = flagTransform.GetComponentInChildren<ButtonAnimator>();
                if (buttonAnimator != null)
                {
                    BaseColourField.SetValue(buttonAnimator, DarkTint);
                    BaseColourSetField.SetValue(buttonAnimator, true);
                    buttonAnimator.highlightColour = new Color(
                        buttonAnimator.highlightColour.r * DarkTint.r,
                        buttonAnimator.highlightColour.g * DarkTint.g,
                        buttonAnimator.highlightColour.b * DarkTint.b,
                        buttonAnimator.highlightColour.a);
                }
            }
        }
    }
}

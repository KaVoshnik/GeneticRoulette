using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GeneticRoulette
{
    /// <summary>
    /// Статусы-«мудлеты» болезней в той же панели, что и родные индикаторы игры.
    /// Используются готовые иконки игры (MoodleManager.icons) и готовый MoodleManager.AddMoodle.
    /// </summary>
    internal static class DiseaseMoodles
    {
        static readonly HashSet<string> missingLogged = new HashSet<string>();

        static string Pick(MoodleManager mm, string[] candidates)
        {
            if (mm.icons == null) return null;
            foreach (string k in candidates)
                if (mm.icons.ContainsKey(k)) return k;
            return null;
        }

        static void AddOne(MoodleManager mm, MoodleSpec s)
        {
            string icon = Pick(mm, s.Icons);
            if (icon == null)
            {
                string key = string.Join("/", s.Icons);
                if (missingLogged.Add(key)) Plugin.Log.LogWarning("No moodle icon found among: " + key);
                return;
            }
            mm.AddMoodle(s.Bg, icon, s.Name, s.Desc, s.Flash, false);
        }

        internal static void Add(MoodleManager mm)
        {
            bool ru = Plugin.Ru();
            foreach (DiseaseDef def in Diseases.All)
            {
                if (def.Moodle == null || !Plugin.Has(def.Id)) continue;
                MoodleSpec spec = def.Moodle(ru);
                if (spec == null) continue;
                string perk = def.Perk(ru);
                if (!string.IsNullOrEmpty(perk)) spec.Desc += " " + (ru ? "Плюс: " : "Perk: ") + perk;
                AddOne(mm, spec);
            }
        }
    }

    [HarmonyPatch(typeof(MoodleManager), "AddAllMoodles")]
    static class Patch_AddAllMoodles
    {
        static void Postfix(MoodleManager __instance)
        {
            try
            {
                if (Plugin.Active.Count == 0) return;
                // как в самой игре: у мёртвого игрока и во время катсцен статусов нет
                PlayerCamera cam = PlayerCamera.main;
                if (cam == null || cam.body == null || !cam.body.alive) return;
                if (WorldGeneration.world != null && WorldGeneration.world.cutsceneActive) return;
                if (MoodleManager.main != null && MoodleManager.main != __instance) return;
                // К этому моменту игра уже включила режим "боковых" статусов (они видны только при наведении).
                // Болезни должны быть видны всегда, поэтому добавляем их как основные.
                bool prevSide = __instance.sideMoodles;
                __instance.sideMoodles = false;
                try { DiseaseMoodles.Add(__instance); }
                finally { __instance.sideMoodles = prevSide; }
            }
            catch (Exception e) { Plugin.Log.LogError("Moodles failed: " + e); }
        }
    }
}
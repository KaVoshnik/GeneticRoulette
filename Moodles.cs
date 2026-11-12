using System;
using System.Collections.Generic;
using System.Globalization;
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

        static string Num(float v)
        {
            return v.ToString("0.#", CultureInfo.InvariantCulture);
        }

        static string Pick(MoodleManager mm, string[] candidates)
        {
            if (mm.icons == null) return null;
            foreach (string k in candidates)
                if (mm.icons.ContainsKey(k)) return k;
            return null;
        }

        static void AddOne(MoodleManager mm, int bg, string name, string desc, bool flash, params string[] iconCandidates)
        {
            string icon = Pick(mm, iconCandidates);
            if (icon == null)
            {
                string key = string.Join("/", iconCandidates);
                if (missingLogged.Add(key)) Plugin.Log.LogWarning("No moodle icon found among: " + key);
                return;
            }
            mm.AddMoodle(bg, icon, name, desc, flash, false);
        }

        internal static void Add(MoodleManager mm)
        {
            bool ru = Plugin.Ru();

            if (Plugin.Has(Dz.Hiv))
            {
                AddOne(mm, 1,
                    ru ? "ВИЧ" : "HIV",
                    ru ? "Хроническая иммунная недостаточность. Шанс заражения ран ×" + Num(Plugin.CfgHivInfection.Value) + "."
                       : "Chronic immunodeficiency. Wound infection chance x" + Num(Plugin.CfgHivInfection.Value) + ".",
                    false, "lowimmunity", "highimmunity", "infected");
            }

            if (Plugin.Has(Dz.Hemophilia))
            {
                AddOne(mm, 1,
                    ru ? "Гемофилия" : "Hemophilia",
                    ru ? "Кровь плохо свёртывается: кровотечения сильнее и останавливаются дольше."
                       : "Blood barely clots: bleeding is heavier and lasts longer.",
                    false, "heavybleeding", "bleeding", "smallbleeding");
            }

            if (Plugin.Has(Dz.Asthma))
            {
                if (Plugin.AsthmaAttack)
                {
                    AddOne(mm, 2,
                        ru ? "Приступ астмы" : "Asthma attack",
                        ru ? "Не хватает воздуха: падают кислород и выносливость."
                           : "You can't get enough air: oxygen and stamina drop.",
                        true, "cantbreathe", "hypoventilation", "oxygen");
                }
                else
                {
                    AddOne(mm, 1,
                        ru ? "Астма" : "Asthma",
                        ru ? "Время от времени накатывают приступы удушья."
                           : "Sudden attacks of suffocation come from time to time.",
                        false, "hypoventilation", "cantbreathe", "oxygen");
                }
            }

            if (Plugin.Has(Dz.Deafness))
            {
                AddOne(mm, 1,
                    ru ? "Тугоухость" : "Hearing loss",
                    ru ? "Постоянная потеря слуха." : "Permanent hearing loss.",
                    false, "hearingloss");
            }

            if (Plugin.Has(Dz.Narcolepsy))
            {
                if (Plugin.NarcWarning)
                {
                    AddOne(mm, 2,
                        ru ? "Клонит в сон" : "Drifting off",
                        ru ? "Вы вот-вот отключитесь. Найдите безопасное место."
                           : "You are about to pass out. Find a safe spot.",
                        true, "tired", "asleep", "badsleep");
                }
                else
                {
                    AddOne(mm, 1,
                        ru ? "Нарколепсия" : "Narcolepsy",
                        ru ? "Внезапные приступы сна. Предметы из рук выпадают."
                           : "Sudden sleep attacks. You drop what you hold.",
                        false, "badsleep", "tired", "asleep");
                }
            }

            if (Plugin.Has(Dz.FragileBones))
            {
                AddOne(mm, 1,
                    ru ? "Хрупкие кости" : "Brittle bones",
                    ru ? "Падения и удары ранят и ломают сильнее (×" + Num(Plugin.CfgBones.Value) + ")."
                       : "Falls and impacts injure and break more (x" + Num(Plugin.CfgBones.Value) + ").",
                    false, "brokenbone", "dislocation");
            }

            if (Plugin.Has(Dz.Deuteranopia))
            {
                AddOne(mm, 0,
                    ru ? "Дальтонизм" : "Color blindness",
                    ru ? "Красный и зелёный сливаются." : "Red and green blur together.",
                    false, "rippedeyes", "rippedeye", "confused", "focused");
            }

            if (Plugin.Has(Dz.Achromatopsia))
            {
                AddOne(mm, 1,
                    ru ? "Ахроматопсия" : "Achromatopsia",
                    ru ? "Весь мир в оттенках серого." : "The whole world is grayscale.",
                    false, "rippedeyes", "rippedeye", "confused", "focused");
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
                if (MoodleManager.main != null && MoodleManager.main != __instance) return;
                DiseaseMoodles.Add(__instance);
            }
            catch (Exception e) { Plugin.Log.LogError("Moodles failed: " + e); }
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimSharp.Services.ModManagement;

namespace RimSharp.Features.Saves.ViewModels
{
    /// <summary>
    /// One colonist of the selected save: the name, the gender, the
    /// xenotype and the compact lines the save carries for that pawn. A line
    /// that carries nothing is left out - a colonist without traits has no
    /// traits line.
    /// </summary>
    public sealed class SaveColonistItemViewModel
    {
        public SaveColonistItemViewModel(SaveColonist colonist)
        {
            ArgumentNullException.ThrowIfNull(colonist);

            NameText = colonist.DisplayName;
            GenderText = FormatGender(colonist.Gender);
            XenotypeText = FormatXenotype(colonist.Xenotype);
            AgeText = FormatAge(colonist);
            TraitsText = FormatTraits(colonist.Traits);
            SkillsText = FormatSkills(colonist.Skills);
            ConditionsText = FormatConditions(colonist.Conditions);
        }

        /// <summary>The name of the colonist: "Hayden "Hayhouse" Simons".</summary>
        public string NameText { get; }

        /// <summary>The gender line - empty only when the record carries none.</summary>
        public string GenderText { get; }

        /// <summary>True when there is a gender line.</summary>
        public bool HasGender => GenderText.Length > 0;

        /// <summary>The xenotype line - empty when the save carries none.</summary>
        public string XenotypeText { get; }

        /// <summary>True when there is a xenotype line.</summary>
        public bool HasXenotype => XenotypeText.Length > 0;

        /// <summary>The age line - empty when the save carries no age.</summary>
        public string AgeText { get; }

        /// <summary>True when there is an age line.</summary>
        public bool HasAge => AgeText.Length > 0;

        /// <summary>The traits line - empty when the save carries no vanilla trait.</summary>
        public string TraitsText { get; }

        /// <summary>True when there is a traits line.</summary>
        public bool HasTraits => TraitsText.Length > 0;

        /// <summary>The skills line - empty when the save carries no vanilla skill.</summary>
        public string SkillsText { get; }

        /// <summary>True when there is a skills line.</summary>
        public bool HasSkills => SkillsText.Length > 0;

        /// <summary>The conditions line - empty when the save carries no vanilla condition.</summary>
        public string ConditionsText { get; }

        /// <summary>True when there is a conditions line.</summary>
        public bool HasConditions => ConditionsText.Length > 0;

        /// <summary>
        /// The gender line. The value is shown exactly as the save records it
        /// ("Male" or "Female" for a human pawn) - a human always has one: the
        /// scribe omits the element when the value equals its default, and the
        /// default of a human is Male.
        /// </summary>
        private static string FormatGender(string? gender)
            => string.IsNullOrWhiteSpace(gender) ? string.Empty : $"Gender: {gender.Trim()}";

        /// <summary>
        /// The xenotype line. It appears only when the save records a
        /// xenotype - a save without Biotech content carries none.
        /// </summary>
        private static string FormatXenotype(string? xenotype)
            => string.IsNullOrWhiteSpace(xenotype) ? string.Empty : $"Xenotype: {xenotype.Trim()}";

        /// <summary>
        /// The age line. The game shows the BIOLOGICAL age (the age of the
        /// body) everywhere a player can read it - the chronological age (the
        /// time that existed) is internal to the game and is not shown.
        /// The full years are the integer division of the ticks by the ticks of
        /// a year, which TRUNCATES towards zero (Pawn_AgeTracker.
        /// AgeBiologicalYears). The fraction is dropped, never rounded up.
        /// </summary>
        private static string FormatAge(SaveColonist colonist)
            => colonist.AgeBiologicalYears.HasValue
                ? $"Age: {FormatYears(colonist.AgeBiologicalYears.Value)}"
                : string.Empty;

        /// <summary>The full years of an age - the fraction is dropped.</summary>
        private static string FormatYears(double years)
            => ((long)Math.Floor(years)).ToString("0", CultureInfo.CurrentCulture);

        /// <summary>The traits line - the def names as the save records them.</summary>
        private static string FormatTraits(IReadOnlyList<string> traits)
            => traits.Count == 0 ? string.Empty : $"Traits: {string.Join(", ", traits)}";

        /// <summary>
        /// The skills line: the def, the level and the passion. The passion is
        /// marked as recorded: "+" for a minor passion, "++" for a major one.
        /// </summary>
        private static string FormatSkills(IReadOnlyList<SaveColonistSkill> skills)
        {
            if (skills.Count == 0)
            {
                return string.Empty;
            }

            var tokens = new List<string>(skills.Count);
            foreach (SaveColonistSkill skill in skills)
            {
                tokens.Add($"{skill.Def} {skill.Level}{FormatPassion(skill.Passion)}");
            }

            return $"Skills: {string.Join(", ", tokens)}";
        }

        /// <summary>The passion marker - nothing, "+" or "++".</summary>
        private static string FormatPassion(string? passion)
        {
            if (string.IsNullOrWhiteSpace(passion))
            {
                return string.Empty;
            }

            return passion.Trim().ToLowerInvariant() switch
            {
                "minor" => "+",
                "major" => "++",
                _ => string.Empty
            };
        }

        /// <summary>
        /// The conditions line. The severity is shown in the unit of its class:
        /// an injury counts in points, the other conditions are a fraction that
        /// is shown as a percentage.
        /// </summary>
        private static string FormatConditions(IReadOnlyList<SaveColonistCondition> conditions)
        {
            if (conditions.Count == 0)
            {
                return string.Empty;
            }

            var tokens = new List<string>(conditions.Count);
            foreach (SaveColonistCondition condition in conditions)
            {
                tokens.Add($"{condition.Def} ({FormatSeverity(condition)})");
            }

            return $"Conditions: {string.Join(", ", tokens)}";
        }

        /// <summary>
        /// The severity of a condition. An injury ("Hediff_Injury") counts in
        /// points, the other classes are a 0..1 fraction and are shown as a
        /// percentage. A class the save does not name falls back to the value:
        /// a value of 1 or more is points, below that it is a fraction.
        /// </summary>
        private static string FormatSeverity(SaveColonistCondition condition)
        {
            if (!condition.Severity.HasValue)
            {
                return condition.PainCategory is null
                    ? "severity unknown"
                    : $"severity unknown, {condition.PainCategory}";
            }

            double severity = condition.Severity.Value;
            bool points = string.Equals(condition.Class, "Hediff_Injury", StringComparison.Ordinal)
                || (string.IsNullOrWhiteSpace(condition.Class) ? severity >= 1d : false);

            string severityText = points
                ? $"severity {Math.Round(severity, MidpointRounding.ToEven).ToString("0", CultureInfo.CurrentCulture)}"
                : $"{(severity * 100d).ToString("0.#", CultureInfo.CurrentCulture)}%";

            return condition.PainCategory is null
                ? severityText
                : $"{severityText}, {condition.PainCategory}";
        }
    }
}

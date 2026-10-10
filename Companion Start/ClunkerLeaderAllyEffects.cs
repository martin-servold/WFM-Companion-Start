using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CompanionStart
{
    // Cards that buff "allied Clunkers" (Mama Tinkerson's Frenzy, Alloy's buff) pick their targets
    // through a TargetConstraintIsCardType whose allowedTypes lists the "Clunker" CardType - an exact
    // reference match, so our ClunkerLeader clone of that type never passes it. Rather than loosening
    // the check for every clunker-typed constraint in the game, walk only these cards' effects and add
    // the clunker leader type next to "Clunker" in the constraints they reach. The constraints are
    // shared assets, so the original arrays are kept and put back on unload.
    internal static class ClunkerLeaderAllyEffects
    {
        internal static readonly string[] SourceCardNames = { "MamaTinkerson", "Turmeep" };

        private static readonly FieldInfo AllowedTypesField =
            typeof(TargetConstraintIsCardType).GetField("allowedTypes", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly Dictionary<TargetConstraintIsCardType, CardType[]> OriginalAllowedTypes =
            new Dictionary<TargetConstraintIsCardType, CardType[]>();

        internal static void Apply(IEnumerable<CardData> sourceCards, CardType clunkerLeaderType)
        {
            foreach (CardData card in sourceCards.Where(card => card != null))
            {
                var visited = new HashSet<UnityEngine.Object>();
                var constraints = new List<TargetConstraintIsCardType>();
                foreach (CardData.StatusEffectStacks stacks in card.startWithEffects ?? new CardData.StatusEffectStacks[0])
                {
                    Collect(stacks?.data, visited, constraints);
                }

                int patched = 0;
                foreach (TargetConstraintIsCardType constraint in constraints)
                {
                    var allowedTypes = (CardType[])AllowedTypesField.GetValue(constraint);
                    if (allowedTypes == null
                        || !allowedTypes.Any(type => type != null && type.name == "Clunker")
                        || allowedTypes.Contains(clunkerLeaderType))
                    {
                        continue;
                    }

                    if (!OriginalAllowedTypes.ContainsKey(constraint))
                    {
                        OriginalAllowedTypes[constraint] = allowedTypes;
                    }

                    AllowedTypesField.SetValue(constraint, allowedTypes.Concat(new[] { clunkerLeaderType }).ToArray());
                    patched++;
                }

                if (patched > 0)
                {
                    Debug.Log($"CompanionStart: {card.name}'s clunker-targeting effects now include clunker leaders ({patched} constraint(s))");
                }
                else
                {
                    Debug.LogWarning($"CompanionStart: found no clunker card-type constraint on {card.name} - its effect won't reach clunker leaders");
                }
            }
        }

        internal static void Restore()
        {
            foreach (KeyValuePair<TargetConstraintIsCardType, CardType[]> entry in OriginalAllowedTypes)
            {
                if (entry.Key != null)
                {
                    AllowedTypesField.SetValue(entry.Key, entry.Value);
                }
            }

            OriginalAllowedTypes.Clear();
        }

        // Effects nest (e.g. a WhileActive effect wrapping the Frenzy it grants, or And/Or constraints
        // wrapping others) under different field names per class, so follow every field holding a
        // StatusEffectData or TargetConstraint, singly or in an array, instead of naming each one.
        private static void Collect(UnityEngine.Object node, HashSet<UnityEngine.Object> visited, List<TargetConstraintIsCardType> found)
        {
            if (node == null || !visited.Add(node))
            {
                return;
            }

            if (node is TargetConstraintIsCardType cardTypeConstraint)
            {
                found.Add(cardTypeConstraint);
            }

            for (Type type = node.GetType(); type != null && type != typeof(ScriptableObject); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (IsWalkable(field.FieldType))
                    {
                        Collect(field.GetValue(node) as UnityEngine.Object, visited, found);
                    }
                    else if (field.FieldType.IsArray && IsWalkable(field.FieldType.GetElementType())
                        && field.GetValue(node) is Array array)
                    {
                        foreach (object element in array)
                        {
                            Collect(element as UnityEngine.Object, visited, found);
                        }
                    }
                }
            }
        }

        private static bool IsWalkable(Type type) =>
            typeof(StatusEffectData).IsAssignableFrom(type) || typeof(TargetConstraint).IsAssignableFrom(type);
    }
}

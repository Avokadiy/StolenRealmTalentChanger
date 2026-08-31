using BepInEx;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using Mono.Cecil.Cil;
using CodeInstruction = HarmonyLib.CodeInstruction;
using OpCodes = System.Reflection.Emit.OpCodes;

namespace FreeTalents
{
    [BepInPlugin("com.stolenrealm.freetalents", "StolenRealm Free Talents", "1.0.0")]
    public class FreeTalentsPlugin : BaseUnityPlugin
    {
        private void Awake()
        {
            var harmony = new Harmony("com.stolenrealm.freetalents");
            
            // Патчим все методы в текущем классе и вложенных классах
            harmony.PatchAll(typeof(FreeTalentsPlugin));
            
            Logger.LogMessage("FreeTalents mod loaded successfully!");
            Logger.LogMessage("You can now change talents without resetting attributes.");
        }
    }

    /// <summary>
    /// Основной патч для обхода проверки необходимости сброса атрибутов при смене талантов.
    /// В Stolen Realm при попытке изменить таланты игра проверяет, были ли уже распределены атрибуты.
    /// Если да - требуется полный сброс. Этот мод обходит данную проверку.
    /// </summary>
    
    #region Talent Change Patches
    
    /// <summary>
    /// Универсальный патч для всех методов связанных со сбросом характеристик.
    /// Модифицирует поведение так, чтобы сброс атрибутов не требовался.
    /// </summary>
    [HarmonyPatch]
    public static class ResetBypassPatch
    {
        /// <summary>
        /// Перехват методов проверки необходимости полного сброса
        /// </summary>
        [HarmonyPrefix]
        public static bool FullResetCheckPrefix(MethodBase __originalMethod, ref bool __result)
        {
            if (__originalMethod == null) return true;
            
            string fullName = __originalMethod.Name;
            string declaringType = __originalMethod.DeclaringType?.Name ?? "";
            
            // Проверяем название метода на наличие ключевых слов
            bool isResetCheck = (fullName.Contains("Reset") || fullName.Contains("Talent")) && 
                               (fullName.Contains("Required") || 
                                fullName.Contains("Need") || 
                                fullName.Contains("Must") ||
                                fullName.Contains("Force") ||
                                fullName.Contains("Can") ||
                                fullName.Contains("Allow"));
            
            // Также проверяем имя типа
            bool isRelatedType = declaringType.Contains("Character") || 
                                declaringType.Contains("Talent") ||
                                declaringType.Contains("Trainer") ||
                                declaringType.Contains("Attribute");
            
            if (isResetCheck && isRelatedType)
            {
                UnityEngine.Debug.Log($"[FreeTalents] Bypassing reset requirement check: {declaringType}.{fullName}");
                __result = false; // Сброс НЕ требуется
                return false; // Skip original method
            }
            
            return true;
        }

        /// <summary>
        /// Транспайлер для удаления вызовов проверки сброса из методов валидации
        /// </summary>
        [HarmonyTranspiler]
        public static System.Collections.Generic.IEnumerable<CodeInstruction> ValidationTranspiler(
            System.Collections.Generic.IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            var codes = new System.Collections.Generic.List<CodeInstruction>(instructions);
            bool modified = false;
            
            for (int i = 0; i < codes.Count; i++)
            {
                // Ищем вызовы методов проверки
                if (codes[i].opcode == OpCodes.Call || codes[i].opcode == OpCodes.Callvirt)
                {
                    string operandStr = codes[i].operand?.ToString() ?? "";
                    
                    // Если вызывается метод проверки необходимости сброса
                    if (operandStr.Contains("RequireReset") || 
                        operandStr.Contains("NeedsFullReset") ||
                        operandStr.Contains("ValidateAttributes") ||
                        operandStr.Contains("CanChangeTalent") ||
                        operandStr.Contains("AllowTalentChange"))
                    {
                        UnityEngine.Debug.Log($"[FreeTalents] Removing validation call in {__originalMethod?.Name}: {operandStr}");
                        
                        // Заменяем вызов на загрузку false (валидация пройдена)
                        codes[i] = new CodeInstruction(OpCodes.Ldc_I4_0);
                        modified = true;
                    }
                }
            }
            
            if (modified)
            {
                UnityEngine.Debug.Log($"[FreeTalents] Modified validation in {__originalMethod?.Name}");
            }
            
            return codes;
        }
    }

    #endregion
}

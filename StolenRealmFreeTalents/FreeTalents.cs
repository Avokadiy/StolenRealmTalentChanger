using BepInEx;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

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
            
            Logger.LogInfo("FreeTalents mod loaded successfully!");
            Logger.LogInfo("You can now change talents without resetting attributes.");
        }
    }

    /// <summary>
    /// Основной патч для обхода проверки необходимости сброса атрибутов при смене талантов.
    /// В Stolen Realm при попытке изменить таланты игра проверяет, были ли уже распределены атрибуты.
    /// Если да - требуется полный сброс. Этот мод обходит данную проверку.
    /// </summary>
    
    #region Talent Change Patches
    
    /// <summary>
    /// Патч для CharacterData - основного класса данных персонажа.
    /// Перехватывает проверку возможности изменения талантов.
    /// </summary>
    [HarmonyPatch]
    public static class CharacterDataPatch
    {
        /// <summary>
        /// Поиск и патчинг метода проверки через TryPatch
        /// </summary>
        [HarmonyPrepare]
        public static bool Prepare(MethodBase original)
        {
            // Патчим только если метод существует в игре
            return original != null;
        }

        /// <summary>
        /// Префикс для любого метода содержащего "Talent" и "Can" в названии
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CharacterData), MethodType.Enumerator)]
        public static bool TalentCheckPrefix(MethodBase __originalMethod, ref bool __result)
        {
            string methodName = __originalMethod?.Name ?? "";
            
            // Если метод связан с проверкой талантов - разрешаем
            if (methodName.Contains("Talent") && (methodName.Contains("Can") || methodName.Contains("Allow")))
            {
                Logger.LogInfo($"[FreeTalents] Intercepted talent check: {methodName}");
                __result = true;
                return false; // Skip original method
            }
            
            return true;
        }
    }

    /// <summary>
    /// Патч для UI тренера - компонента отвечающего за интерфейс смены талантов.
    /// </summary>
    [HarmonyPatch]
    public static class TrainerUIPatch
    {
        /// <summary>
        /// Перехват запроса на изменение таланта
        /// </summary>
        [HarmonyPrefix]
        public static bool OnTalentChangeRequestedPrefix(MethodBase __originalMethod)
        {
            if (__originalMethod == null) return true;
            
            string methodName = __originalMethod.Name;
            string className = __originalMethod.DeclaringType?.Name ?? "";
            
            // Если это метод обработки изменения таланта в тренере
            if (className.Contains("Trainer") && methodName.Contains("Talent"))
            {
                Logger.LogDebug($"[FreeTalents] Processing talent change request: {className}.{methodName}");
                // Позволяем продолжить выполнение, но пропускаем проверки
                return true;
            }
            
            return true;
        }

        /// <summary>
        /// Пост-процессинг для методов отображения предупреждений
        /// </summary>
        [HarmonyPostfix]
        public static void WarningDisplayPostfix(MethodBase __originalMethod)
        {
            if (__originalMethod == null) return;
            
            // Логгируем показ предупреждений (для отладки)
            if (__originalMethod.Name.Contains("Warning") || __originalMethod.Name.Contains("Alert"))
            {
                Logger.LogDebug($"[FreeTalents] Warning displayed: {__originalMethod.Name}");
            }
        }
    }

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
            
            // Проверяем название метода на наличие ключевых слов
            bool isResetCheck = fullName.Contains("Reset") && 
                               (fullName.Contains("Required") || 
                                fullName.Contains("Need") || 
                                fullName.Contains("Must") ||
                                fullName.Contains("Force"));
            
            if (isResetCheck)
            {
                Logger.LogInfo($"[FreeTalents] Bypassing reset requirement check: {fullName}");
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
                        operandStr.Contains("ValidateAttributes"))
                    {
                        Logger.LogDebug($"[FreeTalents] Removing validation call in {__originalMethod?.Name}: {operandStr}");
                        
                        // Заменяем вызов на загрузку false (валидация пройдена)
                        codes[i] = new CodeInstruction(OpCodes.Ldc_I4_0);
                        modified = true;
                    }
                }
            }
            
            if (modified)
            {
                Logger.LogInfo($"[FreeTalents] Modified validation in {__originalMethod?.Name}");
            }
            
            return codes;
        }
    }

    #endregion

    #region Helper Classes
    
    /// <summary>
    /// Вспомогательный класс для логгирования
    /// </summary>
    public static class ModLogger
    {
        private static BepInEx.Logging.ManualLogSource _logger;
        
        public static void Initialize(BepInEx.Logging.ManualLogSource logger)
        {
            _logger = logger;
        }
        
        public static void Info(string message) => _logger?.LogInfo(message);
        public static void Debug(string message) => _logger?.LogDebug(message);
        public static void Warning(string message) => _logger?.LogWarning(message);
        public static void Error(string message) => _logger?.LogError(message);
    }
    
    #endregion
}

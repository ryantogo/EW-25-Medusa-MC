using System;
using HarmonyLib;

namespace MedusaMultiCrew.Core
{
    // All private-member reflection goes through here so game API drift is easy to audit.
    // Adapted from SimpleWSO (CC0, Bongus/Motmir).
    internal static class Reflect
    {
        public static void SetField(object instance, string name, object value)
        {
            var field = AccessTools.Field(instance.GetType(), name);
            if (field == null)
            {
                throw new MissingFieldException(instance.GetType().FullName, name);
            }
            field.SetValue(instance, value);
        }

        public static bool TryGetField<T>(object instance, string name, out T value)
        {
            value = default;
            if (instance == null)
            {
                return false;
            }
            var field = AccessTools.Field(instance.GetType(), name);
            if (field == null)
            {
                return false;
            }
            value = (T)field.GetValue(instance);
            return true;
        }

        public static object Call(object instance, string name, params object[] args)
        {
            var method = AccessTools.Method(instance.GetType(), name);
            if (method == null)
            {
                throw new MissingMethodException(instance.GetType().FullName, name);
            }
            return method.Invoke(instance, args);
        }

        // Best-effort call for optional, per-aircraft widgets; failures are ignored.
        public static void TryCall(object instance, string name, params object[] args)
        {
            try
            {
                Call(instance, name, args);
            }
            catch (Exception)
            {
            }
        }

        // Unsubscribe a private handler from a public event (e.g. StatusDisplay from aircraft.onDisableUnit).
        public static void RemoveEventHandler(object source, string removeMethodName, object target, string handlerName)
        {
            var remove = AccessTools.Method(source.GetType(), removeMethodName);
            var handler = AccessTools.Method(target.GetType(), handlerName);
            if (remove == null || handler == null)
            {
                return;
            }
            Type delegateType = remove.GetParameters()[0].ParameterType;
            remove.Invoke(source, new object[] { Delegate.CreateDelegate(delegateType, target, handler) });
        }
    }
}

using System;
using System.Reflection;
using BepInEx.Unity.IL2CPP;

namespace CookingHub.Adapters;

/// <summary>
/// How many times faster Machine Speed makes the next cook, so the stove line shows the time a cook will really take.
/// Machine Speed divides the length the game hands CookingComponent.StartCooking by its cooking speed, once its hook
/// is in. Its speed and hook are read by name, so Cooking Hub runs without Machine Speed installed. Every read fails
/// soft to 1, so the stove line shows the normal cook time, and a failed lookup is logged once.
/// </summary>
internal static class MachineSpeedCooks
{
    public const string Guid = "com.dev.survivallog.machinespeed";

    private const string PatchType = "MachineSpeed.Adapters.CookingPatch";
    private const BindingFlags Statics = BindingFlags.NonPublic | BindingFlags.Static;

    private static Reader? _reader;
    private static bool _isLookedUp;

    /// <summary>The cooking speed the next cook starts at; 1 without Machine Speed, before its hook is in, or on any failure.</summary>
    public static float Speedup(Action<string> log)
    {
        try
        {
            return (Find(log) is { } reader ? reader.Read() : null) ?? 1f;
        }
        catch (Exception ex)
        {
            _reader = null;
            log($"could not read Machine Speed's cooking speed, the stove shows the game's cooking time: {ex.Message}");

            return 1f;
        }
    }

    private static Reader? Find(Action<string> log)
    {
        if (_isLookedUp) return _reader;

        _isLookedUp = true;

        if (!IL2CPPChainloader.Instance.Plugins.TryGetValue(Guid, out var info) || info.Instance == null) return null;

        _reader = Reader.Of(info.Instance.GetType().Assembly.GetType(PatchType, false));

        if (_reader == null) log("Machine Speed is installed but its cooking speed was not found, the stove shows the game's cooking time");

        return _reader;
    }

    /// <summary>CookingPatch's speed (a Speed, read through its Value) and its hook (a MachineHook, read through IsIn).</summary>
    private sealed record Reader(FieldInfo Speed, PropertyInfo Value, FieldInfo Hook, PropertyInfo IsIn)
    {
        public static Reader? Of(Type? patch)
        {
            var speed = patch?.GetField("_speed", Statics);
            var hook = patch?.GetField("_hook", Statics);
            var value = speed?.FieldType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            var isIn = hook?.FieldType.GetProperty("IsIn", BindingFlags.Public | BindingFlags.Instance);

            if (speed == null || hook == null || value?.PropertyType != typeof(float) || isIn?.PropertyType != typeof(bool)) return null;

            return new Reader(speed, value, hook, isIn);
        }

        /// <summary>Null while the hook is not in: every cook then keeps the game's length.</summary>
        public float? Read()
        {
            var hook = Hook.GetValue(null);

            if (hook == null || IsIn.GetValue(hook) is not true) return null;

            return Speed.GetValue(null) is { } speed && Value.GetValue(speed) is float value ? value : null;
        }
    }
}

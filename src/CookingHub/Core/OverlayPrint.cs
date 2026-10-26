using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CookingHub.Core;

/// <summary>
/// A fingerprint of everything the cooking screen's overlay is drawn from: the kitchen as read, the settings, the queues
/// and what the station asks for. Equal prints draw the same overlay, so a read that found nothing new skips building
/// the overlay (every recipe checked against every stack) and sends nothing. Sets and the queues are taken in any order,
/// since two reads of the same kitchen need not list them alike.
/// </summary>
public static class OverlayPrint
{
    public static ulong Of(Kitchen kitchen, bool isShowingOdds, bool isSpoilingFirst, CookQueue queue, int? last, int startToken)
    {
        var print = new Print();

        print.Add(RuntimeHelpers.GetHashCode(kitchen.Book));
        print.Add(kitchen.Level);
        print.Add(kitchen.MultiPotLevel);
        print.AddSet(kitchen.Cooked);
        print.AddSet(kitchen.CraftUnlocked);
        print.AddStacks(kitchen.Stock);
        print.Add(isShowingOdds ? 1 : 0);
        print.Add(isSpoilingFirst ? 1 : 0);
        print.Add(last ?? -1);
        print.Add(startToken);
        print.AddQueue(queue);

        if (kitchen.Stove is { } stove) print.AddStove(stove);
        else print.Add(-1);

        return print.Value;
    }

    private struct Print
    {
        public ulong Value;

        public void Add(long value) => Value = Mix(Value * 31 + (ulong)value + 0x9E3779B97F4A7C15UL);

        public void Add(string? text) => Add(text == null ? -1 : StringComparer.Ordinal.GetHashCode(text));

        public void Add(double? value) => Add(value is { } number ? BitConverter.DoubleToInt64Bits(number) : long.MinValue);

        public void AddSet(IEnumerable<int>? ids)
        {
            if (ids == null)
            {
                Add(-1);

                return;
            }

            ulong sum = 0;
            var count = 0;

            foreach (var id in ids)
            {
                sum += Mix((ulong)id + 0x632BE59BD9B4E019UL);
                count++;
            }

            Add(count);
            Add((long)sum);
        }

        public void AddStacks(IReadOnlyList<Stack> stacks)
        {
            Add(stacks.Count);

            foreach (var stack in stacks)
            {
                Add(stack.InstanceId);
                Add(stack.ItemId);
                Add(stack.Count);
                Add(stack.OwnerId);
                Add(stack.Place);
                Add(stack.HoursLeft);
                Add(stack.IsSpoiled ? 1 : 0);
            }
        }

        public void AddQueue(CookQueue queue)
        {
            ulong sum = 0;

            foreach (var pair in queue.ByStove)
            {
                var stove = new Print();

                stove.Add(pair.Key);
                stove.Add(pair.Value.Count);

                foreach (var id in pair.Value) stove.Add(id);

                sum += stove.Value;
            }

            Add(queue.ByStove.Count);
            Add((long)sum);
        }

        public void AddStove(Stove stove)
        {
            Add(stove.FurnitureId);
            Add(stove.WorkbenchId);
            Add(stove.BagId);
            Add(stove.MaxFood);
            Add(stove.IsHotPot ? 1 : 0);
            Add(stove.IsCooking ? 1 : 0);
            Add(stove.QualityBonus);
            AddSet(stove.Allowed);
            AddStacks(stove.Pot);
            Add(stove.Forecasts.Count);

            foreach (var forecast in stove.Forecasts)
            {
                Add(forecast.RecipeId);
                Add((forecast.IsExact ? 1 : 0) | (forecast.IsKnown ? 2 : 0));
            }

            Add(stove.Name);
            Add(BitConverter.SingleToInt32Bits(stove.SpeedRate));
            Add(stove.HotPotSeconds);
            Add(stove.SlotsByUnit ? 1 : 0);

            if (stove.Fuel is not { } fuel)
            {
                Add(-1);

                return;
            }

            Add(fuel.Slots);
            Add(fuel.Used);
            AddSet(fuel.Preferred);
            AddStacks(fuel.Stock);
        }

        /// <summary>splitmix64's finisher, so a one-bit change in any field reaches every bit of the print.</summary>
        private static ulong Mix(ulong x)
        {
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;

            return x ^ (x >> 31);
        }
    }
}

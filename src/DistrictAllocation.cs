using System;

namespace CoordinatedPolice;

internal static class DistrictAllocation
{
    public static void allocate(int total, ReadOnlySpan<bool> eligible, ReadOnlySpan<int> weights, Span<int> targets)
    {
        if (eligible.Length != 6 || weights.Length != 6 || targets.Length != 6)
            throw new ArgumentException("Exactly six districts are required.");
        if (total < 0 || total > 64) throw new ArgumentOutOfRangeException(nameof(total));
        targets.Clear();
        int sum = 0;
        for (int i = 0; i < 6; i++)
        {
            if (weights[i] < 0 || weights[i] > 10) throw new ArgumentOutOfRangeException(nameof(weights));
            if (eligible[i]) sum += weights[i];
        }
        if (sum == 0) return;
        Span<int> remainders = stackalloc int[6];
        int assigned = 0;
        for (int i = 0; i < 6; i++)
        {
            int numerator = eligible[i] ? total * weights[i] : 0;
            targets[i] = numerator / sum;
            remainders[i] = numerator % sum;
            assigned += targets[i];
        }
        for (int i = assigned; i < total; i++)
        {
            int best = -1;
            for (int j = 0; j < 6; j++)
                if (eligible[j] && weights[j] > 0 && (best < 0 || remainders[j] > remainders[best])) best = j;
            if (best < 0) throw new InvalidOperationException("No eligible district for remaining patrol allocation.");
            targets[best]++;
            remainders[best] = -1;
        }
    }

    public static int choose_route(ReadOnlySpan<int> districts, ReadOnlySpan<int> members,
        ReadOnlySpan<bool> available, ReadOnlySpan<int> actual, ReadOnlySpan<int> targets, int cursor)
    {
        if (districts.Length > 64 || districts.Length != members.Length || districts.Length != available.Length ||
            actual.Length != 6 || targets.Length != 6) throw new ArgumentException("Invalid district route data.");
        int best = -1;
        int deficit_best = 0;
        for (int i = 0; i < districts.Length; i++)
        {
            int route = (Math.Max(cursor, 0) % districts.Length + i) % districts.Length;
            int district = districts[route];
            if (!available[route] || district < 0 || district >= 6) continue;
            int deficit = targets[district] - actual[district];
            if (deficit <= 0) continue;
            if (best >= 0 && (deficit < deficit_best || (deficit == deficit_best && members[route] >= members[best]))) continue;
            best = route;
            deficit_best = deficit;
        }
        return best;
    }
}

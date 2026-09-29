using System.Collections.Generic;

namespace VaatusRevenge.Core
{
    // Attack tokens: a shared pool that limits how many enemies may attack at the same time. An enemy
    // takes a token before committing to an attack and returns it when the attack ends, or it's staggered,
    // killed, reset or loses interest. The others circle and wait: that's why souls-like groups feel
    // dangerous but fair instead of everyone swinging at once. Share one pool between the enemies of an
    // encounter. Tokens are owner ids, so acquiring twice is harmless and releasing is always safe.
    public sealed class AttackTokenPool
    {
        readonly List<int> holders = new List<int>(4);

        // maxTokens: how many may attack at once (the prototype spec says 2). Can be changed at runtime.
        public AttackTokenPool(int maxTokens)
        {
            MaxTokens = maxTokens;
        }

        public int MaxTokens { get; set; }
        public int Count => holders.Count;

        public bool IsHolding(int ownerId)
        {
            return holders.Contains(ownerId);
        }

        public bool CanAcquire(int ownerId)
        {
            return holders.Contains(ownerId) || holders.Count < MaxTokens;
        }

        public bool TryAcquire(int ownerId)
        {
            if (holders.Contains(ownerId)) return true;
            if (holders.Count >= MaxTokens) return false;
            holders.Add(ownerId);
            return true;
        }

        public void Release(int ownerId)
        {
            holders.Remove(ownerId);
        }

        public void Clear()
        {
            holders.Clear();
        }
    }
}

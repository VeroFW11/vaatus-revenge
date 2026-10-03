namespace VaatusRevenge
{
    // A reference to a running effect that you may want to stop early (a trail, a charge glow).
    // It's a small struct with a generation number, so it never allocates, and a handle kept after its
    // effect finished (and the pooled object was reused) safely does nothing. The default handle is a no-op.
    public struct FireVfxHandle
    {
        internal const int NoKind = 0;
        internal const int PieceKind = 1;
        internal const int TrailKind = 2;
        internal const int EmitterKind = 3;
        internal const int WhipKind = 4;

        internal readonly int Kind;
        internal readonly int Index;
        internal readonly int Generation;

        internal FireVfxHandle(int kind, int index, int generation)
        {
            Kind = kind;
            Index = index;
            Generation = generation;
        }

        public static FireVfxHandle None => default;

        // True while the effect is still playing (including a trail fading out after Stop).
        public bool IsAlive => FireVfx.IsAlive(this);

        // Ends the effect: a trail stops emitting and fades, a charge glow shrinks away.
        public void Stop()
        {
            FireVfx.Stop(this);
        }

        // 0..1 intensity: a charge glow grows and brightens, a trail gets wider.
        public void SetLevel(float level)
        {
            FireVfx.SetLevel(this, level);
        }
    }
}

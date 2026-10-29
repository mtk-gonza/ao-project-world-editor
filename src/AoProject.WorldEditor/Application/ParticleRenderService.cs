using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

/// <summary>
/// Port of clsDX8Engine.Particle_Group_Render and Particle_Render: resolves the
/// frame GRH and tint for every live particle of a group.
/// </summary>
internal sealed class ParticleRenderService
{
    public IReadOnlyList<RenderSprite> Render(
        IReadOnlyList<ParticleGroup> groups,
        IReadOnlyList<ParticleData> catalog,
        float elapsedSeconds,
        int tilePixels = 32)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(catalog);
        var sprites = new List<RenderSprite>();
        foreach (var group in groups)
        {
            if (!group.Active) continue;
            if (group.Type <= 0 || group.Type > catalog.Count) continue;
            var stream = catalog[group.Type - 1];
            if (stream.Grhs.Length == 0) continue;
            if (group.RuntimeType != group.Type)
            {
                group.RuntimeParticles.Clear();
                group.RuntimeType = group.Type;
                group.HasRendered = false;
            }
            // VB6 calls Particle_Group_Render at the tile center (+15,+15
            // for 32px tiles); particle offsets are relative to that point.
            var baseX = (group.X - 1) * tilePixels + tilePixels / 2 - 1;
            var baseY = (group.Y - 1) * tilePixels + tilePixels / 2 - 1;
            var count = Math.Max(1, stream.NumOfParticles);
            while (group.RuntimeParticles.Count < count) group.RuntimeParticles.Add(new ParticleRuntimeState());
            var deltaSeconds = group.HasRendered ? Math.Max(0f, elapsedSeconds - group.LastRenderSeconds) : 0f;
            group.LastRenderSeconds = elapsedSeconds;
            group.HasRendered = true;
            for (var index = 0; index < count; index++)
            {
                var frame = stream.Speed <= 0 || stream.Grhs.Length == 1
                    ? 0
                    : (int)(elapsedSeconds * 1000d * stream.Grhs.Length / stream.Speed) % stream.Grhs.Length;
                if (frame < 0) frame += stream.Grhs.Length;
                var grh = stream.Grhs[frame];
                if (grh <= 0) continue;
                var color = ToRendererRgb(ColorAt(stream.ColorTint, index));
                var state = group.RuntimeParticles[index];
                if (!state.Initialized || state.AliveFrames <= 0)
                    Initialize(state, stream, group.Index, index);
                else if (deltaSeconds > 0)
                    Advance(state, stream, deltaSeconds);
                var offsetX = (int)MathF.Round(state.X);
                var offsetY = (int)MathF.Round(state.Y);
                var width = stream.GrhResize && stream.GrhResizeX > 0 ? stream.GrhResizeX : 0;
                var height = stream.GrhResize && stream.GrhResizeY > 0 ? stream.GrhResizeY : 0;
                sprites.Add(new RenderSprite(grh, baseX + offsetX, baseY + offsetY, color & 0xFFFFFF,
                    (byte)(stream.AlphaBlend != 0 ? 180 : 255), SpriteKind.Particle, true, width, height, state.Angle));
            }
        }
        return sprites;
    }

    // Particle catalog colors use VB6 RGB()/COLORREF byte order (red in the
    // low byte), while RenderSprite colors are consumed as 0xRRGGBB.
    private static int ToRendererRgb(int colorRef) =>
        ((colorRef & 0xFF) << 16) | (colorRef & 0xFF00) | ((colorRef >> 16) & 0xFF);

    private static int ColorAt(IReadOnlyList<int> colors, int index) =>
        colors.Count == 0 ? 0xFFFFFF : colors[index % colors.Count];

    private static void Initialize(ParticleRuntimeState state, ParticleData stream, int groupIndex, int particleIndex)
    {
        var seed = unchecked(groupIndex * 397 + particleIndex * 7919);
        var hasPhysics = HasPhysics(stream);
        state.X = hasPhysics ? Range(stream.X1, stream.X2, seed) - 16 : ((particleIndex * 7) % 16) - 8;
        state.Y = hasPhysics ? Range(stream.Y1, stream.Y2, seed + 17) - 16 : ((particleIndex * 5) % 16) - 8;
        // InitGrh/Particle_Render starts with vector_x/vector_y. XMove/YMove
        // are applied only on subsequent simulation ticks.
        state.VelocityX = hasPhysics ? Range(stream.VecX1, stream.VecX2, seed + 31) : 0;
        state.VelocityY = hasPhysics ? Range(stream.VecY1, stream.VecY2, seed + 47) : 0;
        state.Angle = stream.Angle;
        state.AliveFrames = stream.Life1 == 0 && stream.Life2 == 0
            ? float.PositiveInfinity
            : Math.Max(1, Range(stream.Life1, stream.Life2, seed + 61));
        state.Initialized = true;
    }

    private static void Advance(ParticleRuntimeState state, ParticleData stream, float seconds)
    {
        var frames = Math.Max(0, (int)MathF.Round(seconds * 60f));
        var friction = Math.Max(1, stream.Friction);
        for (var frame = 0; frame < frames; frame++)
        {
            var seed = unchecked(state.SimulationFrame * 7919 + 17);
            // VB6 order: gravity/bounce, spin, random movement, then add the
            // vector using integer division by friction.
            if (stream.Gravity != 0)
            {
                state.VelocityY += stream.GravityStrength;
                if (state.Y > 0) state.VelocityY = stream.BounceStrength;
            }
            if (stream.Spin != 0)
            {
                var spin = stream.SpinSpeedLow + (stream.SpinSpeedHigh - stream.SpinSpeedLow) *
                    ((seed & 1023) / 1023f);
                state.Angle = (state.Angle + spin / 100f) % 360f;
                if (state.Angle < 0) state.Angle += 360f;
            }
            if (stream.XMove != 0)
                state.VelocityX = Range(stream.MoveX1, stream.MoveX2, seed + 31);
            if (stream.YMove != 0)
                state.VelocityY = Range(stream.MoveY1, stream.MoveY2, seed + 47);
            state.X += MathF.Truncate(state.VelocityX / friction);
            state.Y += MathF.Truncate(state.VelocityY / friction);
            state.AliveFrames--;
            state.SimulationFrame++;
        }
    }

    private static bool HasPhysics(ParticleData stream) =>
        stream.X1 != 0 || stream.X2 != 0 || stream.Y1 != 0 || stream.Y2 != 0 ||
        stream.VecX1 != 0 || stream.VecX2 != 0 || stream.VecY1 != 0 || stream.VecY2 != 0 ||
        stream.Gravity != 0 || stream.XMove != 0 || stream.YMove != 0;

    private static int Range(int first, int second, int seed)
    {
        if (second < first) (first, second) = (second, first);
        if (first == second) return first;
        var value = unchecked((uint)seed * 1664525u + 1013904223u);
        return first + (int)(value % (uint)(second - first + 1));
    }
}


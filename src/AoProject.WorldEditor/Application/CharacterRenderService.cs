using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Application;

internal enum SpriteKind { Object, Character, Particle }

internal readonly record struct RenderSprite(int GraphicIndex, int PixelX, int PixelY, int Color, byte Alpha,
    SpriteKind Kind = SpriteKind.Object, bool Animate = true, int Width = 0, int Height = 0, float Angle = 0f);

/// <summary>
/// Port of Char_Render (Graficos_Renderizado.bas:1252-1483): body, head, helmet,
/// weapon and shield with the per-heading draw order, body x shift, dwarf weapon
/// offset and body head offset.
/// </summary>
internal sealed class CharacterRenderService
{
    private enum Layer { Shield, Body, Head, Helmet, Weapon }

    public IReadOnlyList<RenderSprite> Render(
        IReadOnlyList<CharacterEntry> characters,
        IReadOnlyList<DirectionalAnimation> bodies,
        IReadOnlyList<HeadIndexData> heads,
        IReadOnlyList<DirectionalAnimation>? helmets = null,
        IReadOnlyList<DirectionalAnimation>? weapons = null,
        IReadOnlyList<DirectionalAnimation>? shields = null,
        int tilePixels = 32)
    {
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(heads);
        var sprites = new List<RenderSprite>();
        foreach (var character in characters)
        {
            if (!character.Active) continue;
            if (character.Heading is < 1 or > 4) continue;
            var baseX = (character.X - 1) * tilePixels;
            var baseY = (character.Y - 1) * tilePixels;
            if (character.Body <= 0 || character.Body >= bodies.Count) continue;
            var body = bodies[character.Body];
            var heading = (int)character.Heading;
            if (body.Walk[heading].GraphicIndex <= 0) continue;

            var bodyGrh = body.Walk[heading].GraphicIndex;
            var headGrh = character.Head > 0 && character.Head < heads.Count
                ? heads[character.Head].Head[heading].GraphicIndex : 0;
            var helmetGrh = helmets is not null && character.Helmet > 0 && character.Helmet < helmets.Count
                ? helmets[character.Helmet].Walk[heading].GraphicIndex : 0;
            var weaponGrh = weapons is not null && character.Weapon > 0 && character.Weapon < weapons.Count
                ? weapons[character.Weapon].Walk[heading].GraphicIndex : 0;
            var shieldGrh = shields is not null && character.Shield > 0 && character.Shield < shields.Count
                ? shields[character.Shield].Walk[heading].GraphicIndex : 0;

            // VB6 shifts the body one pixel when iBody < 488 and raises the
            // weapon/shield by 7 px for dwarves (HeadOffset.y = -26).
            var bodyShiftX = character.Body < 488 ? 1 : 0;
            var dwarfOffset = body.HeadOffset.Y == -26 ? 7 : 0;

            // Exact per-heading order from Char_Render:
            // EAST shield-body-head-helmet-weapon, NORTH weapon-shield-body-head-helmet,
            // WEST weapon-body-head-helmet-shield, SOUTH body-head-helmet-shield-weapon.
            var order = heading switch
            {
                (int)Heading.East => new[] { Layer.Shield, Layer.Body, Layer.Head, Layer.Helmet, Layer.Weapon },
                (int)Heading.North => new[] { Layer.Weapon, Layer.Shield, Layer.Body, Layer.Head, Layer.Helmet },
                (int)Heading.West => new[] { Layer.Weapon, Layer.Body, Layer.Head, Layer.Helmet, Layer.Shield },
                _ => new[] { Layer.Body, Layer.Head, Layer.Helmet, Layer.Shield, Layer.Weapon }
            };
            foreach (var layer in order)
            {
                var graphic = layer switch
                {
                    Layer.Shield => shieldGrh,
                    Layer.Body => bodyGrh,
                    Layer.Head => headGrh,
                    Layer.Helmet => helmetGrh,
                    _ => weaponGrh
                };
                if (graphic <= 0) continue;
                var x = layer switch
                {
                    Layer.Body => baseX + bodyShiftX,
                    Layer.Head or Layer.Helmet => baseX + body.HeadOffset.X,
                    _ => baseX
                };
                var y = layer switch
                {
                    Layer.Head or Layer.Helmet => baseY + body.HeadOffset.Y,
                    Layer.Weapon => baseY + (heading is (int)Heading.East or (int)Heading.North or (int)Heading.South
                        ? dwarfOffset : 0),
                    Layer.Shield => baseY + (heading is (int)Heading.West or (int)Heading.South
                        ? dwarfOffset : 0),
                    _ => baseY
                };
                // Heads and helmets pass animate=0 in VB6; the rest animate.
                var animate = layer is Layer.Body or Layer.Weapon or Layer.Shield;
                sprites.Add(new RenderSprite(graphic, x, y, 0xFFFFFF, 255, SpriteKind.Character, animate));
            }

            if (character.HasFx && character.GrhIndex > 0)
                sprites.Add(new RenderSprite(character.GrhIndex, baseX, baseY, 0xFFFFFF, 160, SpriteKind.Character));
        }
        return sprites;
    }
}

/// <summary>Port of clsDX8Engine.SetCharacterFx and EstaPCarea.</summary>
internal sealed class CharacterFxService
{
    public void SetFx(CharacterEntry character, int grhIndex, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(character);
        character.GrhIndex = (short)grhIndex;
        character.HasFx = enabled && grhIndex > 0;
    }

    public bool EstaPCarea(short x, short y, int startX, int endX, int startY, int endY)
    {
        if (startX > endX) (startX, endX) = (endX, startX);
        if (startY > endY) (startY, endY) = (endY, startY);
        return x >= startX && x <= endX && y >= startY && y <= endY;
    }
}

/// <summary>Port of clsDX8Engine.DrawInv: inventory slot grid geometry.</summary>
internal sealed class InventoryRenderService
{
    public IReadOnlyList<(int X, int Y, int Width, int Height)> Slots(int x, int y, int columns, int rows, int slotSize, int spacing = 2)
    {
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (slotSize <= 0) throw new ArgumentOutOfRangeException(nameof(slotSize));
        var slots = new List<(int, int, int, int)>(columns * rows);
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
            slots.Add((x + column * (slotSize + spacing), y + row * (slotSize + spacing), slotSize, slotSize));
        return slots;
    }
}

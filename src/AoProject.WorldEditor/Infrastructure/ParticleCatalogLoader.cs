using System.Text;
using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>
/// Port of Module1.CargarParticulasBinary and CargarParticulas.
/// The binary layout was derived from the reference particles.ind: every record
/// is 2 + nameLength + 169 + 4 * NumGrhs bytes, and the editor only consumes the
/// fields read here. `TailBytes` is the part of the fixed block after the colour
/// tint that the editor does not use.
/// </summary>
internal sealed class ParticleCatalogLoader
{
    private const int FixedBlock = 169;
    private const int ParsedFields = 97;
    private const int ColorTint = 48;
    private const int TailBytes = FixedBlock - ParsedFields - ColorTint;

    public IReadOnlyList<ParticleData> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.Default, leaveOpen: false);
        var count = reader.ReadInt16();
        if (count <= 0) throw new InvalidDataException($"Cantidad de particulas invalida: {count}.");

        var particles = new List<ParticleData>(count);
        for (var index = 0; index < count; index++)
        {
            var nameLength = reader.ReadUInt16();
            var particle = new ParticleData { Name = Encoding.Default.GetString(reader.ReadBytes(nameLength)) };
            particle.NumOfParticles = reader.ReadInt32();
            particle.NumGrhs = reader.ReadInt32();
            particle.Id = reader.ReadInt32();
            particle.X1 = reader.ReadInt32();
            particle.Y1 = reader.ReadInt32();
            particle.X2 = reader.ReadInt32();
            particle.Y2 = reader.ReadInt32();
            particle.Angle = reader.ReadInt32();
            particle.VecX1 = reader.ReadInt32();
            particle.VecX2 = reader.ReadInt32();
            particle.VecY1 = reader.ReadInt32();
            particle.VecY2 = reader.ReadInt32();
            particle.Life1 = reader.ReadInt32();
            particle.Life2 = reader.ReadInt32();
            particle.Friction = reader.ReadInt32();
            particle.Spin = reader.ReadByte();
            particle.SpinSpeedLow = reader.ReadSingle();
            particle.SpinSpeedHigh = reader.ReadSingle();
            particle.AlphaBlend = reader.ReadByte();
            particle.Gravity = reader.ReadByte();
            particle.GravityStrength = reader.ReadInt32();
            particle.BounceStrength = reader.ReadInt32();
            particle.XMove = reader.ReadByte();
            particle.YMove = reader.ReadByte();
            particle.MoveX1 = reader.ReadInt32();
            particle.MoveX2 = reader.ReadInt32();
            particle.MoveY1 = reader.ReadInt32();
            particle.MoveY2 = reader.ReadInt32();

            if (particle.NumGrhs is < 0 or > 4096)
                throw new InvalidDataException($"NumGrhs invalido en '{particle.Name}': {particle.NumGrhs}.");
            particle.Grhs = new int[particle.NumGrhs];
            for (var grh = 0; grh < particle.NumGrhs; grh++) particle.Grhs[grh] = reader.ReadInt32();
            for (var color = 0; color < 4; color++)
            {
                var red = reader.ReadInt32();
                var green = reader.ReadInt32();
                var blue = reader.ReadInt32();
                particle.ColorTint[color] = red | (green << 8) | (blue << 16);
            }
            _ = reader.ReadBytes(TailBytes);
            particles.Add(particle);
        }
        return particles;
    }

    /// <summary>Port of Module1.CargarParticulas (particles.ini variant).</summary>
    public IReadOnlyList<ParticleData> LoadIni(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var ini = IniDocument.Load(path);
        var total = ini.GetInt("INIT", "Total");
        var particles = new List<ParticleData>(Math.Max(0, total));
        for (var index = 1; index <= total; index++)
        {
            var section = index.ToString();
            var particle = new ParticleData
            {
                Name = ini.Get(section, "Name"),
                NumGrhs = ini.GetInt(section, "NumGrhs"),
                GrhResize = ini.GetInt(section, "resize") == -1,
                GrhResizeX = (short)ini.GetInt(section, "rx"),
                GrhResizeY = (short)ini.GetInt(section, "ry")
            };
            var listing = ini.Get(section, "Grh_List");
            if (!string.IsNullOrWhiteSpace(listing))
            {
                var parts = listing.Split(',');
                particle.Grhs = new int[Math.Max(0, particle.NumGrhs)];
                for (var grh = 0; grh < particle.Grhs.Length && grh < parts.Length; grh++)
                    particle.Grhs[grh] = int.TryParse(parts[grh].Trim(), out var value) ? value : 0;
            }
            for (var color = 1; color <= 4; color++)
            {
                var set = ini.Get(section, "ColorSet" + color);
                if (string.IsNullOrWhiteSpace(set)) continue;
                var rgb = set.Split(',');
                var r = rgb.Length > 0 && int.TryParse(rgb[0].Trim(), out var red) ? red : 0;
                var g = rgb.Length > 1 && int.TryParse(rgb[1].Trim(), out var green) ? green : 0;
                var b = rgb.Length > 2 && int.TryParse(rgb[2].Trim(), out var blue) ? blue : 0;
                particle.ColorTint[color - 1] = r | (g << 8) | (b << 16);
            }
            particles.Add(particle);
        }
        return particles;
    }
}

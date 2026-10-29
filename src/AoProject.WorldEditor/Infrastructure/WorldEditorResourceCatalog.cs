using AoProject.WorldEditor.Domain;

namespace AoProject.WorldEditor.Infrastructure;

/// <summary>
/// Read-only aggregate of every index consumed by the editor. The catalog never
/// creates, deletes, extracts, or overwrites files in the resource repository.
/// </summary>
internal sealed class WorldEditorResourceCatalog
{
    private WorldEditorResourceCatalog(
        string root,
        IReadOnlyList<GraphicData> graphics,
        IReadOnlyList<SurfaceData> surfaces,
        IReadOnlyList<ObjectData> objects,
        IReadOnlyList<NpcData> npcs,
        IReadOnlyList<TriggerData> triggers,
        HeadIndexData[] heads,
        DirectionalAnimation[] bodies,
        DirectionalAnimation[] helmets,
        DirectionalAnimation[] weapons,
        DirectionalAnimation[] shields,
        IReadOnlyList<ParticleData> particles)
    {
        Root = root;
        Graphics = graphics;
        Surfaces = surfaces;
        Objects = objects;
        Npcs = npcs;
        Triggers = triggers;
        Heads = heads;
        Bodies = bodies;
        Helmets = helmets;
        Weapons = weapons;
        Shields = shields;
        Particles = particles;
    }

    public string Root { get; }
    public IReadOnlyList<GraphicData> Graphics { get; }
    public IReadOnlyList<SurfaceData> Surfaces { get; }
    public IReadOnlyList<ObjectData> Objects { get; }
    public IReadOnlyList<NpcData> Npcs { get; }
    public IReadOnlyList<TriggerData> Triggers { get; }
    public HeadIndexData[] Heads { get; }
    public DirectionalAnimation[] Bodies { get; }
    public DirectionalAnimation[] Helmets { get; }
    public DirectionalAnimation[] Weapons { get; }
    public DirectionalAnimation[] Shields { get; }
    public IReadOnlyList<ParticleData> Particles { get; }

    public static WorldEditorResourceCatalog Load(string resourceRoot)
    {
        var root = Path.GetFullPath(resourceRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"No existe el directorio de recursos: {root}");

        var graphics = new GraphicIndexLoader().Load(root).ToList();
        var indices = new ReferenceIndexLoader();
        var characters = new CharacterIndexLoader();
        var particlePath = Path.Combine(root, "init", "particles.ind");
        var particles = File.Exists(particlePath)
            ? new ParticleCatalogLoader().Load(particlePath)
            : (IReadOnlyList<ParticleData>)[];
        var bodyGraphics = graphics;
        var bodies = characters.LoadBodies(root, bodyGraphics);
        var helmets = characters.LoadHelmets(root);
        var weapons = characters.LoadWeapons(root, bodyGraphics);
        var shields = characters.LoadShields(root, bodyGraphics);
        return new WorldEditorResourceCatalog(
            root,
            graphics,
            indices.LoadSurfaces(root),
            indices.LoadObjects(root),
            indices.LoadNpcs(root),
            indices.LoadTriggers(root),
            characters.LoadHeads(root),
            bodies,
            helmets,
            weapons,
            shields,
            particles);
    }
}
